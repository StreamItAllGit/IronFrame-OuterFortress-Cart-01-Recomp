// @module: IronFrameCart01.Module.ByoRomValidator
// @purpose: Clean-room SHA-256 validation of user BYO .nes (131,088 B) and .prg (131,072 B) images with 3-part remediation.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Security.Cryptography, Foundation.Retro, reCart.Contracts
// @exports: ByoRomValidationStatus, ByoRomRemediation, ByoRomValidationResult, ByoRomValidator

using System.Security.Cryptography;
using Foundation.Retro;
using reCart.Contracts;

namespace IronFrameCart01.Module;

/// <summary>Outcome status produced by <see cref="ByoRomValidator"/>.</summary>
public enum ByoRomValidationStatus
{
    /// <summary>Verified full <c>131,088</c>-byte iNES / NES 2.0 cartridge container.</summary>
    ValidInesFull = 0,
    /// <summary>Verified <c>131,072</c>-byte headerless PRG payload wrapped with a synthesized iNES header in memory.</summary>
    ValidHeaderlessPrg = 1,
    /// <summary>No cartridge file was found (expected during isolated zero-asset checks before BYO dump placement).</summary>
    Missing = 2,
    /// <summary>File byte count did not match either <c>131,088</c> (<c>.nes</c>) or <c>131,072</c> (<c>.prg</c>).</summary>
    InvalidSize = 3,
    /// <summary>Container header magic, mapper, trainer flag, or PRG layout was invalid for Cart 01.</summary>
    InvalidHeader = 4,
    /// <summary>SHA-256 digest did not match the verified cartridge checksums.</summary>
    ChecksumMismatch = 5,
    /// <summary>File could not be read due to OS permissions or I/O failure.</summary>
    Unreadable = 6
}

/// <summary>Structured 3-part clean-room remediation message (<c>What happened / Why / How to supply</c>).</summary>
public sealed record ByoRomRemediation(string WhatHappened, string Why, string HowToSupply)
{
    /// <summary>Formats the 3-part remediation guidance as readable multi-line text.</summary>
    public string FormatMessage() =>
        $"What happened: {WhatHappened}{Environment.NewLine}Why: {Why}{Environment.NewLine}How to supply: {HowToSupply}";
}

/// <summary>Result returned by <see cref="ByoRomValidator"/> containing parsed header, synthesized iNES bytes, or 3-part remediation.</summary>
public sealed record ByoRomValidationResult(
    bool IsValid,
    ByoRomValidationStatus Status,
    string? SourcePath,
    long ActualSizeBytes,
    string? ActualSha256Hex,
    CartRomHeader? Header,
    byte[]? NormalizedInesBytes,
    ByoRomRemediation? Remediation,
    string Summary)
{
    /// <summary>True when no cartridge file was present (safe zero-asset baseline condition).</summary>
    public bool IsMissing => Status == ByoRomValidationStatus.Missing;

    /// <summary>True when the validated input was a headerless <c>.prg</c> dump wrapped in memory.</summary>
    public bool IsHeaderlessPrg => Status == ByoRomValidationStatus.ValidHeaderlessPrg;

    /// <summary>Returns the formatted 3-part remediation text when invalid, or <see cref="Summary"/> when valid.</summary>
    public string FormattedMessage => Remediation?.FormatMessage() ?? Summary;
}

/// <summary>
/// Validates user-supplied BYO <c>.nes</c> (<c>131,088</c> B) and <c>.prg</c> (<c>131,072</c> B) cartridge dumps
/// via SHA-256 and <see cref="CartPrgHeaderSynthesizer"/> without bundling copyrighted assets.
/// </summary>
public static class ByoRomValidator
{
    private const long MaxReadBytesGuard = 1024 * 1024;

    private static readonly string CleanRoomWhy =
        $"{Cart01Constants.DisplayName} ({Cart01Constants.ModuleId}) is strictly clean-room and distributes zero retail cartridge bytes. " +
        $"Local AOT compilation requires a verified {Cart01Constants.NesExpectedSizeBytes:N0}-byte .nes container or " +
        $"{Cart01Constants.PrgExpectedSizeBytes:N0}-byte headerless .prg image matching expected SHA-256 digests.";

    private static readonly string CleanRoomHow =
        $"Dump your legally owned {Cart01Constants.SerialNumber} cartridge and place {Path.GetFileName(Cart01Constants.ExpectedNesRelativePath)} " +
        $"(SHA-256 {Cart01Constants.NesExpectedSha256}) or {Path.GetFileName(Cart01Constants.ExpectedPrgRelativePath)} " +
        $"(SHA-256 {Cart01Constants.PrgExpectedSha256}) under data\\rom\\ (or pass --preload-media <path>).";

    /// <summary>Scans <paramref name="romDirectory"/> (<c>data\rom\</c>) for <c>.nes</c> or <c>.prg</c> dumps without throwing when absent.</summary>
    public static ByoRomValidationResult ValidateDirectory(string? romDirectory, CartPackageManifest? manifest = null)
    {
        if (string.IsNullOrWhiteSpace(romDirectory) || !Directory.Exists(romDirectory))
            return CreateFailure(ByoRomValidationStatus.Missing, romDirectory, 0, null, "No BYO cartridge directory or verified dump was found under data\\rom\\.");

        string fullDir = Path.GetFullPath(romDirectory);
        List<string> candidates = EnumerateCandidateFiles(fullDir, manifest);
        if (candidates.Count == 0)
            return CreateFailure(ByoRomValidationStatus.Missing, fullDir, 0, null, "No .nes or .prg cartridge dump was found under data\\rom\\.");

        ByoRomValidationResult? firstFailure = null;
        foreach (string candidate in candidates)
        {
            ByoRomValidationResult result = ValidateFile(candidate, manifest);
            if (result.IsValid) return result;
            firstFailure ??= result;
        }
        return firstFailure!;
    }

    /// <summary>Validates a <c>.nes</c> or <c>.prg</c> file path, returning <see cref="ByoRomValidationStatus.Missing"/> when absent.</summary>
    public static ByoRomValidationResult ValidateFile(string? romFilePath, CartPackageManifest? manifest = null)
    {
        if (string.IsNullOrWhiteSpace(romFilePath))
            return CreateFailure(ByoRomValidationStatus.Missing, null, 0, null, "No BYO cartridge file path was supplied.");

        string fullPath = Path.GetFullPath(romFilePath);
        if (!File.Exists(fullPath))
            return CreateFailure(ByoRomValidationStatus.Missing, fullPath, 0, null, $"BYO cartridge file '{Path.GetFileName(fullPath)}' was not found.");

        string fileName = Path.GetFileName(fullPath);
        try
        {
            long fileLen = new FileInfo(fullPath).Length;
            if (fileLen <= 0)
                return CreateFailure(ByoRomValidationStatus.Missing, fullPath, 0, null, $"BYO cartridge file '{fileName}' is empty (0 bytes).");

            (int nesSize, _, int prgSize, _) = ResolveExpectedRules(manifest);
            bool hasCustomSize = manifest is not null && manifest.RequiredDiscChecksums.Any(e => e.ExpectedSizeBytes == fileLen);
            if (fileLen > MaxReadBytesGuard || (fileLen != nesSize && fileLen != prgSize && !hasCustomSize))
                return CreateInvalidSize(fullPath, fileName, fileLen, nesSize, prgSize);

            ByoRomValidationResult result = ValidateBytes(File.ReadAllBytes(fullPath), fileName, manifest);
            return result with { SourcePath = fullPath };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return CreateFailure(ByoRomValidationStatus.Unreadable, fullPath, 0, null, $"Unable to read BYO cartridge file '{fileName}': {ex.Message}");
        }
    }

    /// <summary>Validates in-memory <c>.nes</c> (<c>131,088</c> B) or <c>.prg</c> (<c>131,072</c> B) bytes and synthesizes an iNES header for verified <c>.prg</c> dumps.</summary>
    public static ByoRomValidationResult ValidateBytes(ReadOnlySpan<byte> rawBytes, string? sourceLabel = null, CartPackageManifest? manifest = null)
    {
        string label = string.IsNullOrWhiteSpace(sourceLabel) ? "BYO cartridge buffer" : Path.GetFileName(sourceLabel.Trim());
        if (rawBytes.Length == 0)
            return CreateFailure(ByoRomValidationStatus.Missing, null, 0, null, $"No cartridge bytes were present in '{label}'.");

        (int nesSize, string nesSha, int prgSize, string prgSha) = ResolveExpectedRules(manifest);
        ushort expectedMapper = manifest?.MapperId ?? Cart01Constants.MapperId;
        byte expectedSubmapper = manifest?.SubmapperId ?? Cart01Constants.SubmapperId;
        CartVideoRegionStandard expectedRegion = manifest?.VideoRegionStandard is { } r && r != CartVideoRegionStandard.Auto
            ? r : CartVideoRegionStandard.Ntsc;
        string actualSha = ComputeSha256Hex(rawBytes);

        if (rawBytes.Length == prgSize)
            return ValidateHeaderlessPrg(rawBytes, label, actualSha, prgSha, expectedMapper, expectedSubmapper, expectedRegion);
        if (rawBytes.Length == nesSize)
            return ValidateFullInes(rawBytes, label, actualSha, nesSha, prgSha, prgSize, expectedMapper, expectedSubmapper, expectedRegion);
        if (TryValidateCustomManifestEntry(rawBytes, actualSha, manifest, expectedMapper, expectedSubmapper, expectedRegion, out ByoRomValidationResult? customResult))
            return customResult!;

        return CreateInvalidSize(null, label, rawBytes.Length, nesSize, prgSize, actualSha);
    }

    private static ByoRomValidationResult ValidateHeaderlessPrg(
        ReadOnlySpan<byte> rawBytes, string label, string actualSha, string expectedPrgSha,
        ushort mapperId, byte submapperId, CartVideoRegionStandard region)
    {
        if (CartPrgHeaderSynthesizer.HasInesMagic(rawBytes))
            return CreateFailure(ByoRomValidationStatus.InvalidHeader, null, rawBytes.Length, actualSha,
                $"Input '{label}' ({rawBytes.Length:N0} B) starts with iNES header magic inside a headerless PRG length.");

        if (!string.Equals(actualSha, expectedPrgSha, StringComparison.OrdinalIgnoreCase))
            return CreateFailure(ByoRomValidationStatus.ChecksumMismatch, null, rawBytes.Length, actualSha,
                $"Headerless PRG '{label}' SHA-256 '{actualSha}' did not match expected '{expectedPrgSha}'.");

        byte[] synthesized = CartPrgHeaderSynthesizer.SynthesizeIfHeaderless(rawBytes.ToArray(), mapperId, submapperId, CartMirroringMode.Vertical, region);
        CartRomHeader header = CartRomHeaderParser.Parse(synthesized);
        return new ByoRomValidationResult(true, ByoRomValidationStatus.ValidHeaderlessPrg, null, rawBytes.Length, actualSha, header, synthesized, null,
            $"Verified headerless .prg '{label}' ({rawBytes.Length:N0} B, SHA-256 {actualSha[..12]}...) with synthesized mapper {header.MapperId} iNES header.");
    }

    private static ByoRomValidationResult ValidateFullInes(
        ReadOnlySpan<byte> rawBytes, string label, string actualSha, string expectedNesSha, string expectedPrgSha,
        int expectedPrgSize, ushort expectedMapper, byte expectedSubmapper, CartVideoRegionStandard expectedRegion)
    {
        if (!CartPrgHeaderSynthesizer.HasInesMagic(rawBytes))
            return CreateFailure(ByoRomValidationStatus.InvalidHeader, null, rawBytes.Length, actualSha,
                $"Input '{label}' ({rawBytes.Length:N0} B) is missing the 4-byte iNES 'NES\\x1A' header magic.");

        CartRomHeader header;
        try { header = CartRomHeaderParser.Parse(rawBytes); }
        catch (InvalidDataException ex)
        {
            return CreateFailure(ByoRomValidationStatus.InvalidHeader, null, rawBytes.Length, actualSha, $"Invalid iNES header in '{label}': {ex.Message}");
        }

        if (header.HasTrainer || header.PrgRomSizeBytes != expectedPrgSize || header.MapperId != expectedMapper)
            return CreateFailure(ByoRomValidationStatus.InvalidHeader, null, rawBytes.Length, actualSha,
                $"Header geometry in '{label}' (Mapper={header.MapperId}, PRG={header.PrgRomSizeBytes:N0} B, Trainer={header.HasTrainer}) does not match expected Mapper={expectedMapper}, PRG={expectedPrgSize:N0} B without trainer.");

        bool fullMatch = string.Equals(actualSha, expectedNesSha, StringComparison.OrdinalIgnoreCase);
        ReadOnlySpan<byte> prgSpan = rawBytes.Slice(CartRomHeaderParser.HeaderSizeBytes, expectedPrgSize);
        string prgSha = ComputeSha256Hex(prgSpan);
        if (!fullMatch && !string.Equals(prgSha, expectedPrgSha, StringComparison.OrdinalIgnoreCase))
            return CreateFailure(ByoRomValidationStatus.ChecksumMismatch, null, rawBytes.Length, actualSha,
                $"Cartridge '{label}' SHA-256 '{actualSha}' (PRG '{prgSha}') did not match expected '{expectedNesSha}' or PRG '{expectedPrgSha}'.");

        byte[] payloadToWrap = fullMatch ? rawBytes.ToArray() : prgSpan.ToArray();
        byte[] normalized = CartPrgHeaderSynthesizer.SynthesizeIfHeaderless(payloadToWrap, expectedMapper, expectedSubmapper, header.Mirroring, expectedRegion);
        CartRomHeader normalizedHeader = CartRomHeaderParser.Parse(normalized);
        return new ByoRomValidationResult(true, ByoRomValidationStatus.ValidInesFull, null, rawBytes.Length, actualSha, normalizedHeader, normalized, null,
            $"Verified full .nes '{label}' ({rawBytes.Length:N0} B, SHA-256 {actualSha[..12]}..., Mapper {normalizedHeader.MapperId}).");
    }

    private static bool TryValidateCustomManifestEntry(
        ReadOnlySpan<byte> rawBytes, string actualSha, CartPackageManifest? manifest,
        ushort mapperId, byte submapperId, CartVideoRegionStandard region, out ByoRomValidationResult? result)
    {
        result = null;
        if (manifest is null || manifest.RequiredDiscChecksums.Count == 0) return false;

        int rawLen = rawBytes.Length;
        DiscChecksumEntry? match = manifest.RequiredDiscChecksums.FirstOrDefault(e =>
            (!e.ExpectedSizeBytes.HasValue || e.ExpectedSizeBytes.Value == rawLen) &&
            string.Equals(e.ExpectedSha256, actualSha, StringComparison.OrdinalIgnoreCase));
        if (match is null) return false;

        byte[] normalized = CartPrgHeaderSynthesizer.SynthesizeIfHeaderless(rawBytes.ToArray(), mapperId, submapperId, CartMirroringMode.Vertical, region);
        ByoRomValidationStatus status = CartPrgHeaderSynthesizer.HasInesMagic(rawBytes)
            ? ByoRomValidationStatus.ValidInesFull : ByoRomValidationStatus.ValidHeaderlessPrg;
        result = new ByoRomValidationResult(true, status, null, rawLen, actualSha, CartRomHeaderParser.Parse(normalized), normalized, null,
            $"Verified manifest entry '{match.RelativePath}' ({rawLen:N0} B, SHA-256 {actualSha[..12]}...).");
        return true;
    }

    private static (int NesSize, string NesSha, int PrgSize, string PrgSha) ResolveExpectedRules(CartPackageManifest? manifest)
    {
        (int nesSize, string nesSha) = ResolveFormatRule(manifest, Cart01Constants.NesExpectedSizeBytes, Cart01Constants.NesExpectedSha256, ".nes");
        (int prgSize, string prgSha) = ResolveFormatRule(manifest, Cart01Constants.PrgExpectedSizeBytes, Cart01Constants.PrgExpectedSha256, ".prg");
        return (nesSize, nesSha, prgSize, prgSha);
    }

    private static (int Size, string Sha) ResolveFormatRule(CartPackageManifest? manifest, int defaultSize, string defaultSha, string ext)
    {
        if (manifest is null || manifest.RequiredDiscChecksums.Count == 0) return (defaultSize, defaultSha);
        DiscChecksumEntry? entry = manifest.RequiredDiscChecksums.FirstOrDefault(e =>
            e.ExpectedSizeBytes == defaultSize || e.RelativePath.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
        if (entry is null || string.IsNullOrWhiteSpace(entry.ExpectedSha256)) return (defaultSize, defaultSha);

        int size = entry.ExpectedSizeBytes is > 0 and <= MaxReadBytesGuard ? (int)entry.ExpectedSizeBytes.Value : defaultSize;
        return (size, entry.ExpectedSha256.Trim().ToLowerInvariant());
    }

    private static List<string> EnumerateCandidateFiles(string fullDir, CartPackageManifest? manifest)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();
        void AddIfExists(string path) { string full = Path.GetFullPath(path); if (File.Exists(full) && seen.Add(full)) ordered.Add(full); }

        if (manifest is not null)
        {
            foreach (DiscChecksumEntry entry in manifest.RequiredDiscChecksums.OrderByDescending(e => e.IsRequired))
            {
                string fileName = Path.GetFileName(entry.RelativePath);
                if (!string.IsNullOrWhiteSpace(fileName)) AddIfExists(Path.Combine(fullDir, fileName));
            }
        }

        AddIfExists(Path.Combine(fullDir, Path.GetFileName(Cart01Constants.ExpectedNesRelativePath)));
        AddIfExists(Path.Combine(fullDir, Path.GetFileName(Cart01Constants.ExpectedPrgRelativePath)));
        foreach (string nes in Directory.EnumerateFiles(fullDir, "*.nes", SearchOption.TopDirectoryOnly).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            AddIfExists(nes);
        foreach (string prg in Directory.EnumerateFiles(fullDir, "*.prg", SearchOption.TopDirectoryOnly).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            AddIfExists(prg);
        return ordered;
    }

    private static ByoRomValidationResult CreateInvalidSize(string? sourcePath, string label, long actualSize, int nesSize, int prgSize, string? actualSha = null) =>
        CreateFailure(ByoRomValidationStatus.InvalidSize, sourcePath, actualSize, actualSha,
            $"Cartridge '{label}' size ({actualSize:N0} B) does not match expected {nesSize:N0} B (.nes) or {prgSize:N0} B (.prg).");

    private static ByoRomValidationResult CreateFailure(ByoRomValidationStatus status, string? sourcePath, long actualSize, string? actualSha, string whatHappened)
    {
        var remediation = new ByoRomRemediation(whatHappened, CleanRoomWhy, CleanRoomHow);
        return new ByoRomValidationResult(false, status, sourcePath, actualSize, actualSha, null, null, remediation, remediation.FormatMessage());
    }

    private static string ComputeSha256Hex(ReadOnlySpan<byte> bytes)
    {
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(bytes, digest);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }
}

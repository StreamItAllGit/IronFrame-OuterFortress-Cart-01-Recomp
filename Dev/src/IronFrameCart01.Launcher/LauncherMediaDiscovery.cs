// @module: IronFrameCart01.Launcher.LauncherMediaDiscovery
// @purpose: Clean-room pre-flight BYO ROM discovery (<dataDir>\rom\ and SHA-256 matched suite directories) or --preload-media gate routing.
// @depends-on: System, System.Collections.Generic, System.IO, System.IO.Compression, System.Linq, System.Security.Cryptography, Foundation.Core, reCart.Contracts, reEngine.Core.Decomp
// @exports: LauncherMediaDiscovery

using System.IO.Compression;
using System.Security.Cryptography;
using Foundation.Core;
using reCart.Contracts;
using reEngine.Core.Decomp;

namespace IronFrameCart01.Launcher;

internal static class LauncherMediaDiscovery
{
    private static readonly string[] LocalRomExtensions = [".nes", ".prg", ".zip", ".7z"];

    /// <summary>
    /// Resolves a verified local or suite-local BYO ROM (<c>--rom &lt;path&gt;</c>) when none was supplied,
    /// or appends <c>--preload-media</c> so <c>reCart.Player</c> always opens the Clean-Room Media Gate.
    /// </summary>
    internal static string[] ApplyPreFlightRomDiscovery(
        LauncherCliOptions options,
        string[] basePlayerArgs,
        AppEnvironment env,
        string baseDir,
        string? openPath,
        string? playerExecutablePath = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(basePlayerArgs);
        ArgumentNullException.ThrowIfNull(env);

        if (ShouldSkipDiscovery(options))
            return basePlayerArgs;

        string? discoveredRom = DiscoverVerifiedRomPath(options, env, baseDir, openPath, playerExecutablePath);
        var updated = new List<string>(basePlayerArgs.Length + 2);
        updated.AddRange(basePlayerArgs);
        if (!string.IsNullOrWhiteSpace(discoveredRom))
        {
            updated.Add("--rom");
            updated.Add(discoveredRom);
        }
        else
        {
            updated.Add("--preload-media");
        }
        return updated.ToArray();
    }

    internal static string? DiscoverVerifiedRomPath(
        LauncherCliOptions options,
        AppEnvironment env,
        string baseDir,
        string? openPath,
        string? playerExecutablePath = null)
    {
        CartPackageManifest? manifest = TryLoadManifest(openPath);
        HashSet<string> expectedShas = ExtractExpectedChecksums(manifest);
        string fullBase = Path.GetFullPath(baseDir);

        foreach (string dir in new[] { Path.Combine(env.DataDirectory, "rom"), Path.Combine(fullBase, "data", "rom") }
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (TryFindVerifiedLocalRom(dir, expectedShas, playerExecutablePath, baseDir) is { } localHit)
                return localHit;
        }

        if (LauncherCliAndContainment.IsIsolatedTestEnabled() || expectedShas.Count == 0)
            return null;

        string? boundary = env.Sandbox.IsSandboxActive ? env.Sandbox.SandboxRoot : null;
        string? suiteRoot = ResolveSafeSuiteRoot(options, env, fullBase, boundary);
        if (string.IsNullOrWhiteSpace(suiteRoot) || !Directory.Exists(suiteRoot))
            return null;

        foreach (string sha in expectedShas)
        {
            SuiteMediaDiscoveryResult hit = SuiteMediaTreeWalker.Discover(
                fullBase, suiteRoot, LauncherHelpText.ModuleId, sha, "data/rom", boundary);
            SuiteMediaCandidate? exact = hit.Candidates.FirstOrDefault(c => c.MatchesExpectedSha256);
            if (exact is not null)
                return exact.FilePath;
        }

        string gamesDir = Path.Combine(suiteRoot, "games");
        if (!Directory.Exists(gamesDir)) return null;
        foreach (string gameDir in Directory.EnumerateDirectories(gamesDir))
        {
            if ((File.GetAttributes(gameDir) & FileAttributes.ReparsePoint) != 0) continue;
            string romDir = Path.Combine(gameDir, "data", "rom");
            if (TryFindVerifiedLocalRom(romDir, expectedShas, playerExecutablePath, baseDir) is { } siblingPrgHit)
                return siblingPrgHit;
        }
        return null;
    }

    private static bool ShouldSkipDiscovery(LauncherCliOptions options)
    {
        if (options.DryRun || options.Verify || options.ShowHelp || options.ShowVersion || !string.IsNullOrWhiteSpace(options.RomPath))
            return true;

        return options.ForwardedArgs.Any(a =>
            string.Equals(a, "--preload-media", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith("--preload-media=", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "--demo-cart", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "--lockstep-oracle", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "--lockstep-steps", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith("--lockstep-steps=", StringComparison.OrdinalIgnoreCase));
    }

    private static string? TryFindVerifiedLocalRom(
        string romDir,
        HashSet<string> expectedShas,
        string? playerExecutablePath = null,
        string? baseDir = null)
    {
        if (!Directory.Exists(romDir)) return null;
        string? sevenZipPath = ResolveSevenZipOverridePath(playerExecutablePath, baseDir);
        foreach (string candidate in Directory.EnumerateFiles(romDir, "*.*", SearchOption.AllDirectories)
            .Where(f => LocalRomExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            string? sha = ComputeRomSha256Hex(candidate, sevenZipPath, baseDir);
            if (sha is null) continue;
            if (expectedShas.Count == 0 || expectedShas.Contains(sha))
                return Path.GetFullPath(candidate);
        }
        return null;
    }

    private static string? ResolveSevenZipOverridePath(string? playerExecutablePath, string? baseDir)
    {
        List<string> candidates = [];
        if (!string.IsNullOrWhiteSpace(playerExecutablePath))
        {
            string? playerDir = Path.GetDirectoryName(Path.GetFullPath(playerExecutablePath));
            if (!string.IsNullOrWhiteSpace(playerDir))
                candidates.Add(Path.Combine(playerDir, "tools", "7z.exe"));
        }

        if (!string.IsNullOrWhiteSpace(baseDir))
        {
            candidates.Add(Path.Combine(baseDir, "app", "tools", "7z.exe"));
            candidates.Add(Path.Combine(baseDir, "tools", "7z.exe"));
        }

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string? ComputeRomSha256Hex(string filePath, string? sevenZipPath, string? baseDir)
    {
        try
        {
            if (filePath.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
                return ComputeSevenZipRomSha256Hex(filePath, sevenZipPath, baseDir);

            if (!filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using FileStream fs = File.OpenRead(filePath);
                return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
            }

            using ZipArchive archive = ZipFile.OpenRead(filePath);
            ZipArchiveEntry? entry = archive.Entries.FirstOrDefault(e =>
                e.FullName.EndsWith(".nes", StringComparison.OrdinalIgnoreCase) ||
                e.FullName.EndsWith(".prg", StringComparison.OrdinalIgnoreCase));
            if (entry is null) return null;
            using Stream s = entry.Open();
            return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    private static string? ComputeSevenZipRomSha256Hex(string archivePath, string? sevenZipPath, string? baseDir)
    {
        string extractTemp = Path.Combine(Path.GetTempPath(), "mgr_7z_probe_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(extractTemp);
            var options = new Foundation.Retro.MediaExtractionOptions(
                SevenZipOverridePath: sevenZipPath,
                ToolsBaseDirectory: string.IsNullOrWhiteSpace(baseDir) ? AppContext.BaseDirectory : baseDir,
                ComputeSha256: false);
            Foundation.Retro.MediaExtractionResult res = Foundation.Retro.MediaContainerExtractor
                .ExtractOrVerifyAsync(archivePath, extractTemp, options)
                .GetAwaiter().GetResult();
            if (!res.Success)
                return null;

            string? extractedRom = res.ExtractedFiles.FirstOrDefault(f =>
                    f.EndsWith(".nes", StringComparison.OrdinalIgnoreCase))
                ?? res.ExtractedFiles.FirstOrDefault(f =>
                    f.EndsWith(".prg", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(extractedRom) &&
                !string.IsNullOrWhiteSpace(res.PrimaryMediaPath) &&
                (res.PrimaryMediaPath.EndsWith(".nes", StringComparison.OrdinalIgnoreCase) ||
                 res.PrimaryMediaPath.EndsWith(".prg", StringComparison.OrdinalIgnoreCase)))
            {
                extractedRom = res.PrimaryMediaPath;
            }

            if (string.IsNullOrWhiteSpace(extractedRom) || !File.Exists(extractedRom))
                return null;

            using FileStream fs = File.OpenRead(extractedRom);
            return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            return null;
        }
        finally
        {
            try
            {
                if (Directory.Exists(extractTemp))
                    Directory.Delete(extractTemp, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static CartPackageManifest? TryLoadManifest(string? openPath)
    {
        if (string.IsNullOrWhiteSpace(openPath)) return null;
        string full = Path.GetFullPath(openPath);
        try
        {
            if (Directory.Exists(full))
            {
                string mf = Path.Combine(full, "module.recomp.json");
                return File.Exists(mf) ? CartPackageManifest.Deserialize(File.ReadAllText(mf)) : null;
            }
            if (!File.Exists(full)) return null;
            if (full.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                return CartPackageManifest.Deserialize(File.ReadAllText(full));

            using ZipArchive archive = ZipFile.OpenRead(full);
            ZipArchiveEntry? entry = archive.GetEntry("module.recomp.json");
            if (entry is null) return null;
            using var reader = new StreamReader(entry.Open());
            return CartPackageManifest.Deserialize(reader.ReadToEnd());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static HashSet<string> ExtractExpectedChecksums(CartPackageManifest? manifest)
    {
        var shas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (manifest is null) return shas;
        foreach (var entry in manifest.RequiredDiscChecksums)
        {
            if (!string.IsNullOrWhiteSpace(entry.ExpectedSha256))
                shas.Add(entry.ExpectedSha256.Trim());
        }
        return shas;
    }

    private static string? ResolveSafeSuiteRoot(LauncherCliOptions options, AppEnvironment env, string fullBase, string? boundary)
    {
        string? raw = !string.IsNullOrWhiteSpace(options.SuiteDir)
            ? Path.GetFullPath(options.SuiteDir)
            : (!string.IsNullOrWhiteSpace(options.ReEngineDir)
                ? Path.GetFullPath(options.ReEngineDir)
                : (Directory.GetParent(fullBase) is { } gp && gp.Name.Equals("games", StringComparison.OrdinalIgnoreCase)
                    ? gp.Parent?.FullName
                    : SuiteMediaTreeWalker.DeriveSuiteRootDirectory(null, env.DataDirectory, boundary)));

        if (string.IsNullOrWhiteSpace(raw)) return null;
        string full = Path.GetFullPath(raw);
        if (boundary is null) return full;
        try { return AppEnvironment.AssertPathContained(full, boundary); }
        catch (InvalidOperationException) { return null; }
    }
}

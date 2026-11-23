// @module: IronFrameCart01.Tests.TestSandboxSupport
// @purpose: Shared zero-asset synthetic cart builders, isolated env scope, HTTP loopback fixture, and cleanup helpers for xUnit suites.
// @depends-on: System, System.Collections.Generic, System.IO, System.IO.Compression, System.Net, System.Net.Sockets, System.Security.Cryptography, System.Text, Xunit
// @exports: IsolatedEnvCollection, EnvVarScope, SyntheticCartMedia, TestSandboxSupport

using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Foundation.Retro;
using IronFrameCart01.Module;
using reCart.Contracts;
using Xunit;

namespace IronFrameCart01.Tests;

[CollectionDefinition("IsolatedEnvSerial", DisableParallelization = true)]
public sealed class IsolatedEnvCollection
{
}

internal sealed class EnvVarScope : IDisposable
{
    private readonly Dictionary<string, string?> _previous = new(StringComparer.Ordinal);

    public EnvVarScope(params (string Name, string? Value)[] vars)
    {
        foreach ((string name, string? val) in vars)
        {
            _previous[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, val);
        }
    }

    public void Dispose()
    {
        foreach ((string name, string? prev) in _previous)
            Environment.SetEnvironmentVariable(name, prev);
    }
}

internal sealed record SyntheticCartMedia(
    byte[] NesBytes,
    string NesSha256Hex,
    byte[] PrgBytes,
    string PrgSha256Hex,
    CartPackageManifest Manifest);

internal static class TestSandboxSupport
{
    internal static string CreateTempDir(string prefix)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"mgr_test_{prefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    internal static void SafeDeleteDir(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        try { Directory.Delete(path, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    internal static string ResolveDevRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string slnx = Path.Combine(dir.FullName, "IronFrameCart01.slnx");
            if (File.Exists(slnx)) return dir.FullName;

            string subDev = Path.Combine(dir.FullName, "Dev", "IronFrameCart01.slnx");
            if (File.Exists(subDev)) return Path.Combine(dir.FullName, "Dev");
        }
        throw new DirectoryNotFoundException("Unable to locate IronFrameCart01 Dev root from test base directory.");
    }

    /// <summary>
    /// Builds a 100% synthetic zero-retail iNES Mapper 2 (128 KB PRG = 8 x 16 KB, 0 KB CHR-RAM) cartridge image
    /// containing only a clean-room reset spin loop (SEI / CLD / JMP $C001) at fixed-bank $C001.
    /// </summary>
    internal static SyntheticCartMedia BuildSyntheticUxRomMedia()
    {
        var prg = new byte[Cart01Constants.PrgExpectedSizeBytes];
        Array.Fill(prg, (byte)0xEA); // NOP fill

        int fixedOffset = 7 * Cart01Constants.PrgBankSizeBytes;
        // $C001: SEI (78), CLD (D8), JMP $C001 (4C 01 C0)
        prg[fixedOffset + 1] = 0x78;
        prg[fixedOffset + 2] = 0xD8;
        prg[fixedOffset + 3] = 0x4C;
        prg[fixedOffset + 4] = 0x01;
        prg[fixedOffset + 5] = 0xC0;

        // NMI / RESET / IRQ vectors at $FFFA-$FFFF
        prg[fixedOffset + 0x3FFA] = 0x01; prg[fixedOffset + 0x3FFB] = 0xC0;
        prg[fixedOffset + 0x3FFC] = 0x01; prg[fixedOffset + 0x3FFD] = 0xC0;
        prg[fixedOffset + 0x3FFE] = 0x01; prg[fixedOffset + 0x3FFF] = 0xC0;

        byte[] nes = CartPrgHeaderSynthesizer.SynthesizeIfHeaderless(
            prg, Cart01Constants.MapperId, Cart01Constants.SubmapperId,
            CartMirroringMode.Vertical, CartVideoRegionStandard.Ntsc);

        string nesSha = Convert.ToHexString(SHA256.HashData(nes)).ToLowerInvariant();
        string prgSha = Convert.ToHexString(SHA256.HashData(prg)).ToLowerInvariant();

        CartPackageManifest manifest = Cart01Constants.CreateDefaultManifest();
        manifest.RequiredDiscChecksums =
        [
            new DiscChecksumEntry(Cart01Constants.ExpectedNesRelativePath, nesSha, nes.Length, IsRequired: true),
            new DiscChecksumEntry(Cart01Constants.ExpectedPrgRelativePath, prgSha, prg.Length, IsRequired: false)
        ];

        return new SyntheticCartMedia(nes, nesSha, prg, prgSha, manifest);
    }

    internal static (string ZipPath, string Sha256Hex, long SizeBytes) CreateSyntheticInstallerPayloadZip(
        string destZipPath, string devRoot, string version = "0.1.0")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destZipPath)!);
        if (File.Exists(destZipPath)) File.Delete(destZipPath);

        string staging = CreateTempDir("zip_stage");
        try
        {
            Directory.CreateDirectory(Path.Combine(staging, "app"));
            File.WriteAllBytes(Path.Combine(staging, "IronFrame-OuterFortress-Cart-01.exe"), Encoding.ASCII.GetBytes("MZ-SYNTHETIC-TEST-LAUNCHER"));
            File.WriteAllText(Path.Combine(staging, "app", "version.txt"), version + "\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(staging, "LICENSE"), "MIT Test Stub\n", new UTF8Encoding(false));

            string srcExample = Path.Combine(devRoot, "settings.example.json");
            if (File.Exists(srcExample))
                File.Copy(srcExample, Path.Combine(staging, "settings.example.json"), overwrite: true);

            Cart01RecipeLoader.PackageCleanRoomArchiveFromFiles(
                Path.Combine(devRoot, "module.recomp.json"),
                Path.Combine(devRoot, "src", "IronFrameCart01.Module", "Manifests", "aot-recipe.json"),
                Path.Combine(staging, "app", Cart01Constants.ArchivePackageFileName));

            ZipFile.CreateFromDirectory(staging, destZipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            byte[] zipBytes = File.ReadAllBytes(destZipPath);
            string sha = Convert.ToHexString(SHA256.HashData(zipBytes)).ToLowerInvariant();
            return (destZipPath, sha, zipBytes.LongLength);
        }
        finally
        {
            SafeDeleteDir(staging);
        }
    }

    internal static int ReserveFreeLoopbackPort()
    {
        using var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        int port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        return port;
    }

    internal static string WriteDummyPlayerLeaf(string enginesRoot, string engineId, string version, bool writeReady = true)
    {
        string leaf = Path.Combine(enginesRoot, engineId, version);
        string appDir = Path.Combine(leaf, "app");
        Directory.CreateDirectory(appDir);
        string playerExe = Path.Combine(appDir, $"{engineId}.Player.exe");
        File.WriteAllBytes(playerExe, Encoding.ASCII.GetBytes($"DUMMY-{engineId}-{version}"));
        File.WriteAllText(Path.Combine(leaf, "version.txt"), version + "\n", new UTF8Encoding(false));
        if (writeReady) File.WriteAllText(Path.Combine(leaf, ".ready"), "{ \"ready\": true }\n", new UTF8Encoding(false));
        return Path.GetFullPath(playerExe);
    }
}

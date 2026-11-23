// @module: IronFrameCart01.Tests.PrelaunchRegressionTests
// @purpose: Permanent regression suite covering .7z pre-flight ROM discovery, corrupt archive temp cleanup, and clean-room 0xC6B7 jump-table AOT lifting.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.IO, System.Linq, System.Security.Cryptography, System.Threading.Tasks, Xunit, Foundation.Core, IronFrameCart01.Launcher, IronFrameCart01.Module, reCart.Compiler, reCart.Contracts, reEngine.Core
// @exports: PrelaunchRegressionTests

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Foundation.Core;
using Foundation.Retro;
using IronFrameCart01.Launcher;
using IronFrameCart01.Module;
using reCart.Compiler;
using reCart.Contracts;
using Xunit;

namespace IronFrameCart01.Tests;

[Collection("IsolatedEnvSerial")]
public sealed class PrelaunchRegressionTests
{
    [Fact]
    public void LauncherMediaDiscovery_DiscoversAndVerifies7zRom_AndCleansEphemeralTemp()
    {
        string temp = TestSandboxSupport.CreateTempDir("7z_ok");
        HashSet<string> beforeProbeDirs = SnapshotProbeDirs();
        try
        {
            using var envScope = new EnvVarScope(("APP_ISOLATED_TEST", "1"));
            (string baseDir, string dataDir, string openPath, string playerExe, SyntheticCartMedia media) =
                CreateLauncherSandboxFixture(temp);

            string sevenZipExe = StageBundledSevenZipTools(playerExe);
            string rawNesPath = Path.Combine(temp, "stage_src.nes");
            File.WriteAllBytes(rawNesPath, media.NesBytes);
            string sevenZipRom = Path.Combine(dataDir, "rom", "NES-ME-USA.7z");
            CreateSevenZipArchive(sevenZipExe, sevenZipRom, rawNesPath);

            var env = new AppEnvironment(Cart01Constants.ModuleId, ["--data-dir", dataDir], baseDir);
            LauncherCliOptions opts = LauncherCliAndContainment.ParseAndNormalize(["--data-dir", dataDir], temp);
            string[] forwarded = LauncherMediaDiscovery.ApplyPreFlightRomDiscovery(
                opts, ["--data-dir", dataDir], env, baseDir, openPath, playerExe);

            int romFlagIdx = Array.IndexOf(forwarded, "--rom");
            Assert.True(romFlagIdx >= 0 && romFlagIdx + 1 < forwarded.Length, "Expected --rom for verified .7z archive.");
            Assert.Equal(Path.GetFullPath(sevenZipRom), forwarded[romFlagIdx + 1]);
            Assert.DoesNotContain("--preload-media", forwarded);
            Assert.Empty(SnapshotProbeDirs().Except(beforeProbeDirs, StringComparer.OrdinalIgnoreCase));
        }
        finally
        {
            TestSandboxSupport.SafeDeleteDir(temp);
        }
    }

    [Fact]
    public void LauncherMediaDiscovery_RejectsCorrupt7zAndCleansEphemeralTemp()
    {
        string temp = TestSandboxSupport.CreateTempDir("7z_bad");
        HashSet<string> beforeProbeDirs = SnapshotProbeDirs();
        try
        {
            using var envScope = new EnvVarScope(("APP_ISOLATED_TEST", "1"));
            (string baseDir, string dataDir, string openPath, string playerExe, _) =
                CreateLauncherSandboxFixture(temp);
            _ = StageBundledSevenZipTools(playerExe);

            string corrupt7z = Path.Combine(dataDir, "rom", "corrupt.7z");
            File.WriteAllBytes(corrupt7z, Encoding.ASCII.GetBytes("NOT-A-VALID-7Z-ARCHIVE-HEADER"));

            var env = new AppEnvironment(Cart01Constants.ModuleId, ["--data-dir", dataDir], baseDir);
            LauncherCliOptions opts = LauncherCliAndContainment.ParseAndNormalize(["--data-dir", dataDir], temp);
            string[] forwarded = LauncherMediaDiscovery.ApplyPreFlightRomDiscovery(
                opts, ["--data-dir", dataDir], env, baseDir, openPath, playerExe);

            Assert.DoesNotContain("--rom", forwarded);
            Assert.Contains("--preload-media", forwarded);
            Assert.Empty(SnapshotProbeDirs().Except(beforeProbeDirs, StringComparer.OrdinalIgnoreCase));
        }
        finally
        {
            TestSandboxSupport.SafeDeleteDir(temp);
        }
    }

    [Fact]
    public async Task ColdAotCompiler_LiftsInlineC6B7JumpTableTargetsFromCleanRoomRecipeWithoutRetailOffsets()
    {
        string temp = TestSandboxSupport.CreateTempDir("aot_c6b7");
        try
        {
            string devRoot = TestSandboxSupport.ResolveDevRoot();
            string manifestSrc = Path.Combine(devRoot, "module.recomp.json");
            string recipeSrc = Path.Combine(devRoot, "src", "IronFrameCart01.Module", "Manifests", "aot-recipe.json");
            Cart01LoadedRecipePackage loaded = Cart01RecipeLoader.LoadFromFiles(manifestSrc, recipeSrc);

            IReadOnlyList<(uint Bank, ushort Address)> seeds = loaded.Recipe.ToSeedTuples(fixedBankIndex: 7);
            Assert.DoesNotContain(seeds, s => s.Address is 0xC2C6 or 0xC2C9);
            Assert.Contains(loaded.Recipe.JumpTables, j => CartAotRecipeSpec.ParseHexAddress(j.DispatcherAddress) == 0xC6B7);

            byte[] nes = BuildSyntheticC6B7JumpTableNes();
            string nesSha = Convert.ToHexString(SHA256.HashData(nes)).ToLowerInvariant();
            byte[] prg = nes[16..];
            string prgSha = Convert.ToHexString(SHA256.HashData(prg)).ToLowerInvariant();

            CartPackageManifest manifest = Cart01Constants.CreateDefaultManifest();
            manifest.RequiredDiscChecksums =
            [
                new DiscChecksumEntry(Cart01Constants.ExpectedNesRelativePath, nesSha, nes.Length, IsRequired: true),
                new DiscChecksumEntry(Cart01Constants.ExpectedPrgRelativePath, prgSha, prg.Length, IsRequired: false)
            ];

            string recompPath = Path.Combine(temp, "cleanroom.recomp");
            Cart01RecipeLoader.PackageCleanRoomArchive(manifest, loaded.Recipe, recompPath);

            var dispatchers = new HashSet<ushort> { 0xC6B7 };
            LiftedProgramResult lifted = new Lifter6502ToCSharp().Lift(nes, manifest, seeds, dispatchers);
            Assert.Contains((ushort)0xC2C6, lifted.JumpTableTargets);
            Assert.Contains((ushort)0xC2C9, lifted.JumpTableTargets);

            string dataDir = Path.Combine(temp, "data");
            Directory.CreateDirectory(dataDir);
            var compiler = new ColdAotCacheCompiler();
            ICartRecompGameModule module = await compiler.CompileAndLoadAsync(recompPath, nesSha, dataDir, nes);
            Assert.NotNull(module);
            Assert.Equal(Cart01Constants.ModuleId, module.ModuleId);

            string[] generatedDlls = Directory.GetFiles(
                Path.Combine(dataDir, "cache"),
                $"{Cart01Constants.ModuleId}.Generated.dll",
                SearchOption.AllDirectories);
            Assert.Single(generatedDlls);
        }
        finally
        {
            TestSandboxSupport.SafeDeleteDir(temp);
        }
    }

    private static byte[] BuildSyntheticC6B7JumpTableNes()
    {
        var prg = new byte[Cart01Constants.PrgExpectedSizeBytes];
        Array.Fill(prg, (byte)0x60); // RTS fill so unrelated schema seeds terminate cleanly.

        int fixedOffset = 7 * Cart01Constants.PrgBankSizeBytes;
        // $C001: SEI (78), CLD (D8), JSR $C6B7 (20 B7 C6), inline targets [$C2C6, $C2C9], terminator $0000
        prg[fixedOffset + 0x0001] = 0x78;
        prg[fixedOffset + 0x0002] = 0xD8;
        prg[fixedOffset + 0x0003] = 0x20;
        prg[fixedOffset + 0x0004] = 0xB7;
        prg[fixedOffset + 0x0005] = 0xC6;
        prg[fixedOffset + 0x0006] = 0xC6; prg[fixedOffset + 0x0007] = 0xC2;
        prg[fixedOffset + 0x0008] = 0xC9; prg[fixedOffset + 0x0009] = 0xC2;
        prg[fixedOffset + 0x000A] = 0x00; prg[fixedOffset + 0x000B] = 0x00;

        // $C2C6: LDA #$01; RTS
        prg[fixedOffset + 0x02C6] = 0xA9;
        prg[fixedOffset + 0x02C7] = 0x01;
        prg[fixedOffset + 0x02C8] = 0x60;

        // $C2C9: LDA #$02; RTS
        prg[fixedOffset + 0x02C9] = 0xA9;
        prg[fixedOffset + 0x02CA] = 0x02;
        prg[fixedOffset + 0x02CB] = 0x60;

        // $C6B7: RTS
        prg[fixedOffset + 0x06B7] = 0x60;

        // NMI / RESET / IRQ -> $C001
        prg[fixedOffset + 0x3FFA] = 0x01; prg[fixedOffset + 0x3FFB] = 0xC0;
        prg[fixedOffset + 0x3FFC] = 0x01; prg[fixedOffset + 0x3FFD] = 0xC0;
        prg[fixedOffset + 0x3FFE] = 0x01; prg[fixedOffset + 0x3FFF] = 0xC0;

        return CartPrgHeaderSynthesizer.SynthesizeIfHeaderless(
            prg, Cart01Constants.MapperId, Cart01Constants.SubmapperId,
            CartMirroringMode.Vertical, CartVideoRegionStandard.Ntsc);
    }

    private static (string BaseDir, string DataDir, string OpenPath, string PlayerExe, SyntheticCartMedia Media)
        CreateLauncherSandboxFixture(string temp)
    {
        string baseDir = Path.Combine(temp, "game");
        string dataDir = Path.Combine(temp, "data");
        Directory.CreateDirectory(Path.Combine(baseDir, "app"));
        Directory.CreateDirectory(Path.Combine(dataDir, "rom"));

        SyntheticCartMedia media = TestSandboxSupport.BuildSyntheticUxRomMedia();
        string openPath = Path.Combine(baseDir, "app", "module.recomp.json");
        File.WriteAllText(openPath, media.Manifest.Serialize(), new UTF8Encoding(false));

        string playerExe = TestSandboxSupport.WriteDummyPlayerLeaf(
            Path.Combine(baseDir, "app", "engines"), "reCart", "0.1.3");
        return (baseDir, dataDir, openPath, playerExe, media);
    }

    private static string StageBundledSevenZipTools(string playerExe)
    {
        string devRoot = TestSandboxSupport.ResolveDevRoot();
        string personalRoot = Directory.GetParent(Directory.GetParent(devRoot)!.FullName)!.FullName;
        string[] candidates =
        [
            Path.Combine(personalRoot, "reCartOne", "Builds", "v0.1.3", "app", "tools", "7z.exe"),
            Path.Combine(personalRoot, "reCartOne", "Builds", "Latest", "app", "tools", "7z.exe"),
            @"C:\Program Files\7-Zip\7z.exe"
        ];
        string? srcExe = candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("Unable to locate 7z.exe for PrelaunchRegressionTests.");

        string toolsDir = Path.Combine(Path.GetDirectoryName(playerExe)!, "tools");
        Directory.CreateDirectory(toolsDir);
        string dstExe = Path.Combine(toolsDir, "7z.exe");
        File.Copy(srcExe, dstExe, overwrite: true);

        string srcDll = Path.Combine(Path.GetDirectoryName(srcExe)!, "7z.dll");
        if (File.Exists(srcDll))
            File.Copy(srcDll, Path.Combine(toolsDir, "7z.dll"), overwrite: true);

        return dstExe;
    }

    private static void CreateSevenZipArchive(string sevenZipExe, string archivePath, string inputFile)
    {
        if (File.Exists(archivePath)) File.Delete(archivePath);
        var psi = new ProcessStartInfo(sevenZipExe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("a");
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add(archivePath);
        psi.ArgumentList.Add(inputFile);
        using Process proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to launch 7z.exe.");
        Assert.True(proc.WaitForExit(15000), "7z.exe timed out creating test archive.");
        Assert.Equal(0, proc.ExitCode);
    }

    private static HashSet<string> SnapshotProbeDirs() =>
        new(Directory.GetDirectories(Path.GetTempPath(), "mgr_7z_probe_*"), StringComparer.OrdinalIgnoreCase);
}

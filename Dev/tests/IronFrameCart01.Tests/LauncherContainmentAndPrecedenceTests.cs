// @module: IronFrameCart01.Tests.LauncherContainmentAndPrecedenceTests
// @purpose: Verifies Launcher CLI parsing, CWD rebasing, Release ExitCode=2 gate, APP_ISOLATED_TEST=1 containment, settings seeding, and R01 §3.3 precedence.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.IO, System.Text.Json.Nodes, Xunit, Foundation.Core, IronFrameCart01.Launcher, reEngine.Core.Bootstrap
// @exports: LauncherContainmentAndPrecedenceTests

using System.Diagnostics;
using System.Text.Json.Nodes;
using Foundation.Core;
using IronFrameCart01.Launcher;
using reCart.Contracts;
using Xunit;
using LauncherProgram = IronFrameCart01.Launcher.Program;

namespace IronFrameCart01.Tests;

[Collection("IsolatedEnvSerial")]
public sealed class LauncherContainmentAndPrecedenceTests
{
    [Fact]
    public void LauncherCli_RejectsAutomationPortInReleaseBuildWithExitCode2()
    {
#if IRONFRAME_TEST_AUTOMATION
        Assert.True(LauncherCliAndContainment.HasAutomationPortFlag(["--automation-port=5050"]));
#else
        int exit = LauncherProgram.Run(["--automation-port", "5050"]);
        Assert.Equal(2, exit);
#endif
    }

    [Fact]
    public void LauncherCli_StripsAutoStartRebasesRelativePathsAndNormalizesLockstep()
    {
        string temp = TestSandboxSupport.CreateTempDir("launcher_cli");
        try
        {
            string callerCwd = Path.Combine(temp, "caller");
            Directory.CreateDirectory(callerCwd);

            LauncherCliOptions opts = LauncherCliAndContainment.ParseAndNormalize([
                "--auto-start",
                "--autostart=1",
                "--open", "mods/custom.recomp",
                "--rom=roms/cart.nes",
                "--lockstep-oracle", "20000",
                "--data-dir", "data",
                "--feed", "https://example.invalid/feed.json"
            ], callerCwd);

            Assert.True(opts.StrippedAutoStart);
            Assert.True(opts.WaitRequested);
            Assert.Equal(Path.GetFullPath("mods/custom.recomp", callerCwd), opts.OpenPath);
            Assert.Equal(Path.GetFullPath("roms/cart.nes", callerCwd), opts.RomPath);
            Assert.Equal(Path.GetFullPath("data", callerCwd), opts.DataDir);
            Assert.DoesNotContain(opts.ForwardedArgs, a => a.Contains("autostart", StringComparison.OrdinalIgnoreCase) || a.Contains("auto-start", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("--lockstep-oracle", opts.ForwardedArgs);
            Assert.Contains("--lockstep-steps", opts.ForwardedArgs);
            Assert.Contains("20000", opts.ForwardedArgs);
            Assert.Contains("https://example.invalid/feed.json", opts.ForwardedArgs);
        }
        finally { TestSandboxSupport.SafeDeleteDir(temp); }
    }

    [Fact]
    public void IsolatedContainment_RequiresDataDirAndBlocksEscapeOutsideBoundary()
    {
        string temp = TestSandboxSupport.CreateTempDir("launcher_iso");
        string outside = TestSandboxSupport.CreateTempDir("launcher_escape");
        try
        {
            using var envScope = new EnvVarScope(
                ("APP_ISOLATED_TEST", "1"),
                ("DATA_DIR", null),
                ("APP_HOME", null),
                ("IRONFRAME_OUTERFORTRESS_CART_01_DATA_DIR", null));

            int missingDataExit = LauncherProgram.Run(["--dry-run"], baseDirOverride: temp, callerWorkingDirOverride: temp);
            Assert.Equal(SandboxEnvironmentSeam.ContainmentExitCode, missingDataExit);

            string dataDir = Path.Combine(temp, "data");
            int escapeExit = LauncherProgram.Run(
                ["--dry-run", "--data-dir", dataDir, "--save-dir", Path.Combine(outside, "saves")],
                baseDirOverride: temp,
                callerWorkingDirOverride: temp);
            Assert.Equal(SandboxEnvironmentSeam.ContainmentExitCode, escapeExit);
        }
        finally
        {
            TestSandboxSupport.SafeDeleteDir(temp);
            TestSandboxSupport.SafeDeleteDir(outside);
        }
    }

    [Fact]
    public void SeedDefaultSettingsIfMissing_SeedsAllFourDomainsOnceAndPreservesEdits()
    {
        string temp = TestSandboxSupport.CreateTempDir("launcher_settings");
        try
        {
            string dataDir = Path.Combine(temp, "data");
            string configPath = Path.Combine(dataDir, "config", "settings.json");

            LauncherCliAndContainment.SeedDefaultSettingsIfMissing(dataDir, configPath);

            Assert.True(File.Exists(configPath));
            Assert.True(File.Exists(Path.Combine(dataDir, "config", "settings.video.json")));
            Assert.True(File.Exists(Path.Combine(dataDir, "config", "settings.audio.json")));
            string controlsPath = Path.Combine(dataDir, "config", "settings.controls.json");
            Assert.True(File.Exists(controlsPath));

            JsonNode controls = JsonNode.Parse(File.ReadAllText(controlsPath))!;
            Assert.True(controls["useGlobalFormatRules"]?.GetValue<bool>());

            string customJson = "{ \"schemaVersion\": 2, \"domain\": \"controls\", \"profileMode\": \"retroClassic\", \"useGlobalFormatRules\": true }\n";
            File.WriteAllText(controlsPath, customJson);

            LauncherCliAndContainment.SeedDefaultSettingsIfMissing(dataDir, configPath);
            Assert.Contains("retroClassic", File.ReadAllText(controlsPath), StringComparison.Ordinal);
        }
        finally { TestSandboxSupport.SafeDeleteDir(temp); }
    }

    [Fact]
    public void EngineResolver_RespectsR01Precedence_ReEngineDirBeatsSuiteDirBeatsBundled()
    {
        string temp = TestSandboxSupport.CreateTempDir("launcher_prec");
        try
        {
            using var envScope = new EnvVarScope(("APP_ISOLATED_TEST", "1"));
            string dataDir = Path.Combine(temp, "data");
            string baseDir = Path.Combine(temp, "game");
            string suiteRoot = Path.Combine(temp, "suite");
            string explicitRe = Path.Combine(temp, "explicit_re");

            string bundledExe = TestSandboxSupport.WriteDummyPlayerLeaf(Path.Combine(baseDir, "app", "engines"), "reCart", "0.1.3");
            string suiteExe = TestSandboxSupport.WriteDummyPlayerLeaf(Path.Combine(suiteRoot, "engines"), "reCart", "0.1.3");
            string explicitExe = TestSandboxSupport.WriteDummyPlayerLeaf(Path.Combine(explicitRe, "engines"), "reCart", "0.1.3");

            var env = new AppEnvironment("ironframe-outerfortress-cart-01", ["--data-dir", dataDir], baseDir);

            EngineSearchResult r1 = EngineResolver.ResolvePlayerExecutable(
                baseDir, LauncherCliAndContainment.ParseAndNormalize(["--reengine-dir", explicitRe, "--suite-dir", suiteRoot, "--data-dir", dataDir], temp), env);
            Assert.Equal(explicitExe, r1.PlayerExecutablePath);

            EngineSearchResult r2 = EngineResolver.ResolvePlayerExecutable(
                baseDir, LauncherCliAndContainment.ParseAndNormalize(["--suite-dir", suiteRoot, "--data-dir", dataDir], temp), env);
            Assert.Equal(suiteExe, r2.PlayerExecutablePath);

            EngineSearchResult r3 = EngineResolver.ResolvePlayerExecutable(
                baseDir, LauncherCliAndContainment.ParseAndNormalize(["--data-dir", dataDir], temp), env);
            Assert.Equal(bundledExe, r3.PlayerExecutablePath);
        }
        finally { TestSandboxSupport.SafeDeleteDir(temp); }
    }

    [Fact]
    public void EngineResolver_PrefersHigherCompatiblePatchVersionAndCanonicalAlias()
    {
        string temp = TestSandboxSupport.CreateTempDir("launcher_ver");
        try
        {
            using var envScope = new EnvVarScope(("APP_ISOLATED_TEST", "1"));
            string dataDir = Path.Combine(temp, "data");
            string baseDir = Path.Combine(temp, "game");
            string enginesDir = Path.Combine(baseDir, "engines");

            _ = TestSandboxSupport.WriteDummyPlayerLeaf(enginesDir, "reCart", "0.1.3");
            string newerExe = TestSandboxSupport.WriteDummyPlayerLeaf(enginesDir, "reCart", "0.1.4");

            var env = new AppEnvironment("ironframe-outerfortress-cart-01", ["--data-dir", dataDir], baseDir);
            EngineSearchResult res = EngineResolver.ResolvePlayerExecutable(
                baseDir, LauncherCliAndContainment.ParseAndNormalize(["--data-dir", dataDir], temp), env);

            Assert.Equal(newerExe, res.PlayerExecutablePath);
        }
        finally { TestSandboxSupport.SafeDeleteDir(temp); }
    }

    [Fact]
    public void EngineResolver_RefusesSelfSpawnAndReturnsThreePartRemediationWhenMissing()
    {
        string temp = TestSandboxSupport.CreateTempDir("launcher_missing");
        try
        {
            using var envScope = new EnvVarScope(("APP_ISOLATED_TEST", "1"));
            string dataDir = Path.Combine(temp, "data");
            string baseDir = Path.Combine(temp, "game");
            string selfExe = TestSandboxSupport.WriteDummyPlayerLeaf(Path.Combine(baseDir, "engines"), "reCart", "0.1.3");

            var env = new AppEnvironment("ironframe-outerfortress-cart-01", ["--data-dir", dataDir], baseDir);
            EngineSearchResult res = EngineResolver.ResolvePlayerExecutable(
                baseDir, LauncherCliAndContainment.ParseAndNormalize(["--data-dir", dataDir], temp), env, selfProcessPath: selfExe);

            Assert.Null(res.PlayerExecutablePath);
            Assert.Contains("What happened:", res.DiagnosticMessage, StringComparison.Ordinal);
            Assert.Contains("Why:", res.DiagnosticMessage, StringComparison.Ordinal);
            Assert.Contains("How to fix:", res.DiagnosticMessage, StringComparison.Ordinal);
        }
        finally { TestSandboxSupport.SafeDeleteDir(temp); }
    }

    [Fact]
    public void BuildPlayerArguments_ForwardsOpenAndFourCanonicalRuntimePaths()
    {
        string temp = TestSandboxSupport.CreateTempDir("launcher_args");
        try
        {
            using var envScope = new EnvVarScope(("APP_ISOLATED_TEST", "1"));
            string baseDir = Path.Combine(temp, "game");
            string dataDir = Path.Combine(temp, "data");
            string saveDir = Path.Combine(dataDir, "saves");
            string configPath = Path.Combine(dataDir, "config", "settings.json");
            string logDir = Path.Combine(temp, "logs");

            var env = new AppEnvironment("ironframe-outerfortress-cart-01",
                ["--data-dir", dataDir, "--config", configPath, "--log-dir", logDir], baseDir);

            LauncherCliOptions opts = LauncherCliAndContainment.ParseAndNormalize([
                "--data-dir", dataDir, "--config", configPath, "--log-dir", logDir,
                "--save-dir", saveDir, "--module", "app/cart.recomp", "--verify"
            ], temp);

            string? openPath = LauncherProgram.ResolveEffectiveOpenPath(opts, baseDir);
            string[] forwarded = LauncherProgram.BuildPlayerArguments(opts, env, saveDir, openPath);

            Assert.Equal("--module", forwarded[0]);
            Assert.Contains("--data-dir", forwarded);
            Assert.Contains("--config", forwarded);
            Assert.Contains("--log-dir", forwarded);
            Assert.Contains("--save-dir", forwarded);
            Assert.Contains("--verify", forwarded);
        }
        finally { TestSandboxSupport.SafeDeleteDir(temp); }
    }

    [Fact]
    public void RunLauncher_WithTestSeamForwardsResolvedPlayerWithoutRealProcessSpawn()
    {
        string temp = TestSandboxSupport.CreateTempDir("launcher_seam");
        try
        {
            using var envScope = new EnvVarScope(("APP_ISOLATED_TEST", "1"));
            string baseDir = Path.Combine(temp, "game");
            string dataDir = Path.Combine(temp, "data");
            string playerExe = TestSandboxSupport.WriteDummyPlayerLeaf(Path.Combine(baseDir, "app", "engines"), "reCart", "0.1.3");

            ProcessStartInfo? captured = null;
            LauncherProgram.LaunchSeamForTests = psi => { captured = psi; return 42; };

            int verifyExit = LauncherProgram.Run(["--data-dir", dataDir, "--verify"], baseDirOverride: baseDir, callerWorkingDirOverride: temp);
            Assert.Equal(42, verifyExit);
            Assert.NotNull(captured);
            Assert.Equal(playerExe, captured!.FileName);
            Assert.Contains("--verify", captured.ArgumentList);
            Assert.False(File.Exists(Path.Combine(dataDir, "config", "settings.json")));

            int launchExit = LauncherProgram.Run(["--data-dir", dataDir, "--wait"], baseDirOverride: baseDir, callerWorkingDirOverride: temp);
            Assert.Equal(42, launchExit);
            Assert.Contains("--preload-media", captured.ArgumentList);
            Assert.True(File.Exists(Path.Combine(dataDir, "config", "settings.json")));
        }
        finally
        {
            LauncherProgram.LaunchSeamForTests = null;
            TestSandboxSupport.SafeDeleteDir(temp);
        }
    }

    [Fact]
    public void LauncherMediaDiscovery_ResolvesVerifiedDataRomRejectsMismatchAndRoutesMissingToGate()
    {
        string temp = TestSandboxSupport.CreateTempDir("launcher_media_disc");
        try
        {
            using var envScope = new EnvVarScope(("APP_ISOLATED_TEST", "1"));
            string baseDir = Path.Combine(temp, "suite", "games", "IronFrame-OuterFortress-Cart-01");
            string appDir = Path.Combine(baseDir, "app");
            string dataDir = Path.Combine(baseDir, "data");
            string romDir = Path.Combine(dataDir, "rom");
            Directory.CreateDirectory(appDir);
            Directory.CreateDirectory(romDir);

            SyntheticCartMedia media = TestSandboxSupport.BuildSyntheticUxRomMedia();
            string recompPath = Path.Combine(appDir, "IronFrame-OuterFortress-Cart-01.recomp");
            IronFrameCart01.Module.Cart01RecipeLoader.PackageCleanRoomArchive(
                media.Manifest,
                new CartAotRecipeSpec { ModuleId = media.Manifest.ModuleId, MapperId = media.Manifest.MapperId, EntryAddresses = ["0xC001"] },
                recompPath);

            var env = new AppEnvironment("ironframe-outerfortress-cart-01", ["--data-dir", dataDir], baseDir);
            LauncherCliOptions opts = LauncherCliAndContainment.ParseAndNormalize(["--data-dir", dataDir], temp);

            // 1. Missing ROM -> forwards --preload-media (never silently falls back to demo grid)
            string[] missingArgs = LauncherMediaDiscovery.ApplyPreFlightRomDiscovery(opts, ["--open", recompPath], env, baseDir, recompPath);
            Assert.Contains("--preload-media", missingArgs);
            Assert.DoesNotContain("--rom", missingArgs);

            // 2. Wrong SHA-256 ROM in data\rom\ -> rejected and still forwards --preload-media
            byte[] wrongBytes = (byte[])media.NesBytes.Clone();
            wrongBytes[48] ^= 0x5A;
            string wrongPath = Path.Combine(romDir, "wrong.nes");
            File.WriteAllBytes(wrongPath, wrongBytes);
            string[] wrongArgs = LauncherMediaDiscovery.ApplyPreFlightRomDiscovery(opts, ["--open", recompPath], env, baseDir, recompPath);
            Assert.Contains("--preload-media", wrongArgs);
            Assert.DoesNotContain("--rom", wrongArgs);
            File.Delete(wrongPath);

            // 3. Valid SHA-256 ROM in data\rom\ -> auto-discovered and forwards --rom <path>
            string validPath = Path.Combine(romDir, "NES-ME-USA.nes");
            File.WriteAllBytes(validPath, media.NesBytes);
            string[] validArgs = LauncherMediaDiscovery.ApplyPreFlightRomDiscovery(opts, ["--open", recompPath], env, baseDir, recompPath);
            int romIdx = Array.IndexOf(validArgs, "--rom");
            Assert.True(romIdx >= 0 && romIdx + 1 < validArgs.Length);
            Assert.Equal(Path.GetFullPath(validPath), Path.GetFullPath(validArgs[romIdx + 1]));
        }
        finally { TestSandboxSupport.SafeDeleteDir(temp); }
    }

    [Fact]
    public void LauncherMediaDiscovery_RespectsIsolatedContainmentButDiscoversSiblingSuiteRomWhenUnisolated()
    {
        string temp = TestSandboxSupport.CreateTempDir("launcher_suite_rom");
        try
        {
            string suiteDir = Path.Combine(temp, "StreamItAll", "reEngine");
            string baseDir = Path.Combine(suiteDir, "games", "IronFrame-OuterFortress-Cart-01");
            string siblingRomDir = Path.Combine(suiteDir, "games", "LegacyCartSibling", "data", "rom");
            string appDir = Path.Combine(baseDir, "app");
            string dataDir = Path.Combine(baseDir, "data");
            Directory.CreateDirectory(appDir);
            Directory.CreateDirectory(Path.Combine(dataDir, "rom"));
            Directory.CreateDirectory(siblingRomDir);

            SyntheticCartMedia media = TestSandboxSupport.BuildSyntheticUxRomMedia();
            string recompPath = Path.Combine(appDir, "IronFrame-OuterFortress-Cart-01.recomp");
            IronFrameCart01.Module.Cart01RecipeLoader.PackageCleanRoomArchive(
                media.Manifest,
                new CartAotRecipeSpec { ModuleId = media.Manifest.ModuleId, MapperId = media.Manifest.MapperId, EntryAddresses = ["0xC001"] },
                recompPath);

            string siblingRom = Path.Combine(siblingRomDir, "IronFrame_Cart01.nes");
            File.WriteAllBytes(siblingRom, media.NesBytes);

            // Under APP_ISOLATED_TEST=1, sibling suite search is suppressed
            using (new EnvVarScope(("APP_ISOLATED_TEST", "1"), ("FOUNDATION_SANDBOX_ROOT", temp)))
            {
                var isoEnv = new AppEnvironment("ironframe-outerfortress-cart-01", ["--data-dir", dataDir], baseDir);
                LauncherCliOptions isoOpts = LauncherCliAndContainment.ParseAndNormalize(["--data-dir", dataDir], temp);
                string[] isoArgs = LauncherMediaDiscovery.ApplyPreFlightRomDiscovery(isoOpts, [], isoEnv, baseDir, recompPath);
                Assert.Contains("--preload-media", isoArgs);
                Assert.DoesNotContain("--rom", isoArgs);
            }

            // Inside Stage 2 sandbox (APP_ISOLATED_TEST unset, FOUNDATION_SANDBOX_ROOT=temp), exact SHA-256 sibling ROM IS discovered cleanly
            using (new EnvVarScope(("APP_ISOLATED_TEST", null), ("FOUNDATION_SANDBOX_ROOT", temp)))
            {
                var sbEnv = new AppEnvironment("ironframe-outerfortress-cart-01", ["--data-dir", dataDir], baseDir);
                LauncherCliOptions sbOpts = LauncherCliAndContainment.ParseAndNormalize(["--data-dir", dataDir, "--suite-dir", suiteDir], temp);
                string[] sbArgs = LauncherMediaDiscovery.ApplyPreFlightRomDiscovery(sbOpts, [], sbEnv, baseDir, recompPath);
                int romIdx = Array.IndexOf(sbArgs, "--rom");
                Assert.True(romIdx >= 0 && romIdx + 1 < sbArgs.Length);
                Assert.Equal(Path.GetFullPath(siblingRom), Path.GetFullPath(sbArgs[romIdx + 1]));
            }
        }
        finally { TestSandboxSupport.SafeDeleteDir(temp); }
    }
}

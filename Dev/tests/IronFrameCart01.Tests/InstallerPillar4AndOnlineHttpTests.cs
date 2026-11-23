// @module: IronFrameCart01.Tests.InstallerPillar4AndOnlineHttpTests
// @purpose: Verifies Installer Release ExitCode=2 gate, Pillar 4 install/upgrade ROM+settings preservation, foreign ownership rejection, HTTP 200/404/tamper, offline-bundle, and full-profile bootstrapper.
// @depends-on: System, System.Collections.Generic, System.IO, System.IO.Compression, System.Net, System.Text, System.Text.Json, System.Threading.Tasks, Xunit, Foundation.Installer, IronFrameCart01.Installer
// @exports: InstallerPillar4AndOnlineHttpTests

using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Foundation.Core;
using Foundation.Installer;
using IronFrameCart01.Installer;
using reCart.Contracts;
using Xunit;
using InstallerProgram = IronFrameCart01.Installer.Program;

namespace IronFrameCart01.Tests;

[Collection("IsolatedEnvSerial")]
public sealed class InstallerPillar4AndOnlineHttpTests
{
    [Fact]
    public void InstallerCli_RejectsAutomationPortInReleaseBuildsWithExitCode2()
    {
#if DEBUG || IRONFRAME_TEST_AUTOMATION
        Assert.True(InstallerArgsAndContainment.HasAutomationPortFlag(["--automation-port=5110"]));
#else
        int exit = InstallerProgram.RunMain(["--automation-port", "5110"]);
        Assert.Equal(2, exit);
#endif
    }

    [Fact]
    public void InstallerCli_ValidatesProfileValuesAndIsolatedBoundary()
    {
        Assert.Throws<ArgumentException>(() => InstallerArgsAndContainment.ExtractHostArgs(["--profile", "bogus"]));
        Assert.Throws<ArgumentException>(() => InstallerArgsAndContainment.ExtractHostArgs(["--automation-port", "99999"]));

        string temp = TestSandboxSupport.CreateTempDir("inst_bound");
        string outside = TestSandboxSupport.CreateTempDir("inst_outside");
        try
        {
            using var env = new EnvVarScope(("APP_ISOLATED_TEST", "1"));
            int exit = InstallerProgram.RunMain([
                "--silent", "--no-launch", "--no-shortcuts",
                "--data-dir", Path.Combine(temp, "data"),
                "--install-dir", Path.Combine(outside, InstallerArgsAndContainment.ProductName)
            ]);
            Assert.Equal(Foundation.Core.SandboxEnvironmentSeam.ContainmentExitCode, exit);
        }
        finally
        {
            TestSandboxSupport.SafeDeleteDir(temp);
            TestSandboxSupport.SafeDeleteDir(outside);
        }
    }

    [Fact]
    public void OfflineSilentInstall_UpgradePreservesByoRomAndCustomSettings_AndUninstallCleansUp()
    {
        string temp = TestSandboxSupport.CreateTempDir("inst_upgrade");
        try
        {
            using var env = new EnvVarScope(("APP_ISOLATED_TEST", "1"));
            string devRoot = TestSandboxSupport.ResolveDevRoot();
            string payloadZip = Path.Combine(temp, "payload.zip");
            _ = TestSandboxSupport.CreateSyntheticInstallerPayloadZip(payloadZip, devRoot);

            string dataDir = Path.Combine(temp, "data");
            string installDir = Path.Combine(temp, "install", InstallerArgsAndContainment.ProductName);

            InstallerProgram.DescriptorFactoryForTests = () => CreateOfflineDescriptor(payloadZip);
            int installExit = InstallerProgram.RunMain([
                "--silent", "--no-launch", "--no-shortcuts", "--profile", "lightweight",
                "--payload", payloadZip, "--install-dir", installDir, "--data-dir", dataDir
            ]);

            Assert.Equal(0, installExit);
            Assert.True(File.Exists(Path.Combine(installDir, InstallerArgsAndContainment.MainExecutable)));
            Assert.True(File.Exists(Path.Combine(installDir, ".ready")));
            Assert.True(File.Exists(Path.Combine(dataDir, "rom", ".keep")));

            // Seed user BYO dump placeholder + custom settings prior to upgrade.
            string romFile = Path.Combine(dataDir, "rom", "custom_user.prg");
            byte[] romContent = Encoding.ASCII.GetBytes("USER-OWNED-PRG-PLACEHOLDER");
            File.WriteAllBytes(romFile, romContent);
            string cfgFile = Path.Combine(dataDir, "config", "settings.json");
            JsonObject cfg = (JsonObject)JsonNode.Parse(File.ReadAllText(cfgFile))!;
            cfg["customUserPref"] = "keep-across-upgrade";
            File.WriteAllText(cfgFile, cfg.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            int upgradeExit = InstallerProgram.RunMain([
                "--silent", "--no-launch", "--no-shortcuts", "--profile", "lightweight",
                "--payload", payloadZip, "--install-dir", installDir, "--data-dir", dataDir
            ]);

            Assert.Equal(0, upgradeExit);
            Assert.True(File.Exists(romFile));
            Assert.Equal(romContent, File.ReadAllBytes(romFile));
            JsonObject afterCfg = (JsonObject)JsonNode.Parse(File.ReadAllText(cfgFile))!;
            Assert.Equal("keep-across-upgrade", afterCfg["customUserPref"]?.GetValue<string>());

            int uninstallExit = InstallerProgram.RunMain([
                "--uninstall", "--silent", "--install-dir", installDir, "--data-dir", dataDir
            ]);
            Assert.Equal(0, uninstallExit);
            Assert.False(File.Exists(Path.Combine(installDir, ".ready")));
            Assert.True(File.Exists(romFile));
        }
        finally
        {
            ResetInstallerTestSeams();
            TestSandboxSupport.SafeDeleteDir(temp);
        }
    }

    [Fact]
    public void InstallerEngine_RejectsForeignProductOwnedDirectory()
    {
        string temp = TestSandboxSupport.CreateTempDir("inst_foreign");
        try
        {
            using var env = new EnvVarScope(("APP_ISOLATED_TEST", "1"));
            string devRoot = TestSandboxSupport.ResolveDevRoot();
            string payloadZip = Path.Combine(temp, "payload.zip");
            _ = TestSandboxSupport.CreateSyntheticInstallerPayloadZip(payloadZip, devRoot);

            string dataDir = Path.Combine(temp, "data");
            string installDir = Path.Combine(temp, "foreign_app");
            Directory.CreateDirectory(installDir);
            File.WriteAllText(Path.Combine(installDir, "foreign.txt"), "hands-off");
            File.WriteAllText(Path.Combine(installDir, "install-manifest.json"), """
            {
              "product_name": "CompletelyDifferentProduct",
              "version": "1.0.0",
              "install_directory": "foreign_app",
              "payload_sha256": "abc",
              "installed_relative_files": ["foreign.txt"]
            }
            """);

            InstallerProgram.DescriptorFactoryForTests = () => CreateOfflineDescriptor(payloadZip);
            int exit = InstallerProgram.RunMain([
                "--silent", "--no-launch", "--no-shortcuts",
                "--payload", payloadZip, "--install-dir", installDir, "--data-dir", dataDir
            ]);

            Assert.NotEqual(0, exit);
            Assert.True(File.Exists(Path.Combine(installDir, "foreign.txt")));
            Assert.False(File.Exists(Path.Combine(installDir, ".ready")));
        }
        finally
        {
            ResetInstallerTestSeams();
            TestSandboxSupport.SafeDeleteDir(temp);
        }
    }

    [Fact]
    public async Task OnlineSetup_Http200ValidSha256_SucceedsAcrossLoopbackFixtureAsync()
    {
        string temp = TestSandboxSupport.CreateTempDir("inst_http_200");
        using var listener = new HttpListener();
        try
        {
            using var env = new EnvVarScope(("APP_ISOLATED_TEST", "1"));
            string devRoot = TestSandboxSupport.ResolveDevRoot();
            const string assetName = "IronFrame-OuterFortress-Cart-01-v0.1.0-win-x64-Portable.zip";
            (string zipPath, string sha, long size) = TestSandboxSupport.CreateSyntheticInstallerPayloadZip(Path.Combine(temp, assetName), devRoot);
            byte[] zipBytes = File.ReadAllBytes(zipPath);

            int port = TestSandboxSupport.ReserveFreeLoopbackPort();
            string prefix = $"http://127.0.0.1:{port}/";
            listener.Prefixes.Add(prefix);
            listener.Start();
            Task serverTask = ServeSingleFileAsync(listener, $"/{assetName}", 200, zipBytes);

            string dataDir = Path.Combine(temp, "data");
            string installDir = Path.Combine(temp, "installed", InstallerArgsAndContainment.ProductName);
            InstallerProgram.AllowLoopbackHttpForTesting = true;
            InstallerProgram.DescriptorFactoryForTests = () => CreateOnlineDescriptor(prefix, assetName, size, sha);

            int exit = InstallerProgram.RunMain([
                "--silent", "--no-launch", "--no-shortcuts", "--profile", "lightweight",
                "--install-dir", installDir, "--data-dir", dataDir
            ]);
            await serverTask;

            Assert.Equal(0, exit);
            Assert.True(File.Exists(Path.Combine(installDir, InstallerArgsAndContainment.MainExecutable)));
            Assert.True(File.Exists(Path.Combine(installDir, "app", InstallerArgsAndContainment.ModuleArchiveName)));
        }
        finally
        {
            ResetInstallerTestSeams();
            TestSandboxSupport.SafeDeleteDir(temp);
        }
    }

    [Fact]
    public async Task OnlineSetup_Http404_FailsClosedWithoutPartialInstallAsync()
    {
        string temp = TestSandboxSupport.CreateTempDir("inst_http_404");
        using var listener = new HttpListener();
        try
        {
            using var env = new EnvVarScope(("APP_ISOLATED_TEST", "1"));
            const string assetName = "IronFrame-OuterFortress-Cart-01-v0.1.0-win-x64-Portable.zip";
            int port = TestSandboxSupport.ReserveFreeLoopbackPort();
            string prefix = $"http://127.0.0.1:{port}/";
            listener.Prefixes.Add(prefix);
            listener.Start();
            Task serverTask = ServeSingleFileAsync(listener, $"/{assetName}", 404, Array.Empty<byte>());

            string installDir = Path.Combine(temp, "installed", InstallerArgsAndContainment.ProductName);
            InstallerProgram.AllowLoopbackHttpForTesting = true;
            InstallerProgram.DescriptorFactoryForTests = () => CreateOnlineDescriptor(prefix, assetName, 128, new string('a', 64));

            int exit = InstallerProgram.RunMain([
                "--silent", "--no-launch", "--no-shortcuts", "--profile", "lightweight",
                "--install-dir", installDir, "--data-dir", Path.Combine(temp, "data")
            ]);
            await serverTask;

            Assert.NotEqual(0, exit);
            Assert.False(File.Exists(Path.Combine(installDir, ".ready")));
        }
        finally
        {
            ResetInstallerTestSeams();
            TestSandboxSupport.SafeDeleteDir(temp);
        }
    }

    [Fact]
    public async Task OnlineSetup_TamperedSha256_FailsClosedAndAbortsExtractionAsync()
    {
        string temp = TestSandboxSupport.CreateTempDir("inst_http_tamper");
        using var listener = new HttpListener();
        try
        {
            using var env = new EnvVarScope(("APP_ISOLATED_TEST", "1"));
            string devRoot = TestSandboxSupport.ResolveDevRoot();
            const string assetName = "IronFrame-OuterFortress-Cart-01-v0.1.0-win-x64-Portable.zip";
            (string zipPath, _, long size) = TestSandboxSupport.CreateSyntheticInstallerPayloadZip(Path.Combine(temp, assetName), devRoot);
            byte[] zipBytes = File.ReadAllBytes(zipPath);

            int port = TestSandboxSupport.ReserveFreeLoopbackPort();
            string prefix = $"http://127.0.0.1:{port}/";
            listener.Prefixes.Add(prefix);
            listener.Start();
            Task serverTask = ServeSingleFileAsync(listener, $"/{assetName}", 200, zipBytes);

            string installDir = Path.Combine(temp, "installed", InstallerArgsAndContainment.ProductName);
            InstallerProgram.AllowLoopbackHttpForTesting = true;
            InstallerProgram.DescriptorFactoryForTests = () => CreateOnlineDescriptor(prefix, assetName, size, new string('f', 64));

            int exit = InstallerProgram.RunMain([
                "--silent", "--no-launch", "--no-shortcuts", "--profile", "lightweight",
                "--install-dir", installDir, "--data-dir", Path.Combine(temp, "data")
            ]);
            await serverTask;

            Assert.NotEqual(0, exit);
            Assert.False(File.Exists(Path.Combine(installDir, InstallerArgsAndContainment.MainExecutable)));
        }
        finally
        {
            ResetInstallerTestSeams();
            TestSandboxSupport.SafeDeleteDir(temp);
        }
    }

    [Fact]
    public void OnlineSetup_WithOfflineBundleFlag_InstallsWithZeroNetworkCalls()
    {
        string temp = TestSandboxSupport.CreateTempDir("inst_offline_bundle");
        try
        {
            using var env = new EnvVarScope(("APP_ISOLATED_TEST", "1"));
            string devRoot = TestSandboxSupport.ResolveDevRoot();
            const string assetName = "IronFrame-OuterFortress-Cart-01-v0.1.0-win-x64-Portable.zip";
            (string bundlePath, string sha, long size) = TestSandboxSupport.CreateSyntheticInstallerPayloadZip(Path.Combine(temp, assetName), devRoot);

            string installDir = Path.Combine(temp, "installed", InstallerArgsAndContainment.ProductName);
            string dataDir = Path.Combine(temp, "data");
            InstallerProgram.DescriptorFactoryForTests = () => CreateOnlineDescriptor("http://127.0.0.1:1/", assetName, size, sha);

            int exit = InstallerProgram.RunMain([
                "--silent", "--no-launch", "--no-shortcuts", "--profile", "lightweight",
                "--offline-bundle", bundlePath, "--install-dir", installDir, "--data-dir", dataDir
            ]);

            Assert.Equal(0, exit);
            Assert.True(File.Exists(Path.Combine(installDir, InstallerArgsAndContainment.MainExecutable)));
            Assert.True(File.Exists(Path.Combine(installDir, ".ready")));
        }
        finally
        {
            ResetInstallerTestSeams();
            TestSandboxSupport.SafeDeleteDir(temp);
        }
    }

    [Fact]
    public void VerifyOnly_WithOfflineBundle_ValidatesSha256ZeroNetwork()
    {
        string temp = TestSandboxSupport.CreateTempDir("inst_verify_bundle");
        try
        {
            using var env = new EnvVarScope(("APP_ISOLATED_TEST", "1"));
            string devRoot = TestSandboxSupport.ResolveDevRoot();
            const string assetName = "IronFrame-OuterFortress-Cart-01-v0.1.0-win-x64-Portable.zip";
            (string bundlePath, string sha, long size) = TestSandboxSupport.CreateSyntheticInstallerPayloadZip(Path.Combine(temp, assetName), devRoot);

            InstallerProgram.DescriptorFactoryForTests = () => CreateOnlineDescriptor("https://unreachable.invalid/releases/v0.1.0/", assetName, size, sha);
            int okExit = InstallerProgram.RunMain(["--verify", "--offline-bundle", bundlePath, "--data-dir", Path.Combine(temp, "data")]);
            Assert.Equal(0, okExit);
        }
        finally
        {
            ResetInstallerTestSeams();
            TestSandboxSupport.SafeDeleteDir(temp);
        }
    }

    [Fact]
    public async Task InstallerBootstrapHelper_ExtractsGzipSetupAndForwardsSuiteFlagsInFullProfileAsync()
    {
        string temp = TestSandboxSupport.CreateTempDir("inst_bootstrap");
        try
        {
            using var env = new EnvVarScope(("APP_ISOLATED_TEST", "1"));
            string dataDir = Path.Combine(temp, "data");
            string suiteRoot = Path.Combine(temp, "reEngine");
            string gameDir = Path.Combine(suiteRoot, "games", InstallerArgsAndContainment.ProductName);
            string reDir = suiteRoot;

            var invocations = new List<(string Exe, string[] Args)>();
            InstallerBootstrapHelper.ResourceStreamOpenerForTests = _ =>
            {
                var ms = new MemoryStream();
                using (var gz = new GZipStream(ms, CompressionMode.Compress, leaveOpen: true))
                    gz.Write(Encoding.ASCII.GetBytes("MZ-SYNTHETIC-BOOTSTRAP"));
                ms.Position = 0;
                return ms;
            };
            InstallerBootstrapHelper.ExternalBootstrapRunnerForTests = (exe, args, _) =>
            {
                invocations.Add((Path.GetFileName(exe), args));
                return Task.FromResult(0);
            };

            var host = InstallerArgsAndContainment.ExtractHostArgs(["--profile", "full", "--suite-dir", suiteRoot, "--data-dir", dataDir]);
            var ctx = new InstallerRuntimeContext(dataDir, null, null, dryRun: false, "IRONFRAME_OUTERFORTRESS_CART_01");
            var paths = new ResolvedInstallPaths(gameDir, suiteRoot, reDir);

            BootstrapOutcome outcome = await InstallerBootstrapHelper.EnsureFullProfileBootstrappedAsync(host, ctx, paths, "full", suiteRoot, null);
            Assert.True(outcome.Success, outcome.Message);
            Assert.Equal(2, invocations.Count);
            Assert.Equal("reEngine-Setup.exe", invocations[0].Exe);
            Assert.Equal("reCart-Setup.exe", invocations[1].Exe);
            Assert.Contains("--silent", invocations[0].Args);
            Assert.Equal(suiteRoot, ReadArgValue(invocations[0].Args, "--install-dir"));
            Assert.Equal(Path.Combine(suiteRoot, "engines", "reCart", $"v{CartEngineConstants.Version}"), ReadArgValue(invocations[1].Args, "--install-dir"));
            Assert.Contains("--suite-dir", invocations[1].Args);
        }
        finally
        {
            ResetInstallerTestSeams();
            TestSandboxSupport.SafeDeleteDir(temp);
        }
    }

    [Fact]
    public void InstallerPathResolver_SuiteFirstResolution_PrefersExistingReEngineAndNeverDoubleNests()
    {
        string temp = TestSandboxSupport.CreateTempDir("inst_suite_first");
        try
        {
            using var env = new EnvVarScope(
                ("APP_ISOLATED_TEST", null),
                (SandboxEnvironmentSeam.EnvSandboxRoot, null),
                ("REENGINE_HOME", null));
            string sbC = Path.Combine(temp, "C");
            string sbD = Path.Combine(temp, "D");
            string sbProf = Path.Combine(sbC, "Users", "Sandbox");
            string sbSuite = Path.Combine(sbD, "CustomGames", "StreamItAll", "reEngine");
            Directory.CreateDirectory(sbSuite);
            Directory.CreateDirectory(Path.Combine(temp, "registry"));
            Directory.CreateDirectory(Path.Combine(temp, "locks"));

            string hivePath = Path.Combine(temp, "registry", "registry.hive.json");
            var reg = new JsonHivePlatformRegistry(hivePath, temp);
            reg.SetValue(PlatformRegistryHive.CurrentUser, InstallerPathResolver.ReEngineUninstallSubKey, "InstallLocation", sbSuite);
            reg.SetValue(PlatformRegistryHive.CurrentUser, InstallerEngine.SharedStreamItAllRegistrySubKey, InstallerEngine.LastInstallBaseDirValueName, Path.Combine(sbD, "Stale", "StreamItAll", "reEngine"));

            var sandbox = SandboxEnvironmentSeam.Resolve(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                [SandboxEnvironmentSeam.EnvSandboxRoot] = temp,
                [SandboxEnvironmentSeam.EnvSandboxCDrive] = sbC,
                [SandboxEnvironmentSeam.EnvSandboxDDrive] = sbD,
                [SandboxEnvironmentSeam.EnvSandboxProfileDir] = sbProf
            });
            Assert.True(sandbox.IsSandboxActive);
            var ctx = new InstallerRuntimeContext(null, null, null, false, "IRONFRAME_OUTERFORTRESS_CART_01", sandbox, reg);
            var host = InstallerArgsAndContainment.ExtractHostArgs(["--profile", "full"]);
            var desc = InstallerProgram.CreateDescriptor();

            ResolvedInstallPaths resolved = InstallerPathResolver.ResolveInitialPaths(host, ctx, desc);
            Assert.Equal(Path.Combine(sbSuite, "games", InstallerArgsAndContainment.ProductName), resolved.GameInstallDir);
            Assert.Equal(sbSuite, resolved.SuiteRootDir);
            Assert.Equal(sbSuite, resolved.ReEngineInstallDir);
            Assert.DoesNotContain(@"reEngine\reEngine", resolved.ReEngineInstallDir, StringComparison.OrdinalIgnoreCase);

            string misnested = Path.Combine(sbSuite, "reEngine", "engines", "reCart", InstallerArgsAndContainment.ProductName);
            Assert.True(InstallerPathResolver.TryCollapseUmbrellaNesting(misnested, out string collapsed));
            Assert.Equal(resolved.GameInstallDir, collapsed);

            Assert.True(InstallerPathResolver.TryRepairLastInstallBaseDir(reg, resolved.GameInstallDir, sandbox, suppressHostMutations: false));
            Assert.Equal(Path.Combine(sbD, "CustomGames"),
                reg.GetValue(PlatformRegistryHive.CurrentUser, InstallerEngine.SharedStreamItAllRegistrySubKey, InstallerEngine.LastInstallBaseDirValueName)?.AsString());
        }
        finally
        {
            ResetInstallerTestSeams();
            TestSandboxSupport.SafeDeleteDir(temp);
        }
    }

    [Fact]
    public async Task InstallerBootstrapHelper_FullProfile_InstallsReEngineDirectlyAtSuiteRootWithoutNesting()
    {
        string temp = TestSandboxSupport.CreateTempDir("inst_full_no_nest");
        try
        {
            using var env = new EnvVarScope(("APP_ISOLATED_TEST", "1"));
            string dataDir = Path.Combine(temp, "data");
            string canonicalBase = Path.Combine(temp, "StreamItAll", "reEngine");

            var invocations = new List<(string Exe, string[] Args)>();
            InstallerBootstrapHelper.ResourceStreamOpenerForTests = _ =>
            {
                var ms = new MemoryStream();
                using (var gz = new GZipStream(ms, CompressionMode.Compress, leaveOpen: true))
                    gz.Write(Encoding.ASCII.GetBytes("MZ-SYNTHETIC-BOOTSTRAP"));
                ms.Position = 0;
                return ms;
            };
            InstallerBootstrapHelper.ExternalBootstrapRunnerForTests = (exe, args, _) =>
            {
                invocations.Add((Path.GetFileName(exe), args));
                return Task.FromResult(0);
            };

            var host = InstallerArgsAndContainment.ExtractHostArgs(["--profile", "full", "--reengine-dir", canonicalBase, "--data-dir", dataDir]);
            var ctx = new InstallerRuntimeContext(dataDir, null, null, dryRun: false, "IRONFRAME_OUTERFORTRESS_CART_01");
            ResolvedInstallPaths resolved = InstallerPathResolver.ResolveInitialPaths(host, ctx, InstallerProgram.CreateDescriptor());

            Assert.Equal(canonicalBase, resolved.SuiteRootDir);
            Assert.Equal(canonicalBase, resolved.ReEngineInstallDir);
            Assert.Equal(Path.Combine(canonicalBase, "games", InstallerArgsAndContainment.ProductName), resolved.GameInstallDir);

            BootstrapOutcome outcome = await InstallerBootstrapHelper.EnsureFullProfileBootstrappedAsync(host, ctx, resolved, "full", null, null);
            Assert.True(outcome.Success, outcome.Message);
            Assert.True(Directory.Exists(Path.Combine(canonicalBase, "engines")));
            Assert.Equal(2, invocations.Count);
            Assert.Equal(canonicalBase, ReadArgValue(invocations[0].Args, "--install-dir"));
            Assert.Equal(canonicalBase, ReadArgValue(invocations[1].Args, "--reengine-dir"));
            Assert.Equal(Path.Combine(canonicalBase, "engines", "reCart", $"v{CartEngineConstants.Version}"), ReadArgValue(invocations[1].Args, "--install-dir"));
        }
        finally
        {
            ResetInstallerTestSeams();
            TestSandboxSupport.SafeDeleteDir(temp);
        }
    }

    [Fact]
    public void OnlineSetup_AdjacentVerifiedPortableZip_ResolvesValidAndRejectsTampered()
    {
        string temp = TestSandboxSupport.CreateTempDir("inst_adj_portable");
        try
        {
            string devRoot = TestSandboxSupport.ResolveDevRoot();
            const string assetName = "IronFrame-OuterFortress-Cart-01-v0.1.0-win-x64-Portable.zip";
            string zipPath = Path.Combine(temp, assetName);
            (string _, string sha, long size) = TestSandboxSupport.CreateSyntheticInstallerPayloadZip(zipPath, devRoot);
            InstallerAppDescriptor desc = CreateOnlineDescriptor("https://example.invalid/rel/", assetName, size, sha);

            Assert.Null(InstallerProgram.TryResolveAdjacentVerifiedPortableZip(desc, Path.Combine(temp, "explicit.zip"), temp));
            Assert.Equal(Path.GetFullPath(zipPath), InstallerProgram.TryResolveAdjacentVerifiedPortableZip(desc, null, temp));

            InstallerAppDescriptor badHashDesc = CreateOnlineDescriptor("https://example.invalid/rel/", assetName, size, new string('0', 64));
            Assert.Null(InstallerProgram.TryResolveAdjacentVerifiedPortableZip(badHashDesc, null, temp));

            InstallerAppDescriptor badSizeDesc = CreateOnlineDescriptor("https://example.invalid/rel/", assetName, size + 1, sha);
            Assert.Null(InstallerProgram.TryResolveAdjacentVerifiedPortableZip(badSizeDesc, null, temp));
        }
        finally
        {
            TestSandboxSupport.SafeDeleteDir(temp);
        }
    }

    private static string? ReadArgValue(IReadOnlyList<string> args, string flag)
    {
        for (int i = 0; i < args.Count - 1; i++)
            if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }

    private static InstallerAppDescriptor CreateOfflineDescriptor(string payloadZipPath) => new(
        InstallerArgsAndContainment.ProductName,
        InstallerArgsAndContainment.DisplayName,
        InstallerArgsAndContainment.Publisher,
        "0.1.0",
        InstallerArgsAndContainment.MainExecutable,
        "Clean-Room Offline Test",
        EmbeddedPayloadResourceName: Path.GetFileName(payloadZipPath),
        AllowAutoStart: false,
        StudioName: InstallerArgsAndContainment.Publisher,
        SuiteName: "reEngine",
        UmbrellaSubpath: InstallerArgsAndContainment.UmbrellaSubpath,
        DefaultProfile: "lightweight",
        AllowLegacyMigration: false)
    {
        InstallerDeliveryKind = InstallerDeliveryKinds.OfflineSetup
    };

    private static InstallerAppDescriptor CreateOnlineDescriptor(string baseUrl, string assetName, long sizeBytes, string sha256) => new(
        InstallerArgsAndContainment.ProductName,
        InstallerArgsAndContainment.DisplayName,
        InstallerArgsAndContainment.Publisher,
        "0.1.0",
        InstallerArgsAndContainment.MainExecutable,
        "Clean-Room Online Test",
        EmbeddedPayloadResourceName: null,
        AllowAutoStart: false,
        StudioName: InstallerArgsAndContainment.Publisher,
        SuiteName: "reEngine",
        UmbrellaSubpath: InstallerArgsAndContainment.UmbrellaSubpath,
        DefaultProfile: "lightweight",
        AllowLegacyMigration: false)
    {
        InstallerDeliveryKind = InstallerDeliveryKinds.OnlineSetup,
        PayloadDownloadUrl = baseUrl,
        PayloadAssets = [new InstallerPayloadAsset("*", assetName, sizeBytes, sha256)]
    };

    private static async Task ServeSingleFileAsync(HttpListener listener, string expectedPath, int statusCode, byte[] body)
    {
        HttpListenerContext ctx = await listener.GetContextAsync().ConfigureAwait(false);
        ctx.Response.StatusCode = ctx.Request.Url?.AbsolutePath == expectedPath ? statusCode : 404;
        if (ctx.Response.StatusCode == 200)
        {
            ctx.Response.ContentType = "application/zip";
            ctx.Response.ContentLength64 = body.LongLength;
            await ctx.Response.OutputStream.WriteAsync(body).ConfigureAwait(false);
        }
        ctx.Response.Close();
    }

    private static void ResetInstallerTestSeams()
    {
        InstallerProgram.AllowLoopbackHttpForTesting = false;
        InstallerProgram.InstallerEngineFactoryForTests = null;
        InstallerProgram.DescriptorFactoryForTests = null;
        InstallerBootstrapHelper.ResourceStreamOpenerForTests = null;
        InstallerBootstrapHelper.ExternalBootstrapRunnerForTests = null;
    }
}

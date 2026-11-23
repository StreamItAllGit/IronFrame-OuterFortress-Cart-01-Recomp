// @module: IronFrameCart01.Installer.Program
// @purpose: 4-pillar Dual-Setup Windows installer host loading installer.manifest.json, preserving data\rom\ & settings, bootstrapping reEngine+reCart, and enforcing Release ExitCode=2.
// @depends-on: System, System.Diagnostics, System.Drawing, System.Globalization, System.IO, System.Linq, System.Reflection, System.Text, System.Text.Json, System.Threading, System.Threading.Tasks, System.Windows.Forms, Foundation.Core, Foundation.Installer
// @exports: Program

using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Foundation.Core;
using Foundation.Installer;

namespace IronFrameCart01.Installer;

internal static class Program
{
    internal const string IconResourceName = "IronFrameCart01.Installer.Assets.icon.ico";
    internal const string LogoResourceName = "IronFrameCart01.Installer.Assets.logo.png";
    private const string PayloadResourceName = "IronFrameCart01.Installer.Embedded.payload.zip";

    internal static bool AllowLoopbackHttpForTesting { get; set; }
    internal static Func<InstallerAppDescriptor, InstallerRuntimeContext, Assembly, InstallerEngine>? InstallerEngineFactoryForTests { get; set; }
    internal static Func<InstallerAppDescriptor>? DescriptorFactoryForTests { get; set; }

    [STAThread]
    private static int Main(string[] args) => RunMain(args ?? Array.Empty<string>());

    internal static int RunMain(string[] args)
    {
        args ??= Array.Empty<string>();
#if !DEBUG && !IRONFRAME_TEST_AUTOMATION
        if (InstallerArgsAndContainment.HasAutomationPortFlag(args))
        {
            InstallerArgsAndContainment.AttachParentConsoleIfInteractive(args);
            Console.Error.WriteLine("Automation loopback (--automation-port) is disabled in Release installer builds (ExitCode = 2).");
            return 2;
        }
#endif

        HostCliOptions host;
        try { host = InstallerArgsAndContainment.ExtractHostArgs(args); }
        catch (ArgumentException ex) { Console.Error.WriteLine($"Setup argument error: {ex.Message}"); return 2; }

        InstallerAppDescriptor descriptor = CreateDescriptor();
        if (host.ShowVersion || host.ShowHelp)
            return StandardInstallerHost.Run(descriptor, host.ForwardedArgs, typeof(Program).Assembly);

        try
        {
            InstallerCliOptions cli = InstallerEngine.ParseCliArgs(host.ForwardedArgs, descriptor);
            var ctx = new InstallerRuntimeContext(cli.DataDir, cli.ConfigPath, cli.LogDir, cli.DryRun, descriptor.EnvPrefix);
            InstallerArgsAndContainment.ValidateContainment(ctx, host);

            ResolvedInstallPaths initialPaths = InstallerArgsAndContainment.ResolveInitialPaths(host, ctx, descriptor, cli.InstallDir);
            string effectiveDataDir = InstallerArgsAndContainment.ResolveEffectiveDataDir(host, ctx, initialPaths.GameInstallDir);
            string selectedProfile = ResolveEffectiveProfile(host, effectiveDataDir);
            string? explicitOfflineBundle = cli.OfflineBundlePath ?? host.OfflineBundlePath;
            string? adjacentBundle = TryResolveAdjacentVerifiedPortableZip(descriptor, explicitOfflineBundle);
            string? effectiveOfflineBundle = explicitOfflineBundle ?? adjacentBundle;
            if (adjacentBundle is not null)
                ctx.WriteLog("INFO", $"Auto-detected verified adjacent portable bundle '{adjacentBundle}'.");

            bool forceAutomatedWizard =
#if DEBUG || IRONFRAME_TEST_AUTOMATION
                host.AutomationPort.HasValue;
#else
                false;
#endif
            bool interactiveGui = !host.Silent && !host.DryRun && !host.VerifyOnly &&
                                    ((!ctx.IsIsolatedTestProcess && string.IsNullOrWhiteSpace(cli.InstallDir)) || forceAutomatedWizard) &&
                                    OperatingSystem.IsWindows();
            if (interactiveGui)
                return InstallerWizardHost.RunInteractiveWizard(descriptor, ctx, host, cli, initialPaths, selectedProfile, effectiveOfflineBundle);

            return RunCliFlow(descriptor, ctx, host, cli, initialPaths, effectiveDataDir, selectedProfile, effectiveOfflineBundle);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Setup argument error: {ex.Message}");
            return 2;
        }
        catch (Exception ex) when (SandboxEnvironmentSeam.IsContainmentViolation(ex))
        {
            Console.Error.WriteLine(ex.Message);
            return SandboxEnvironmentSeam.ContainmentExitCode;
        }
    }

    internal static InstallerAppDescriptor CreateDescriptor()
    {
        if (DescriptorFactoryForTests is { } custom) return custom();

        string baseVer = ResolveAssemblyVersion();
        var baseDesc = new InstallerAppDescriptor(
            ProductName: InstallerArgsAndContainment.ProductName,
            DisplayName: InstallerArgsAndContainment.DisplayName,
            Publisher: InstallerArgsAndContainment.Publisher,
            Version: baseVer,
            MainExecutableRelativePath: InstallerArgsAndContainment.MainExecutable,
            Subtitle: "Clean-Room Static Recompilation Module (Cart 01) for reCart & reEngine",
            EmbeddedPayloadResourceName: PayloadResourceName,
            BrandAccentHex: "#F59E0B",
            AllowAutoStart: false,
            StudioName: InstallerArgsAndContainment.Publisher,
            SuiteName: "reEngine",
            UmbrellaSubpath: InstallerArgsAndContainment.UmbrellaSubpath,
            DefaultProfile: "lightweight",
            AllowLegacyMigration: false)
        {
            IconPath = IconResourceName,
            LogoPath = LogoResourceName,
            SurfaceBackdropHex = "#0E0E10",
            SurfaceCardHex = "#18181B",
            TextPrimaryHex = "#ECEFF4",
            BorderHex = "#3F3F46"
        };

        InstallerMarkupManifest? manifest = InstallerManifestStore.TryLoadFromDirectoryOrAssembly(AppContext.BaseDirectory, typeof(Program).Assembly);
        return manifest is not null
            ? InstallerManifestStore.ApplyToDescriptor(baseDesc, manifest) with
              {
                  AllowAutoStart = false,
                  AllowLegacyMigration = false,
                  SuiteName = "reEngine",
                  EmbeddedPayloadResourceName = PayloadResourceName,
                  IconPath = manifest.IconPath ?? IconResourceName,
                  LogoPath = manifest.LogoPath ?? LogoResourceName
              }
            : baseDesc;
    }

    internal static InstallerThemeOptions CreateThemeOptions(
        InstallerAppDescriptor desc, InstallerRuntimeContext ctx, string? secondarySuiteDir, string? secondaryFlag) =>
        InstallerWizardHost.CreateThemeOptions(desc, ctx, secondarySuiteDir, secondaryFlag);

    internal static string? TryResolveAdjacentVerifiedPortableZip(
        InstallerAppDescriptor descriptor, string? explicitOfflineBundle, string? searchDirOverride = null)
    {
        if (!descriptor.IsOnlineSetup || !string.IsNullOrWhiteSpace(explicitOfflineBundle)) return null;
        if (searchDirOverride is null && (AllowLoopbackHttpForTesting || DescriptorFactoryForTests is not null)) return null;
        if (descriptor.PayloadAssets is not { Count: 1 }) return null;

        InstallerPayloadAsset asset = descriptor.PayloadAssets[0];
        if (asset.ExpectedSizeBytes <= 0 || string.IsNullOrWhiteSpace(asset.ExpectedSha256)) return null;

        string? searchDir = !string.IsNullOrWhiteSpace(searchDirOverride)
            ? searchDirOverride
            : (!string.IsNullOrWhiteSpace(Environment.ProcessPath) ? Path.GetDirectoryName(Environment.ProcessPath) : AppContext.BaseDirectory);
        if (string.IsNullOrWhiteSpace(searchDir) || !Directory.Exists(searchDir)) return null;

        string candidate = Path.Combine(searchDir, Path.GetFileName(asset.FileName));
        if (!File.Exists(candidate)) return null;

        var info = new FileInfo(candidate);
        if (info.Length != asset.ExpectedSizeBytes) return null;

        using FileStream fs = File.OpenRead(candidate);
        string actualSha = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
        return string.Equals(actualSha, asset.ExpectedSha256.Trim(), StringComparison.OrdinalIgnoreCase)
            ? Path.GetFullPath(candidate)
            : null;
    }

    internal static InstallerEngine CreateInstallerEngine(
        InstallerAppDescriptor descriptor, InstallerRuntimeContext ctx, Assembly payloadAssembly)
    {
        InstallerEngine engine = InstallerEngineFactoryForTests?.Invoke(descriptor, ctx, payloadAssembly)
            ?? new InstallerEngine(descriptor, ctx, payloadAssembly);
        if (AllowLoopbackHttpForTesting) engine.AllowLoopbackHttpForTesting = true;
        return engine;
    }

    private static int RunCliFlow(
        InstallerAppDescriptor descriptor, InstallerRuntimeContext ctx, HostCliOptions host,
        InstallerCliOptions cli, ResolvedInstallPaths paths, string effectiveDataDir, string profileId, string? effectiveOfflineBundle)
    {
        if (host.VerifyOnly)
        {
            if (AllowLoopbackHttpForTesting || InstallerEngineFactoryForTests is not null || DescriptorFactoryForTests is not null)
            {
                var verify = CreateInstallerEngine(descriptor, ctx, typeof(Program).Assembly)
                    .VerifyPayloadAsync(cli.PayloadPath, effectiveOfflineBundle, profileId).GetAwaiter().GetResult();
                Console.WriteLine(verify.Message);
                return verify.Valid ? 0 : 1;
            }
            string[] verifyArgs = AppendFlagIfMissing(host.ForwardedArgs, "--offline-bundle", effectiveOfflineBundle);
            return StandardInstallerHost.Run(descriptor, verifyArgs, typeof(Program).Assembly);
        }

        if (host.Uninstall)
        {
            string[] unArgs = InstallerArgsAndContainment.ReplaceInstallDirArg(host.ForwardedArgs, paths.GameInstallDir);
            int unExit = StandardInstallerHost.Run(descriptor, unArgs, typeof(Program).Assembly);
            if (unExit == 0 && !host.DryRun) RemoveReadyMarkerIfPresent(paths.GameInstallDir);
            return unExit;
        }

        string effectiveSuite = paths.SuiteRootDir;
        string effectiveReDir = paths.ReEngineInstallDir;
        if (!host.DryRun && string.Equals(profileId, "full", StringComparison.OrdinalIgnoreCase))
        {
            BootstrapOutcome boot = InstallerBootstrapHelper.EnsureFullProfileBootstrappedAsync(
                host, ctx, paths, profileId, host.ReEngineDir ?? host.SuiteDir, null, CancellationToken.None).GetAwaiter().GetResult();
            if (!boot.Success) { ctx.WriteLog("ERROR", boot.Message); Console.Error.WriteLine(boot.Message); return 1; }
            effectiveSuite = boot.SuiteRootDir;
            effectiveReDir = boot.ReEngineInstallDir;
        }

        using PreUpgradePreservationSnapshot? snapshot = InstallerSettingsAndRomPreserver.CapturePreUpgradeSnapshot(effectiveDataDir, ctx);
        bool useCustomEngine = AllowLoopbackHttpForTesting || InstallerEngineFactoryForTests is not null || DescriptorFactoryForTests is not null;
        int exitCode;
        bool wasRunning = false;
        string installedDir = paths.GameInstallDir;

        if (useCustomEngine)
        {
            InstallerEngine engine = CreateInstallerEngine(descriptor, ctx, typeof(Program).Assembly);
            var opts = new InstallerInstallOptions(
                ExplicitInstallDir: paths.GameInstallDir,
                CreateShortcuts: !cli.NoShortcuts,
                RegisterAutoStart: false,
                ExternalPayloadPath: cli.PayloadPath,
                InstallProfile: profileId,
                SecondaryInstallDir: effectiveReDir,
                SecondaryFlagName: "--reengine-dir",
                MigrateLegacy: false)
            { OfflineBundlePath = effectiveOfflineBundle };
            InstallerOperationResult op = engine.InstallAsync(opts).GetAwaiter().GetResult();
            Console.WriteLine(op.Message);
            exitCode = op.Success ? 0 : 1;
            wasRunning = op.WasRunningBeforeInstall;
            if (!string.IsNullOrWhiteSpace(op.InstallDirectory)) installedDir = op.InstallDirectory;
        }
        else
        {
            string[] instArgs = InstallerArgsAndContainment.ReplaceInstallDirArg(host.ForwardedArgs, paths.GameInstallDir);
            instArgs = AppendFlagIfMissing(instArgs, "--offline-bundle", effectiveOfflineBundle);
            if (!HasAnyFlag(instArgs, "--reengine-dir", "--suite-dir"))
                instArgs = AppendFlagIfMissing(instArgs, "--reengine-dir", effectiveReDir);
            if (!instArgs.Any(a => a.Equals("--no-launch", StringComparison.OrdinalIgnoreCase)))
                instArgs = instArgs.Append("--no-launch").ToArray();
            InstallerHostRunResult hostRes = StandardInstallerHost.RunWithResult(descriptor, instArgs, typeof(Program).Assembly);
            exitCode = hostRes.ExitCode;
            wasRunning = hostRes.OperationResult?.WasRunningBeforeInstall ?? false;
            if (!string.IsNullOrWhiteSpace(hostRes.OperationResult?.InstallDirectory))
                installedDir = hostRes.OperationResult.InstallDirectory;
        }

        if (exitCode == 0 && !host.DryRun)
        {
            InstallerPathResolver.TryRepairLastInstallBaseDir(
                ctx.Registry, installedDir, ctx.IsSandboxActive ? ctx.Sandbox : SandboxResolution.Inactive, ctx.ShouldSuppressHostMutations);
            string postDataDir = string.IsNullOrWhiteSpace(host.DataDir) && string.IsNullOrWhiteSpace(ctx.DataDirectory)
                ? Path.Combine(installedDir, "data") : effectiveDataDir;
            InstallerSettingsAndRomPreserver.RestoreAndPersistAfterInstall(
                snapshot, postDataDir, installedDir, profileId, effectiveSuite, effectiveReDir, ctx);
            WriteReadyMarker(installedDir, descriptor);

            if ((cli.LaunchAfter || wasRunning) && !cli.NoLaunch && !ctx.ShouldSuppressHostMutations)
                TryLaunchInstalledApp(installedDir, ctx);
        }
        return exitCode;
    }

    internal static void WriteReadyMarker(string installDir, InstallerAppDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(installDir)) return;
        string full = Path.GetFullPath(installDir);
        Directory.CreateDirectory(full);
        string readyPath = Path.Combine(full, ".ready");
        string tmp = readyPath + ".tmp";
        string json = JsonSerializer.Serialize(new
        {
            appId = InstallerArgsAndContainment.AppId,
            moduleId = InstallerArgsAndContainment.ModuleId,
            productName = descriptor.ProductName,
            version = descriptor.Version,
            completedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)
        }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(tmp, json + Environment.NewLine, new UTF8Encoding(false));
        File.Move(tmp, readyPath, overwrite: true);
    }

    internal static void RemoveReadyMarkerIfPresent(string? installDir)
    {
        if (string.IsNullOrWhiteSpace(installDir)) return;
        string full = Path.GetFullPath(installDir);
        string ready = Path.Combine(full, ".ready");
        try
        {
            if (File.Exists(ready)) File.Delete(ready);
            if (Directory.Exists(full) && !Directory.EnumerateFileSystemEntries(full).Any()) Directory.Delete(full, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static string ResolveEffectiveProfile(HostCliOptions host, string effectiveDataDir) =>
        host.HasExplicitProfile
            ? host.ProfileId
            : (InstallerSettingsAndRomPreserver.TryReadSavedInstallSettings(effectiveDataDir, out string? saved, out _, out _) && saved is not null
                ? saved : host.ProfileId);

    private static string ResolveAssemblyVersion()
    {
        string? info = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info)) return info.Split('+')[0].Trim();
        Version? v = typeof(Program).Assembly.GetName().Version;
        return v is not null ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.1.0";
    }

    private static void TryLaunchInstalledApp(string installDir, InstallerRuntimeContext ctx)
    {
        string exe = Path.Combine(installDir, InstallerArgsAndContainment.MainExecutable);
        if (!File.Exists(exe)) return;
        if (ctx.IsSandboxActive) exe = SandboxEnvironmentSeam.AssertSandboxPathIfActive(exe, ctx.Sandbox);
        Process.Start(new ProcessStartInfo { FileName = exe, WorkingDirectory = installDir, UseShellExecute = true });
    }

    private static string[] AppendFlagIfMissing(string[] args, string flag, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || HasAnyFlag(args, flag)) return args;
        return [.. args, flag, value];
    }

    private static bool HasAnyFlag(IReadOnlyList<string> args, params string[] flags) =>
        args.Any(a => flags.Any(f => a.Equals(f, StringComparison.OrdinalIgnoreCase)));
}

// @module: IronFrameCart01.Installer.InstallerWizardHost
// @purpose: Interactive WinForms wizard setup/uninstall flow, theme options, brand assets, and Full/Lightweight profile coordination.
// @depends-on: System, System.Collections.Generic, System.Drawing, System.IO, System.Linq, System.Reflection, System.Threading, System.Threading.Tasks, System.Windows.Forms, Foundation.Core, Foundation.Installer
// @exports: InstallerWizardHost

using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Foundation.Core;
using Foundation.Installer;

namespace IronFrameCart01.Installer;

internal static class InstallerWizardHost
{
    internal static InstallerThemeOptions CreateThemeOptions(
        InstallerAppDescriptor desc, InstallerRuntimeContext ctx, string? secondarySuiteDir, string? secondaryFlag)
    {
        SandboxResolution? sandbox = ctx.IsSandboxActive ? ctx.Sandbox : null;
        string defaultInstallDir = desc.ResolveCanonicalDefaultInstallDir(sandbox);
        string suiteFallback = secondarySuiteDir ?? InstallerPathResolver.DeriveCanonicalSuiteRoot(sandbox);
        IReadOnlyList<InstallerProfileOption> profiles =
        [
            new("lightweight", InstallerProfileKind.Lightweight, "Lightweight", "Clean-room .recomp module and clean-root launcher.", IsDefault: true),
            new("full", InstallerProfileKind.Full, "Full (reEngine + reCart)", "Discovers or installs companion reEngine + reCart suite.", IsDefault: false)
        ];
        var picker = new SecondaryPathPickerOptions(
            secondaryFlag ?? "--suite-dir",
            string.Equals(secondaryFlag, "--reengine-dir", StringComparison.OrdinalIgnoreCase) ? "reEngine Directory:" : "reEngine Suite Root:",
            suiteFallback,
            @"%LOCALAPPDATA%\Programs\StreamItAll\reEngine",
            "Parent StreamItAll\\reEngine directory containing reEngine and side-by-side engines.");

        InstallerMarkupManifest? manifest = InstallerManifestStore.TryLoadFromDirectoryOrAssembly(AppContext.BaseDirectory, typeof(Program).Assembly);
        if (manifest is not null)
        {
            return InstallerManifestStore.ToThemeOptions(manifest, defaultInstallDir) with
            {
                AllowAutoStart = false,
                BootstrapSharedSuiteDaemon = false,
                AllowLegacyMigration = false,
                SuiteName = "reEngine",
                ExecutableFileName = InstallerArgsAndContainment.MainExecutable,
                WindowIconResource = Program.IconResourceName,
                HeaderLogoResource = Program.LogoResourceName,
                AvailableProfiles = profiles,
                SecondaryPathPicker = picker
            };
        }

        return new InstallerThemeOptions
        {
            AppId = InstallerArgsAndContainment.AppId,
            DisplayName = desc.DisplayName,
            Version = desc.Version,
            Subtitle = desc.EffectiveSubtitle,
            BrandAccentHex = desc.BrandAccentHex ?? "#F59E0B",
            SurfaceBackdropHex = desc.SurfaceBackdropHex ?? "#0E0E10",
            SurfaceCardHex = desc.SurfaceCardHex ?? "#18181B",
            TextPrimaryHex = desc.TextPrimaryHex ?? "#ECEFF4",
            BorderHex = desc.BorderHex ?? "#3F3F46",
            WindowIconResource = Program.IconResourceName,
            HeaderLogoResource = Program.LogoResourceName,
            DefaultInstallDir = defaultInstallDir,
            ExecutableFileName = InstallerArgsAndContainment.MainExecutable,
            AllowAutoStart = false,
            BootstrapSharedSuiteDaemon = false,
            AllowLegacyMigration = false,
            StudioName = InstallerArgsAndContainment.Publisher,
            SuiteName = "reEngine",
            UmbrellaSubpath = InstallerArgsAndContainment.UmbrellaSubpath,
            AvailableProfiles = profiles,
            DefaultProfileId = "lightweight",
            SecondaryPathPicker = picker,
            InstallerDeliveryKind = desc.InstallerDeliveryKind,
            PayloadDownloadUrl = desc.PayloadDownloadUrl,
            PayloadAssets = desc.PayloadAssets
        };
    }

    internal static int RunInteractiveWizard(
        InstallerAppDescriptor descriptor, InstallerRuntimeContext ctx, HostCliOptions host,
        InstallerCliOptions cli, ResolvedInstallPaths initialPaths, string selectedProfile, string? effectiveOfflineBundle)
    {
        InitializeWinForms();
        if (cli.Uninstall)
        {
            InstallerEngine unEngine = Program.CreateInstallerEngine(descriptor, ctx, typeof(Program).Assembly);
            using var unWizard = new InstallerWizardForm(
                unEngine, ctx, initialPaths.GameInstallDir, cli.PayloadPath, isUninstallMode: true,
                initialCreateShortcuts: false, initialAutoStart: false, initialLaunchAfter: false,
                initialPurgeData: cli.PurgeData, initialProfileId: selectedProfile,
                initialSecondaryDir: initialPaths.SuiteRootDir, secondaryFlagName: "--suite-dir",
                initialMigrateLegacy: false, showLegacyMigration: false, payloadAssembly: typeof(Program).Assembly,
                offlineBundlePath: effectiveOfflineBundle);
            using IDisposable? unAuto = InstallerArgsAndContainment.TryAttachWizardAutomation(unWizard, host.AutomationPort, host.AutomationToken, ctx);
            Application.Run(unWizard);
            if (unWizard.ExitCode == 0) Program.RemoveReadyMarkerIfPresent(unWizard.InstallDirTextBox.Text);
            return unWizard.ExitCode;
        }

        string secFlag = !string.IsNullOrWhiteSpace(host.ReEngineDir) && string.IsNullOrWhiteSpace(host.SuiteDir) ? "--reengine-dir" : "--suite-dir";
        InstallerThemeOptions theme = CreateThemeOptions(descriptor, ctx, initialPaths.SuiteRootDir, secFlag);
        var envArgs = new List<string>();
        if (ctx.DryRun) envArgs.Add("--dry-run");
        if (!string.IsNullOrWhiteSpace(ctx.DataDirectory)) { envArgs.Add("--data-dir"); envArgs.Add(ctx.DataDirectory); }
        if (!string.IsNullOrWhiteSpace(ctx.ConfigFilePath)) { envArgs.Add("--config"); envArgs.Add(ctx.ConfigFilePath); }
        var appEnv = new AppEnvironment(InstallerArgsAndContainment.AppId, envArgs, null, null, ctx.IsSandboxActive ? ctx.Sandbox : null);
        var coreEngine = new InstallerEngineCore(theme, appEnv, ctx.Registry);

        InstallerWizardForm? wizardRef = null;
        Task<FoundationInstallResult> RunWizardInstallAsync(CancellationToken ct, Action<int, string> reportProgress)
        {
            string targetDir = string.IsNullOrWhiteSpace(wizardRef!.InstallDirTextBox.Text)
                ? initialPaths.GameInstallDir : InstallerArgsAndContainment.NormalizeWizardGameInstallDir(wizardRef.InstallDirTextBox.Text);
            string chosenProfile = wizardRef.SelectedProfileId;
            string chosenSuite = string.IsNullOrWhiteSpace(wizardRef.SecondaryInstallDir)
                ? initialPaths.SuiteRootDir : InstallerArgsAndContainment.NormalizeSuiteRoot(wizardRef.SecondaryInstallDir);
            bool createShortcuts = wizardRef.ShortcutsCheckBox.Checked;
            IProgress<string> msgProg = new Progress<string>(msg => reportProgress(72, msg));

            return Task.Run(async () =>
            {
                var resolvedTarget = new ResolvedInstallPaths(targetDir, chosenSuite, chosenSuite);
                BootstrapOutcome boot = await InstallerBootstrapHelper.EnsureFullProfileBootstrappedAsync(
                    host, ctx, resolvedTarget, chosenProfile, chosenSuite, (pct, m) => reportProgress(pct, m), ct).ConfigureAwait(false);
                if (!boot.Success) return new FoundationInstallResult(false, targetDir, string.Empty, 0, boot.Message, false);

                string dataDir = InstallerArgsAndContainment.ResolveEffectiveDataDir(host, ctx, targetDir);
                using PreUpgradePreservationSnapshot? snapshot = InstallerSettingsAndRomPreserver.CapturePreUpgradeSnapshot(dataDir, ctx);
                reportProgress(55, $"Installing {descriptor.DisplayName} v{descriptor.Version}...");

                InstallerEngine engine = Program.CreateInstallerEngine(descriptor, ctx, typeof(Program).Assembly);
                InstallerOperationResult op = await engine.InstallAsync(new InstallerInstallOptions(
                    ExplicitInstallDir: targetDir, CreateShortcuts: createShortcuts, RegisterAutoStart: false,
                    ExternalPayloadPath: cli.PayloadPath, InstallProfile: chosenProfile,
                    SecondaryInstallDir: boot.ReEngineInstallDir, SecondaryFlagName: "--reengine-dir", MigrateLegacy: false)
                { OfflineBundlePath = effectiveOfflineBundle }, ct, msgProg).ConfigureAwait(false);

                if (!op.Success) return new FoundationInstallResult(false, op.InstallDirectory, op.PayloadSha256, op.FileCount, op.Message, false);
                InstallerPathResolver.TryRepairLastInstallBaseDir(
                    ctx.Registry, op.InstallDirectory, ctx.IsSandboxActive ? ctx.Sandbox : SandboxResolution.Inactive, ctx.ShouldSuppressHostMutations);
                string finalDataDir = string.IsNullOrWhiteSpace(host.DataDir) && string.IsNullOrWhiteSpace(ctx.DataDirectory)
                    ? Path.Combine(op.InstallDirectory, "data") : dataDir;
                InstallerSettingsAndRomPreserver.RestoreAndPersistAfterInstall(
                    snapshot, finalDataDir, op.InstallDirectory, chosenProfile, boot.SuiteRootDir, boot.ReEngineInstallDir, ctx);
                Program.WriteReadyMarker(op.InstallDirectory, descriptor);
                reportProgress(100, op.Message);
                return new FoundationInstallResult(true, op.InstallDirectory, op.PayloadSha256, op.FileCount, op.Message, false);
            }, ct);
        }

        wizardRef = new InstallerWizardForm(
            coreEngine, isUninstallMode: false, initialPaths.GameInstallDir, RunWizardInstallAsync,
            selectedProfile, initialPaths.SuiteRootDir, initialMigrateLegacy: false, showLegacyMigration: false,
            payloadAssembly: typeof(Program).Assembly);
        wizardRef.ShortcutsCheckBox.Checked = !cli.NoShortcuts;
        wizardRef.LaunchAfterCheckBox.Checked = !cli.NoLaunch;
        InstallerArgsAndContainment.WireWizardAutoSortHandlers(wizardRef);
        ApplyBrandAssets(wizardRef);
        using IDisposable? autoHook = InstallerArgsAndContainment.TryAttachWizardAutomation(wizardRef, host.AutomationPort, host.AutomationToken, ctx);
        Application.Run(wizardRef);
        return wizardRef.ExitCode;
    }

    private static void ApplyBrandAssets(InstallerWizardForm wizard)
    {
        Assembly asm = typeof(Program).Assembly;
        try
        {
            using Stream? iconStream = asm.GetManifestResourceStream(Program.IconResourceName);
            if (iconStream is not null) wizard.Icon = new Icon(iconStream);
            using Stream? logoStream = asm.GetManifestResourceStream(Program.LogoResourceName);
            if (logoStream is not null && wizard.HeaderPanel.Controls.OfType<PictureBox>().FirstOrDefault() is { } pic)
            {
                pic.Image = Image.FromStream(logoStream);
                pic.Visible = true;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException) { }
        wizard.LightweightProfileRadioButton.Text = "Lightweight (Module Only)";
        wizard.FullProfileRadioButton.Text = "Full (Bundle reEngine + reCart)";
        wizard.OptionsPanel.PerformLayout();
        wizard.FullProfileRadioButton.Left = wizard.LightweightProfileRadioButton.Right + 16;
        wizard.UpdateLayoutBounds();
    }

    private static void InitializeWinForms()
    {
        try { Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); }
        catch (InvalidOperationException ex) { _ = ex; }
    }
}

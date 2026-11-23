// @module: IronFrameCart01.Installer.InstallerArgsAndContainment
// @purpose: Clean-room CLI argument parsing, path normalization, Stage 1/2 containment, wizard auto-sort, and Debug/Test automation hook.
// @depends-on: System, System.Collections.Generic, System.Globalization, System.IO, System.Linq, System.Runtime.InteropServices, System.Windows.Forms, Foundation.Core, Foundation.Installer, reEngine.Core.Bootstrap
// @exports: HostCliOptions, ResolvedInstallPaths, InstallerArgsAndContainment

using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Foundation.Core;
using Foundation.Installer;
using reEngine.Core.Bootstrap;
#if DEBUG || IRONFRAME_TEST_AUTOMATION
using System.Security.Cryptography;
using Foundation.Automation;
#endif

namespace IronFrameCart01.Installer;

internal sealed record HostCliOptions(
    string ProfileId,
    bool HasExplicitProfile,
    string? SuiteDir,
    string? ReEngineDir,
    string? OfflineBundlePath,
    string? InstallDir,
    string? DataDir,
    string? ConfigPath,
    string? LogDir,
    string? PayloadPath,
    bool Silent,
    bool DryRun,
    bool VerifyOnly,
    bool Uninstall,
    bool PurgeData,
    bool NoShortcuts,
    bool LaunchAfter,
    bool NoLaunch,
    bool ShowHelp,
    bool ShowVersion,
    int? AutomationPort,
    string? AutomationToken,
    string[] ForwardedArgs);

internal static class InstallerArgsAndContainment
{
    internal const string AppId = "ironframe-outerfortress-cart-01";
    internal const string ModuleId = "ironframe-outerfortress-cart-01";
    internal const string ProductName = "IronFrame-OuterFortress-Cart-01";
    internal const string DisplayName = "IronFrame OuterFortress (Cart) 01";
    internal const string Publisher = "StreamItAll";
    internal const string UmbrellaSubpath = @"StreamItAll\reEngine\games\IronFrame-OuterFortress-Cart-01";
    internal const string MainExecutable = "IronFrame-OuterFortress-Cart-01.exe";
    internal const string ModuleArchiveName = "IronFrame-OuterFortress-Cart-01.recomp";
    private const int AttachParentProcess = -1;

    private static readonly HashSet<string> ValueFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "--install-dir", "--target-dir", "--data-dir", "--config", "--log-dir", "--payload",
        "--offline-bundle", "--profile", "--reengine-dir", "--suite-dir", "--automation-port", "--automation-token"
    };

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    internal static void AttachParentConsoleIfInteractive(string[]? args)
    {
        if (args is { Length: > 0 } && OperatingSystem.IsWindows() && !Console.IsOutputRedirected && !Console.IsErrorRedirected)
            _ = AttachConsole(AttachParentProcess);
    }

    internal static bool HasAutomationPortFlag(string[]? args) =>
        args is not null && args.Any(raw =>
        {
            string trimmed = (raw ?? string.Empty).Trim();
            return trimmed.Equals("--automation-port", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.StartsWith("--automation-port=", StringComparison.OrdinalIgnoreCase);
        });

    internal static string NormalizeProfileId(string? rawProfile)
    {
        if (string.IsNullOrWhiteSpace(rawProfile)) return ReEnginePackagingConstants.DefaultInstallProfile;
        return rawProfile.Trim().ToLowerInvariant() switch
        {
            "lightweight" => "lightweight",
            "full" => "full",
            _ => throw new ArgumentException($"Invalid --profile '{rawProfile}'. Expected 'lightweight' or 'full'.")
        };
    }

    internal static string[] SanitizeArgs(string[]? args)
    {
        if (args is null || args.Length == 0) return Array.Empty<string>();
        var list = new List<string>(args.Length);
        foreach (string raw in args)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string t = raw.Trim();
            int eq = t.IndexOf('=');
            if (eq > 2 && t.StartsWith("--", StringComparison.Ordinal))
            {
                string key = t[..eq].Trim();
                string val = t[(eq + 1)..].Trim().Trim('"');
                if (IsAutoStartFlag(key)) continue;
                list.Add(NormalizeAliasFlag(key));
                list.Add(val);
                continue;
            }
            if (IsAutoStartFlag(t)) continue;
            list.Add(NormalizeAliasFlag(t));
        }
        return list.ToArray();
    }

    internal static HostCliOptions ExtractHostArgs(string[]? args)
    {
        string[] clean = SanitizeArgs(args);
        string profileId = ReEnginePackagingConstants.DefaultInstallProfile;
        bool hasExplicitProfile = false, silent = false, dryRun = false, verifyOnly = false, uninstall = false;
        bool purgeData = false, noShortcuts = false, launchAfter = false, noLaunch = false, showHelp = false, showVersion = false;
        string? suiteDir = null, reEngineDir = null, offlineBundle = null, installDir = null;
        string? dataDir = null, configPath = null, logDir = null, payloadPath = null, automationToken = null;
        int? automationPort = null;
        var forwarded = new List<string>(clean.Length + 2);

        for (int i = 0; i < clean.Length; i++)
        {
            string token = clean[i];
            string lower = token.ToLowerInvariant();
            if (ValueFlags.Contains(lower))
            {
                if (i + 1 >= clean.Length || string.IsNullOrWhiteSpace(clean[i + 1]))
                    throw new ArgumentException($"Option '{token}' requires a value.");
                string val = clean[++i].Trim();
                switch (lower)
                {
                    case "--automation-port":
                        if (!int.TryParse(val, NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port is < 0 or > 65535)
                            throw new ArgumentException($"--automation-port expects an integer in 0..65535, got '{val}'.");
                        automationPort = port;
                        continue;
                    case "--automation-token":
                        automationToken = val;
                        continue;
                    case "--profile":
                        profileId = NormalizeProfileId(val);
                        hasExplicitProfile = true;
                        val = profileId;
                        break;
                    case "--suite-dir":
                        suiteDir = NormalizeSuiteRoot(val);
                        val = suiteDir;
                        break;
                    case "--reengine-dir":
                        reEngineDir = NormalizeSuiteRoot(val);
                        val = reEngineDir;
                        break;
                    case "--offline-bundle":
                        offlineBundle = Path.GetFullPath(val);
                        val = offlineBundle;
                        break;
                    case "--install-dir":
                        installDir = val;
                        break;
                    case "--data-dir":
                        dataDir = Path.GetFullPath(val);
                        val = dataDir;
                        break;
                    case "--config":
                        configPath = Path.GetFullPath(val);
                        val = configPath;
                        break;
                    case "--log-dir":
                        logDir = Path.GetFullPath(val);
                        val = logDir;
                        break;
                    case "--payload":
                        payloadPath = Path.GetFullPath(val);
                        val = payloadPath;
                        break;
                }
                forwarded.Add(lower);
                forwarded.Add(val);
                continue;
            }

            forwarded.Add(token);
            if (lower is "--silent" or "--quiet") silent = true;
            else if (lower == "--dry-run") dryRun = true;
            else if (lower == "--verify") verifyOnly = true;
            else if (lower == "--uninstall") uninstall = true;
            else if (lower == "--purge-data") purgeData = true;
            else if (lower == "--no-shortcuts") noShortcuts = true;
            else if (lower == "--launch" || lower == "--launch-after") launchAfter = true;
            else if (lower is "--no-launch" or "--no-restart") noLaunch = true;
            else if (lower is "--version" or "-v") showVersion = true;
            else if (lower is "--help" or "-h" or "/?" or "help") showHelp = true;
        }

        if (!uninstall && !string.IsNullOrWhiteSpace(installDir))
            installDir = NormalizeGameInstallDir(installDir);
        else if (!string.IsNullOrWhiteSpace(installDir))
            installDir = SafeFullTrim(installDir);

        return new HostCliOptions(
            profileId, hasExplicitProfile, suiteDir, reEngineDir, offlineBundle, installDir,
            dataDir, configPath, logDir, payloadPath, silent, dryRun, verifyOnly, uninstall,
            purgeData, noShortcuts, launchAfter, noLaunch, showHelp, showVersion,
            automationPort, automationToken, forwarded.ToArray());
    }

    internal static void ValidateContainment(InstallerRuntimeContext ctx, HostCliOptions host)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(host);

        if (ctx.IsSandboxActive)
        {
            SandboxEnvironmentSeam.ValidateResolution(ctx.Sandbox);
            foreach (string? target in new[] { ctx.DataDirectory, ctx.ConfigFilePath, ctx.LogDirectory, host.InstallDir, host.SuiteDir, host.ReEngineDir })
            {
                if (!string.IsNullOrWhiteSpace(target))
                    _ = SandboxEnvironmentSeam.AssertSandboxPathIfActive(Path.GetFullPath(target), ctx.Sandbox);
            }
        }

        if (!ctx.IsIsolatedTestProcess) return;
        if (string.IsNullOrWhiteSpace(ctx.DataDirectory))
            SandboxEnvironmentSeam.ThrowContainmentViolation("Fail-Closed Containment Violation: APP_ISOLATED_TEST=1 requires explicit --data-dir.");

        string boundary = ResolveIsolatedBoundary(ctx.DataDirectory);
        foreach (string? target in new[] { host.InstallDir, host.SuiteDir, host.ReEngineDir, host.ConfigPath, host.LogDir })
        {
            if (string.IsNullOrWhiteSpace(target)) continue;
            try { _ = AppEnvironment.AssertPathContained(Path.GetFullPath(target), boundary); }
            catch (InvalidOperationException ex) { SandboxEnvironmentSeam.ThrowContainmentViolation(ex.Message, ex); }
        }
    }

    internal static string ResolveIsolatedBoundary(string dataDir)
    {
        string full = SafeFullTrim(dataDir);
        string? parent = Path.GetDirectoryName(full);
        if (string.IsNullOrWhiteSpace(parent) || !string.Equals(Path.GetFileName(full), "data", StringComparison.OrdinalIgnoreCase))
            return full;
        string? gamesParent = Directory.GetParent(parent)?.FullName;
        if (!string.IsNullOrWhiteSpace(gamesParent) && string.Equals(Path.GetFileName(gamesParent), "games", StringComparison.OrdinalIgnoreCase))
        {
            string? suite = Path.GetDirectoryName(gamesParent);
            if (!string.IsNullOrWhiteSpace(suite) && !IsDriveRoot(suite)) return suite;
        }
        return !IsDriveRoot(parent) ? parent : full;
    }

    internal static ResolvedInstallPaths ResolveInitialPaths(
        HostCliOptions host, InstallerRuntimeContext ctx, InstallerAppDescriptor desc, string? cliInstallDir = null) =>
        InstallerPathResolver.ResolveInitialPaths(host, ctx, desc, cliInstallDir);

    internal static string NormalizeSuiteRoot(string? rawPath) => InstallerPathResolver.NormalizeSuiteRoot(rawPath);
    internal static string NormalizeGameInstallDir(string? rawPath) => InstallerPathResolver.NormalizeGameInstallDir(rawPath);
    internal static string NormalizeWizardGameInstallDir(string? rawPath) => InstallerPathResolver.NormalizeWizardGameInstallDir(rawPath);
    internal static string? TryExtractSuiteRootFromGameDir(string? gameDir) => InstallerPathResolver.TryExtractSuiteRootFromGameDir(gameDir);

    internal static string ResolveEffectiveDataDir(HostCliOptions host, InstallerRuntimeContext ctx, string installDir) =>
        !string.IsNullOrWhiteSpace(ctx.DataDirectory) ? Path.GetFullPath(ctx.DataDirectory)
        : !string.IsNullOrWhiteSpace(host.DataDir) ? Path.GetFullPath(host.DataDir)
        : Path.Combine(Path.GetFullPath(installDir), "data");

    internal static string[] ReplaceInstallDirArg(string[] args, string resolvedInstallDir)
    {
        var updated = new List<string>(args.Length + 2);
        bool replaced = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--install-dir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                updated.Add("--install-dir");
                updated.Add(resolvedInstallDir);
                i++;
                replaced = true;
            }
            else updated.Add(args[i]);
        }
        if (!replaced) { updated.Add("--install-dir"); updated.Add(resolvedInstallDir); }
        return updated.ToArray();
    }

    internal static void WireWizardAutoSortHandlers(InstallerWizardForm wizard)
    {
        DisableMnemonics(wizard);
        bool updating = false, customPri = false, customSec = false;
        string lastPri = SafeFullTrim(wizard.InstallDirTextBox.Text), lastSec = SafeFullTrim(wizard.SecondaryDirTextBox.Text);

        void SyncPrimary()
        {
            if (updating || !wizard.InstallDirTextBox.Enabled) return;
            string sorted = NormalizeWizardGameInstallDir(wizard.InstallDirTextBox.Text);
            if (string.IsNullOrWhiteSpace(sorted)) return;
            updating = true;
            try
            {
                SetBox(wizard.InstallDirTextBox, sorted);
                bool changed = !PathsEqual(sorted, lastPri);
                if (changed) { customPri = true; lastPri = sorted; }
                string? suite = TryExtractSuiteRootFromGameDir(sorted);
                if (changed && !customSec && !string.IsNullOrWhiteSpace(suite)) { SetBox(wizard.SecondaryDirTextBox, suite); lastSec = suite; }
            }
            finally { updating = false; }
        }

        void SyncSecondary()
        {
            if (updating || !wizard.SecondaryDirTextBox.Visible || !wizard.SecondaryDirTextBox.Enabled) return;
            string sortedSuite = NormalizeSuiteRoot(wizard.SecondaryDirTextBox.Text);
            if (string.IsNullOrWhiteSpace(sortedSuite)) return;
            updating = true;
            try
            {
                SetBox(wizard.SecondaryDirTextBox, sortedSuite);
                bool changed = !PathsEqual(sortedSuite, lastSec);
                if (changed) { customSec = true; lastSec = sortedSuite; }
                string curPri = SafeFullTrim(wizard.InstallDirTextBox.Text);
                if (changed && (!customPri || string.IsNullOrWhiteSpace(curPri) || PathsEqual(curPri, lastPri)))
                {
                    string syncedGame = Path.Combine(sortedSuite, "games", ProductName);
                    SetBox(wizard.InstallDirTextBox, syncedGame);
                    lastPri = syncedGame;
                }
            }
            finally { updating = false; }
        }

        HookInputSync(wizard.InstallDirTextBox, wizard.BrowseButton, SyncPrimary);
        Button? secBtn = EnumerateButtons(wizard).FirstOrDefault(b => b != wizard.BrowseButton && (b.Text.StartsWith("Browse", StringComparison.OrdinalIgnoreCase) || b.Text.StartsWith("Change", StringComparison.OrdinalIgnoreCase)));
        HookInputSync(wizard.SecondaryDirTextBox, secBtn, SyncSecondary);
        wizard.PrimaryButton.MouseDown += (_, _) => { SyncSecondary(); SyncPrimary(); };
        wizard.PrimaryButton.Enter += (_, _) => { SyncSecondary(); SyncPrimary(); };
    }

#if DEBUG || IRONFRAME_TEST_AUTOMATION
    internal static IDisposable? TryAttachWizardAutomation(InstallerWizardForm wizard, int? port, string? token, InstallerRuntimeContext ctx) =>
        port is { } p ? WizardAutomationHook.Attach(wizard, p, token, ctx) : null;

    private sealed class WizardAutomationHook : IDisposable
    {
        private readonly AutomationLoopbackServer _server;
        private bool _disposed;
        private WizardAutomationHook(AutomationLoopbackServer server) => _server = server;

        public static WizardAutomationHook Attach(InstallerWizardForm wizard, int port, string? token, InstallerRuntimeContext ctx)
        {
            ApplyControlNames(wizard);
            string? envToken = Environment.GetEnvironmentVariable("FOUNDATION_AUTOMATION_TOKEN");
            string effectiveToken = !string.IsNullOrWhiteSpace(token) ? token.Trim()
                : (!string.IsNullOrWhiteSpace(envToken) ? envToken.Trim() : Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant());
            var opts = new AutomationServerOptions(port, effectiveToken, ModuleId, AllowedSnapshotRoot: null);
            var server = new AutomationLoopbackServer(opts, wizard, null, ctx.IsSandboxActive ? ctx.Sandbox : SandboxResolution.Inactive);
            var hook = new WizardAutomationHook(server);
            wizard.Load += (_, _) =>
            {
                if (hook._disposed || server.IsListening) return;
                server.Start();
                ctx.WriteLog("AUTOMATION", $"Wizard loopback listening on 127.0.0.1:{server.AutomationPort}");
                Console.WriteLine($"AUTOMATION: Wizard loopback listening on 127.0.0.1:{server.AutomationPort}");
            };
            wizard.FormClosed += (_, _) => hook.Dispose();
            return hook;
        }

        private static void ApplyControlNames(InstallerWizardForm w)
        {
            w.Name = "InstallerWizardRoot"; w.PrimaryButton.Name = "Wizard_PrimaryButton"; w.CancelButton.Name = "Wizard_CancelButton";
            w.BrowseButton.Name = "Wizard_BrowseButton"; w.DetailsToggleButton.Name = "Wizard_DetailsToggle";
            w.InstallDirTextBox.Name = "Wizard_InstallDir"; w.SecondaryDirTextBox.Name = "Wizard_SecondaryDir";
            w.LightweightProfileRadioButton.Name = "Wizard_ProfileLightweight"; w.FullProfileRadioButton.Name = "Wizard_ProfileFull";
            w.ShortcutsCheckBox.Name = "Wizard_Shortcuts"; w.LaunchAfterCheckBox.Name = "Wizard_LaunchAfter";
            w.MigrateLegacyCheckBox.Name = "Wizard_MigrateLegacy"; w.StatusLabel.Name = "Wizard_StatusLabel"; w.LogTextBox.Name = "Wizard_LogTextBox";
        }

        public void Dispose() { if (!_disposed) { _disposed = true; _server.Dispose(); } }
    }
#else
    internal static IDisposable? TryAttachWizardAutomation(InstallerWizardForm wizard, int? port, string? token, InstallerRuntimeContext ctx) => null;
#endif

    internal static string SafeFullTrim(string? raw) => InstallerPathResolver.SafeFullTrim(raw);
    internal static bool PathsEqual(string a, string b) => InstallerPathResolver.PathsEqual(a, b);

    private static bool IsAutoStartFlag(string token) =>
        token.Equals("--autostart", StringComparison.OrdinalIgnoreCase) || token.Equals("--auto-start", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("--no-auto-start", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeAliasFlag(string token) =>
        token.Equals("--dir", StringComparison.OrdinalIgnoreCase) || token.Equals("--target-dir", StringComparison.OrdinalIgnoreCase)
            ? "--install-dir" : token;

    private static bool IsDriveRoot(string full) =>
        string.Equals(full, Path.GetPathRoot(full), StringComparison.OrdinalIgnoreCase);

    private static void HookInputSync(TextBox box, Button? browse, Action sync)
    {
        if (browse is not null) browse.Click += (_, _) => sync();
        box.Leave += (_, _) => sync(); box.Validating += (_, _) => sync();
        box.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) sync(); };
    }

    private static void SetBox(TextBox tb, string value)
    {
        if (!string.Equals(tb.Text.Trim(), value, StringComparison.Ordinal)) { tb.Text = value; tb.SelectionStart = value.Length; }
    }

    private static void DisableMnemonics(Control root)
    {
        foreach (Control c in root.Controls) { if (c is Label l) l.UseMnemonic = false; else if (c is ButtonBase b) b.UseMnemonic = false; if (c.HasChildren) DisableMnemonics(c); }
    }

    private static IEnumerable<Button> EnumerateButtons(Control root)
    {
        foreach (Control c in root.Controls) { if (c is Button b) yield return b; foreach (Button n in EnumerateButtons(c)) yield return n; }
    }
}

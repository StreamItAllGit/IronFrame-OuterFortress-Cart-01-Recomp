// @module: IronFrameCart01.Installer.InstallerBootstrapHelper
// @purpose: Full-profile suite discovery (reEngine + reCart via ReEngineBootstrapHelper, UmbrellaSubpath, EngineAliasCatalog) and offline-only .gz bootstrapper.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.IO, System.IO.Compression, System.Linq, System.Reflection, System.Text.Json, System.Threading, System.Threading.Tasks, Foundation.Core, Foundation.Installer, reCart.Contracts, reEngine.Core.Bootstrap, reEngine.Core.Resolution
// @exports: SuiteBootstrapPlan, BootstrapOutcome, InstallerBootstrapHelper

using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Foundation.Core;
using Foundation.Installer;
using reCart.Contracts;
using reEngine.Core.Bootstrap;
using reEngine.Core.Resolution;

namespace IronFrameCart01.Installer;

internal sealed record SuiteBootstrapPlan(
    string SuiteRootDir,
    string TargetReEngineDir,
    string TargetEnginesRoot,
    string? InstalledReEngineVersion,
    string? InstalledReCartVersion,
    string? ResolvedReCartExecutable,
    bool NeedsReEngine,
    bool NeedsReCart,
    string Summary);

internal sealed record BootstrapOutcome(
    bool Success,
    string SuiteRootDir,
    string ReEngineInstallDir,
    string Message);

internal static class InstallerBootstrapHelper
{
    internal const string ReEngineResourceName = "IronFrameCart01.Installer.Embedded.reEngine-Setup.gz";
    internal const string ReCartResourceName = "IronFrameCart01.Installer.Embedded.reCart-Setup.gz";
    private const string ReEngineUninstallSubKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\reEngine";
    private const string ReCartUninstallSubKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\reCart";

    internal static Func<string, Stream?>? ResourceStreamOpenerForTests { get; set; }
    internal static Func<string, string[], CancellationToken, Task<int>>? ExternalBootstrapRunnerForTests { get; set; }

    internal static bool HasBundledResources(Assembly? assembly = null)
    {
        Assembly asm = assembly ?? typeof(InstallerBootstrapHelper).Assembly;
        return HasResource(asm, ReEngineResourceName) || HasResource(asm, ReCartResourceName);
    }

    internal static SuiteBootstrapPlan InspectSuite(
        HostCliOptions host,
        InstallerRuntimeContext ctx,
        ResolvedInstallPaths resolved,
        string? overrideSecondaryDir = null)
    {
        string? isolatedBoundary = ctx.IsIsolatedTestProcess && !string.IsNullOrWhiteSpace(ctx.DataDirectory)
            ? InstallerArgsAndContainment.ResolveIsolatedBoundary(ctx.DataDirectory)
            : null;
        SandboxResolution? sandbox = !ctx.IsIsolatedTestProcess && ctx.IsSandboxActive ? ctx.Sandbox : null;

        string explicitSuite = !string.IsNullOrWhiteSpace(overrideSecondaryDir)
            ? InstallerArgsAndContainment.NormalizeSuiteRoot(overrideSecondaryDir)
            : resolved.SuiteRootDir;

        (string? foundReDir, SemVer? foundReVer) = DiscoverInstalledReEngine(
            explicitSuite, host, ctx, isolatedBoundary, sandbox);

        string effectiveSuiteRoot = foundReDir is not null
            ? InstallerArgsAndContainment.NormalizeSuiteRoot(foundReDir)
            : explicitSuite;
        string targetReEngineDir = foundReDir ?? ResolveTargetReEngineInstallDir(effectiveSuiteRoot);
        string targetEnginesRoot = Path.Combine(effectiveSuiteRoot, "engines");

        (string? cartExe, SemVer? cartVer) = DiscoverInstalledReCart(
            effectiveSuiteRoot, targetEnginesRoot, ctx, isolatedBoundary, sandbox);

        _ = SemVer.TryParse(ReEnginePackagingConstants.Version, out SemVer minReVer);
        _ = SemVer.TryParse(CartEngineConstants.Version, out SemVer minCartVer);
        bool needsRe = foundReVer is null || foundReVer.Value.CompareTo(minReVer) < 0;
        bool needsCart = cartExe is null || cartVer is null || cartVer.Value.CompareTo(minCartVer) < 0;

        string summary = $"reEngine={(foundReVer?.ToString() ?? "missing")} (need >= {ReEnginePackagingConstants.Version}), " +
                         $"reCart={(cartVer?.ToString() ?? "missing")} (need >= {CartEngineConstants.Version})";

        return new SuiteBootstrapPlan(
            effectiveSuiteRoot, targetReEngineDir, targetEnginesRoot,
            foundReVer?.ToString(), cartVer?.ToString(), cartExe,
            needsRe, needsCart, summary);
    }

    internal static async Task<BootstrapOutcome> EnsureFullProfileBootstrappedAsync(
        HostCliOptions host,
        InstallerRuntimeContext ctx,
        ResolvedInstallPaths resolved,
        string profileId,
        string? overrideSecondaryDir,
        Action<int, string>? reportProgress,
        CancellationToken ct = default)
    {
        string normalizedProfile = InstallerArgsAndContainment.NormalizeProfileId(profileId);
        if (!string.Equals(normalizedProfile, "full", StringComparison.OrdinalIgnoreCase))
        {
            reportProgress?.Invoke(20, "Lightweight profile: skipping suite runtime bootstrap.");
            return new BootstrapOutcome(true, resolved.SuiteRootDir, resolved.ReEngineInstallDir, "Lightweight profile selected.");
        }

        SuiteBootstrapPlan plan = InspectSuite(host, ctx, resolved, overrideSecondaryDir);
        string suiteRoot = EnforceWritablePath(plan.SuiteRootDir, ctx);
        string targetReDir = EnforceWritablePath(plan.TargetReEngineDir, ctx);

        if (!plan.NeedsReEngine && !plan.NeedsReCart)
        {
            reportProgress?.Invoke(48, $"Verified existing suite ({plan.Summary}).");
            return new BootstrapOutcome(true, suiteRoot, targetReDir, $"Suite ready: {plan.Summary}");
        }

        if (ctx.DryRun)
        {
            return new BootstrapOutcome(true, suiteRoot, targetReDir, $"[DRY-RUN] Would bootstrap {plan.Summary} into '{suiteRoot}'.");
        }

        string? isolatedBoundary = ctx.IsIsolatedTestProcess && !string.IsNullOrWhiteSpace(ctx.DataDirectory)
            ? InstallerArgsAndContainment.ResolveIsolatedBoundary(ctx.DataDirectory)
            : null;
        string extractRoot = CreateTempExtractRoot(ctx, isolatedBoundary);
        try
        {
            Assembly asm = typeof(InstallerBootstrapHelper).Assembly;
            if (plan.NeedsReEngine)
            {
                reportProgress?.Invoke(18, $"Extracting bundled reEngine v{ReEnginePackagingConstants.Version} setup...");
                string reEngineExe = Path.Combine(extractRoot, "reEngine-Setup.exe");
                if (!await TryExtractGzipResourceAsync(asm, ReEngineResourceName, reEngineExe, ct).ConfigureAwait(false))
                {
                    return new BootstrapOutcome(false, suiteRoot, targetReDir,
                        BuildMissingBootstrapMessage("reEngine", ReEnginePackagingConstants.Version, plan.InstalledReEngineVersion));
                }

                reportProgress?.Invoke(28, $"Installing reEngine v{ReEnginePackagingConstants.Version} into '{targetReDir}'...");
                BootstrapOutcome reRes = await InstallReEngineAsync(
                    reEngineExe, targetReDir, suiteRoot, ctx, isolatedBoundary, ct).ConfigureAwait(false);
                if (!reRes.Success) return reRes;
                ctx.WriteLog("BOOTSTRAP", reRes.Message);
            }

            if (plan.NeedsReCart)
            {
                reportProgress?.Invoke(36, $"Extracting bundled reCart v{CartEngineConstants.Version} setup...");
                string reCartExe = Path.Combine(extractRoot, "reCart-Setup.exe");
                if (!await TryExtractGzipResourceAsync(asm, ReCartResourceName, reCartExe, ct).ConfigureAwait(false))
                {
                    return new BootstrapOutcome(false, suiteRoot, targetReDir,
                        BuildMissingBootstrapMessage("reCart", CartEngineConstants.Version, plan.InstalledReCartVersion));
                }

                Directory.CreateDirectory(EnforceWritablePath(Path.Combine(suiteRoot, "engines"), ctx));
                string targetCartDir = ReEngineBootstrapHelper.BuildEngineVersionInstallDir(
                    targetReDir, CartEngineConstants.EngineId, CartEngineConstants.Version);
                targetCartDir = EnforceWritablePath(targetCartDir, ctx);
                var args = new List<string>
                {
                    "--silent", "--no-launch", "--no-shortcuts",
                    "--reengine-dir", targetReDir,
                    "--suite-dir", suiteRoot,
                    "--install-dir", targetCartDir
                };
                if (ctx.IsIsolatedTestProcess && !string.IsNullOrWhiteSpace(isolatedBoundary))
                {
                    args.Add("--data-dir");
                    args.Add(isolatedBoundary);
                }

                reportProgress?.Invoke(44, $"Installing reCart v{CartEngineConstants.Version} into '{targetCartDir}'...");
                int code = await RunBootstrapExeAsync(reCartExe, args.ToArray(), ctx.IsIsolatedTestProcess, ct).ConfigureAwait(false);
                if (code != 0)
                {
                    return new BootstrapOutcome(false, suiteRoot, targetReDir,
                        $"Bundled reCart v{CartEngineConstants.Version} installer exited with code {code}.");
                }
                ctx.WriteLog("BOOTSTRAP", $"Installed reCart v{CartEngineConstants.Version} at '{targetCartDir}'.");
            }

            reportProgress?.Invoke(50, $"Suite bootstrap complete (reEngine v{ReEnginePackagingConstants.Version} + reCart v{CartEngineConstants.Version}).");
            return new BootstrapOutcome(true, suiteRoot, targetReDir, $"Full profile bootstrapped suite under '{suiteRoot}'.");
        }
        finally
        {
            TryDeleteDir(extractRoot);
        }
    }

    private static (string? Dir, SemVer? Version) DiscoverInstalledReEngine(
        string explicitSuite, HostCliOptions host, InstallerRuntimeContext ctx, string? isoBoundary, SandboxResolution? sandbox)
    {
        var candidates = new List<string>();
        void AddSuiteCandidate(string? suiteOrEngine)
        {
            if (string.IsNullOrWhiteSpace(suiteOrEngine)) return;
            string norm = InstallerArgsAndContainment.SafeFullTrim(suiteOrEngine);
            candidates.Add(norm);
            candidates.Add(InstallerPathResolver.NormalizeSuiteRoot(norm));
            if (!string.Equals(Path.GetFileName(norm), ReEnginePackagingConstants.SuiteName, StringComparison.OrdinalIgnoreCase))
                candidates.Add(Path.Combine(norm, ReEnginePackagingConstants.SuiteName));
        }

        AddSuiteCandidate(explicitSuite);
        if (!string.IsNullOrWhiteSpace(host.ReEngineDir)) AddSuiteCandidate(host.ReEngineDir);
        if (!string.IsNullOrWhiteSpace(host.SuiteDir)) AddSuiteCandidate(host.SuiteDir);

        if (!ctx.IsIsolatedTestProcess && !ctx.ShouldSuppressHostMutations)
        {
            string? regLoc = ctx.Registry.GetValue(PlatformRegistryHive.CurrentUser, ReEngineUninstallSubKey, "InstallLocation")?.AsString();
            if (!string.IsNullOrWhiteSpace(regLoc) && Path.IsPathFullyQualified(regLoc.Trim()))
            {
                string full = InstallerArgsAndContainment.SafeFullTrim(regLoc);
                if (sandbox is not { IsSandboxActive: true } || SandboxEnvironmentSeam.IsPathWithinSandbox(full, sandbox))
                    AddSuiteCandidate(full);
            }
        }

        string defaultSuite = ctx.IsIsolatedTestProcess
            ? ReEngineBootstrapHelper.ResolveDefaultSuiteInstallDir(null, isoBoundary, isolatedTest: true)
            : ReEngineBootstrapHelper.ResolveDefaultSuiteInstallDir();
        if (sandbox is { IsSandboxActive: true })
            defaultSuite = KnownFolderPaths.MapHostPathToSandbox(defaultSuite, sandbox);
        AddSuiteCandidate(defaultSuite);

        foreach (string dir in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (TryReadInstalledReEngineVersion(dir, out SemVer ver))
                return (dir, ver);
        }
        return (null, null);
    }

    private static (string? ExePath, SemVer? Version) DiscoverInstalledReCart(
        string suiteRoot, string enginesRoot, InstallerRuntimeContext ctx, string? isoBoundary, SandboxResolution? sandbox)
    {
        var engineRoots = new List<string> { enginesRoot };
        if (!ctx.IsIsolatedTestProcess)
        {
            string programsRoot = KnownFolderPaths.ResolveLocalProgramsDirectory(sandbox);
            string umbrellaLeaf = Path.GetFullPath(Path.Combine(programsRoot, CartEngineConstants.UmbrellaSubpath));
            if (Directory.GetParent(umbrellaLeaf)?.Parent is { } umbEnginesRoot)
                engineRoots.Add(umbEnginesRoot.FullName);

            if (!ctx.ShouldSuppressHostMutations)
            {
                string? regCart = ctx.Registry.GetValue(PlatformRegistryHive.CurrentUser, ReCartUninstallSubKey, "InstallLocation")?.AsString();
                if (!string.IsNullOrWhiteSpace(regCart) && Path.IsPathFullyQualified(regCart.Trim()))
                {
                    string full = InstallerArgsAndContainment.SafeFullTrim(regCart);
                    if ((sandbox is not { IsSandboxActive: true } || SandboxEnvironmentSeam.IsPathWithinSandbox(full, sandbox)) &&
                        Directory.GetParent(full)?.Parent is { } regEngines)
                        engineRoots.Add(regEngines.FullName);
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(isoBoundary))
        {
            engineRoots.Add(Path.Combine(isoBoundary, "reEngine", "engines"));
        }

        _ = suiteRoot;
        string? bestExe = null;
        SemVer? bestVer = null;
        bool bestIsCanonical = false;
        IReadOnlyList<string> aliasIds = EngineAliasCatalog.GetEquivalentIds(CartEngineConstants.EngineId);

        foreach (string root in engineRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root)) continue;
            foreach (string id in aliasIds)
            {
                string idDir = Path.Combine(root, id);
                if (!Directory.Exists(idDir)) continue;
                bool isCanonical = id.Equals(CartEngineConstants.EngineId, StringComparison.OrdinalIgnoreCase);

                foreach (string verDir in Directory.EnumerateDirectories(idDir))
                {
                    string leaf = Path.GetFileName(verDir);
                    if (leaf.StartsWith(".staging-", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!SemVer.TryParse(leaf, out SemVer ver) &&
                        !TryReadVersionFromManifest(Path.Combine(verDir, "engine.manifest.json"), out ver))
                        continue;
                    if (!EngineTrackMatcher.IsTrackCompatible(CartEngineConstants.CompatibleEngineTrack, ver) || !IsReadyEngineDir(verDir, ver))
                        continue;

                    string? exe = FindCartExe(verDir);
                    if (exe is null) continue;

                    int cmp = bestVer is null ? 1 : ver.CompareTo(bestVer.Value);
                    if (cmp > 0 || (cmp == 0 && isCanonical && !bestIsCanonical))
                    {
                        bestVer = ver;
                        bestExe = exe;
                        bestIsCanonical = isCanonical;
                    }
                }
            }
        }
        return (bestExe, bestVer);
    }

    private static async Task<BootstrapOutcome> InstallReEngineAsync(
        string reEngineExe, string targetReDir, string suiteRoot, InstallerRuntimeContext ctx, string? isoBoundary, CancellationToken ct)
    {
        Directory.CreateDirectory(EnforceWritablePath(Path.Combine(suiteRoot, "engines"), ctx));
        if (ExternalBootstrapRunnerForTests is { } runner)
        {
            var args = new List<string> { "--silent", "--no-launch", "--no-shortcuts", "--suite-dir", suiteRoot, "--install-dir", targetReDir };
            if (ctx.IsIsolatedTestProcess && !string.IsNullOrWhiteSpace(isoBoundary)) { args.Add("--data-dir"); args.Add(isoBoundary); }
            int code = await runner(reEngineExe, args.ToArray(), ct).ConfigureAwait(false);
            return code == 0
                ? new BootstrapOutcome(true, suiteRoot, targetReDir, $"Installed reEngine into '{targetReDir}'.")
                : new BootstrapOutcome(false, suiteRoot, targetReDir, $"Bundled reEngine setup exited with code {code}.");
        }

        ReEngineBootstrapResult res = await ReEngineBootstrapHelper.EnsureReEngineInstalledAsync(
            reEngineExe, targetReDir, !ctx.ShouldSuppressHostMutations, ctx.IsIsolatedTestProcess, isoBoundary, ct).ConfigureAwait(false);
        return new BootstrapOutcome(res.Success, suiteRoot, res.ReEngineInstallDir, res.DiagnosticMessage);
    }

    private static string ResolveTargetReEngineInstallDir(string suiteRoot) =>
        InstallerPathResolver.NormalizeSuiteRoot(suiteRoot);

    private static bool TryReadInstalledReEngineVersion(string dir, out SemVer version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return false;
        string? exe = new[] { Path.Combine(dir, "reEngine.exe"), Path.Combine(dir, "app", "reEngine.exe") }.FirstOrDefault(File.Exists);
        if (exe is null) return false;
        foreach (string txt in new[] { Path.Combine(dir, "app", "version.txt"), Path.Combine(dir, "version.txt") })
            if (File.Exists(txt) && SemVer.TryParse(File.ReadAllText(txt).Trim(), out version)) return true;
        foreach (string mf in new[] { Path.Combine(dir, "app", "install-manifest.json"), Path.Combine(dir, "install-manifest.json") })
            if (TryReadVersionFromManifest(mf, out version)) return true;
        FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(exe);
        return SemVer.TryParse(fvi.ProductVersion ?? fvi.FileVersion, out version);
    }

    private static bool TryReadVersionFromManifest(string path, out SemVer version)
    {
        version = default;
        if (!File.Exists(path)) return false;
        try { using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path)); return doc.RootElement.TryGetProperty("version", out JsonElement p) && SemVer.TryParse(p.GetString(), out version); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return false; }
    }

    private static bool IsReadyEngineDir(string dir, SemVer version) =>
        File.Exists(Path.Combine(dir, ".ready")) ||
        (version.CompareTo(SemVer.Track030Threshold) < 0 && !Directory.EnumerateFiles(dir, "*.part", SearchOption.TopDirectoryOnly).Any());

    private static string? FindCartExe(string versionDir)
    {
        foreach (string id in EngineAliasCatalog.GetEquivalentIds(CartEngineConstants.EngineId))
        {
            foreach (string candidate in new[]
            {
                Path.Combine(versionDir, $"{id}.exe"),
                Path.Combine(versionDir, "app", $"{id}.Player.exe"),
                Path.Combine(versionDir, $"{id}.Player.exe"),
                Path.Combine(versionDir, "app", $"{id}.exe")
            })
            {
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    private static async Task<bool> TryExtractGzipResourceAsync(Assembly asm, string resourceName, string destExePath, CancellationToken ct)
    {
        Stream? raw = ResourceStreamOpenerForTests?.Invoke(resourceName) ?? asm.GetManifestResourceStream(resourceName);
        if (raw is null) return false;
        await using (raw.ConfigureAwait(false))
        await using (var gzip = new GZipStream(raw, CompressionMode.Decompress))
        await using (var fs = File.Create(destExePath))
        {
            await gzip.CopyToAsync(fs, ct).ConfigureAwait(false);
            await fs.FlushAsync(ct).ConfigureAwait(false);
        }
        return File.Exists(destExePath);
    }

    private static async Task<int> RunBootstrapExeAsync(string exePath, string[] args, bool isolatedTest, CancellationToken ct)
    {
        if (ExternalBootstrapRunnerForTests is { } runner)
            return await runner(exePath, args, ct).ConfigureAwait(false);

        var psi = new ProcessStartInfo(exePath) { UseShellExecute = false, CreateNoWindow = true };
        if (isolatedTest) psi.Environment["APP_ISOLATED_TEST"] = "1";
        foreach (string a in args) psi.ArgumentList.Add(a);
        using var guard = new JobObjectGuard();
        using Process? proc = Process.Start(psi);
        if (proc is null) return 1;
        guard.TryAttachProcess(proc);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        return proc.ExitCode;
    }

    private static bool HasResource(Assembly asm, string name) =>
        ResourceStreamOpenerForTests is not null ? true : asm.GetManifestResourceInfo(name) is not null;

    private static string EnforceWritablePath(string candidate, InstallerRuntimeContext ctx)
    {
        string full = Path.GetFullPath(candidate);
        if (ctx.IsIsolatedTestProcess && !string.IsNullOrWhiteSpace(ctx.DataDirectory))
            return AppEnvironment.AssertPathContained(full, InstallerArgsAndContainment.ResolveIsolatedBoundary(ctx.DataDirectory));
        if (ctx.IsSandboxActive)
            return SandboxEnvironmentSeam.AssertSandboxPathIfActive(full, ctx.Sandbox);
        return full;
    }

    private static string CreateTempExtractRoot(InstallerRuntimeContext ctx, string? isolatedBoundary)
    {
        string baseDir = isolatedBoundary is not null
            ? isolatedBoundary
            : (ctx.IsSandboxActive && !string.IsNullOrWhiteSpace(ctx.Sandbox.SandboxRoot) ? ctx.Sandbox.SandboxRoot : Path.GetTempPath());
        string dir = Path.Combine(baseDir, "_mgr_boot_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string BuildMissingBootstrapMessage(string component, string requiredVersion, string? installedVersion) =>
        $"Full profile requires {component} >= {requiredVersion} (found: {installedVersion ?? "none"}). " +
        $"Online Setup does not download external engine installers at runtime. " +
        $"Remedy: Run {InstallerArgsAndContainment.ProductName}-Offline-Setup.exe (bundles {component}), install {component} >= {requiredVersion} under --suite-dir, or choose --profile lightweight.";

    private static void TryDeleteDir(string path)
    {
        if (!Directory.Exists(path)) return;
        try { Directory.Delete(path, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

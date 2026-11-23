// @module: IronFrameCart01.Installer.InstallerPathResolver
// @purpose: Suite-first coded install path resolution, umbrella deduplication, and HKCU LastInstallBaseDir repair.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, Foundation.Core, Foundation.Installer, reEngine.Core.Bootstrap
// @exports: ResolvedInstallPaths, InstallerPathResolver

using Foundation.Core;
using Foundation.Installer;
using reEngine.Core.Bootstrap;

namespace IronFrameCart01.Installer;

internal sealed record ResolvedInstallPaths(string GameInstallDir, string SuiteRootDir, string ReEngineInstallDir);

internal static class InstallerPathResolver
{
    internal const string ProductUninstallSubKey = $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{InstallerArgsAndContainment.ProductName}";
    internal const string ReEngineUninstallSubKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\reEngine";
    internal const string ReCartUninstallSubKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\reCart";
    internal const string ReSlateUninstallSubKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\reSlate";

    internal static bool IsMisnestedInstallPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string[] segs = SplitRelativeSegments(SafeFullTrim(path), out _);
        int studioHits = 0, suiteHits = 0;
        foreach (string seg in segs)
        {
            if (EqualsIgnoreCase(seg, "engines")) return true;
            if (EqualsIgnoreCase(seg, ReEnginePackagingConstants.StudioName)) studioHits++;
            if (EqualsIgnoreCase(seg, ReEnginePackagingConstants.SuiteName)) suiteHits++;
        }
        return studioHits >= 2 || suiteHits >= 2;
    }

    internal static bool TryCollapseUmbrellaNesting(string? rawPath, out string normalizedGameDir)
    {
        normalizedGameDir = string.Empty;
        if (string.IsNullOrWhiteSpace(rawPath)) return false;
        string[] segs = SplitRelativeSegments(SafeFullTrim(rawPath), out string root);

        int engIdx = FindIndexIgnoreCase(segs, "engines");
        int studioCount = CountIgnoreCase(segs, ReEnginePackagingConstants.StudioName);
        int reCount = CountIgnoreCase(segs, ReEnginePackagingConstants.SuiteName);
        int studioIdx = FindIndexIgnoreCase(segs, ReEnginePackagingConstants.StudioName);
        int reIdx = FindIndexIgnoreCase(segs, ReEnginePackagingConstants.SuiteName);
        bool studioDirectGames = studioIdx >= 0 && studioIdx + 1 < segs.Length && EqualsIgnoreCase(segs[studioIdx + 1], "games");
        bool reDirectGame = reIdx >= 0 && reIdx + 1 == segs.Length - 1 && EqualsIgnoreCase(segs[reIdx + 1], InstallerArgsAndContainment.ProductName);

        if (engIdx < 0 && studioCount < 2 && reCount < 2 && !studioDirectGames && !reDirectGame)
            return false;

        string reRoot;
        if (engIdx >= 0)
        {
            List<string> prefix = segs.Take(engIdx).ToList();
            int firstRe = prefix.FindIndex(s => EqualsIgnoreCase(s, ReEnginePackagingConstants.SuiteName));
            int firstStudio = prefix.FindIndex(s => EqualsIgnoreCase(s, ReEnginePackagingConstants.StudioName));
            reRoot = firstRe >= 0 ? RebuildPath(root, prefix.Take(firstRe + 1))
                : firstStudio >= 0 ? Path.Combine(RebuildPath(root, prefix.Take(firstStudio + 1)), ReEnginePackagingConstants.SuiteName)
                : prefix.Count > 0 ? Path.Combine(RebuildPath(root, prefix), ReEnginePackagingConstants.SuiteName)
                : Path.Combine(root, ReEnginePackagingConstants.StudioName, ReEnginePackagingConstants.SuiteName);
        }
        else if (studioCount >= 2 || studioDirectGames)
            reRoot = Path.Combine(RebuildPath(root, segs.Take(studioIdx + 1)), ReEnginePackagingConstants.SuiteName);
        else
            reRoot = RebuildPath(root, segs.Take(reIdx + 1));

        normalizedGameDir = Path.Combine(reRoot, "games", InstallerArgsAndContainment.ProductName);
        return true;
    }

    internal static string NormalizeSuiteRoot(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath)) return string.Empty;
        string[] segs = SplitRelativeSegments(SafeFullTrim(rawPath), out string root);

        int firstRe = FindIndexIgnoreCase(segs, ReEnginePackagingConstants.SuiteName);
        if (firstRe >= 0) return RebuildPath(root, segs.Take(firstRe + 1));

        int firstStudio = FindIndexIgnoreCase(segs, ReEnginePackagingConstants.StudioName);
        if (firstStudio >= 0)
            return Path.Combine(RebuildPath(root, segs.Take(firstStudio + 1)), ReEnginePackagingConstants.SuiteName);

        int cut = segs.Length;
        if (cut >= 2 && EqualsIgnoreCase(segs[^2], "games") && EqualsIgnoreCase(segs[^1], InstallerArgsAndContainment.ProductName))
            cut -= 2;
        else if (cut >= 1 && (EqualsIgnoreCase(segs[^1], "games") || EqualsIgnoreCase(segs[^1], "engines")))
            cut -= 1;
        else
        {
            int engIdx = FindIndexIgnoreCase(segs, "engines");
            if (engIdx >= 0) cut = engIdx;
        }

        string candidate = RebuildPath(root, segs.Take(cut));
        if (HasDirectReEngineMarkers(candidate)) return candidate;
        string nestedStudioRe = Path.Combine(candidate, ReEnginePackagingConstants.StudioName, ReEnginePackagingConstants.SuiteName);
        return Directory.Exists(nestedStudioRe) ? nestedStudioRe : Path.Combine(candidate, ReEnginePackagingConstants.SuiteName);
    }

    internal static string NormalizeReEngineDir(string? rawPath) => NormalizeSuiteRoot(rawPath);

    internal static string NormalizeGameInstallDir(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath)) return string.Empty;
        if (TryCollapseUmbrellaNesting(rawPath, out string collapsed)) return collapsed;

        string norm = SafeFullTrim(rawPath);
        string leaf = Path.GetFileName(norm);
        if (EqualsIgnoreCase(leaf, ReEnginePackagingConstants.StudioName))
            return Path.Combine(norm, ReEnginePackagingConstants.SuiteName, "games", InstallerArgsAndContainment.ProductName);
        if (EqualsIgnoreCase(leaf, ReEnginePackagingConstants.SuiteName))
            return Path.Combine(norm, "games", InstallerArgsAndContainment.ProductName);
        if (EqualsIgnoreCase(leaf, "games"))
            return Path.Combine(norm, InstallerArgsAndContainment.ProductName);
        return norm;
    }

    internal static string NormalizeWizardGameInstallDir(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath)) return string.Empty;
        string norm = SafeFullTrim(rawPath);
        string cliNorm = NormalizeGameInstallDir(norm);
        if (!PathsEqual(cliNorm, norm)) return cliNorm;

        if (EqualsIgnoreCase(Path.GetFileName(norm), InstallerArgsAndContainment.ProductName) || HasDirectGameMarkers(norm))
            return norm;

        return Path.Combine(norm, ReEnginePackagingConstants.StudioName, ReEnginePackagingConstants.SuiteName,
            "games", InstallerArgsAndContainment.ProductName);
    }

    internal static string? TrySanitizeLastInstallBaseDir(string? rawBaseDir)
    {
        if (string.IsNullOrWhiteSpace(rawBaseDir)) return null;
        string[] segs = SplitRelativeSegments(SafeFullTrim(rawBaseDir), out string root);
        int studioIdx = FindIndexIgnoreCase(segs, ReEnginePackagingConstants.StudioName);
        if (studioIdx >= 0) return studioIdx == 0 ? null : RebuildPath(root, segs.Take(studioIdx));

        int reIdx = FindIndexIgnoreCase(segs, ReEnginePackagingConstants.SuiteName);
        if (reIdx >= 0) return reIdx == 0 ? null : RebuildPath(root, segs.Take(reIdx));

        int end = segs.Length;
        int engIdx = FindIndexIgnoreCase(segs, "engines");
        if (engIdx >= 0) end = engIdx;
        else if (end >= 2 && EqualsIgnoreCase(segs[^2], "games") && EqualsIgnoreCase(segs[^1], InstallerArgsAndContainment.ProductName))
            end -= 2;
        return end <= 0 ? null : RebuildPath(root, segs.Take(end));
    }

    internal static string? TryExtractCleanBaseDirAboveSuite(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string[] segs = SplitRelativeSegments(SafeFullTrim(path), out string root);
        int studioIdx = FindIndexIgnoreCase(segs, ReEnginePackagingConstants.StudioName);
        return studioIdx > 0 ? RebuildPath(root, segs.Take(studioIdx)) : null;
    }

    internal static bool TryRepairLastInstallBaseDir(
        IPlatformRegistry registry, string installDir, SandboxResolution sandbox, bool suppressHostMutations)
    {
        if (suppressHostMutations || registry is NoOpPlatformRegistry) return false;
        string? cleanBase = TryExtractCleanBaseDirAboveSuite(installDir)
            ?? TrySanitizeLastInstallBaseDir(registry.GetValue(PlatformRegistryHive.CurrentUser, InstallerEngine.SharedStreamItAllRegistrySubKey, InstallerEngine.LastInstallBaseDirValueName)?.AsString());
        if (!InstallerEngine.TryValidateLastInstallBaseDir(cleanBase, sandbox, out string validBase)) return false;
        try
        {
            registry.SetValue(PlatformRegistryHive.CurrentUser, InstallerEngine.SharedStreamItAllRegistrySubKey,
                InstallerEngine.LastInstallBaseDirValueName, validBase, PlatformRegistryValueKind.String);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    internal static string? TryExtractSuiteRootFromGameDir(string? gameDir)
    {
        if (string.IsNullOrWhiteSpace(gameDir)) return null;
        DirectoryInfo? parent = Directory.GetParent(SafeFullTrim(gameDir));
        return parent is not null && EqualsIgnoreCase(parent.Name, "games") && parent.Parent is { } suite
            ? suite.FullName
            : null;
    }

    internal static ResolvedInstallPaths ResolveInitialPaths(
        HostCliOptions host, InstallerRuntimeContext ctx, InstallerAppDescriptor desc, string? cliInstallDir = null)
    {
        SandboxResolution? sandbox = !ctx.IsIsolatedTestProcess && ctx.IsSandboxActive ? ctx.Sandbox : null;
        string? rawInstall = !string.IsNullOrWhiteSpace(host.InstallDir) ? host.InstallDir : cliInstallDir;
        string? rawSuite = !string.IsNullOrWhiteSpace(host.ReEngineDir) ? host.ReEngineDir : host.SuiteDir;
        string? explicitSuite = !string.IsNullOrWhiteSpace(rawSuite) ? NormalizeSuiteRoot(rawSuite) : null;

        if (ctx.IsIsolatedTestProcess)
        {
            string boundary = !string.IsNullOrWhiteSpace(ctx.DataDirectory) ? InstallerArgsAndContainment.ResolveIsolatedBoundary(ctx.DataDirectory) : AppContext.BaseDirectory;
            string isoSuite = explicitSuite ?? Path.Combine(boundary, ReEnginePackagingConstants.SuiteName);
            string isoGame = !string.IsNullOrWhiteSpace(rawInstall)
                ? (host.Uninstall ? SafeFullTrim(rawInstall) : NormalizeGameInstallDir(rawInstall))
                : (!string.IsNullOrWhiteSpace(rawSuite)
                    ? Path.Combine(isoSuite, "games", InstallerArgsAndContainment.ProductName)
                    : (!string.IsNullOrWhiteSpace(ctx.DataDirectory) ? Path.Combine(ctx.DataDirectory, "installed-app") : desc.ResolveCanonicalDefaultInstallDir(null)));
            return new ResolvedInstallPaths(isoGame, isoSuite, isoSuite);
        }

        string? regGame = null, validExistingGame = null, collapsedFromMisnested = null;
        if (!ctx.ShouldSuppressHostMutations && TryReadRegPath(ctx.Registry, ProductUninstallSubKey, "InstallLocation", sandbox) is { } foundGame)
        {
            regGame = foundGame;
            if (IsMisnestedInstallPath(foundGame) && TryCollapseUmbrellaNesting(foundGame, out string clean))
                collapsedFromMisnested = clean;
            else
                validExistingGame = foundGame;
        }

        string? regReEngine = !ctx.ShouldSuppressHostMutations ? TryReadRegPath(ctx.Registry, ReEngineUninstallSubKey, "InstallLocation", sandbox) : null;
        string? suiteFromEnv = ResolveValidatedEnvReEngine(sandbox);
        string? suiteFromEngine = !ctx.ShouldSuppressHostMutations ? ResolveSuiteFromSiblingKeys(ctx.Registry, sandbox) : null;
        string? sanitizedLastBase = !ctx.ShouldSuppressHostMutations
            ? TrySanitizeLastInstallBaseDir(TryReadRegPath(ctx.Registry, InstallerEngine.SharedStreamItAllRegistrySubKey, InstallerEngine.LastInstallBaseDirValueName, sandbox))
            : null;
        string? discoveredSuite = (regReEngine is not null ? NormalizeSuiteRoot(regReEngine) : null) ?? suiteFromEnv ?? suiteFromEngine;

        string gameDir = !string.IsNullOrWhiteSpace(rawInstall)
            ? (host.Uninstall ? SafeFullTrim(rawInstall) : NormalizeGameInstallDir(rawInstall))
            : validExistingGame ?? collapsedFromMisnested
            ?? (explicitSuite is not null ? Path.Combine(explicitSuite, "games", InstallerArgsAndContainment.ProductName) : null)
            ?? (discoveredSuite is not null ? Path.Combine(discoveredSuite, "games", InstallerArgsAndContainment.ProductName) : null)
            ?? (sanitizedLastBase is not null ? Path.GetFullPath(Path.Combine(sanitizedLastBase, desc.GetEffectiveUmbrellaSubpath())) : null)
            ?? Path.GetFullPath(desc.ResolveCanonicalDefaultInstallDir(sandbox));

        string suiteRoot = explicitSuite
            ?? (regReEngine is not null ? NormalizeSuiteRoot(regReEngine) : null)
            ?? TryExtractSuiteRootFromGameDir(gameDir)
            ?? discoveredSuite
            ?? (sanitizedLastBase is not null ? Path.Combine(sanitizedLastBase, ReEnginePackagingConstants.StudioName, ReEnginePackagingConstants.SuiteName) : null)
            ?? DeriveCanonicalSuiteRoot(sandbox);

        if (sandbox is { IsSandboxActive: true })
        {
            gameDir = SandboxEnvironmentSeam.AssertSandboxPathIfActive(gameDir, sandbox);
            suiteRoot = SandboxEnvironmentSeam.AssertSandboxPathIfActive(suiteRoot, sandbox);
        }
        return new ResolvedInstallPaths(gameDir, suiteRoot, suiteRoot);
    }

    internal static string DeriveCanonicalSuiteRoot(SandboxResolution? sandbox) =>
        Path.Combine(KnownFolderPaths.ResolveLocalProgramsDirectory(sandbox), ReEnginePackagingConstants.StudioName, ReEnginePackagingConstants.SuiteName);

    internal static string SafeFullTrim(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        string trimmed = raw.Trim().Trim('"');
        try { string full = Path.GetFullPath(trimmed); return IsDriveRoot(full) ? full : Path.TrimEndingDirectorySeparator(full); }
        catch { return Path.TrimEndingDirectorySeparator(trimmed); }
    }

    internal static bool PathsEqual(string a, string b) => EqualsIgnoreCase(SafeFullTrim(a), SafeFullTrim(b));
    internal static bool IsDriveRoot(string full) => EqualsIgnoreCase(full, Path.GetPathRoot(full));

    private static string? ResolveSuiteFromSiblingKeys(IPlatformRegistry reg, SandboxResolution? sandbox)
    {
        foreach (string key in new[] { ReCartUninstallSubKey, ReSlateUninstallSubKey })
            if (TryReadRegPath(reg, key, "InstallLocation", sandbox) is { } p) return NormalizeSuiteRoot(p);
        return null;
    }

    private static string? ResolveValidatedEnvReEngine(SandboxResolution? sandbox)
    {
        string? raw = Environment.GetEnvironmentVariable("REENGINE_HOME");
        if (string.IsNullOrWhiteSpace(raw) || !Path.IsPathFullyQualified(raw.Trim())) return null;
        string norm = NormalizeSuiteRoot(raw.Trim().Trim('"'));
        return sandbox is { IsSandboxActive: true } && !SandboxEnvironmentSeam.IsPathWithinSandbox(norm, sandbox) ? null : norm;
    }

    private static string? TryReadRegPath(IPlatformRegistry reg, string subKey, string valName, SandboxResolution? sandbox)
    {
        string? raw = reg.GetValue(PlatformRegistryHive.CurrentUser, subKey, valName)?.AsString();
        if (string.IsNullOrWhiteSpace(raw) || !Path.IsPathFullyQualified(raw.Trim())) return null;
        string full = SafeFullTrim(raw);
        return sandbox is { IsSandboxActive: true } && !SandboxEnvironmentSeam.IsPathWithinSandbox(full, sandbox) ? null : full;
    }

    private static bool HasDirectReEngineMarkers(string dir) => Directory.Exists(dir) &&
        (File.Exists(Path.Combine(dir, "reEngine.exe")) || File.Exists(Path.Combine(dir, "app", "reEngine.exe")) ||
         File.Exists(Path.Combine(dir, "app", "reEngine-Uninstall.exe")) || Directory.Exists(Path.Combine(dir, "engines")));

    private static bool HasDirectGameMarkers(string dir) => Directory.Exists(dir) &&
        (File.Exists(Path.Combine(dir, ".ready")) || File.Exists(Path.Combine(dir, InstallerArgsAndContainment.MainExecutable)) ||
         File.Exists(Path.Combine(dir, "app", InstallerArgsAndContainment.ModuleArchiveName)));

    private static string[] SplitRelativeSegments(string fullTrimmed, out string root)
    {
        root = Path.GetPathRoot(fullTrimmed) ?? string.Empty;
        string rel = root.Length > 0 && fullTrimmed.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? fullTrimmed[root.Length..] : fullTrimmed;
        return rel.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
    }

    private static string RebuildPath(string root, IEnumerable<string> segments) => segments.Aggregate(root, Path.Combine);
    private static int FindIndexIgnoreCase(string[] arr, string val) => Array.FindIndex(arr, s => EqualsIgnoreCase(s, val));
    private static int CountIgnoreCase(string[] arr, string val) => arr.Count(s => EqualsIgnoreCase(s, val));
    private static bool EqualsIgnoreCase(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

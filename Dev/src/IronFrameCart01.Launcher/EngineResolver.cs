// @module: IronFrameCart01.Launcher.EngineResolver
// @purpose: R01 §3.3 reCart player discovery (--reengine-dir -> --suite-dir -> bundled -> registry/installed) with EngineAliasCatalog and isolation.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Text.Json, Microsoft.Win32, Foundation.Core, reCart.Contracts, reEngine.Core.Bootstrap, reEngine.Core.Manifests, reEngine.Core.Resolution, reEngine.Core.Updates
// @exports: EngineSearchResult, EngineResolver

using System.Text.Json;
using Foundation.Core;
using Microsoft.Win32;
using reCart.Contracts;
using reEngine.Core.Bootstrap;
using reEngine.Core.Manifests;
using reEngine.Core.Resolution;
using reEngine.Core.Updates;

namespace IronFrameCart01.Launcher;

internal sealed record EngineSearchResult(string? PlayerExecutablePath, string DiagnosticMessage);

internal static class EngineResolver
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Resolves the <c>reCart</c> player executable following R01 §3.3 precedence and <c>APP_ISOLATED_TEST=1</c> containment.
    /// </summary>
    internal static EngineSearchResult ResolvePlayerExecutable(
        string baseDir,
        LauncherCliOptions options,
        AppEnvironment env,
        string? moduleOrRecompPath = null,
        string? selfProcessPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDir);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(env);

        bool isolated = LauncherCliAndContainment.IsIsolatedTestEnabled();
        string? boundary = isolated ? LauncherCliAndContainment.ResolveIsolatedBoundary(env.DataDirectory) : null;
        RecompPackageManifest manifest = LoadManifestOrDefault(moduleOrRecompPath);
        ReEngineSettings? settings = TryReadSettings(env.ConfigFilePath, isolated, boundary, env.Sandbox);

        string fullBase = Path.GetFullPath(baseDir);
        string? bundledEngines = new[] { Path.Combine(fullBase, "engines"), Path.Combine(fullBase, "app", "engines") }.FirstOrDefault(Directory.Exists);
        var resolver = new EngineVersionResolver();

        foreach (string root in EnumerateSearchRoots(fullBase, options, env, isolated, boundary))
        {
            if (TryResolveFromRoot(root, manifest, settings, bundledEngines, resolver, selfProcessPath, out EngineSearchResult? found))
                return AssertContainment(found!, env.Sandbox);
        }

        string minVer = string.IsNullOrWhiteSpace(manifest.EngineVersion) ? "0.1.0" : manifest.EngineVersion;
        string track = string.IsNullOrWhiteSpace(manifest.CompatibleEngineTrack) ? CartEngineConstants.CompatibleEngineTrack : manifest.CompatibleEngineTrack;
        return new EngineSearchResult(null, LauncherHelpText.FormatMissingEngineRemediation(manifest.EngineId, track, minVer));
    }

    private static bool TryResolveFromRoot(
        string rootOrLeaf, RecompPackageManifest manifest, ReEngineSettings? settings, string? bundledEnginesDir,
        EngineVersionResolver resolver, string? selfPath, out EngineSearchResult? result)
    {
        result = null;
        if (!Directory.Exists(rootOrLeaf)) return false;

        EngineResolutionResult res = resolver.ResolveForManifest(rootOrLeaf, manifest, settings, bundledEnginesDir);
        if (res.Found && res.SelectedEngine is { } sel)
        {
            string exe = FindPlayerExeInLeaf(sel.EngineDirectory, selfPath) ?? sel.ExecutablePath;
            if (!IsSelfExecutable(exe, selfPath) && File.Exists(exe))
            {
                result = new EngineSearchResult(Path.GetFullPath(exe), $"Resolved {sel.EngineId} v{sel.Version} at '{exe}'.");
                return true;
            }
        }

        string? leafExe = TryResolveSideBySideLeafFallback(rootOrLeaf, manifest, selfPath) ?? FindPlayerExeInLeaf(rootOrLeaf, selfPath);
        if (leafExe is null) return false;
        result = new EngineSearchResult(Path.GetFullPath(leafExe), $"Resolved {manifest.EngineId} player at '{leafExe}'.");
        return true;
    }

    private static IEnumerable<string> EnumerateSearchRoots(
        string fullBase, LauncherCliOptions options, AppEnvironment env, bool isolated, string? boundary)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> Raw()
        {
            if (!string.IsNullOrWhiteSpace(options.ReEngineDir))
            {
                string fullRe = Path.GetFullPath(options.ReEngineDir);
                yield return Path.Combine(fullRe, "engines");
                yield return ReEngineBootstrapHelper.ResolveEnginesRootDir(fullRe);
                yield return fullRe;
            }
            if (!string.IsNullOrWhiteSpace(options.SuiteDir))
            {
                string fullSuite = Path.GetFullPath(options.SuiteDir);
                yield return Path.Combine(fullSuite, "engines");
                yield return Path.Combine(fullSuite, "reEngine", "engines");
                yield return fullSuite;
            }
            yield return Path.Combine(fullBase, "engines");
            yield return Path.Combine(fullBase, "app", "engines");
            foreach (string id in EngineAliasCatalog.GetEquivalentIds(CartEngineConstants.EngineId))
            {
                yield return Path.Combine(fullBase, "app", id);
                yield return Path.Combine(fullBase, id);
            }
            yield return Path.Combine(fullBase, "app");

            if (isolated) yield break;

            foreach (string persisted in ReadPersistedRoots(env.ConfigFilePath, env.Sandbox)) yield return persisted;

            string? envHome = Environment.GetEnvironmentVariable("REENGINE_HOME");
            if (!string.IsNullOrWhiteSpace(envHome) && (!env.Sandbox.IsSandboxActive || SandboxEnvironmentSeam.IsPathWithinSandbox(envHome, env.Sandbox)))
            {
                string fullHome = Path.GetFullPath(envHome.Trim());
                yield return Path.Combine(fullHome, "engines");
                yield return ReEngineBootstrapHelper.ResolveEnginesRootDir(fullHome);
            }

            if (!env.Sandbox.IsSandboxActive)
            {
                foreach (string regRoot in EnumerateRegistryEngineRoots()) yield return regRoot;
                string suiteDefault = ReEngineBootstrapHelper.ResolveDefaultSuiteInstallDir();
                yield return Path.Combine(suiteDefault, "engines");
                yield return Path.Combine(suiteDefault, "reEngine", "engines");
            }

            string localPrograms = KnownFolderPaths.ResolveLocalProgramsDirectory(env.Sandbox);
            if (!string.IsNullOrWhiteSpace(localPrograms))
            {
                yield return Path.Combine(localPrograms, ReEnginePackagingConstants.StudioName, ReEnginePackagingConstants.SuiteName, "engines");
                yield return Path.Combine(localPrograms, ReEnginePackagingConstants.StudioName, ReEnginePackagingConstants.SuiteName, ReEnginePackagingConstants.SuiteName, "engines");
            }
        }

        foreach (string raw in Raw())
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string norm = Path.GetFullPath(raw).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (isolated && !IsWithinRoot(norm, boundary) && !IsWithinRoot(norm, fullBase)) continue;
            if (env.Sandbox.IsSandboxActive && !SandboxEnvironmentSeam.IsPathWithinSandbox(norm, env.Sandbox) && !IsWithinRoot(norm, fullBase)) continue;
            if (seen.Add(norm)) yield return norm;
        }
    }

    private static IEnumerable<string> ReadPersistedRoots(string? configPath, SandboxResolution sandbox)
    {
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath)) yield break;
        List<string> discovered = [];
        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(configPath));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) yield break;
            foreach (string key in new[] { "engineInstallDir", "reEngineInstallDir", "suiteInstallDir", "enginesDirectoryOverride" })
            {
                if (!doc.RootElement.TryGetProperty(key, out JsonElement el) || el.ValueKind != JsonValueKind.String) continue;
                string? val = el.GetString();
                if (string.IsNullOrWhiteSpace(val)) continue;
                string full = Path.GetFullPath(val.Trim());
                if (sandbox.IsSandboxActive && !SandboxEnvironmentSeam.IsPathWithinSandbox(full, sandbox)) continue;
                discovered.Add(full); discovered.Add(Path.Combine(full, "engines")); discovered.Add(Path.Combine(full, "reEngine", "engines"));
            }
        }
        catch { yield break; }
        foreach (string item in discovered) yield return item;
    }

    private static IEnumerable<string> EnumerateRegistryEngineRoots()
    {
        if (!OperatingSystem.IsWindows()) yield break;
        var roots = new List<string>();
        try
        {
            foreach (RegistryKey hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using RegistryKey? reKey = hive.OpenSubKey(@"Software\StreamItAll\reEngine", writable: false);
                foreach (string valName in new[] { "EnginesDir", "SuiteDir", "InstallDir", "" })
                {
                    if (reKey?.GetValue(valName) is not string s || string.IsNullOrWhiteSpace(s)) continue;
                    string full = Path.GetFullPath(s.Trim());
                    roots.Add(Path.Combine(full, "engines")); roots.Add(ReEngineBootstrapHelper.ResolveEnginesRootDir(full)); roots.Add(full);
                }
            }
            foreach (string subKey in new[] { @"Software\Classes\reEngine.RecompModule\shell\open\command", @"Software\Classes\reEngine.DecompModule\shell\open\command" })
            {
                using RegistryKey? cmdKey = Registry.CurrentUser.OpenSubKey(subKey, writable: false);
                string? exeDir = ExtractExeFromCommand(cmdKey?.GetValue(null) as string) is { } e ? Path.GetDirectoryName(e) : null;
                if (!string.IsNullOrWhiteSpace(exeDir)) { roots.Add(Path.Combine(exeDir, "engines")); roots.Add(ReEngineBootstrapHelper.ResolveEnginesRootDir(exeDir)); }
            }
        }
        catch { }
        foreach (string r in roots) yield return r;
    }

    private static string? TryResolveSideBySideLeafFallback(string rootDir, RecompPackageManifest manifest, string? selfPath)
    {
        SemVer.TryParse(manifest.EngineVersion, out SemVer minVer);
        string track = string.IsNullOrWhiteSpace(manifest.CompatibleEngineTrack) ? CartEngineConstants.CompatibleEngineTrack : manifest.CompatibleEngineTrack;

        foreach (string id in EngineAliasCatalog.GetEquivalentIds(manifest.EngineId))
        {
            string idDir = Directory.Exists(Path.Combine(rootDir, id)) ? Path.Combine(rootDir, id) : rootDir;
            if (!Directory.Exists(idDir)) continue;
            foreach (string dir in Directory.EnumerateDirectories(idDir)
                .Select(d => (Dir: d, Ok: SemVer.TryParse(Path.GetFileName(d), out SemVer v), Ver: v))
                .Where(x => x.Ok && !Path.GetFileName(x.Dir).StartsWith(".staging-", StringComparison.OrdinalIgnoreCase))
                .Where(x => x.Ver.CompareTo(SemVer.Track030Threshold) < 0 || File.Exists(Path.Combine(x.Dir, ".ready")))
                .Where(x => (minVer == default || x.Ver.CompareTo(minVer) >= 0) &&
                            EngineTrackMatcher.IsTrackCompatible(track, x.Ver) &&
                            !EngineCompatibilityEvaluator.IsVersionIncompatible(x.Ver, manifest.IncompatibleEngineVersions))
                .OrderByDescending(x => x.Ver)
                .Select(x => x.Dir))
            {
                if (FindPlayerExeInLeaf(dir, selfPath) is { } exe) return exe;
            }
        }
        return null;
    }

    internal static string? FindPlayerExeInLeaf(string leafDir, string? selfPath = null)
    {
        foreach (string id in EngineAliasCatalog.GetEquivalentIds(CartEngineConstants.EngineId))
        {
            foreach (string cand in new[]
            {
                Path.Combine(leafDir, "app", $"{id}.Player.exe"), Path.Combine(leafDir, $"{id}.Player.exe"),
                Path.Combine(leafDir, $"{id}.exe"), Path.Combine(leafDir, "app", $"{id}.exe")
            })
            {
                if (File.Exists(cand) && !IsSelfExecutable(cand, selfPath)) return cand;
            }
        }
        return null;
    }

    private static RecompPackageManifest LoadManifestOrDefault(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) { try { return RecompPackageReader.Load(path).Manifest; } catch { } }
        return new RecompPackageManifest
        {
            SchemaVersion = 1, ModuleId = LauncherHelpText.ModuleId, Title = LauncherHelpText.DisplayTitle,
            Version = LauncherHelpText.ProductVersion, EngineId = CartEngineConstants.EngineId, EngineVersion = LauncherHelpText.DefaultMinEngineVersion,
            CompatibleEngineTrack = CartEngineConstants.CompatibleEngineTrack, SupportedEngineTracks = [.. CartEngineConstants.SupportedEngineTracks]
        };
    }

    private static ReEngineSettings? TryReadSettings(string configPath, bool isolated, string? boundary, SandboxResolution sandbox)
    {
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath)) return null;
        try
        {
            ReEngineSettings? settings = JsonSerializer.Deserialize<ReEngineSettings>(File.ReadAllText(configPath), JsonOpts);
            if (settings is not null && !string.IsNullOrWhiteSpace(settings.EnginesDirectoryOverride) &&
                ((isolated && !IsWithinRoot(settings.EnginesDirectoryOverride, boundary)) ||
                 (sandbox.IsSandboxActive && !SandboxEnvironmentSeam.IsPathWithinSandbox(settings.EnginesDirectoryOverride, sandbox))))
                settings.EnginesDirectoryOverride = null;
            return settings;
        }
        catch { return null; }
    }

    private static string? ExtractExeFromCommand(string? cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd)) return null;
        string t = cmd.Trim();
        if (t.StartsWith('"')) { int end = t.IndexOf('"', 1); return end > 1 ? t[1..end] : null; }
        int sp = t.IndexOf(' ');
        return sp > 0 ? t[..sp] : t;
    }

    private static EngineSearchResult AssertContainment(EngineSearchResult found, SandboxResolution sandbox)
    {
        if (sandbox.IsSandboxActive && !string.IsNullOrWhiteSpace(found.PlayerExecutablePath))
            SandboxEnvironmentSeam.AssertSandboxPathIfActive(found.PlayerExecutablePath, sandbox);
        return found;
    }

    private static bool IsSelfExecutable(string cand, string? selfPath) =>
        !string.IsNullOrWhiteSpace(selfPath) && string.Equals(Path.GetFullPath(cand), Path.GetFullPath(selfPath), StringComparison.OrdinalIgnoreCase);

    private static bool IsWithinRoot(string cand, string? root)
    {
        if (string.IsNullOrWhiteSpace(root)) return false;
        try { _ = AppEnvironment.AssertPathContained(Path.GetFullPath(cand), root); return true; } catch { return false; }
    }
}

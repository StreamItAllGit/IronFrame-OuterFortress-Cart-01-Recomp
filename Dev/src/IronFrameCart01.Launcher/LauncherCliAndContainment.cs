// @module: IronFrameCart01.Launcher.LauncherCliAndContainment
// @purpose: Argument sanitization, CWD path rebasing, containment checks, and Standard 19 SettingsDomainStore config seeding.
// @depends-on: System, System.Collections.Generic, System.Globalization, System.IO, System.Linq, System.Reflection, System.Text, System.Text.Json, System.Text.Json.Nodes, Foundation.Core, reEngine.Core.Updates
// @exports: LauncherCliOptions, LauncherCliAndContainment

using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Foundation.Core;
using reEngine.Core.Updates;

namespace IronFrameCart01.Launcher;

internal sealed record LauncherCliOptions(
    string[] RawArgs,
    string[] SanitizedArgs,
    string[] ForwardedArgs,
    string? ReEngineDir,
    string? SuiteDir,
    string? DataDir,
    string? ConfigPath,
    string? LogDir,
    string? SaveDir,
    string? OpenPath,
    bool HasExplicitOpenOrModule,
    bool UsedLegacyModuleFlag,
    string? RomPath,
    string? DumpScreenshotPath,
    string? DumpAudioPath,
    string? FixturePath,
    int? AutomationPort,
    bool ShowHelp,
    bool ShowVersion,
    bool DryRun,
    bool Verify,
    bool WaitRequested,
    bool StrippedAutoStart);

internal static class LauncherCliAndContainment
{
    private const string SettingsExampleResource = "IronFrameCart01.Launcher.SettingsExample.json";
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions JsonReadOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
    private static readonly JsonSerializerOptions JsonWriteOpts = new() { WriteIndented = true };

    private static readonly HashSet<string> ValueOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "--open", "--module", "--rom", "--preload-media", "--media-path", "--verify-module",
        "--reengine-dir", "--suite-dir", "--data-dir", "--config", "--log-dir", "--save-dir", "--feed",
        "--port", "--ui", "--backend", "--aspect-mode", "--scale", "--region", "--execution-tier",
        "--audio-driver", "--frames", "--lockstep-steps", "--overscan", "--profile", "--suite-tab",
        "--automation-port", "--automation-token", "--fixture", "--dump-screenshot", "--dump-audio",
        "--shutdown-timeout-ms"
    };

    private static readonly HashSet<string> PathOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "--open", "--module", "--rom", "--preload-media", "--media-path", "--verify-module",
        "--reengine-dir", "--suite-dir", "--data-dir", "--config", "--log-dir", "--save-dir", "--feed",
        "--fixture", "--dump-screenshot", "--dump-audio"
    };

    private static readonly HashSet<string> LauncherStrippedOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "--reengine-dir", "--suite-dir", "--data-dir", "--config", "--log-dir", "--save-dir", "--open", "--module"
    };

    private static readonly HashSet<string> SyncWaitFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "--wait", "--lockstep-oracle", "--lockstep-steps", "--verify", "--verify-module",
        "--frames", "--dry-run", "--dump-screenshot", "--dump-audio", "--headless",
        "--tray-only", "--doctor", "--automation-port"
    };

    internal static bool HasAutomationPortFlag(IReadOnlyList<string>? args) =>
        args is not null && args.Any(a =>
            string.Equals(a, "--automation-port", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith("--automation-port=", StringComparison.OrdinalIgnoreCase));

    internal static string[] SanitizeArgs(string[]? args) => SanitizeArgs(args, out _);

    internal static string[] SanitizeArgs(string[]? args, out bool strippedAutoStart)
    {
        strippedAutoStart = false;
        if (args is null || args.Length == 0) return Array.Empty<string>();
        var clean = new List<string>(args.Length);
        foreach (string raw in args)
        {
            string trimmed = (raw ?? string.Empty).Trim();
            if (trimmed.StartsWith("--autostart", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("--auto-start", StringComparison.OrdinalIgnoreCase))
            {
                strippedAutoStart = true;
                continue;
            }
            clean.Add(raw ?? string.Empty);
        }
        return clean.ToArray();
    }

    internal static string[] NormalizeRelativePathArgs(IReadOnlyList<string>? args, string? callerWorkingDir = null)
    {
        if (args is null || args.Count == 0) return Array.Empty<string>();
        string baseDir = string.IsNullOrWhiteSpace(callerWorkingDir) ? Environment.CurrentDirectory : Path.GetFullPath(callerWorkingDir);
        var normalized = new List<string>(args.Count);

        for (int i = 0; i < args.Count; i++)
        {
            string raw = args[i] ?? string.Empty;
            string trimmed = raw.Trim();
            if (trimmed.Length == 0) { normalized.Add(raw); continue; }

            if (TrySplitInlineOption(trimmed, out string key, out string inlineVal))
            {
                if (ValueOptions.Contains(key) && string.IsNullOrWhiteSpace(inlineVal))
                    throw new ArgumentException($"Option '{key}' requires a non-empty value.");
                normalized.Add(PathOptions.Contains(key) && ShouldRebasePath(key, inlineVal, baseDir)
                    ? $"{key}={Path.GetFullPath(inlineVal, baseDir)}"
                    : raw);
                continue;
            }

            if (string.Equals(trimmed, "--preload-media", StringComparison.OrdinalIgnoreCase))
            {
                normalized.Add(trimmed);
                if (IsPreloadPathToken(args, i)) normalized.Add(Path.GetFullPath(args[++i].Trim().Trim('"'), baseDir));
                continue;
            }

            if (string.Equals(trimmed, "--lockstep-oracle", StringComparison.OrdinalIgnoreCase))
            {
                normalized.Add(trimmed);
                if (i + 1 < args.Count && int.TryParse(args[i + 1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int steps) && steps > 0)
                {
                    normalized.Add("--lockstep-steps");
                    normalized.Add(steps.ToString(CultureInfo.InvariantCulture));
                    i++;
                }
                continue;
            }

            if (ValueOptions.Contains(trimmed))
            {
                if (i + 1 >= args.Count || args[i + 1].TrimStart().StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"Option '{trimmed}' requires a value.");
                string unquoted = (args[++i] ?? string.Empty).Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(unquoted))
                    throw new ArgumentException($"Option '{trimmed}' requires a non-empty value.");
                normalized.Add(trimmed);
                normalized.Add(PathOptions.Contains(trimmed) && ShouldRebasePath(trimmed, unquoted, baseDir)
                    ? Path.GetFullPath(unquoted, baseDir)
                    : unquoted);
                continue;
            }

            if (!trimmed.StartsWith("-", StringComparison.Ordinal) &&
                !string.Equals(trimmed, "/?", StringComparison.Ordinal) &&
                !string.Equals(trimmed, "help", StringComparison.OrdinalIgnoreCase))
            {
                normalized.Add(Path.GetFullPath(trimmed.Trim('"'), baseDir));
                continue;
            }
            normalized.Add(raw);
        }
        return normalized.ToArray();
    }

    internal static LauncherCliOptions ParseAndNormalize(string[]? rawArgs, string? callerWorkingDir = null)
    {
        string[] rawCopy = rawArgs ?? Array.Empty<string>();
        string[] clean = SanitizeArgs(rawCopy, out bool strippedAutoStart);
        string[] rebased = NormalizeRelativePathArgs(clean, callerWorkingDir);
        var state = new ParseState();

        for (int i = 0; i < rebased.Length; i++)
        {
            string token = rebased[i].Trim();
            if (token.Length == 0) continue;

            if (TrySplitInlineOption(token, out string key, out string val))
            {
                if (SyncWaitFlags.Contains(key)) state.Wait = true;
                state.ApplyOption(key, val);
                if (!LauncherStrippedOptions.Contains(key)) state.Forwarded.Add(token);
                continue;
            }

            if (token.Equals("--help", StringComparison.OrdinalIgnoreCase) || token.Equals("-h", StringComparison.OrdinalIgnoreCase) || token.Equals("/?", StringComparison.OrdinalIgnoreCase)) { state.Help = true; continue; }
            if (token.Equals("--version", StringComparison.OrdinalIgnoreCase) || token.Equals("-v", StringComparison.OrdinalIgnoreCase)) { state.Version = true; continue; }
            if (token.Equals("--dry-run", StringComparison.OrdinalIgnoreCase)) state.DryRun = true;
            if (token.Equals("--verify", StringComparison.OrdinalIgnoreCase)) state.Verify = true;
            if (SyncWaitFlags.Contains(token)) state.Wait = true;

            if (token.Equals("--preload-media", StringComparison.OrdinalIgnoreCase))
            {
                state.Forwarded.Add(token);
                if (IsPreloadPathToken(rebased, i)) { state.RomPath ??= rebased[++i]; state.Forwarded.Add(rebased[i]); }
                continue;
            }

            if (ValueOptions.Contains(token))
            {
                string optVal = rebased[++i];
                state.ApplyOption(token, optVal);
                if (!LauncherStrippedOptions.Contains(token)) { state.Forwarded.Add(token); state.Forwarded.Add(optVal); }
                continue;
            }

            if (!token.StartsWith("-", StringComparison.Ordinal))
            {
                if (!state.HasExplicitOpenOrModule) { state.OpenPath = token; state.HasExplicitOpenOrModule = true; }
                continue;
            }
            state.Forwarded.Add(token);
        }

        return state.Build(rawCopy, rebased, strippedAutoStart);
    }

    internal static bool IsIsolatedTestEnabled()
    {
        string? raw = Environment.GetEnvironmentVariable("APP_ISOLATED_TEST");
        return string.Equals(raw?.Trim(), "1", StringComparison.Ordinal) ||
               string.Equals(raw?.Trim(), "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(raw?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
    }

    internal static void ValidateContainment(LauncherCliOptions options, AppEnvironment env, string resolvedSaveDir)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(env);
        string?[] paths =
        [
            env.DataDirectory, env.ConfigFilePath, env.LogDirectory, resolvedSaveDir,
            options.OpenPath, options.RomPath, options.ReEngineDir, options.SuiteDir,
            options.DumpScreenshotPath, options.DumpAudioPath, options.FixturePath
        ];

        if (env.Sandbox.IsSandboxActive)
        {
            foreach (string? p in paths)
                if (!string.IsNullOrWhiteSpace(p)) SandboxEnvironmentSeam.AssertSandboxPathIfActive(p, env.Sandbox);
        }

        if (!IsIsolatedTestEnabled()) return;

        bool hasDataRoot = !string.IsNullOrWhiteSpace(options.DataDir) || new[]
        {
            "IRONFRAME_OUTERFORTRESS_CART_01_DATA_DIR", "DATA_DIR", "APP_HOME"
        }.Any(v => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(v)));

        if (!hasDataRoot)
            SandboxEnvironmentSeam.ThrowContainmentViolation("Fail-Closed Containment Violation: APP_ISOLATED_TEST=1 requires explicit --data-dir.");

        string boundary = ResolveIsolatedBoundary(env.DataDirectory);
        foreach (string? p in paths)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            try { _ = AppEnvironment.AssertPathContained(Path.GetFullPath(p), boundary); }
            catch (InvalidOperationException ex) { SandboxEnvironmentSeam.ThrowContainmentViolation(ex.Message, ex); }
        }
    }

    internal static string ResolveIsolatedBoundary(string dataDir)
    {
        string full = Path.GetFullPath(dataDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? parent = Path.GetDirectoryName(full);
        if (!string.IsNullOrWhiteSpace(parent) && string.Equals(Path.GetFileName(full), "data", StringComparison.OrdinalIgnoreCase))
        {
            string? gamesDir = Path.GetDirectoryName(parent);
            if (!string.IsNullOrWhiteSpace(gamesDir) && string.Equals(Path.GetFileName(gamesDir), "games", StringComparison.OrdinalIgnoreCase))
            {
                string? suiteDir = Path.GetDirectoryName(gamesDir);
                if (!string.IsNullOrWhiteSpace(suiteDir) && !IsDriveRoot(suiteDir)) return suiteDir;
            }
            if (!IsDriveRoot(parent)) return parent;
        }
        return full;
    }

    /// <summary>
    /// Seeds missing <c>&lt;dataDir&gt;\config\settings*.json</c> files from embedded <c>SettingsExample.json</c>
    /// using Standard 19 <see cref="SettingsDomainStore"/> without overwriting existing settings.
    /// </summary>
    internal static void SeedDefaultSettingsIfMissing(string dataDirectory, string? configFilePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        string fullDataDir = Path.GetFullPath(dataDirectory);
        string corePath = string.IsNullOrWhiteSpace(configFilePath) ? Path.Combine(fullDataDir, "config", "settings.json") : Path.GetFullPath(configFilePath);
        string configDir = Path.GetDirectoryName(corePath) ?? Path.Combine(fullDataDir, "config");
        string videoPath = Path.Combine(configDir, "settings.video.json");
        string audioPath = Path.Combine(configDir, "settings.audio.json");
        string controlsPath = Path.Combine(configDir, "settings.controls.json");

        bool hasCore = File.Exists(corePath), hasVideo = File.Exists(videoPath), hasAudio = File.Exists(audioPath), hasControls = File.Exists(controlsPath);
        if (hasCore && hasVideo && hasAudio && hasControls) return;

        JsonObject example = LoadEmbeddedSettingsExample();
        var store = new SettingsDomainStore(fullDataDir, corePath);
        Directory.CreateDirectory(configDir);

        if (!hasCore)
        {
            var coreObj = (example.DeepClone() as JsonObject) ?? new JsonObject();
            coreObj.Remove("$schema"); coreObj.Remove("video"); coreObj.Remove("audio"); coreObj.Remove("controls");
            store.SaveCore(coreObj.Deserialize<ReEngineSettings>(JsonReadOpts) ?? new ReEngineSettings());
        }
        if (!hasVideo)
            store.SaveVideo((example["video"] as JsonObject)?.Deserialize<ReEngineVideoSettings>(JsonReadOpts) ?? new ReEngineVideoSettings());
        if (!hasAudio)
            store.SaveAudio((example["audio"] as JsonObject)?.Deserialize<ReEngineAudioSettings>(JsonReadOpts) ?? new ReEngineAudioSettings());
        if (!hasControls)
            WriteControlsFileAtomically(controlsPath, example["controls"]?.DeepClone() as JsonObject);
    }

    private static void WriteControlsFileAtomically(string controlsPath, JsonObject? controlsObj)
    {
        controlsObj ??= new JsonObject { ["schemaVersion"] = 2, ["domain"] = "controls", ["profileMode"] = "modernPc", ["useGlobalFormatRules"] = true };
        string tmp = $"{controlsPath}.{Guid.NewGuid():N}.tmp";
        try { File.WriteAllText(tmp, controlsObj.ToJsonString(JsonWriteOpts), Utf8NoBom); File.Move(tmp, controlsPath, overwrite: false); }
        finally { if (File.Exists(tmp)) { try { File.Delete(tmp); } catch { } } }
    }

    private static JsonObject LoadEmbeddedSettingsExample()
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(SettingsExampleResource);
        if (stream is null) return new JsonObject();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return JsonNode.Parse(reader.ReadToEnd()) as JsonObject ?? new JsonObject();
    }

    private static bool TrySplitInlineOption(string token, out string key, out string val)
    {
        key = token; val = string.Empty;
        if (!token.StartsWith("--", StringComparison.Ordinal)) return false;
        int eq = token.IndexOf('=');
        if (eq <= 2) return false;
        key = token[..eq]; val = token[(eq + 1)..].Trim().Trim('"');
        return true;
    }

    private static bool IsPreloadPathToken(IReadOnlyList<string> args, int idx) =>
        idx + 1 < args.Count && !args[idx + 1].TrimStart().StartsWith("-", StringComparison.Ordinal) &&
        !args[idx + 1].EndsWith(".recomp", StringComparison.OrdinalIgnoreCase) && !args[idx + 1].EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    private static bool ShouldRebasePath(string key, string val, string baseDir)
    {
        if (string.IsNullOrWhiteSpace(val)) return false;
        if (key.Equals("--feed", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(val, UriKind.Absolute, out Uri? uri) &&
            (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (key.Equals("--verify-module", StringComparison.OrdinalIgnoreCase))
            return Path.IsPathRooted(val) || val.Contains('/') || val.Contains('\\') || val.StartsWith('.') ||
                   File.Exists(Path.Combine(baseDir, val)) || Directory.Exists(Path.Combine(baseDir, val));
        return true;
    }

    private static bool IsDriveRoot(string path) =>
        string.Equals(Path.GetPathRoot(path)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private sealed class ParseState
    {
        public readonly List<string> Forwarded = [];
        public string? ReEngineDir, SuiteDir, DataDir, ConfigPath, LogDir, SaveDir, OpenPath, RomPath, DumpShot, DumpAudio, FixturePath;
        public bool HasExplicitOpenOrModule, UsedLegacyModule, Help, Version, DryRun, Verify, Wait;
        public int? AutomationPort;

        public void ApplyOption(string key, string val)
        {
            switch (key.ToLowerInvariant())
            {
                case "--reengine-dir": ReEngineDir = val; break;
                case "--suite-dir": SuiteDir = val; break;
                case "--data-dir": DataDir = val; break;
                case "--config": ConfigPath = val; break;
                case "--log-dir": LogDir = val; break;
                case "--save-dir": SaveDir = val; break;
                case "--open": OpenPath = val; HasExplicitOpenOrModule = true; UsedLegacyModule = false; break;
                case "--module": if (!HasExplicitOpenOrModule) { OpenPath = val; UsedLegacyModule = true; } HasExplicitOpenOrModule = true; break;
                case "--rom": case "--preload-media": RomPath ??= val; break;
                case "--dump-screenshot": DumpShot = val; break;
                case "--dump-audio": DumpAudio = val; break;
                case "--fixture": FixturePath = val; break;
                case "--automation-port":
                    if (!int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) || port < 0 || port > 65535)
                        throw new ArgumentException($"Invalid --automation-port value '{val}'.");
                    AutomationPort = port; break;
            }
        }

        public LauncherCliOptions Build(string[] raw, string[] rebased, bool strippedAutoStart) => new(
            raw, rebased, Forwarded.ToArray(), ReEngineDir, SuiteDir, DataDir, ConfigPath, LogDir, SaveDir,
            OpenPath, HasExplicitOpenOrModule, UsedLegacyModule, RomPath, DumpShot, DumpAudio, FixturePath,
            AutomationPort, Help, Version, DryRun, Verify, Wait, strippedAutoStart);
    }
}

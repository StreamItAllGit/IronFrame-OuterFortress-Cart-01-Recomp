// @module: IronFrameCart01.Installer.InstallerSettingsAndRomPreserver
// @purpose: Pre-upgrade data\rom\ + data\config\settings*.json snapshot preservation and Standard 19 SettingsDomainStore profile persistence.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Text, System.Text.Json, System.Text.Json.Nodes, Foundation.Installer, reEngine.Core.Updates
// @exports: PreUpgradePreservationSnapshot, InstallerSettingsAndRomPreserver

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Foundation.Installer;
using reEngine.Core.Updates;

namespace IronFrameCart01.Installer;

internal sealed class PreUpgradePreservationSnapshot : IDisposable
{
    internal string DataDirectory { get; }
    internal string TempRoot { get; }
    internal int RomFileCount { get; }
    internal int ConfigFileCount { get; }

    internal PreUpgradePreservationSnapshot(string dataDirectory, string tempRoot, int romFileCount, int configFileCount)
    {
        DataDirectory = dataDirectory;
        TempRoot = tempRoot;
        RomFileCount = romFileCount;
        ConfigFileCount = configFileCount;
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(TempRoot) || !Directory.Exists(TempRoot)) return;
        try { Directory.Delete(TempRoot, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

internal static class InstallerSettingsAndRomPreserver
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    internal static PreUpgradePreservationSnapshot? CapturePreUpgradeSnapshot(
        string effectiveDataDir,
        InstallerRuntimeContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (ctx.DryRun || string.IsNullOrWhiteSpace(effectiveDataDir)) return null;

        string fullData = Path.GetFullPath(effectiveDataDir);
        string romDir = Path.Combine(fullData, "rom");
        string configDir = Path.Combine(fullData, "config");
        string flatSettings = Path.Combine(fullData, "settings.json");

        bool hasRom = Directory.Exists(romDir) && Directory.EnumerateFiles(romDir, "*", SearchOption.AllDirectories).Any();
        bool hasCfg = Directory.Exists(configDir) && Directory.EnumerateFiles(configDir, "settings*.json", SearchOption.TopDirectoryOnly).Any();
        bool hasFlat = File.Exists(flatSettings);
        if (!hasRom && !hasCfg && !hasFlat) return null;

        string tempRoot = Path.Combine(Path.GetTempPath(), $"mgr_preserve_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        int romCount = hasRom ? CopyDirectoryFiles(romDir, Path.Combine(tempRoot, "rom")) : 0;
        int cfgCount = 0;

        if (hasCfg)
        {
            string dstCfg = Path.Combine(tempRoot, "config");
            Directory.CreateDirectory(dstCfg);
            foreach (string src in Directory.EnumerateFiles(configDir, "settings*.json", SearchOption.TopDirectoryOnly))
            {
                File.Copy(src, Path.Combine(dstCfg, Path.GetFileName(src)), overwrite: true);
                cfgCount++;
            }
        }
        else if (hasFlat)
        {
            File.Copy(flatSettings, Path.Combine(tempRoot, "settings.json"), overwrite: true);
            cfgCount++;
        }

        ctx.WriteLog("PRESERVE", $"Captured pre-upgrade snapshot (romFiles={romCount}, settingsFiles={cfgCount}) from '{fullData}'.");
        return new PreUpgradePreservationSnapshot(fullData, tempRoot, romCount, cfgCount);
    }

    internal static void RestoreAndPersistAfterInstall(
        PreUpgradePreservationSnapshot? snapshot,
        string effectiveDataDir,
        string installDir,
        string profileId,
        string? suiteInstallDir,
        string? reEngineInstallDir,
        InstallerRuntimeContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (ctx.DryRun || string.IsNullOrWhiteSpace(effectiveDataDir)) return;

        string fullData = Path.GetFullPath(effectiveDataDir);
        string romDir = Path.Combine(fullData, "rom");
        string configDir = Path.Combine(fullData, "config");
        Directory.CreateDirectory(romDir);
        Directory.CreateDirectory(configDir);

        int restoredRom = 0;
        int restoredCfg = 0;
        if (snapshot is not null && Directory.Exists(snapshot.TempRoot))
        {
            string snapRom = Path.Combine(snapshot.TempRoot, "rom");
            if (Directory.Exists(snapRom))
                restoredRom = RestoreMissingFiles(snapRom, romDir);

            string snapCfg = Path.Combine(snapshot.TempRoot, "config");
            if (Directory.Exists(snapCfg))
                restoredCfg = RestoreMissingFiles(snapCfg, configDir);

            string snapFlat = Path.Combine(snapshot.TempRoot, "settings.json");
            string targetConfigSettings = Path.Combine(configDir, "settings.json");
            if (File.Exists(snapFlat) && !File.Exists(targetConfigSettings))
            {
                File.Copy(snapFlat, targetConfigSettings, overwrite: false);
                restoredCfg++;
            }
        }

        EnsurePreloadRomKeepMarker(romDir);
        SeedDefaultSettingsIfMissing(fullData, configDir, installDir);

        var store = new SettingsDomainStore(fullData);
        ReEngineSettingsBundle bundle = store.LoadOrMigrate();
        bundle.Core.ExtensionData ??= new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        string normalizedProfile = InstallerArgsAndContainment.NormalizeProfileId(profileId);
        bundle.Core.ExtensionData["moduleId"] = JsonSerializer.SerializeToElement(InstallerArgsAndContainment.ModuleId);
        bundle.Core.ExtensionData["installProfile"] = JsonSerializer.SerializeToElement(normalizedProfile);
        bundle.Core.ExtensionData["umbrellaSubpath"] = JsonSerializer.SerializeToElement(InstallerArgsAndContainment.UmbrellaSubpath);

        if (!string.IsNullOrWhiteSpace(suiteInstallDir))
            bundle.Core.ExtensionData["suiteInstallDir"] = JsonSerializer.SerializeToElement(InstallerArgsAndContainment.NormalizeSuiteRoot(suiteInstallDir));
        if (!string.IsNullOrWhiteSpace(reEngineInstallDir))
            bundle.Core.ExtensionData["reEngineInstallDir"] = JsonSerializer.SerializeToElement(InstallerArgsAndContainment.SafeFullTrim(reEngineInstallDir));

        store.SaveCore(bundle.Core);
        ctx.WriteLog("CONFIG",
            $"Preserved data/rom (restored={restoredRom}) & settings (restored={restoredCfg}); persisted installProfile='{normalizedProfile}' via SettingsDomainStore.");
    }

    internal static bool TryReadSavedInstallSettings(
        string? effectiveDataDir,
        out string? profileId,
        out string? suiteDir,
        out string? reEngineDir)
    {
        profileId = null;
        suiteDir = null;
        reEngineDir = null;
        if (string.IsNullOrWhiteSpace(effectiveDataDir)) return false;

        string fullData = Path.GetFullPath(effectiveDataDir);
        string canonical = Path.Combine(fullData, "config", "settings.json");
        string legacyFlat = Path.Combine(fullData, "settings.json");
        string? source = File.Exists(canonical) ? canonical : (File.Exists(legacyFlat) ? legacyFlat : null);
        if (source is null) return false;

        try
        {
            if (JsonNode.Parse(File.ReadAllText(source, Utf8NoBom)) is not JsonObject root) return false;
            if (root["installProfile"]?.GetValue<string?>() is { } p && p.Trim().ToLowerInvariant() is "lightweight" or "full")
                profileId = p.Trim().ToLowerInvariant();
            if (root["suiteInstallDir"]?.GetValue<string?>() is { } s && !string.IsNullOrWhiteSpace(s))
                suiteDir = Path.GetFullPath(s.Trim());
            if (root["reEngineInstallDir"]?.GetValue<string?>() is { } r && !string.IsNullOrWhiteSpace(r))
                reEngineDir = Path.GetFullPath(r.Trim());
            return profileId is not null || suiteDir is not null || reEngineDir is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    private static void SeedDefaultSettingsIfMissing(string fullData, string configDir, string installDir)
    {
        string targetSettings = Path.Combine(configDir, "settings.json");
        string legacyFlat = Path.Combine(fullData, "settings.json");
        if (File.Exists(targetSettings)) return;

        if (File.Exists(legacyFlat))
        {
            File.Copy(legacyFlat, targetSettings, overwrite: false);
            return;
        }

        foreach (string candidate in new[]
        {
            Path.Combine(installDir, "settings.example.json"),
            Path.Combine(AppContext.BaseDirectory, "settings.example.json")
        })
        {
            if (File.Exists(candidate))
            {
                File.Copy(candidate, targetSettings, overwrite: false);
                return;
            }
        }
    }

    private static void EnsurePreloadRomKeepMarker(string romDir)
    {
        if (Directory.EnumerateFiles(romDir, "*", SearchOption.AllDirectories).Any()) return;
        string keepPath = Path.Combine(romDir, ".keep");
        if (!File.Exists(keepPath))
            File.WriteAllText(keepPath, "# Place legally obtained NES-ME-USA cartridge image (.nes or .prg) here.\n", Utf8NoBom);
    }

    private static int CopyDirectoryFiles(string srcDir, string dstDir)
    {
        Directory.CreateDirectory(dstDir);
        int count = 0;
        foreach (string src in Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories))
        {
            string dst = Path.Combine(dstDir, Path.GetRelativePath(srcDir, src));
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(src, dst, overwrite: true);
            count++;
        }
        return count;
    }

    private static int RestoreMissingFiles(string srcDir, string dstDir)
    {
        int count = 0;
        foreach (string src in Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories))
        {
            string dst = Path.Combine(dstDir, Path.GetRelativePath(srcDir, src));
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            if (!File.Exists(dst))
            {
                File.Copy(src, dst, overwrite: false);
                count++;
            }
        }
        return count;
    }
}

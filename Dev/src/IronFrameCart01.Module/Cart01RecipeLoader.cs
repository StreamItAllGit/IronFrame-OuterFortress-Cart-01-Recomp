// @module: IronFrameCart01.Module.Cart01RecipeLoader
// @purpose: Loads, cross-validates, and packages clean-room 2-member [module.recomp.json, aot-recipe.json] .recomp cartridges.
// @depends-on: System, System.IO, System.IO.Compression, System.Text, Foundation.Core, reCart.Compiler, reCart.Contracts, reEngine.Core.Manifests, reEngine.Core.Resolution
// @exports: Cart01LoadedRecipePackage, Cart01RecipeLoader

using System.IO.Compression;
using System.Text;
using Foundation.Core;
using reCart.Compiler;
using reCart.Contracts;
using reEngine.Core.Manifests;
using reEngine.Core.Resolution;

namespace IronFrameCart01.Module;

/// <summary>
/// Verified clean-room manifest and AOT recipe descriptor pair loaded from disk or a 2-member <c>.recomp</c> archive.
/// </summary>
public sealed record Cart01LoadedRecipePackage(
    CartPackageManifest Manifest,
    CartAotRecipeSpec Recipe,
    string SourcePath,
    bool IsArchive,
    string PreloadRomDirectory);

/// <summary>
/// Loads and validates <see cref="CartPackageManifest"/> + <see cref="CartAotRecipeSpec"/> from disk or <c>.recomp</c> ZIPs,
/// and packages strictly 2-member clean-room archives via <see cref="CleanRoomRecompPackager.PackageCleanRoomRecipeRecomp"/>.
/// </summary>
public static class Cart01RecipeLoader
{
    /// <summary>
    /// Loads and validates a clean-room manifest and recipe from an explicit manifest/recipe file pair,
    /// directory, or 2-member <c>.recomp</c> ZIP archive.
    /// </summary>
    public static Cart01LoadedRecipePackage Load(string pathOrDirectory, string? explicitRecipePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pathOrDirectory);
        if (!string.IsNullOrWhiteSpace(explicitRecipePath))
        {
            return LoadFromFiles(pathOrDirectory, explicitRecipePath);
        }

        string fullPath = Path.GetFullPath(pathOrDirectory);
        if (Directory.Exists(fullPath))
        {
            return LoadFromDirectory(fullPath);
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Recipe source or package not found: '{fullPath}'.", fullPath);
        }

        if (IsZipFile(fullPath) || fullPath.EndsWith(".recomp", StringComparison.OrdinalIgnoreCase) ||
            fullPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return LoadFromArchive(fullPath);
        }

        string manifestDir = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException($"Cannot determine directory for manifest '{fullPath}'.");
        string manifestJson = File.ReadAllText(fullPath, Encoding.UTF8);
        CartPackageManifest manifest = CartPackageManifest.Deserialize(manifestJson);
        string recipePath = ResolveRecipePathUnderRoot(manifestDir, manifest);
        return LoadFromFiles(fullPath, recipePath);
    }

    /// <summary>
    /// Loads and cross-validates <c>module.recomp.json</c> and <c>aot-recipe.json</c> from explicit disk paths.
    /// </summary>
    public static Cart01LoadedRecipePackage LoadFromFiles(string manifestJsonPath, string recipeJsonPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestJsonPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipeJsonPath);

        string fullManifest = Path.GetFullPath(manifestJsonPath);
        string fullRecipe = Path.GetFullPath(recipeJsonPath);
        if (!File.Exists(fullManifest)) throw new FileNotFoundException($"Manifest not found: '{fullManifest}'.", fullManifest);
        if (!File.Exists(fullRecipe)) throw new FileNotFoundException($"AOT recipe not found: '{fullRecipe}'.", fullRecipe);

        CartPackageManifest manifest = CartPackageManifest.Deserialize(File.ReadAllText(fullManifest, Encoding.UTF8));
        CartAotRecipeSpec recipe = CartAotRecipeSpec.Deserialize(File.ReadAllText(fullRecipe, Encoding.UTF8));
        ValidateManifestAndRecipe(manifest, recipe);

        manifest.ResolvedPackagePath = fullManifest;
        string moduleRoot = RecompPackageReader.ResolveModuleRootDirectory(fullManifest);
        string preloadDir = ResolveContainedPreloadDir(moduleRoot, manifest.PreloadRomDir);
        return new Cart01LoadedRecipePackage(manifest, recipe, fullManifest, IsArchive: false, preloadDir);
    }

    /// <summary>
    /// Loads and cross-validates <c>module.recomp.json</c> and <c>aot-recipe.json</c> from a directory.
    /// </summary>
    public static Cart01LoadedRecipePackage LoadFromDirectory(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        string fullDir = Path.GetFullPath(directoryPath);
        if (!Directory.Exists(fullDir))
        {
            throw new DirectoryNotFoundException($"Module directory not found: '{fullDir}'.");
        }

        string manifestPath = AppEnvironment.AssertPathContained(
            Path.Combine(fullDir, CleanRoomRecompPackager.ManifestEntryName),
            fullDir);
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"Directory missing '{CleanRoomRecompPackager.ManifestEntryName}'.", manifestPath);
        }

        CartPackageManifest manifest = CartPackageManifest.Deserialize(File.ReadAllText(manifestPath, Encoding.UTF8));
        string recipePath = ResolveRecipePathUnderRoot(fullDir, manifest);
        return LoadFromFiles(manifestPath, recipePath);
    }

    /// <summary>
    /// Validates and loads a 2-member clean-room <c>.recomp</c> ZIP archive (<c>[module.recomp.json, aot-recipe.json]</c>).
    /// </summary>
    public static Cart01LoadedRecipePackage LoadFromArchive(string recompArchivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recompArchivePath);
        string fullZip = Path.GetFullPath(recompArchivePath);
        if (!File.Exists(fullZip))
        {
            throw new FileNotFoundException($"Clean-room .recomp archive not found: '{fullZip}'.", fullZip);
        }

        CleanRoomRecompPackager.AssertValidCleanRoomArchive(fullZip);
        if (!CleanRoomRecompPackager.IsRecipeArchive(fullZip))
        {
            throw new InvalidDataException(
                $"Archive '{Path.GetFileName(fullZip)}' is not a 2-member clean-room recipe package [{CleanRoomRecompPackager.ManifestEntryName}, {CleanRoomRecompPackager.AotRecipeEntryName}].");
        }

        using FileStream fs = File.OpenRead(fullZip);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
        CartPackageManifest manifest = CartPackageManifest.Deserialize(ReadZipEntryUtf8(zip, CleanRoomRecompPackager.ManifestEntryName));
        CartAotRecipeSpec recipe = CartAotRecipeSpec.Deserialize(ReadZipEntryUtf8(zip, CleanRoomRecompPackager.AotRecipeEntryName));
        ValidateManifestAndRecipe(manifest, recipe);

        manifest.ResolvedPackagePath = fullZip;
        string moduleRoot = RecompPackageReader.ResolveModuleRootDirectory(fullZip);
        string preloadDir = ResolveContainedPreloadDir(moduleRoot, manifest.PreloadRomDir);
        return new Cart01LoadedRecipePackage(manifest, recipe, fullZip, IsArchive: true, preloadDir);
    }

    /// <summary>
    /// Validates <paramref name="manifest"/> and <paramref name="recipe"/> individually and verifies cross-manifest consistency.
    /// </summary>
    public static void ValidateManifestAndRecipe(
        CartPackageManifest manifest,
        CartAotRecipeSpec recipe,
        bool requireCart01Identity = false)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(recipe);

        manifest.Validate();
        recipe.Validate();

        if (!EngineAliasCatalog.AreEquivalent(manifest.EngineId, Cart01Constants.EngineId))
        {
            throw new InvalidDataException(
                $"Manifest '{manifest.ModuleId}' targets incompatible engine '{manifest.EngineId}' (expected '{Cart01Constants.EngineId}').");
        }

        if (!string.Equals(manifest.ModuleId.Trim(), recipe.ModuleId.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Manifest moduleId '{manifest.ModuleId}' does not match recipe moduleId '{recipe.ModuleId}'.");
        }

        if (!string.Equals(manifest.PlatformId.Trim(), recipe.PlatformId.Trim(), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(manifest.PlatformId.Trim(), Cart01Constants.PlatformId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Platform mismatch between manifest ('{manifest.PlatformId}') and recipe ('{recipe.PlatformId}'); expected '{Cart01Constants.PlatformId}'.");
        }

        if (manifest.MapperId != recipe.MapperId || manifest.SubmapperId != recipe.SubmapperId)
        {
            throw new InvalidDataException(
                $"Mapper mismatch: manifest declares {manifest.MapperId}.{manifest.SubmapperId}, recipe declares {recipe.MapperId}.{recipe.SubmapperId}.");
        }

        if (requireCart01Identity &&
            (!string.Equals(manifest.ModuleId.Trim(), Cart01Constants.ModuleId, StringComparison.Ordinal) ||
             manifest.MapperId != Cart01Constants.MapperId))
        {
            throw new InvalidDataException(
                $"Package identity '{manifest.ModuleId}' (mapper {manifest.MapperId}) does not match expected '{Cart01Constants.ModuleId}' (mapper {Cart01Constants.MapperId}).");
        }
    }

    /// <summary>
    /// Validates and packages a 2-member clean-room <c>.recomp</c> ZIP containing strictly
    /// <c>[module.recomp.json, aot-recipe.json]</c> via <see cref="CleanRoomRecompPackager.PackageCleanRoomRecipeRecomp"/>.
    /// </summary>
    public static string PackageCleanRoomArchive(
        CartPackageManifest manifest,
        CartAotRecipeSpec recipe,
        string outputRecompPath)
    {
        ValidateManifestAndRecipe(manifest, recipe);
        return CleanRoomRecompPackager.PackageCleanRoomRecipeRecomp(manifest, recipe, outputRecompPath);
    }

    /// <summary>
    /// Loads <paramref name="manifestJsonPath"/> and <paramref name="recipeJsonPath"/>, validates both,
    /// and packages a 2-member clean-room <c>.recomp</c> archive at <paramref name="outputRecompPath"/>.
    /// </summary>
    public static string PackageCleanRoomArchiveFromFiles(
        string manifestJsonPath,
        string recipeJsonPath,
        string outputRecompPath)
    {
        Cart01LoadedRecipePackage loaded = LoadFromFiles(manifestJsonPath, recipeJsonPath);
        return CleanRoomRecompPackager.PackageCleanRoomRecipeRecomp(loaded.Manifest, loaded.Recipe, outputRecompPath);
    }

    private static string ResolveRecipePathUnderRoot(string rootDir, CartPackageManifest manifest)
    {
        List<string> candidates = [];
        if (!string.IsNullOrWhiteSpace(manifest.EntryAssembly) &&
            manifest.EntryAssembly.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            string entryRel = manifest.EntryAssembly.Trim().Replace('/', Path.DirectorySeparatorChar);
            if (!Path.IsPathRooted(entryRel))
            {
                candidates.Add(AppEnvironment.AssertPathContained(Path.Combine(rootDir, entryRel), rootDir));
            }
        }

        candidates.Add(AppEnvironment.AssertPathContained(Path.Combine(rootDir, CleanRoomRecompPackager.AotRecipeEntryName), rootDir));
        candidates.Add(AppEnvironment.AssertPathContained(Path.Combine(rootDir, "Manifests", CleanRoomRecompPackager.AotRecipeEntryName), rootDir));
        candidates.Add(AppEnvironment.AssertPathContained(
            Path.Combine(rootDir, "src", "IronFrameCart01.Module", "Manifests", CleanRoomRecompPackager.AotRecipeEntryName),
            rootDir));

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException(
            $"AOT recipe '{CleanRoomRecompPackager.AotRecipeEntryName}' not found under '{rootDir}'.",
            candidates[0]);
    }

    private static string ResolveContainedPreloadDir(string moduleRoot, string? preloadRomDir)
    {
        string rel = string.IsNullOrWhiteSpace(preloadRomDir) ? Cart01Constants.PreloadRomDir : preloadRomDir.Trim();
        if (Path.IsPathRooted(rel))
        {
            throw new InvalidOperationException($"Fail-Closed Containment Violation: preloadRomDir '{rel}' must be relative.");
        }

        string osRel = rel.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        return AppEnvironment.AssertPathContained(Path.Combine(moduleRoot, osRel), moduleRoot);
    }

    private static string ReadZipEntryUtf8(ZipArchive zip, string entryName)
    {
        ZipArchiveEntry entry = zip.GetEntry(entryName)
            ?? throw new InvalidDataException($"Clean-room archive missing '{entryName}'.");
        using Stream stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static bool IsZipFile(string path)
    {
        Span<byte> head = stackalloc byte[4];
        using FileStream fs = File.OpenRead(path);
        return fs.Read(head) >= 4 && head[0] == 0x50 && head[1] == 0x4B &&
               (head[2] == 0x03 || head[2] == 0x05) && (head[3] == 0x04 || head[3] == 0x06);
    }
}

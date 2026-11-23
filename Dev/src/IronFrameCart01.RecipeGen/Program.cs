// @module: IronFrameCart01.RecipeGen.Program
// @purpose: CLI entry point generating deterministic clean-room aot-recipe.json from upstream oracle schemas.
// @depends-on: System, System.IO, System.Text, reCart.Contracts
// @exports: Program

using System.Text;
using reCart.Contracts;

namespace IronFrameCart01.RecipeGen;

/// <summary>CLI tool emitting UTF-8 no-BOM <c>aot-recipe.json</c> descriptors from read-only oracle schemas.</summary>
public static class Program
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    /// <summary>Executes recipe generation with injected writers for deterministic CLI and test verification.</summary>
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        string? schemasDir = null;
        string outPath = "aot-recipe.json";

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg is "-h" or "--help" or "/?")
            {
                PrintUsage(output);
                return 0;
            }

            if (TryReadFlagValue(args, ref i, "--schemas-dir", out string? parsedSchemas, error))
            {
                if (parsedSchemas is null) return 2;
                schemasDir = parsedSchemas;
                continue;
            }

            if (TryReadFlagValue(args, ref i, "--out", out string? parsedOut, error))
            {
                if (parsedOut is null) return 2;
                outPath = parsedOut;
                continue;
            }

            error.WriteLine($"Unknown or unexpected argument: '{arg}'.");
            PrintUsage(error);
            return 2;
        }

        string? resolvedSchemas = schemasDir ?? OracleSchemaLoader.TryResolveDefaultSchemasDirectory();
        if (string.IsNullOrWhiteSpace(resolvedSchemas))
        {
            error.WriteLine("Upstream read-only schemas directory was not found automatically. Pass --schemas-dir <dir>.");
            return 1;
        }

        try
        {
            OracleSchemaSet schemas = OracleSchemaLoader.Load(resolvedSchemas);
            CartAotRecipeSpec spec = AotRecipeEmitter.Emit(schemas);
            string json = spec.Serialize();

            string fullOutPath = Path.GetFullPath(outPath);
            string? outDir = Path.GetDirectoryName(fullOutPath);
            if (!string.IsNullOrEmpty(outDir))
            {
                Directory.CreateDirectory(outDir);
            }

            File.WriteAllText(fullOutPath, json, Utf8NoBom);
            output.WriteLine(
                $"Generated '{fullOutPath}' ({spec.Seeds.Count} seeds, {spec.EntryAddresses.Count} entries, " +
                $"{spec.JumpTables.Count} jumpTables, {spec.Trampolines.Count} trampolines, {spec.MemoryWatches.Count} memoryWatches).");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            error.WriteLine($"RecipeGen failed: {ex.Message}");
            return 1;
        }
    }

    private static bool TryReadFlagValue(
        string[] args,
        ref int index,
        string flagName,
        out string? value,
        TextWriter error)
    {
        string arg = args[index];
        string prefix = $"{flagName}=";

        if (arg.StartsWith(prefix, StringComparison.Ordinal))
        {
            string inlineVal = arg[prefix.Length..];
            if (string.IsNullOrWhiteSpace(inlineVal))
            {
                error.WriteLine($"Missing value for '{flagName}'.");
                value = null;
                return true;
            }

            value = inlineVal;
            return true;
        }

        if (!string.Equals(arg, flagName, StringComparison.Ordinal))
        {
            value = null;
            return false;
        }

        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            error.WriteLine($"Missing value after '{flagName}'.");
            value = null;
            return true;
        }

        value = args[++index];
        return true;
    }

    private static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("Usage: IronFrameCart01.RecipeGen [--schemas-dir <dir>] [--out <path>]");
        writer.WriteLine("  --schemas-dir <dir>  Read-only oracle Dev\\schemas directory (auto-detected if omitted).");
        writer.WriteLine("  --out <path>         Output UTF-8 no-BOM JSON path (default: aot-recipe.json).");
    }
}

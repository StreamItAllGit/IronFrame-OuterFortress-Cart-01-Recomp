// @module: IronFrameCart01.RecipeGen.OracleSchemaLoader
// @purpose: Loads read-only upstream oracle JSON schemas into clean-room function and module descriptors.
// @depends-on: System, System.Collections.Generic, System.Globalization, System.IO, System.Linq, System.Text.Json, System.Text.Json.Serialization
// @exports: OracleFunctionRecord, OracleModuleRecord, OracleSchemaSet, OracleSchemaLoader

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IronFrameCart01.RecipeGen;

/// <summary>Clean-room subroutine descriptor parsed from upstream oracle JSON schemas.</summary>
public sealed record OracleFunctionRecord(
    uint PrgBank, string AddressHex, ushort AddressValue, string Name, string Domain, int Wave, bool IsAnchor);

/// <summary>Clean-room module descriptor parsed from <c>d3_module_map.json</c>.</summary>
public sealed record OracleModuleRecord(
    string RelativePath, string Domain, uint PrgBank, IReadOnlyList<string> Functions);

/// <summary>Validated set of clean-room oracle records ready for recipe emission.</summary>
public sealed record OracleSchemaSet(
    string SourceDirectory,
    IReadOnlyList<OracleFunctionRecord> Functions,
    IReadOnlyList<OracleModuleRecord> Modules,
    int AnchorCount);

/// <summary>
/// Strictly read-only loader for upstream <c>functions_catalog.json</c>, <c>anchor_functions.json</c>,
/// and <c>d3_module_map.json</c>, stripping Ghidra names, summaries, C bodies, and commercial marks.
/// </summary>
public static class OracleSchemaLoader
{
    public const string TargetGameId = "ironframe-outerfortress-cart-01";
    public const uint FixedPrgBank = 7;

    private static readonly string[] RequiredSchemaFiles =
        ["functions_catalog.json", "anchor_functions.json", "d3_module_map.json"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly string[] ForbiddenSubstrings =
    [
        string.Concat("f", "un_"),
        string.Concat("me", "tal", "_", "ge", "ar"),
        string.Concat("me", "tal", "ge", "ar"),
        string.Concat("ko", "na", "mi"),
        string.Concat("nin", "ten", "do"),
        "#include", ";", "{", "}"
    ];

    /// <summary>Attempts to locate the upstream read-only <c>Dev\schemas</c> directory from ancestor roots.</summary>
    public static string? TryResolveDefaultSchemasDirectory()
    {
        string? envDir = Environment.GetEnvironmentVariable("RECOMP_SCHEMAS_DIR");
        if (!string.IsNullOrWhiteSpace(envDir) && HasRequiredSchemaFiles(envDir))
            return Path.GetFullPath(envDir);

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string? start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            if (string.IsNullOrWhiteSpace(start)) continue;
            for (var dir = new DirectoryInfo(Path.GetFullPath(start)); dir is not null; dir = dir.Parent)
            {
                if (!visited.Add(dir.FullName)) continue;
                string? found = ProbeResearchDirectory(Path.Combine(dir.FullName, "Research"));
                if (found is not null) return found;
            }
        }

        return null;
    }

    /// <summary>Loads and validates all three upstream clean-room schema files from <paramref name="schemasDir"/>.</summary>
    public static OracleSchemaSet Load(string schemasDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemasDir);
        string fullDir = Path.GetFullPath(schemasDir);
        if (!Directory.Exists(fullDir))
            throw new DirectoryNotFoundException($"Schemas directory not found: '{fullDir}'.");

        string[] paths = RequiredSchemaFiles
            .Select(f => Path.Combine(fullDir, f))
            .Select(p => File.Exists(p) ? p : throw new FileNotFoundException($"Required schema file not found: '{p}'.", p))
            .ToArray();

        List<RawFunctionDto> rawCatalog = ReadJson<List<RawFunctionDto>>(paths[0]);
        List<RawFunctionDto> rawAnchors = ReadJson<List<RawFunctionDto>>(paths[1]);
        RawModuleMapDto rawModuleMap = ReadJson<RawModuleMapDto>(paths[2]);
        if (rawCatalog.Count == 0)
            throw new InvalidDataException($"'{paths[0]}' contains zero function entries.");

        var anchorKeys = new HashSet<(uint Bank, ushort Addr)>();
        var merged = new Dictionary<(uint Bank, ushort Addr), OracleFunctionRecord>();

        foreach (RawFunctionDto entry in rawAnchors)
        {
            OracleFunctionRecord record = ConvertFunction(entry, isAnchor: true);
            anchorKeys.Add((record.PrgBank, record.AddressValue));
            merged[(record.PrgBank, record.AddressValue)] = record;
        }

        foreach (RawFunctionDto entry in rawCatalog)
        {
            uint bank = ParseProgramBank(entry.Program);
            (ushort addrVal, _) = ParseHexAddress(entry.Address);
            merged[(bank, addrVal)] = ConvertFunction(entry, anchorKeys.Contains((bank, addrVal)));
        }

        List<OracleModuleRecord> modules = ParseModules(rawModuleMap, paths[2]);
        ValidateModuleCoverage(modules, merged.Values);

        var sorted = merged.Values
            .OrderBy(f => f.PrgBank)
            .ThenBy(f => f.AddressValue)
            .ThenBy(f => f.Name, StringComparer.Ordinal)
            .ToList();

        return new OracleSchemaSet(fullDir, sorted, modules, anchorKeys.Count);
    }

    /// <summary>Maps an upstream program filename (e.g., <c>BANK_00.prg</c>, <c>PRG_FIXED_07.prg</c>) to PRG bank <c>0..7</c>.</summary>
    public static uint ParseProgramBank(string? program)
    {
        if (string.IsNullOrWhiteSpace(program))
            throw new InvalidDataException("Schema entry is missing 'program'.");

        return program.Trim().ToUpperInvariant() switch
        {
            "BANK_00.PRG" => 0, "BANK_01.PRG" => 1, "BANK_02.PRG" => 2, "BANK_03.PRG" => 3,
            "BANK_04.PRG" => 4, "BANK_05.PRG" => 5, "BANK_06.PRG" => 6, "PRG_FIXED_07.PRG" => FixedPrgBank,
            _ => throw new InvalidDataException($"Unsupported program bank token '{program}'.")
        };
    }

    /// <summary>Normalizes an address such as <c>"0x000099a8"</c> into canonical 16-bit <c>"0x99A8"</c>.</summary>
    public static (ushort Value, string CanonicalHex) ParseHexAddress(string? rawAddress)
    {
        if (string.IsNullOrWhiteSpace(rawAddress))
            throw new InvalidDataException("Schema entry is missing 'address'.");

        string token = rawAddress.Trim();
        if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) token = token[2..];
        else if (token.StartsWith("$", StringComparison.Ordinal)) token = token[1..];

        if (!uint.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint parsed) ||
            parsed > ushort.MaxValue)
            throw new InvalidDataException($"Invalid 16-bit hex address '{rawAddress}'.");

        ushort value = (ushort)parsed;
        return (value, $"0x{value:X4}");
    }

    /// <summary>Enforces clean-room token rules against Ghidra names, C syntax, and commercial marks.</summary>
    public static string AssertCleanRoomText(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"Schema field '{fieldName}' must not be empty.");

        string trimmed = value.Trim();
        foreach (string forbidden in ForbiddenSubstrings)
        {
            if (trimmed.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Clean-room violation in '{fieldName}': disallowed token '{forbidden}'.");
        }
        return trimmed;
    }

    private static OracleFunctionRecord ConvertFunction(RawFunctionDto dto, bool isAnchor)
    {
        uint bank = ParseProgramBank(dto.Program);
        (ushort addrVal, string addrHex) = ParseHexAddress(dto.Address);
        string name = AssertCleanRoomText(dto.Name, "name");
        string domain = AssertCleanRoomText(dto.Domain, "domain");
        if (dto.Wave < 0)
            throw new InvalidDataException($"Invalid negative wave '{dto.Wave}' on '{name}'.");
        return new OracleFunctionRecord(bank, addrHex, addrVal, name, domain, dto.Wave, isAnchor);
    }

    private static List<OracleModuleRecord> ParseModules(RawModuleMapDto map, string path)
    {
        if (!string.IsNullOrWhiteSpace(map.GameId))
            _ = AssertCleanRoomText(map.GameId, "d3_module_map.gameId");
        if (map.Modules is null || map.Modules.Count == 0)
            throw new InvalidDataException($"'{path}' contains zero modules.");

        var list = map.Modules.Select(m => new OracleModuleRecord(
            AssertCleanRoomText(m.RelativePath, "relativePath"),
            AssertCleanRoomText(m.Domain, "module.domain"),
            ParseProgramBank(m.BankProgram),
            (m.Functions ?? []).Select(f => AssertCleanRoomText(f, "module.function")).ToList()));

        return [.. list.OrderBy(m => m.PrgBank).ThenBy(m => m.RelativePath, StringComparer.Ordinal)];
    }

    private static void ValidateModuleCoverage(
        IReadOnlyList<OracleModuleRecord> modules,
        IEnumerable<OracleFunctionRecord> functions)
    {
        var known = new Dictionary<(uint PrgBank, string Name), string>();
        foreach (OracleFunctionRecord fn in functions)
        {
            if (!known.TryAdd((fn.PrgBank, fn.Name), fn.Domain))
                throw new InvalidDataException($"Duplicate function '{fn.Name}' in bank {fn.PrgBank}.");
        }

        foreach (OracleModuleRecord mod in modules)
        {
            foreach (string fnName in mod.Functions)
            {
                if (!known.TryGetValue((mod.PrgBank, fnName), out string? fnDomain))
                    throw new InvalidDataException($"Module '{mod.RelativePath}' references missing function '{fnName}' in bank {mod.PrgBank}.");
                if (!string.Equals(fnDomain, mod.Domain, StringComparison.Ordinal))
                    throw new InvalidDataException($"Domain mismatch for '{fnName}': catalog '{fnDomain}' vs module '{mod.Domain}'.");
            }
        }
    }

    private static string? ProbeResearchDirectory(string researchDir)
    {
        if (!Directory.Exists(researchDir)) return null;
        string coded = Path.Combine(researchDir, "IronFrame-OuterFortress-Cart-01-Decomp", "Dev", "schemas");
        if (HasRequiredSchemaFiles(coded)) return Path.GetFullPath(coded);

        foreach (string sub in Directory.EnumerateDirectories(researchDir))
        {
            string candidate = Path.Combine(sub, "Dev", "schemas");
            if (HasRequiredSchemaFiles(candidate) && ModuleMapMatchesGameId(Path.Combine(candidate, "d3_module_map.json")))
                return Path.GetFullPath(candidate);
        }
        return null;
    }

    private static bool ModuleMapMatchesGameId(string moduleMapPath)
    {
        try
        {
            using FileStream fs = File.OpenRead(moduleMapPath);
            using JsonDocument doc = JsonDocument.Parse(fs);
            return doc.RootElement.TryGetProperty("gameId", out JsonElement idEl) &&
                string.Equals(idEl.GetString(), TargetGameId, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool HasRequiredSchemaFiles(string dir) =>
        Directory.Exists(dir) && RequiredSchemaFiles.All(f => File.Exists(Path.Combine(dir, f)));

    private static T ReadJson<T>(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException($"Schema file '{path}' deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Invalid JSON in '{path}': {ex.Message}", ex);
        }
    }

    private sealed record RawFunctionDto(
        [property: JsonPropertyName("program")] string? Program,
        [property: JsonPropertyName("address")] string? Address,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("domain")] string? Domain,
        [property: JsonPropertyName("wave")] int Wave);

    private sealed record RawModuleMapDto(
        [property: JsonPropertyName("gameId")] string? GameId,
        [property: JsonPropertyName("modules")] List<RawModuleDto>? Modules);

    private sealed record RawModuleDto(
        [property: JsonPropertyName("relativePath")] string? RelativePath,
        [property: JsonPropertyName("domain")] string? Domain,
        [property: JsonPropertyName("bankProgram")] string? BankProgram,
        [property: JsonPropertyName("functions")] List<string>? Functions);
}

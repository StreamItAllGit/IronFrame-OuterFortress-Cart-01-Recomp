// @module: IronFrameCart01.RecipeGen.AotRecipeEmitter
// @purpose: Maps validated oracle schema records into a deterministic CartAotRecipeSpec clean-room recipe.
// @depends-on: System, System.Collections.Generic, System.Linq, reCart.Contracts
// @exports: AotRecipeEmitter

using reCart.Contracts;

namespace IronFrameCart01.RecipeGen;

/// <summary>
/// Builds and validates a deterministic <see cref="CartAotRecipeSpec"/> for <c>ironframe-outerfortress-cart-01</c>.
/// </summary>
public static class AotRecipeEmitter
{
    public const string DefaultModuleId = OracleSchemaLoader.TargetGameId;
    public const ushort MapperUxRom = 2;
    public const byte DefaultSubmapper = 0;
    public const ushort ResetVectorAddress = 0xC001;
    public const ushort NmiVectorAddress = 0xC06C;
    public const ushort IrqVectorAddress = 0xC0B7;
    public const ushort JumpTableDispatcherAddress = 0xC6B7;
    public const ushort TrampolineRangeStart = 0xC932;
    public const ushort TrampolineRangeEnd = 0xC958;
    public const ushort BankSwitchTableStart = 0xFFD0;
    public const ushort BankSwitchTableEnd = 0xFFD7;

    private static readonly (ushort Address, uint? TargetBank, string Kind)[] CanonicalTrampolines =
    [
        (0xC932, 0u, "unromSelectBank"),
        (0xC936, 1u, "unromSelectBank"),
        (0xC93A, 2u, "unromSelectBank"),
        (0xC93E, 3u, "unromSelectBank"),
        (0xC942, 4u, "unromSelectBank"),
        (0xC946, 5u, "unromSelectBank"),
        (0xC94A, 6u, "unromSelectBank"),
        (0xC94C, null, "unromSelectBankIndex"),
        (0xC958, null, "unromRestoreActiveBank")
    ];

    /// <summary>Converts <paramref name="schemas"/> into a validated <see cref="CartAotRecipeSpec"/>.</summary>
    public static CartAotRecipeSpec Emit(OracleSchemaSet schemas)
    {
        ArgumentNullException.ThrowIfNull(schemas);

        List<CartAotRecipeSeed> seeds = BuildSeeds(schemas.Functions);
        List<string> entries = BuildEntryAddresses(schemas.Functions);
        List<CartAotJumpTableHint> jumpTables = BuildJumpTables(schemas.Functions);
        List<CartAotTrampolineHint> trampolines = BuildTrampolines(schemas.Functions);
        List<CartAotMemoryWatchHint> watches = BuildMemoryWatches();

        var spec = new CartAotRecipeSpec
        {
            SchemaVersion = 1,
            ModuleId = DefaultModuleId,
            PlatformId = CartEngineConstants.PlatformId,
            MapperId = MapperUxRom,
            SubmapperId = DefaultSubmapper,
            EntryAddresses = entries,
            Seeds = seeds,
            JumpTables = jumpTables,
            Trampolines = trampolines,
            MemoryWatches = watches
        };

        spec.Validate();
        AssertCleanRoomSpec(spec);
        return spec;
    }

    private static List<CartAotRecipeSeed> BuildSeeds(IReadOnlyList<OracleFunctionRecord> functions)
    {
        return functions
            .OrderBy(f => f.PrgBank)
            .ThenBy(f => f.AddressValue)
            .ThenBy(f => f.Name, StringComparer.Ordinal)
            .Select(f => new CartAotRecipeSeed(
                f.PrgBank,
                f.AddressHex,
                ClassifySeedKind(f),
                OracleSchemaLoader.AssertCleanRoomText(f.Name, "seed.label")))
            .ToList();
    }

    private static string ClassifySeedKind(OracleFunctionRecord fn)
    {
        if (fn.PrgBank == OracleSchemaLoader.FixedPrgBank)
        {
            if (fn.AddressValue is ResetVectorAddress or NmiVectorAddress or IrqVectorAddress)
                return "vector";
            if (fn.AddressValue == JumpTableDispatcherAddress)
                return "jumpTable";
            if (fn.AddressValue is >= TrampolineRangeStart and <= TrampolineRangeEnd)
                return "trampoline";
        }

        return fn.IsAnchor ? "anchor" : "function";
    }

    private static List<string> BuildEntryAddresses(IReadOnlyList<OracleFunctionRecord> functions)
    {
        var addresses = new HashSet<ushort> { ResetVectorAddress, NmiVectorAddress };
        foreach (OracleFunctionRecord fn in functions)
        {
            if (fn.IsAnchor && fn.PrgBank == OracleSchemaLoader.FixedPrgBank && fn.AddressValue >= 0xC000)
                addresses.Add(fn.AddressValue);
        }

        return addresses.OrderBy(a => a).Select(a => $"0x{a:X4}").ToList();
    }

    private static List<CartAotJumpTableHint> BuildJumpTables(IReadOnlyList<OracleFunctionRecord> functions)
    {
        OracleFunctionRecord? dispatcher = functions.FirstOrDefault(
            f => f.PrgBank == OracleSchemaLoader.FixedPrgBank && f.AddressValue == JumpTableDispatcherAddress);

        string notes = dispatcher is null
            ? "RTS inline jump-table dispatcher"
            : $"{dispatcher.Name}: RTS inline jump-table dispatcher";

        return [new CartAotJumpTableHint(OracleSchemaLoader.FixedPrgBank, $"0x{JumpTableDispatcherAddress:X4}", notes)];
    }

    private static List<CartAotTrampolineHint> BuildTrampolines(IReadOnlyList<OracleFunctionRecord> functions)
    {
        var map = new Dictionary<ushort, CartAotTrampolineHint>();

        foreach ((ushort addr, uint? targetBank, string kind) in CanonicalTrampolines)
        {
            map[addr] = new CartAotTrampolineHint($"0x{addr:X4}", targetBank, kind);
        }

        foreach (OracleFunctionRecord fn in functions)
        {
            if (fn.PrgBank == OracleSchemaLoader.FixedPrgBank &&
                fn.AddressValue is >= TrampolineRangeStart and <= TrampolineRangeEnd &&
                !map.ContainsKey(fn.AddressValue))
            {
                map[fn.AddressValue] = new CartAotTrampolineHint(fn.AddressHex, null, fn.Name);
            }
        }

        for (ushort addr = BankSwitchTableStart; addr <= BankSwitchTableEnd; addr++)
        {
            uint bank = (uint)(addr - BankSwitchTableStart);
            map[addr] = new CartAotTrampolineHint($"0x{addr:X4}", bank, "unromBankSwitchTable($FFD0-$FFD7)");
        }

        return map.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList();
    }

    private static List<CartAotMemoryWatchHint> BuildMemoryWatches() =>
    [
        new CartAotMemoryWatchHint("oam", "0x0200", 256),
        new CartAotMemoryWatchHint("actors", "0x0300", 128),
        new CartAotMemoryWatchHint("audio", "0x0380", 128)
    ];

    private static void AssertCleanRoomSpec(CartAotRecipeSpec spec)
    {
        OracleSchemaLoader.AssertCleanRoomText(spec.ModuleId, "spec.moduleId");
        OracleSchemaLoader.AssertCleanRoomText(spec.PlatformId, "spec.platformId");

        foreach (CartAotRecipeSeed seed in spec.Seeds)
        {
            if (seed.Kind is not null) OracleSchemaLoader.AssertCleanRoomText(seed.Kind, "seed.kind");
            if (seed.Label is not null) OracleSchemaLoader.AssertCleanRoomText(seed.Label, "seed.label");
        }

        foreach (CartAotJumpTableHint jt in spec.JumpTables)
        {
            if (jt.Notes is not null) OracleSchemaLoader.AssertCleanRoomText(jt.Notes, "jumpTable.notes");
        }

        foreach (CartAotTrampolineHint tr in spec.Trampolines)
        {
            if (tr.Kind is not null) OracleSchemaLoader.AssertCleanRoomText(tr.Kind, "trampoline.kind");
        }

        foreach (CartAotMemoryWatchHint mw in spec.MemoryWatches)
        {
            OracleSchemaLoader.AssertCleanRoomText(mw.RegionId, "memoryWatch.regionId");
        }
    }
}

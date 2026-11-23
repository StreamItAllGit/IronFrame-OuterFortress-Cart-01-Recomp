// @module: IronFrameCart01.Tests.RecipeGenTests
// @purpose: Verifies clean-room OracleSchemaLoader, deterministic AotRecipeEmitter generation, checked-in aot-recipe.json invariants, and zero retail/PII leaks.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, Xunit, IronFrameCart01.RecipeGen, reCart.Contracts
// @exports: RecipeGenTests

using System.Text;
using IronFrameCart01.RecipeGen;
using reCart.Contracts;
using Xunit;

namespace IronFrameCart01.Tests;

public sealed class RecipeGenTests
{
    [Fact]
    public void CheckedInAotRecipe_ContainsCanonicalSeedEntryJumpTableTrampolineAndWatchCounts()
    {
        string devRoot = TestSandboxSupport.ResolveDevRoot();
        string recipePath = Path.Combine(devRoot, "src", "IronFrameCart01.Module", "Manifests", "aot-recipe.json");
        Assert.True(File.Exists(recipePath), $"Missing checked-in recipe: {recipePath}");

        string json = File.ReadAllText(recipePath, Encoding.UTF8);
        CartAotRecipeSpec spec = CartAotRecipeSpec.Deserialize(json);
        spec.Validate();

        Assert.Equal("ironframe-outerfortress-cart-01", spec.ModuleId);
        Assert.Equal((ushort)2, spec.MapperId);
        Assert.Equal(499, spec.Seeds.Count);
        Assert.Equal(37, spec.EntryAddresses.Count);
        Assert.Single(spec.JumpTables);
        Assert.Equal("0xC6B7", spec.JumpTables[0].DispatcherAddress);
        Assert.Equal(17, spec.Trampolines.Count);
        Assert.Equal(3, spec.MemoryWatches.Count);
        Assert.Contains(spec.EntryAddresses, a => a == "0xC001");
        Assert.Contains(spec.EntryAddresses, a => a == "0xC06C");
    }

    [Fact]
    public void CheckedInAotRecipe_AndModuleManifest_ContainZeroRetailCodeAssetOrPiiLeaks()
    {
        string devRoot = TestSandboxSupport.ResolveDevRoot();
        string[] paths =
        [
            Path.Combine(devRoot, "module.recomp.json"),
            Path.Combine(devRoot, "src", "IronFrameCart01.Module", "Manifests", "aot-recipe.json")
        ];

        string[] forbidden =
        [
            string.Concat("f", "un_"),
            string.Concat("me", "tal", "ge", "ar"),
            string.Concat("me", "tal", "_", "ge", "ar"),
            string.Concat("ko", "na", "mi"),
            string.Concat("nin", "ten", "do"),
            string.Concat("C:\\", "Users\\"),
            string.Concat("D:\\", "Projects\\"),
            "#include", ";"
        ];

        foreach (string path in paths)
        {
            string raw = File.ReadAllText(path, Encoding.UTF8);
            foreach (string token in forbidden)
            {
                Assert.DoesNotContain(token, raw, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void AotRecipeEmitter_IsByteIdenticalAcrossRepeatedSyntheticLoads()
    {
        string dir = TestSandboxSupport.CreateTempDir("recipe_determ");
        try
        {
            WriteSyntheticOracleSchemas(dir);
            OracleSchemaSet firstLoad = OracleSchemaLoader.Load(dir);
            OracleSchemaSet secondLoad = OracleSchemaLoader.Load(dir);

            string firstJson = AotRecipeEmitter.Emit(firstLoad).Serialize();
            string secondJson = AotRecipeEmitter.Emit(secondLoad).Serialize();

            Assert.Equal(firstJson, secondJson);
            CartAotRecipeSpec parsed = CartAotRecipeSpec.Deserialize(firstJson);
            Assert.Equal(4, parsed.Seeds.Count);
            Assert.Contains(parsed.Seeds, s => s.Address == "0xC6B7" && s.Kind == "jumpTable");
            Assert.Contains(parsed.Seeds, s => s.Address == "0xC932" && s.Kind == "trampoline");
            Assert.Contains(parsed.Seeds, s => s.Address == "0xC001" && s.Kind == "vector");
        }
        finally { TestSandboxSupport.SafeDeleteDir(dir); }
    }

    [Theory]
    [InlineData("BANK_00.prg", 0u)]
    [InlineData("bank_06.prg", 6u)]
    [InlineData("PRG_FIXED_07.prg", 7u)]
    public void ParseProgramBank_MapsExpectedUxRomBanks(string token, uint expectedBank)
    {
        Assert.Equal(expectedBank, OracleSchemaLoader.ParseProgramBank(token));
    }

    [Theory]
    [InlineData("BANK_09.prg")]
    [InlineData("CHR_00.chr")]
    [InlineData("")]
    public void ParseProgramBank_RejectsUnsupportedTokens(string badToken)
    {
        Assert.Throws<InvalidDataException>(() => OracleSchemaLoader.ParseProgramBank(badToken));
    }

    [Theory]
    [InlineData("0x0000c001", 0xC001, "0xC001")]
    [InlineData("$c6b7", 0xC6B7, "0xC6B7")]
    [InlineData("0x0200", 0x0200, "0x0200")]
    public void ParseHexAddress_NormalizesCanonicalUpperHex(string raw, ushort expectedVal, string expectedHex)
    {
        (ushort val, string hex) = OracleSchemaLoader.ParseHexAddress(raw);
        Assert.Equal(expectedVal, val);
        Assert.Equal(expectedHex, hex);
    }

    [Fact]
    public void AssertCleanRoomText_RejectsGhidraCAndCommercialTokens()
    {
        Assert.Equal("reset_entry_boot", OracleSchemaLoader.AssertCleanRoomText("  reset_entry_boot  ", "name"));

        string ghidra = string.Concat("f", "un_", "c001");
        string cBody = "void foo() { return; }";
        string commercial = string.Concat("Me", "tal", "Ge", "ar");

        Assert.Throws<InvalidDataException>(() => OracleSchemaLoader.AssertCleanRoomText(ghidra, "name"));
        Assert.Throws<InvalidDataException>(() => OracleSchemaLoader.AssertCleanRoomText(cBody, "summary"));
        Assert.Throws<InvalidDataException>(() => OracleSchemaLoader.AssertCleanRoomText(commercial, "title"));
    }

    [Fact]
    public void RecipeGenCli_MatchesCheckedInRecipeWhenUpstreamSchemasPresent()
    {
        string? schemasDir = OracleSchemaLoader.TryResolveDefaultSchemasDirectory();
        if (schemasDir is null) return; // Upstream Research repo may not be adjacent in clean CI clones.

        string devRoot = TestSandboxSupport.ResolveDevRoot();
        string checkedIn = File.ReadAllText(Path.Combine(devRoot, "src", "IronFrameCart01.Module", "Manifests", "aot-recipe.json"), Encoding.UTF8);
        string regenerated = AotRecipeEmitter.Emit(OracleSchemaLoader.Load(schemasDir)).Serialize();

        Assert.Equal(checkedIn, regenerated);
    }

    private static void WriteSyntheticOracleSchemas(string dir)
    {
        File.WriteAllText(Path.Combine(dir, "functions_catalog.json"), """
        [
          { "program": "PRG_FIXED_07.prg", "address": "0x0000c001", "name": "boot_reset_entry", "domain": "engine", "wave": 1 },
          { "program": "PRG_FIXED_07.prg", "address": "0x0000c6b7", "name": "rts_jump_dispatch", "domain": "engine", "wave": 1 },
          { "program": "PRG_FIXED_07.prg", "address": "0x0000c932", "name": "trampoline_bank_0", "domain": "engine", "wave": 1 },
          { "program": "BANK_00.prg", "address": "0x00008000", "name": "stage_tick_step", "domain": "gameplay", "wave": 2 }
        ]
        """);

        File.WriteAllText(Path.Combine(dir, "anchor_functions.json"), """
        [
          { "program": "PRG_FIXED_07.prg", "address": "0x0000c001", "name": "boot_reset_entry", "domain": "engine", "wave": 1 }
        ]
        """);

        File.WriteAllText(Path.Combine(dir, "d3_module_map.json"), """
        {
          "gameId": "ironframe-outerfortress-cart-01",
          "modules": [
            { "relativePath": "core/boot.cs", "domain": "engine", "bankProgram": "PRG_FIXED_07.prg", "functions": ["boot_reset_entry", "rts_jump_dispatch", "trampoline_bank_0"] },
            { "relativePath": "features/stage.cs", "domain": "gameplay", "bankProgram": "BANK_00.prg", "functions": ["stage_tick_step"] }
          ]
        }
        """);
    }
}

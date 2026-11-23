// @module: IronFrameCart01.Module.Cart01Constants
// @purpose: Clean-room coded identity, UNROM mapper/trampoline addresses, BYO ROM digests, and WRAM watch constants.
// @depends-on: System.Collections.Generic, Foundation.Retro, reCart.Contracts
// @exports: Cart01Constants

using Foundation.Retro;
using reCart.Contracts;

namespace IronFrameCart01.Module;

/// <summary>
/// Canonical clean-room constants and default manifest factory for <c>IronFrame OuterFortress (Cart) 01</c>.
/// </summary>
public static class Cart01Constants
{
    /// <summary>Manifest schema version (<c>1</c>).</summary>
    public const int SchemaVersion = 1;

    /// <summary>Canonical lowercase-kebab clean-room module identifier.</summary>
    public const string ModuleId = "ironframe-outerfortress-cart-01";

    /// <summary>Clean-room human-facing cartridge display name.</summary>
    public const string DisplayName = "IronFrame OuterFortress (Cart) 01";

    /// <summary>Target hardware platform identifier (<c>nes</c>).</summary>
    public const string PlatformId = CartEngineConstants.PlatformId;

    /// <summary>Current module semantic version (<c>0.1.0</c>).</summary>
    public const string ModuleVersion = "0.1.0";

    /// <summary>Target coded platform engine identifier (<c>reCart</c>).</summary>
    public const string EngineId = CartEngineConstants.EngineId;

    /// <summary>Minimum compatible <c>reCart</c> engine version.</summary>
    public const string EngineVersion = CartEngineConstants.Version;

    /// <summary>Primary compatible engine release track.</summary>
    public const string CompatibleEngineTrack = CartEngineConstants.CompatibleEngineTrack;

    /// <summary>Ordered list of compatible <c>reCart</c> engine tracks.</summary>
    public static IReadOnlyList<string> SupportedEngineTracks { get; } = [.. CartEngineConstants.SupportedEngineTracks];

    /// <summary>Technical cartridge catalog serial code.</summary>
    public const string SerialNumber = "NES-ME-USA";

    /// <summary>Default relative BYO cartridge media directory (<c>data/rom</c>).</summary>
    public const string PreloadRomDir = "data/rom";

    /// <summary>Entry descriptor packaged inside the clean-room <c>.recomp</c> archive.</summary>
    public const string EntryAssembly = "aot-recipe.json";

    /// <summary>Fixed-bank boot entrypoint descriptor.</summary>
    public const string DefaultBootExecutable = "PRG_FIXED_07:0xC001";

    /// <summary>Fully qualified statically lifted module class name.</summary>
    public const string ModuleTypeName = "IronFrame.OuterFortress.Cart01.CartLiftedGameModule";

    /// <summary>Clean-room output filename for the 2-member recipe <c>.recomp</c> cartridge archive.</summary>
    public const string ArchivePackageFileName = "IronFrame-OuterFortress-Cart-01.recomp";

    /// <summary>Numeric iNES / NES 2.0 mapper identifier (<c>2</c> = UNROM).</summary>
    public const ushort MapperId = 2;

    /// <summary>Numeric NES 2.0 submapper identifier (<c>0</c>).</summary>
    public const byte SubmapperId = 0;

    /// <summary>Index of the permanently mapped upper <c>16 KB</c> PRG bank (<c>$C000-$FFFF</c>).</summary>
    public const uint FixedBank = 7;

    /// <summary>Total count of <c>16 KB</c> PRG-ROM banks in the cartridge (<c>8 * 16 KB = 128 KB</c>).</summary>
    public const int PrgBankCount = 8;

    /// <summary>Size in bytes of a single switchable or fixed <c>16 KB</c> PRG bank.</summary>
    public const int PrgBankSizeBytes = CartPrgHeaderSynthesizer.PrgBankSizeBytes;

    /// <summary>Fixed-bank <c>RESET</c> entrypoint CPU address (<c>0xC001</c>).</summary>
    public const ushort ResetVectorAddress = 0xC001;

    /// <summary>Fixed-bank VBlank <c>NMI</c> entrypoint CPU address (<c>0xC06C</c>).</summary>
    public const ushort NmiVectorAddress = 0xC06C;

    /// <summary>Fixed-bank inline RTS jump-table dispatcher address (<c>0xC6B7</c>).</summary>
    public const ushort JumpTableDispatcher = 0xC6B7;

    /// <summary>Inclusive start address of the cross-bank UNROM trampoline block (<c>0xC932</c>).</summary>
    public const ushort TrampolineStart = 0xC932;

    /// <summary>Inclusive end address of the cross-bank UNROM trampoline block (<c>0xC958</c>).</summary>
    public const ushort TrampolineEnd = 0xC958;

    /// <summary>Inclusive start address of the bus-conflict-safe bank-select lookup table (<c>0xFFD0</c>).</summary>
    public const ushort BankSwitchTableStart = 0xFFD0;

    /// <summary>Inclusive end address of the bus-conflict-safe bank-select lookup table (<c>0xFFD7</c>).</summary>
    public const ushort BankSwitchTableEnd = 0xFFD7;

    /// <summary>Expected lowercase SHA-256 digest for the full <c>131,088</c>-byte NES 2.0 cartridge container.</summary>
    public const string NesExpectedSha256 = "79095355436222161cb3841ff9b5c7a572e77af11461935e433bd6e8dfd0f164";

    /// <summary>Expected total byte length for a headered <c>.nes</c> cartridge container (<c>16 + 131,072</c>).</summary>
    public const int NesExpectedSizeBytes = 131088;

    /// <summary>Expected lowercase CRC32 digest for the full <c>131,088</c>-byte <c>.nes</c> container.</summary>
    public const string NesExpectedCrc32 = "25ba3d04";

    /// <summary>Canonical relative path inside <c>data\rom\</c> for the headered <c>.nes</c> dump.</summary>
    public const string ExpectedNesRelativePath = "data/rom/NES-ME-USA.nes";

    /// <summary>Expected lowercase SHA-256 digest for the headerless <c>131,072</c>-byte PRG payload.</summary>
    public const string PrgExpectedSha256 = "f6d29afbd7ddad33672852232791a396695de2e77dccb83088a8d6b139d8c9cb";

    /// <summary>Expected byte length for a headerless <c>.prg</c> PRG-ROM payload (<c>8 * 16,384</c>).</summary>
    public const int PrgExpectedSizeBytes = 131072;

    /// <summary>Expected lowercase CRC32 digest for the headerless <c>131,072</c>-byte PRG payload.</summary>
    public const string PrgExpectedCrc32 = "817431ec";

    /// <summary>Canonical relative path inside <c>data\rom\</c> for a headerless <c>.prg</c> dump.</summary>
    public const string ExpectedPrgRelativePath = "data/rom/NES-ME-USA.prg";

    /// <summary>Start CPU WRAM address for the <c>256</c>-byte OAM DMA shadow buffer (<c>$0200</c>).</summary>
    public const ushort OamShadowAddress = 0x0200;

    /// <summary>Byte length of the OAM DMA shadow buffer (<c>256</c> bytes).</summary>
    public const int OamShadowLengthBytes = 256;

    /// <summary>Start CPU WRAM address for the <c>32</c>-slot SoA actor/sprite array (<c>$0300</c>).</summary>
    public const ushort ActorArrayAddress = 0x0300;

    /// <summary>Byte length of the SoA actor/sprite telemetry region (<c>128</c> bytes).</summary>
    public const int ActorArrayLengthBytes = 128;

    /// <summary>Start CPU WRAM address for the <c>6</c>-channel SoA audio telemetry block (<c>$0380</c>).</summary>
    public const ushort AudioTelemetryAddress = 0x0380;

    /// <summary>Byte length of the SoA audio telemetry region (<c>128</c> bytes).</summary>
    public const int AudioTelemetryLengthBytes = 128;

    /// <summary>Returns whether <paramref name="address"/> falls within the cross-bank trampoline range (<c>$C932-$C958</c>).</summary>
    public static bool IsTrampolineAddress(ushort address) => address is >= TrampolineStart and <= TrampolineEnd;

    /// <summary>Returns whether <paramref name="address"/> falls within the bank-switch identity table (<c>$FFD0-$FFD7</c>).</summary>
    public static bool IsBankSwitchTableAddress(ushort address) => address is >= BankSwitchTableStart and <= BankSwitchTableEnd;

    /// <summary>Creates a clean-room <see cref="CartPackageManifest"/> populated with canonical Cart 01 defaults.</summary>
    public static CartPackageManifest CreateDefaultManifest() => new()
    {
        SchemaVersion = SchemaVersion,
        ModuleId = ModuleId,
        Title = DisplayName,
        PlatformId = PlatformId,
        Version = ModuleVersion,
        EngineId = EngineId,
        EngineVersion = EngineVersion,
        CompatibleEngineTrack = CompatibleEngineTrack,
        SupportedEngineTracks = [.. SupportedEngineTracks],
        MapperId = MapperId,
        SubmapperId = SubmapperId,
        VideoRegionStandardName = "ntsc",
        ExecutionTierMode = "staticAotOnly",
        BackendId = CartRenderBackendIds.OpenGl,
        AspectRatioMode = "nativePar",
        PreloadRomDir = PreloadRomDir,
        SerialNumber = SerialNumber,
        EntryAssembly = EntryAssembly,
        ModuleTypeName = ModuleTypeName,
        DefaultFramerateMode = RecompTimingMode.LockedCap,
        RequiredDiscChecksums =
        [
            new DiscChecksumEntry(ExpectedNesRelativePath, NesExpectedSha256, NesExpectedSizeBytes, IsRequired: true),
            new DiscChecksumEntry(ExpectedPrgRelativePath, PrgExpectedSha256, PrgExpectedSizeBytes, IsRequired: false)
        ]
    };
}

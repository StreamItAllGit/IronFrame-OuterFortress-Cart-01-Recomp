// @module: IronFrameCart01.Module.Cart01MemoryWatch
// @purpose: Read-only Structure-of-Arrays (SoA) WRAM snapshot helper for $0200 OAM, $0300 actors, and $0380 audio telemetry.
// @depends-on: System, System.Collections.Generic, reCart.Contracts
// @exports: Cart01OamSpriteEntry, Cart01OamSnapshot, Cart01ActorSlot, Cart01ActorSoaSnapshot, Cart01AudioSoaSnapshot, Cart01MemorySnapshot, Cart01MemoryWatch

using reCart.Contracts;

namespace IronFrameCart01.Module;

/// <summary>
/// Decoded 4-byte OAM sprite slot sampled read-only from <c>$0200..$02FF</c>.
/// </summary>
public readonly record struct Cart01OamSpriteEntry(int Index, byte Y, byte TileIndex, byte Attributes, byte X)
{
    /// <summary>Indicates whether the sprite Y coordinate falls inside the visible <c>240</c>-line NTSC frame.</summary>
    public bool IsVisible => Y < 0xEF;
}

/// <summary>
/// Read-only snapshot of the <c>256</c>-byte OAM shadow buffer (<c>$0200..$02FF</c>).
/// </summary>
public sealed class Cart01OamSnapshot
{
    private readonly byte[] _raw;
    private readonly Cart01OamSpriteEntry[] _entries;

    internal Cart01OamSnapshot(byte[] rawBytes)
    {
        _raw = rawBytes;
        _entries = new Cart01OamSpriteEntry[Cart01MemoryWatch.OamSpriteCount];
        int visible = 0;
        for (int i = 0; i < _entries.Length; i++)
        {
            int o = i * 4;
            var entry = new Cart01OamSpriteEntry(i, rawBytes[o], rawBytes[o + 1], rawBytes[o + 2], rawBytes[o + 3]);
            _entries[i] = entry;
            if (entry.IsVisible) visible++;
        }
        VisibleSpriteCount = visible;
    }

    /// <summary>Immutable view of the <c>64</c> decoded OAM entries.</summary>
    public IReadOnlyList<Cart01OamSpriteEntry> Entries => Array.AsReadOnly(_entries);

    /// <summary>Count of OAM slots whose <c>Y</c> coordinate is less than <c>0xEF</c>.</summary>
    public int VisibleSpriteCount { get; }

    /// <summary>Read-only view of the raw <c>256</c>-byte OAM shadow copy.</summary>
    public ReadOnlyMemory<byte> RawBytes => _raw;
}

/// <summary>
/// Reconstructed slot view assembled from the <c>$0300..$037F</c> SoA sprite/actor arrays.
/// </summary>
public readonly record struct Cart01ActorSlot(
    int SlotIndex,
    bool IsPlayerSlot,
    byte SpriteId,
    byte X,
    byte Y,
    byte Attributes)
{
    /// <summary>Indicates whether the slot carries a non-zero sprite ID or on-screen Y coordinate.</summary>
    public bool IsActive => SpriteId != 0 || Y < 0xEF;
}

/// <summary>
/// Read-only Structure-of-Arrays (SoA) snapshot of <c>$0300..$037F</c> (<c>8</c> player slots + <c>24</c> actor slots).
/// </summary>
public sealed class Cart01ActorSoaSnapshot
{
    private readonly byte[] _raw;
    private readonly byte[] _spriteIds;
    private readonly byte[] _positionsX;
    private readonly byte[] _positionsY;
    private readonly byte[] _attributes;

    internal Cart01ActorSoaSnapshot(byte[] rawBytes)
    {
        _raw = rawBytes;
        const int count = Cart01MemoryWatch.TotalActorArraySlots;
        _spriteIds = rawBytes.AsSpan(0x00, count).ToArray();
        _positionsX = rawBytes.AsSpan(0x20, count).ToArray();
        _positionsY = rawBytes.AsSpan(0x40, count).ToArray();
        _attributes = rawBytes.AsSpan(0x60, count).ToArray();

        int playerActive = 0;
        int actorActive = 0;
        for (int i = 0; i < count; i++)
        {
            if (_spriteIds[i] == 0) continue;
            if (i < Cart01MemoryWatch.PlayerSpriteSlotCount) playerActive++;
            else actorActive++;
        }

        ActivePlayerSlotCount = playerActive;
        ActiveActorSlotCount = actorActive;
    }

    /// <summary>SoA sprite ID array (<c>$0300..$031F</c>, slots <c>0..7</c> player, <c>8..31</c> actors).</summary>
    public IReadOnlyList<byte> SpriteIds => Array.AsReadOnly(_spriteIds);

    /// <summary>SoA horizontal coordinate array (<c>$0320..$033F</c>).</summary>
    public IReadOnlyList<byte> PositionsX => Array.AsReadOnly(_positionsX);

    /// <summary>SoA vertical coordinate array (<c>$0340..$035F</c>).</summary>
    public IReadOnlyList<byte> PositionsY => Array.AsReadOnly(_positionsY);

    /// <summary>SoA sprite attribute flags (<c>$0360..$037F</c>).</summary>
    public IReadOnlyList<byte> Attributes => Array.AsReadOnly(_attributes);

    /// <summary>Number of non-zero player sprite slots (<c>0..7</c>).</summary>
    public int ActivePlayerSlotCount { get; }

    /// <summary>Number of non-zero actor sprite slots (<c>8..31</c>).</summary>
    public int ActiveActorSlotCount { get; }

    /// <summary>Read-only view of the raw <c>128</c>-byte <c>$0300..$037F</c> block.</summary>
    public ReadOnlyMemory<byte> RawBytes => _raw;

    /// <summary>Assembles a single SoA slot descriptor for <paramref name="slotIndex"/> (<c>0..31</c>).</summary>
    public Cart01ActorSlot GetSlot(int slotIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slotIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slotIndex, Cart01MemoryWatch.TotalActorArraySlots);
        return new Cart01ActorSlot(
            slotIndex,
            slotIndex < Cart01MemoryWatch.PlayerSpriteSlotCount,
            _spriteIds[slotIndex],
            _positionsX[slotIndex],
            _positionsY[slotIndex],
            _attributes[slotIndex]);
    }
}

/// <summary>
/// Read-only Structure-of-Arrays (SoA) snapshot of the <c>6</c>-channel audio telemetry block (<c>$0380..$03FF</c>).
/// </summary>
public sealed class Cart01AudioSoaSnapshot
{
    private readonly byte[] _raw;
    private readonly byte[] _noteTimers;
    private readonly byte[] _trackIds;
    private readonly byte[] _freqLow;
    private readonly byte[] _freqHigh;
    private readonly byte[] _channelFlags;
    private readonly byte[] _channelVolumes;
    private readonly byte[] _envelopeIndices;
    private readonly byte[] _dutyMasks;

    internal Cart01AudioSoaSnapshot(byte[] rawBytes)
    {
        _raw = rawBytes;
        const int ch = Cart01MemoryWatch.AudioChannelCount;
        _noteTimers = rawBytes.AsSpan(0x00, ch).ToArray();
        _trackIds = rawBytes.AsSpan(0x06, ch).ToArray();
        _freqLow = rawBytes.AsSpan(0x0C, ch).ToArray();
        _freqHigh = rawBytes.AsSpan(0x12, ch).ToArray();
        _channelFlags = rawBytes.AsSpan(0x2A, ch).ToArray();
        _channelVolumes = rawBytes.AsSpan(0x36, ch).ToArray();
        _envelopeIndices = rawBytes.AsSpan(0x3C, ch).ToArray();
        _dutyMasks = rawBytes.AsSpan(0x54, ch).ToArray();

        SfxDutyState = rawBytes[0x26];
        GlobalMuteFlag = rawBytes[0x42];
        PauseState = rawBytes[0x7F];

        int active = 0;
        for (int i = 0; i < ch; i++)
        {
            if (_trackIds[i] != 0 || _noteTimers[i] != 0 || _channelVolumes[i] != 0) active++;
        }
        ActiveChannelCount = active;
    }

    /// <summary>Per-channel note countdown timers (<c>$0380..$0385</c>).</summary>
    public IReadOnlyList<byte> NoteTimers => Array.AsReadOnly(_noteTimers);

    /// <summary>Per-channel active track/sequence IDs (<c>$0386..$038B</c>).</summary>
    public IReadOnlyList<byte> TrackIds => Array.AsReadOnly(_trackIds);

    /// <summary>Per-channel frequency/timer low bytes (<c>$038C..$0391</c>).</summary>
    public IReadOnlyList<byte> FrequencyLow => Array.AsReadOnly(_freqLow);

    /// <summary>Per-channel frequency/timer high bytes (<c>$0392..$0397</c>).</summary>
    public IReadOnlyList<byte> FrequencyHigh => Array.AsReadOnly(_freqHigh);

    /// <summary>Per-channel state bitflags (<c>$03AA..$03AF</c>).</summary>
    public IReadOnlyList<byte> ChannelFlags => Array.AsReadOnly(_channelFlags);

    /// <summary>Per-channel output volume registers (<c>$03B6..$03BB</c>).</summary>
    public IReadOnlyList<byte> ChannelVolumes => Array.AsReadOnly(_channelVolumes);

    /// <summary>Per-channel envelope table indices (<c>$03BC..$03C1</c>).</summary>
    public IReadOnlyList<byte> EnvelopeIndices => Array.AsReadOnly(_envelopeIndices);

    /// <summary>Per-channel duty cycle masks (<c>$03D4..$03D9</c>).</summary>
    public IReadOnlyList<byte> DutyMasks => Array.AsReadOnly(_dutyMasks);

    /// <summary>SFX duty state byte (<c>$03A6</c>).</summary>
    public byte SfxDutyState { get; }

    /// <summary>Global sound mute byte (<c>$03C2</c>).</summary>
    public byte GlobalMuteFlag { get; }

    /// <summary>Audio engine pause state byte (<c>$03FF</c>).</summary>
    public byte PauseState { get; }

    /// <summary>Indicates whether global audio mute is active (<c>$03C2 != 0</c>).</summary>
    public bool IsGloballyMuted => GlobalMuteFlag != 0;

    /// <summary>Indicates whether audio playback is paused (<c>$03FF != 0</c>).</summary>
    public bool IsSoundPaused => PauseState != 0;

    /// <summary>Count of audio channels (<c>0..5</c>) with non-zero track, note timer, or volume.</summary>
    public int ActiveChannelCount { get; }

    /// <summary>Read-only view of the raw <c>128</c>-byte <c>$0380..$03FF</c> block.</summary>
    public ReadOnlyMemory<byte> RawBytes => _raw;

    /// <summary>Combines <see cref="FrequencyLow"/> and <see cref="FrequencyHigh"/> for channel <paramref name="channelIndex"/> (<c>0..5</c>).</summary>
    public ushort GetChannelPeriod(int channelIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(channelIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(channelIndex, Cart01MemoryWatch.AudioChannelCount);
        return (ushort)(_freqLow[channelIndex] | (_freqHigh[channelIndex] << 8));
    }
}

/// <summary>
/// Unified read-only telemetry snapshot covering <c>$0200</c> OAM, <c>$0300</c> SoA actors, and <c>$0380</c> SoA audio.
/// </summary>
public sealed record Cart01MemorySnapshot(
    uint ActivePrgBank,
    uint ActiveChrBank,
    Cart01OamSnapshot Oam,
    Cart01ActorSoaSnapshot Actors,
    Cart01AudioSoaSnapshot Audio);

/// <summary>
/// Read-only WRAM telemetry sampler querying <see cref="ICartMemoryBus"/> without mutating RAM, PPU/APU registers, or bank latches.
/// </summary>
public static class Cart01MemoryWatch
{
    /// <summary>Hardware OAM sprite slot count (<c>64</c>).</summary>
    public const int OamSpriteCount = 64;

    /// <summary>Number of player sprite slots inside <c>$0300..$0307</c> (<c>8</c>).</summary>
    public const int PlayerSpriteSlotCount = 8;

    /// <summary>Number of actor sprite slots inside <c>$0308..$031F</c> (<c>24</c>).</summary>
    public const int ActorSpriteSlotCount = 24;

    /// <summary>Total SoA sprite/actor slots across <c>$0300..$037F</c> (<c>32</c>).</summary>
    public const int TotalActorArraySlots = PlayerSpriteSlotCount + ActorSpriteSlotCount;

    /// <summary>Number of sound sequencer channels inside <c>$0380..$03FF</c> (<c>6</c>).</summary>
    public const int AudioChannelCount = 6;

    /// <summary>Captures an immutable read-only SoA snapshot of OAM, actors, audio, and active bank indices.</summary>
    public static Cart01MemorySnapshot CaptureSnapshot(ICartMemoryBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        return new Cart01MemorySnapshot(
            bus.ActivePrgBank,
            bus.ActiveChrBank,
            ReadOamShadow(bus),
            ReadActorArray(bus),
            ReadAudioTelemetry(bus));
    }

    /// <summary>Reads the <c>256</c>-byte OAM shadow buffer (<c>$0200..$02FF</c>) strictly read-only.</summary>
    public static Cart01OamSnapshot ReadOamShadow(ICartMemoryBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        return new Cart01OamSnapshot(ReadWramSlice(bus, Cart01Constants.OamShadowAddress, Cart01Constants.OamShadowLengthBytes));
    }

    /// <summary>Reads the <c>128</c>-byte SoA actor/sprite array (<c>$0300..$037F</c>) strictly read-only.</summary>
    public static Cart01ActorSoaSnapshot ReadActorArray(ICartMemoryBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        return new Cart01ActorSoaSnapshot(ReadWramSlice(bus, Cart01Constants.ActorArrayAddress, Cart01Constants.ActorArrayLengthBytes));
    }

    /// <summary>Reads the <c>128</c>-byte SoA audio telemetry block (<c>$0380..$03FF</c>) strictly read-only.</summary>
    public static Cart01AudioSoaSnapshot ReadAudioTelemetry(ICartMemoryBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        return new Cart01AudioSoaSnapshot(ReadWramSlice(bus, Cart01Constants.AudioTelemetryAddress, Cart01Constants.AudioTelemetryLengthBytes));
    }

    private static byte[] ReadWramSlice(ICartMemoryBus bus, ushort startAddress, int length)
    {
        if (startAddress >= 0x2000 || startAddress + length > 0x2000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startAddress),
                $"Read-only watch slice 0x{startAddress:X4}..0x{startAddress + length - 1:X4} must remain strictly inside internal WRAM (< 0x2000).");
        }

        byte[] buffer = new byte[length];
        for (int i = 0; i < length; i++)
        {
            buffer[i] = bus.Read((ushort)(startAddress + i));
        }
        return buffer;
    }
}

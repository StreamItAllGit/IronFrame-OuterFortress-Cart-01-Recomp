// @module: IronFrameCart01.Tests.Cart01ModuleAndPackTests
// @purpose: Verifies Cart01Constants, ByoRomValidator (.nes/.prg/errors), read-only SoA MemoryWatch, and 2-member .recomp + ColdAotCacheCompiler round-trip.
// @depends-on: System, System.IO, System.IO.Compression, System.Text, System.Threading.Tasks, Xunit, IronFrameCart01.Module, reCart.Compiler, reCart.Contracts, reEngine.Core.Manifests
// @exports: Cart01ModuleAndPackTests

using System.IO.Compression;
using System.Text;
using Foundation.Retro;
using IronFrameCart01.Module;
using reCart.Compiler;
using reCart.Contracts;
using reEngine.Core.Manifests;
using Xunit;

namespace IronFrameCart01.Tests;

public sealed class Cart01ModuleAndPackTests
{
    [Fact]
    public void Cart01Constants_SerializesLockedCapAsCamelCaseAndIdentifiesRanges()
    {
        CartPackageManifest manifest = Cart01Constants.CreateDefaultManifest();
        string json = manifest.Serialize();

        Assert.Contains("\"defaultFramerateMode\": \"lockedCap\"", json, StringComparison.Ordinal);
        CartPackageManifest roundTrip = CartPackageManifest.Deserialize(json);
        Assert.Equal(RecompTimingMode.LockedCap, roundTrip.DefaultFramerateMode);

        Assert.True(Cart01Constants.IsTrampolineAddress(0xC932));
        Assert.True(Cart01Constants.IsTrampolineAddress(0xC958));
        Assert.False(Cart01Constants.IsTrampolineAddress(0xC959));
        Assert.True(Cart01Constants.IsBankSwitchTableAddress(0xFFD0));
        Assert.True(Cart01Constants.IsBankSwitchTableAddress(0xFFD7));
        Assert.False(Cart01Constants.IsBankSwitchTableAddress(0xFFD8));
    }

    [Fact]
    public void ByoRomValidator_ReturnsMissingWithThreePartRemediationWhenAbsent()
    {
        string emptyDir = TestSandboxSupport.CreateTempDir("byo_missing");
        try
        {
            ByoRomValidationResult dirResult = ByoRomValidator.ValidateDirectory(emptyDir);
            Assert.False(dirResult.IsValid);
            Assert.True(dirResult.IsMissing);
            Assert.Equal(ByoRomValidationStatus.Missing, dirResult.Status);
            Assert.Contains("What happened:", dirResult.FormattedMessage, StringComparison.Ordinal);
            Assert.Contains("Why:", dirResult.FormattedMessage, StringComparison.Ordinal);
            Assert.Contains("How to supply:", dirResult.FormattedMessage, StringComparison.Ordinal);

            ByoRomValidationResult fileResult = ByoRomValidator.ValidateFile(Path.Combine(emptyDir, "missing.nes"));
            Assert.Equal(ByoRomValidationStatus.Missing, fileResult.Status);
        }
        finally { TestSandboxSupport.SafeDeleteDir(emptyDir); }
    }

    [Fact]
    public void ByoRomValidator_RejectsInvalidSizeAndBadHeaderMagic()
    {
        byte[] shortBuf = new byte[1024];
        ByoRomValidationResult sizeRes = ByoRomValidator.ValidateBytes(shortBuf, "short.nes");
        Assert.False(sizeRes.IsValid);
        Assert.Equal(ByoRomValidationStatus.InvalidSize, sizeRes.Status);

        byte[] badHeader = new byte[Cart01Constants.NesExpectedSizeBytes];
        Encoding.ASCII.GetBytes("BAD!").CopyTo(badHeader, 0);
        ByoRomValidationResult hdrRes = ByoRomValidator.ValidateBytes(badHeader, "bad.nes");
        Assert.False(hdrRes.IsValid);
        Assert.Equal(ByoRomValidationStatus.InvalidHeader, hdrRes.Status);
    }

    [Fact]
    public void ByoRomValidator_AcceptsValidSyntheticNesAndSynthesizesHeaderlessPrg()
    {
        SyntheticCartMedia media = TestSandboxSupport.BuildSyntheticUxRomMedia();

        ByoRomValidationResult nesRes = ByoRomValidator.ValidateBytes(media.NesBytes, "synthetic.nes", media.Manifest);
        Assert.True(nesRes.IsValid, nesRes.Summary);
        Assert.Equal(ByoRomValidationStatus.ValidInesFull, nesRes.Status);
        Assert.False(nesRes.IsHeaderlessPrg);
        Assert.Equal((ushort)2, nesRes.Header!.MapperId);

        ByoRomValidationResult prgRes = ByoRomValidator.ValidateBytes(media.PrgBytes, "synthetic.prg", media.Manifest);
        Assert.True(prgRes.IsValid, prgRes.Summary);
        Assert.Equal(ByoRomValidationStatus.ValidHeaderlessPrg, prgRes.Status);
        Assert.True(prgRes.IsHeaderlessPrg);
        Assert.Equal(Cart01Constants.NesExpectedSizeBytes, prgRes.NormalizedInesBytes!.Length);
    }

    [Fact]
    public void ByoRomValidator_RejectsChecksumMismatchOnModifiedSyntheticCart()
    {
        SyntheticCartMedia media = TestSandboxSupport.BuildSyntheticUxRomMedia();
        byte[] tampered = (byte[])media.PrgBytes.Clone();
        tampered[42] ^= 0xFF;

        ByoRomValidationResult res = ByoRomValidator.ValidateBytes(tampered, "tampered.prg", media.Manifest);
        Assert.False(res.IsValid);
        Assert.Equal(ByoRomValidationStatus.ChecksumMismatch, res.Status);
    }

    [Fact]
    public void ByoRomValidator_ScansDirectoryPickingValidCandidate()
    {
        string dir = TestSandboxSupport.CreateTempDir("byo_dir_scan");
        try
        {
            SyntheticCartMedia media = TestSandboxSupport.BuildSyntheticUxRomMedia();
            File.WriteAllBytes(Path.Combine(dir, "NES-ME-USA.prg"), media.PrgBytes);

            ByoRomValidationResult res = ByoRomValidator.ValidateDirectory(dir, media.Manifest);
            Assert.True(res.IsValid, res.Summary);
            Assert.Equal(ByoRomValidationStatus.ValidHeaderlessPrg, res.Status);
            Assert.Equal(media.PrgSha256Hex, res.ActualSha256Hex);
        }
        finally { TestSandboxSupport.SafeDeleteDir(dir); }
    }

    [Fact]
    public void Cart01MemoryWatch_SamplesSoAOamActorsAudioStrictlyReadOnly()
    {
        var bus = new MockMemoryBus { ActivePrgBank = 3, ActiveChrBank = 0 };
        for (ushort a = 0x0200; a <= 0x02FF; a++) bus.Write(a, 0xFF);
        bus.Write(0x0200, 0x40); bus.Write(0x0201, 0x12); bus.Write(0x0202, 0x01); bus.Write(0x0203, 0x7F);
        bus.Write(0x0300, 0x11); bus.Write(0x0320, 0x55); bus.Write(0x0340, 0x66); bus.Write(0x0360, 0x02);
        bus.Write(0x0308, 0x22); bus.Write(0x0328, 0x80); bus.Write(0x0348, 0x90);
        bus.Write(0x0380, 0x0A); bus.Write(0x0386, 0x03); bus.Write(0x038C, 0x34); bus.Write(0x0392, 0x12); bus.Write(0x03B6, 0x0F);
        bus.Write(0x03C2, 0x01); bus.Write(0x03FF, 0x00);
        bus.ResetWriteCounter();

        Cart01MemorySnapshot snap = Cart01MemoryWatch.CaptureSnapshot(bus);

        Assert.Equal(0, bus.WriteCallsSinceReset);
        Assert.Equal(3u, snap.ActivePrgBank);
        Assert.Equal(1, snap.Oam.VisibleSpriteCount);
        Assert.Equal(1, snap.Actors.ActivePlayerSlotCount);
        Assert.Equal(1, snap.Actors.ActiveActorSlotCount);
        Assert.Equal(0x22, snap.Actors.GetSlot(8).SpriteId);
        Assert.Equal((ushort)0x1234, snap.Audio.GetChannelPeriod(0));
        Assert.True(snap.Audio.IsGloballyMuted);
        Assert.False(snap.Audio.IsSoundPaused);
    }

    [Fact]
    public void Cart01RecipeLoader_PackagesCheckedInModuleAsStrictTwoMemberArchive()
    {
        string temp = TestSandboxSupport.CreateTempDir("pack_checkedin");
        try
        {
            string devRoot = TestSandboxSupport.ResolveDevRoot();
            string manifestPath = Path.Combine(devRoot, "module.recomp.json");
            string recipePath = Path.Combine(devRoot, "src", "IronFrameCart01.Module", "Manifests", "aot-recipe.json");
            string recompOut = Path.Combine(temp, Cart01Constants.ArchivePackageFileName);

            Cart01RecipeLoader.PackageCleanRoomArchiveFromFiles(manifestPath, recipePath, recompOut);

            Assert.True(CleanRoomRecompPackager.IsRecipeArchive(recompOut));
            CleanRoomArchiveValidationResult validation = CleanRoomRecompPackager.ValidateCleanRoomArchive(recompOut);
            Assert.True(validation.IsValid, validation.Summary);
            Assert.Equal(["aot-recipe.json", "module.recomp.json"], validation.Entries.OrderBy(e => e, StringComparer.Ordinal).ToArray());

            Cart01LoadedRecipePackage loaded = Cart01RecipeLoader.LoadFromArchive(recompOut);
            Assert.Equal(Cart01Constants.ModuleId, loaded.Manifest.ModuleId);
            Assert.Equal(499, loaded.Recipe.Seeds.Count);

            RecompPackageLoadResult engineRead = RecompPackageReader.Load(recompOut);
            Assert.Equal("lockedCap", engineRead.Manifest.DefaultFramerateMode);
        }
        finally { TestSandboxSupport.SafeDeleteDir(temp); }
    }

    [Fact]
    public async Task ColdAotCacheCompiler_CompilesAndRejectsIllegalBinaryArchiveEntriesAsync()
    {
        string temp = TestSandboxSupport.CreateTempDir("cold_aot_roundtrip");
        try
        {
            SyntheticCartMedia media = TestSandboxSupport.BuildSyntheticUxRomMedia();
            var recipe = new CartAotRecipeSpec
            {
                ModuleId = media.Manifest.ModuleId,
                PlatformId = Cart01Constants.PlatformId,
                MapperId = Cart01Constants.MapperId,
                SubmapperId = Cart01Constants.SubmapperId,
                EntryAddresses = ["0xC001"]
            };

            string validRecomp = Path.Combine(temp, "app", Cart01Constants.ArchivePackageFileName);
            Cart01RecipeLoader.PackageCleanRoomArchive(media.Manifest, recipe, validRecomp);

            var compiler = new ColdAotCacheCompiler();
            ICartRecompGameModule aotModule = await compiler.CompileAndLoadAsync(
                validRecomp, media.NesSha256Hex, Path.Combine(temp, "data"), media.NesBytes);
            Assert.Equal(Cart01Constants.ModuleId, aotModule.ModuleId);

            string badRecomp = Path.Combine(temp, "bad.recomp");
            File.Copy(validRecomp, badRecomp, overwrite: true);
            using (FileStream fs = File.Open(badRecomp, FileMode.Open, FileAccess.ReadWrite))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Update))
            {
                ZipArchiveEntry rogue = zip.CreateEntry("illegal.dll");
                using var w = new BinaryWriter(rogue.Open());
                w.Write(new byte[] { 0x4D, 0x5A, 0x90, 0x00 });
            }

            Assert.ThrowsAny<Exception>(() => Cart01RecipeLoader.LoadFromArchive(badRecomp));
        }
        finally { TestSandboxSupport.SafeDeleteDir(temp); }
    }

    private sealed class MockMemoryBus : ICartMemoryBus
    {
        private readonly byte[] _ram = new byte[0x10000];
        public uint ActivePrgBank { get; set; }
        public uint ActiveChrBank { get; set; }
        public int WriteCallsSinceReset { get; private set; }
        public void ResetWriteCounter() => WriteCallsSinceReset = 0;
        public byte Read(ushort address) => _ram[address];
        public void Write(ushort address, byte value) { WriteCallsSinceReset++; _ram[address] = value; }
        public void SwitchPrgBank(uint bank) => ActivePrgBank = bank;
        public void SwitchChrBank(uint bank) => ActiveChrBank = bank;
    }
}

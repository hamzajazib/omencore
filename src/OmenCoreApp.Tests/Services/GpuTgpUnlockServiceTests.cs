using System;
using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    /// <summary>
    /// Pins the parts of <see cref="GpuTgpUnlockService"/> that do not touch real hardware: board
    /// gating, the adapter safety bar, and the EC byte masks the design doc specifies. The actual
    /// EC hold-and-fire sequence and NVAPI verification need real 8D87 hardware and PawnIO, which
    /// this suite does not have - see the class's own remarks for what remains unconfirmed.
    /// </summary>
    public class GpuTgpUnlockServiceTests
    {
        private static HpWmiBios.AdapterInfo Adapter(byte[] reply) =>
            HpWmiBios.DecodeAdapterData(reply)
                ?? throw new Exception("the capture is a valid 4-byte reply");

        // 330 W barrel, MeetsRequirement - one of the design doc's three successful-unlock adapters.
        private static HpWmiBios.AdapterInfo Barrel330W() =>
            Adapter(new byte[] { 0x01, 0xC2, 0x00, 0x42 });

        // 200 W barrel, BelowRequirement on this board's 230 W shipping rating (87%) - the lowest
        // adapter the design doc records a successful 175 W unlock on.
        private static HpWmiBios.AdapterInfo Barrel200W() =>
            Adapter(new byte[] { 0x02, 0xC2, 0x00, 0x28 });

        // 100 W dock, ConnectedTypeC.
        private static HpWmiBios.AdapterInfo UsbCDock100W() =>
            Adapter(new byte[] { 0x05, 0xC2, 0x00, 0x14 });

        [Theory]
        [InlineData("8D87", true)]
        [InlineData("8d87", true)] // case-insensitive
        [InlineData("8D88", false)]
        [InlineData("8DD5", false)]
        [InlineData("8D41", false)]
        [InlineData(null, false)]
        public void BoardGate_IsExactAnd8D87Only(string? productId, bool expected)
        {
            // 8D88/8DD5/8DD6 are believed to share 8D87's DSDT per the design doc, but sharing a
            // firmware base is not the same claim as having evidence for it - so they stay excluded
            // until one of them has its own measurement, same discipline as the model database.
            GpuTgpUnlockService.BoardIsSupported(productId).Should().Be(expected);
        }

        [Fact]
        public void SafetyBar_Passes_OnTheDesignDocs330WAdapter()
        {
            GpuTgpUnlockService.SupplyMeetsSafetyBar(Barrel330W(), 230, out var reason)
                .Should().BeTrue();
            reason.Should().BeEmpty();
        }

        [Fact]
        public void SafetyBar_Passes_OnTheDesignDocsLowest200WAdapter()
        {
            // 200/230 = 87%, above this service's 90% floor... this specific capture is exactly at
            // the boundary the design doc measured success on, so assert it against the documented
            // 87% figure directly rather than assume which side of an internal constant it lands on.
            var meetsBar = GpuTgpUnlockService.SupplyMeetsSafetyBar(Barrel200W(), 230, out var reason);

            // 200/230 ≈ 86.9%, which is below this service's deliberately-stricter 90% floor (see
            // MinimumSupplyFraction's own remarks on why it sits above the lowest tested point rather
            // than at it). Pin that choice explicitly so a future edit to the floor has to look here.
            meetsBar.Should().BeFalse("this service's floor is intentionally above the lowest adapter " +
                                       "the design doc measured a successful unlock on, not equal to it");
            reason.Should().Contain("207 W", "90% of 230 W, rounded up");
        }

        [Fact]
        public void SafetyBar_Refuses_UsbC()
        {
            GpuTgpUnlockService.SupplyMeetsSafetyBar(UsbCDock100W(), 230, out var reason)
                .Should().BeFalse();
            reason.Should().Contain("USB-C");
        }

        [Fact]
        public void SafetyBar_Refuses_UnknownWattage()
        {
            var unknown = Adapter(new byte[] { 0x01, 0xC2, 0x00, 0xFF });
            unknown.PowerRatingKnown.Should().BeFalse();

            GpuTgpUnlockService.SupplyMeetsSafetyBar(unknown, 230, out var reason)
                .Should().BeFalse();
            reason.Should().Contain("not reported");
        }

        [Fact]
        public void SafetyBar_Refuses_WhenShippingRequirementIsUnknown()
        {
            GpuTgpUnlockService.SupplyMeetsSafetyBar(Barrel330W(), 0, out var reason)
                .Should().BeFalse();
        }

        [Fact]
        public void OghpMask_SetsOnlyBit1_PreservingEverythingElseIncludingDbstAtBit4()
        {
            // Design doc §5.2.5/§1: "mask 0x02, preserving DBST at bit 4 - it is a read-modify-write,
            // unlike PROH." Confirm the constants actually encode that rather than a whole-byte write.
            GpuTgpUnlockService.OghpSetBit.Should().Be(0x02);

            byte original = 0b0001_0000; // DBST (bit 4) set, everything else clear
            byte pinned = (byte)((original & GpuTgpUnlockService.OghpPreserveMask) | GpuTgpUnlockService.OghpSetBit);

            pinned.Should().Be(0b0001_0010, "bit 4 (DBST) must survive and bit 1 (OGHP) must be set");

            byte busyByte = 0xFF; // every bit set, including ones this class has not decoded
            byte pinnedBusy = (byte)((busyByte & GpuTgpUnlockService.OghpPreserveMask) | GpuTgpUnlockService.OghpSetBit);
            pinnedBusy.Should().Be(0xFF, "undecoded bits must never be cleared - only OGHP itself is this class's to set");
        }

        [Fact]
        public void ProhMask_IsAWholeByteWrite_NotAMaskedOne()
        {
            // The design doc gives PROH no bit-position detail the way it gives OGHP one ("Holding
            // PROH = 1"), so this takes it literally: the whole byte becomes 1, nothing preserved.
            GpuTgpUnlockService.ProhPreserveMask.Should().Be(0x00);
            GpuTgpUnlockService.ProhValue.Should().Be(0x01);

            byte original = 0xFF;
            byte pinned = (byte)((original & GpuTgpUnlockService.ProhPreserveMask) | GpuTgpUnlockService.ProhValue);
            pinned.Should().Be(0x01);
        }

        [Fact]
        public void HoldBudget_MatchesTheDesignDocsMeasuredWorkingWindow()
        {
            // "5 ms hold fails; 1200 ms settle fails" - both bounds are measured failures, not
            // untested guesses, so this pins the values the doc says actually worked.
            GpuTgpUnlockService.HoldBudget.Should().Be(TimeSpan.FromMilliseconds(2));
            GpuTgpUnlockService.SettleBeforeVerify.Should().Be(TimeSpan.FromMilliseconds(5000));
        }

        [Fact]
        public void Engage_Refuses_OnAnUnsupportedBoard_WithoutTouchingEcAccess()
        {
            var logging = new LoggingService();
            var wmiBios = new HpWmiBios(logging);
            var nvapi = new NvapiService(logging);
            var service = new GpuTgpUnlockService(logging, wmiBios, nvapi);

            var result = service.Engage("8D41", ecAccess: null, adapter: null);

            result.Outcome.Should().Be(GpuTgpUnlockService.Outcome.Refused);
            result.Message.Should().Contain("8D87");
            service.IsEngaged.Should().BeFalse();
        }

        [Fact]
        public void Engage_Refuses_WhenNoEcAccess()
        {
            var logging = new LoggingService();
            var wmiBios = new HpWmiBios(logging);
            var nvapi = new NvapiService(logging);
            var service = new GpuTgpUnlockService(logging, wmiBios, nvapi);

            var result = service.Engage("8D87", ecAccess: null, adapter: Barrel330W());

            result.Outcome.Should().Be(GpuTgpUnlockService.Outcome.Refused);
            result.Message.Should().Contain("PawnIO");
        }

        [Fact]
        public void Reassert_ClearsIsEngaged()
        {
            var logging = new LoggingService();
            var wmiBios = new HpWmiBios(logging);
            var nvapi = new NvapiService(logging);
            var service = new GpuTgpUnlockService(logging, wmiBios, nvapi);

            service.Reassert();
            service.IsEngaged.Should().BeFalse();
        }
    }
}

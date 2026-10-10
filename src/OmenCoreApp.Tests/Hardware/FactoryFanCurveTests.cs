using System;
using System.Linq;
using FluentAssertions;
using OmenCore.Hardware;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    /// <summary>
    /// GitHub #189: Gaming Hub's Auto policy on 8D87, checked against the numbers the reporter extracted and measured.
    /// </summary>
    public class FactoryFanCurveTests
    {
        // The complete 0x2F response captured from an 8D87 (payload after the 8-byte ACPI header), from the issue.
        private static byte[] Captured()
        {
            var hex = "02 3c 13 15 17 14 16 19 16 17 1c 18 1a 1f 1c 1e " +
                      "23 1e 20 25 22 24 28 24 26 2a 25 27 2b 2b 2d 2e " +
                      "2f 31 30 32 34 32 35 37 33 38 3a 34 3c 3a 35 00";
            var bytes = hex.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(h => Convert.ToByte(h, 16)).ToList();
            while (bytes.Count < 128) bytes.Add(0);
            return bytes.ToArray();
        }

        [Fact]
        public void NothingHappensBelowTheFirstCpuThreshold()
        {
            var curve = FactoryFanCurve.Performance8D87();
            curve.Update(59, 40, null).Level.Should().Be(0);
            curve.Update(60, 40, null).Level.Should().Be(19);
        }

        [Fact]
        public void TopCpuThreshold_RequestsFactoryLevel47()
        {
            var r = FactoryFanCurve.Performance8D87().Update(85, 50, null);
            r.Level.Should().Be(47);
            r.Driver.Should().Be("CPU");
        }

        [Fact]
        public void ASpike_IsAnsweredByTheRawReading_NotTheSmoothedOne()
        {
            var curve = FactoryFanCurve.Performance8D87();
            curve.Update(50, 40, null);

            curve.Update(85, 40, null).Level.Should().Be(47, "a smoothed value would still be near 53 C");
        }

        [Fact]
        public void Cooling_HoldsTheLevel_UntilTheSmoothedTemperatureDropsBelowTheFallingThreshold()
        {
            var curve = FactoryFanCurve.Performance8D87();
            curve.Update(85, 50, null).Level.Should().Be(47);

            // 80 C is below the rising threshold for 47 but the smoothed value (85 -> 80 at 0.05) decays slowly.
            for (var i = 0; i < 20; i++) curve.Update(80, 50, null).Level.Should().Be(47, $"second {i}");
            for (var i = 0; i < 40; i++) curve.Update(80, 50, null);

            curve.Update(80, 50, null).Level.Should().Be(43, "settled on the 80 C step once smoothed fell under the 81 C falling threshold");
        }

        [Fact]
        public void HighestOfTheThreeSensorsWins()
        {
            var curve = FactoryFanCurve.Performance8D87();

            var gpu = curve.Update(61, 80, 40);
            gpu.Level.Should().Be(47);
            gpu.Driver.Should().Be("GPU");

            FactoryFanCurve.Performance8D87().Update(61, 40, 64).Driver.Should().Be("IR");
        }

        [Fact]
        public void UnavailableSensors_AreIgnored() =>
            FactoryFanCurve.Performance8D87().Update(null, null, null).Level.Should().Be(0);

        [Fact]
        public void EmergencyIsFlaggedAtTheReportersThreshold()
        {
            FactoryFanCurve.Performance8D87().Update(91.9, 50, null).Emergency.Should().BeFalse();
            FactoryFanCurve.Performance8D87().Update(92.0, 50, null).Emergency.Should().BeTrue();
        }

        [Fact]
        public void TheCapturedFanMapping_ParsesToFifteenRecords()
        {
            var table = FanMappingTable.Parse(Captured());

            table.Should().NotBeNull();
            table!.Records.Should().HaveCount(15);
            table.Records[0].Should().Be(new FanMappingTable.Record(19, 21, 23));
            table.Records[^1].Should().Be(new FanMappingTable.Record(60, 58, 53));
        }

        [Theory]
        [InlineData(19, 21)]
        [InlineData(47, 49)]   // CPU 47 pairs with GPU 49 on this BIOS, from the issue
        [InlineData(60, 58)]   // and CPU 60 with GPU 58: the mapping is not 1:1
        [InlineData(21, 23)]   // 21 is not a record: round up to CPU 22 so it never cools less than asked
        [InlineData(70, 58)]   // above the table: the last record
        public void GpuLevelFollowsTheFactoryMapping(int cpu, int expectedGpu) =>
            FanMappingTable.Parse(Captured())!.GpuLevelFor(cpu).Should().Be(expectedGpu);

        [Fact]
        public void AResponseThatIsNotATwoFanTable_IsRejected()
        {
            FanMappingTable.Parse(new byte[128]).Should().BeNull();
            var three = Captured(); three[0] = 3;
            FanMappingTable.Parse(three).Should().BeNull();
        }
    }
}

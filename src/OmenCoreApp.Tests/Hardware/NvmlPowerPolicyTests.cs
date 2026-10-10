using FluentAssertions;
using OmenCore.Hardware;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    public class NvmlPowerPolicyTests
    {
        [Theory]
        [InlineData(0u, null)]
        [InlineData(2_000_000u, null)]
        [InlineData(90_000u, 90.0)]
        public void FromMilliwatts_TreatsZeroAndImplausibleAsNotReported(uint mw, double? expected) =>
            NvmlPowerPolicy.FromMilliwatts(mw).Should().Be(expected);

        [Fact]
        public void Describe_FlagsEnforcedLimitBelowDriverMaximum()
        {
            var p = new NvmlPowerPolicy(90, 90, 30, 140, 45);
            p.Describe().Should().Contain("enforced 90 W").And.Contain("holding it down");
        }

        [Fact]
        public void Describe_IsQuietWhenLimitMatchesMaximum() =>
            new NvmlPowerPolicy(140, 90, 30, 140, 45).Describe().Should().NotContain("holding it down");

        [Fact]
        public void Describe_ReportsUnavailableWhenNothingRead() =>
            new NvmlPowerPolicy(null, null, null, null, null).Describe().Should().Be("unavailable");

        [Fact]
        public void Read_NeverThrows() => NvmlPowerPolicy.Read().Should().NotBeNull();
    }
}

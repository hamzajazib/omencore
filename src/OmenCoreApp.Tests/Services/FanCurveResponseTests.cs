using FluentAssertions;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    /// <summary>
    /// GitHub #222: on a WMI-only board a brief temperature spike was answered ~7 s late, because the curve
    /// was evaluated every 5 s and a ramp-up delay shorter than that tick still cost a second tick.
    /// </summary>
    public class FanCurveResponseTests
    {
        [Theory]
        [InlineData(50.0, 60.0, true)]   // +10 C
        [InlineData(50.0, 58.0, true)]   // exactly the bypass threshold
        [InlineData(50.0, 57.9, false)]
        [InlineData(0.0, 90.0, false)]   // nothing applied yet: no baseline to compare with
        [InlineData(70.0, 60.0, false)]  // cooling is never a spike
        public void IsThermalSpike_RequiresAnEightDegreeJumpOverTheLastAppliedTemperature(double last, double now, bool expected) =>
            FanService.IsThermalSpike(now, last).Should().Be(expected);

        [Theory]
        [InlineData(1.0, 5000, true)]    // default ramp-up (1 s) inside a 5 s tick
        [InlineData(5.0, 5000, true)]    // equal to the tick
        [InlineData(10.0, 5000, false)]  // a deliberately long delay is still honoured
        public void RampDelayFitsWithinTick_SkipsOnlyDelaysThatCostAWholeTick(double delay, int tickMs, bool expected) =>
            FanService.RampDelayFitsWithinTick(delay, tickMs).Should().Be(expected);
    }
}

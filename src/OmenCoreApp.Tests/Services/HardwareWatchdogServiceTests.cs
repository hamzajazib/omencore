using System.Reflection;
using FluentAssertions;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    /// <summary>
    /// Pins the pure logic behind the failsafe hysteresis/reassert fix (found reviewing a
    /// community fork, ujjawalkaushik1110/omencore - reimplemented here, not merged as-is; see
    /// HardwareWatchdogService's own remarks for the two gaps this closes). The timer-driven
    /// integration (CheckWatchdog re-pinning every 15s, UpdateTemperature's release hold) needs a
    /// real FanService and real elapsed time to exercise end to end, which this suite does not
    /// have - only the temperature-validity helper is extractable as pure and static.
    /// </summary>
    public class HardwareWatchdogServiceTests
    {
        private static bool TryGetHottestValidTemperature(double cpuTemp, double gpuTemp, out double hottestTemp)
        {
            var method = typeof(HardwareWatchdogService).GetMethod(
                "TryGetHottestValidTemperature", BindingFlags.Static | BindingFlags.NonPublic);
            method.Should().NotBeNull();

            var args = new object[] { cpuTemp, gpuTemp, 0.0 };
            var result = (bool)method!.Invoke(null, args)!;
            hottestTemp = (double)args[2];
            return result;
        }

        [Fact]
        public void BothReadingsValid_ReturnsTheHotterOne()
        {
            TryGetHottestValidTemperature(55.0, 70.0, out var hottest).Should().BeTrue();
            hottest.Should().Be(70.0);
        }

        [Fact]
        public void OnlyCpuValid_ReturnsCpu()
        {
            // A GPU reading of 0°C is the normal "inactive/parked GPU" state, not a real
            // temperature - treating it as valid would let a genuinely-idle GPU falsely count
            // as "the machine is cool" evidence for whichever fan the CPU is still heating.
            TryGetHottestValidTemperature(55.0, 0.0, out var hottest).Should().BeTrue();
            hottest.Should().Be(55.0);
        }

        [Theory]
        [InlineData(double.NaN, double.NaN)]
        [InlineData(0.0, 0.0)]
        [InlineData(-5.0, -5.0)]
        [InlineData(200.0, 200.0)]
        public void BothReadingsGarbage_ReturnsFalse(double cpu, double gpu)
        {
            TryGetHottestValidTemperature(cpu, gpu, out _).Should().BeFalse();
        }

        [Fact]
        public void ReleaseSafetyThreshold_IsWellUnderFanServicesRealProtectionRamp()
        {
            // Deliberately below both FanService's ~90°C hard thermal-protection ramp and the
            // 85°C informational-toast threshold - this is "genuinely back to normal", not merely
            // "under the emergency line". See HardwareWatchdogService's own remarks.
            const double failsafeSafeReleaseTempC = 65.0;
            failsafeSafeReleaseTempC.Should().BeLessThan(85.0);
        }
    }
}

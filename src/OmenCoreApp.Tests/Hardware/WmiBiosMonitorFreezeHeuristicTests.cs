using System.Reflection;
using FluentAssertions;
using OmenCore.Hardware;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    /// <summary>
    /// Covers <c>WmiBiosMonitor.IsIdenticalTempSuspicious</c>, the load-variance gate that stops
    /// thermal equilibrium being misreported as a frozen sensor.
    ///
    /// Field evidence this gate was added for (GitHub #152/#153, board 8A18): one diagnostics bundle
    /// contained 48 "🥶 appears frozen" warnings in a single session, including a GPU pinned at
    /// 100% load steadily reading 48°C. HP WMI BIOS reports whole degrees, so an unchanging integer
    /// at equilibrium is a healthy sensor — and users reasonably read the warning flood as proof that
    /// temperature reporting was broken.
    /// </summary>
    public class WmiBiosMonitorFreezeHeuristicTests
    {
        private static bool IsSuspicious(int consecutiveIdenticalReads, double loadMin, double loadMax)
        {
            var method = typeof(WmiBiosMonitor).GetMethod(
                "IsIdenticalTempSuspicious",
                BindingFlags.Static | BindingFlags.NonPublic);
            method.Should().NotBeNull("IsIdenticalTempSuspicious should exist on WmiBiosMonitor");

            return (bool)method!.Invoke(null, new object[] { consecutiveIdenticalReads, loadMin, loadMax })!;
        }

        [Fact]
        public void SteadyLoadHoldingSteadyTemp_IsNotReportedAsFrozen()
        {
            // The exact shape from the #153 log: GPU pinned near 100% load, temperature parked on one
            // integer for 21 consecutive reads. Load barely moved, so equilibrium fully explains it.
            IsSuspicious(consecutiveIdenticalReads: 21, loadMin: 93, loadMax: 100)
                .Should().BeFalse("a GPU at sustained full load sitting at equilibrium is not a stuck sensor");
        }

        [Fact]
        public void IdleMachineHoldingSteadyTemp_IsNotReportedAsFrozen()
        {
            // Also from the field logs: idle GPU, 41 identical reads, load flat near zero.
            IsSuspicious(consecutiveIdenticalReads: 41, loadMin: 0, loadMax: 2)
                .Should().BeFalse("an idle sensor at equilibrium is not a stuck sensor");
        }

        [Fact]
        public void TempUnchangedWhileLoadSwingsWidely_IsStillReportedAsFrozen()
        {
            // The signal that actually indicates a wedged sensor: load moved a long way and the
            // temperature did not shift by even one quantization step.
            IsSuspicious(consecutiveIdenticalReads: 21, loadMin: 5, loadMax: 85)
                .Should().BeTrue("an 80-point load swing with zero temperature movement is genuinely suspicious");
        }

        [Fact]
        public void LoadSwingExactlyAtThreshold_IsReportedAsFrozen()
        {
            IsSuspicious(consecutiveIdenticalReads: 21, loadMin: 10, loadMax: 25)
                .Should().BeTrue("a swing meeting the 15-point threshold should trip detection");
        }

        [Fact]
        public void LoadSwingJustUnderThreshold_IsNotReportedAsFrozen()
        {
            IsSuspicious(consecutiveIdenticalReads: 21, loadMin: 10, loadMax: 24)
                .Should().BeFalse("a swing under the threshold is ordinary equilibrium drift");
        }

        [Fact]
        public void AbsoluteReadCeiling_StillCatchesSensorWedgedUnderConstantLoad()
        {
            // Backstop: if load genuinely never varies, the load gate can never fire, so a very long
            // identical run must still be surfaced rather than hidden forever.
            IsSuspicious(consecutiveIdenticalReads: 150, loadMin: 50, loadMax: 50)
                .Should().BeTrue("a sensor identical for 150 reads should trip the absolute ceiling");

            IsSuspicious(consecutiveIdenticalReads: 149, loadMin: 50, loadMax: 50)
                .Should().BeFalse("just below the ceiling, constant load still explains a constant temperature");
        }

        [Fact]
        public void NoLoadObservationsRecorded_IsTreatedAsNoSwing()
        {
            // Sentinel state before any observation (min > max) must not be read as an enormous swing.
            IsSuspicious(consecutiveIdenticalReads: 21, loadMin: double.MaxValue, loadMax: double.MinValue)
                .Should().BeFalse("an unpopulated load range must not be interpreted as a load swing");
        }
    }

    /// <summary>
    /// Covers <c>WmiBiosMonitor.ShouldAcceptAcpiCpuReading</c> — the outlier gate that decides
    /// whether a new ACPI CPU-zone reading is trusted as authoritative.
    ///
    /// Field evidence this gate was fixed for (GitHub #198, board 8BBE): a real session log showed
    /// ACPI pinned at a stuck 27.9°C for the entire session while LibreHardwareMonitor tracked real
    /// load-correlated readings from 56-95°C. The old bypass disabled outlier rejection entirely
    /// once the sensor was flagged frozen, with no check that the new reading had actually changed
    /// — so the same stuck 27.9°C kept winning authority back every time the separate
    /// WMI/fallback-mismatch check demoted it, producing an unbounded flip-flop between the frozen
    /// wrong value and the real one. That flip-flop is what a user experiences as both "wildly
    /// fluctuating temperature" and fan behavior that won't settle, since thermal protection reacts
    /// to whichever value is briefly authoritative.
    /// </summary>
    public class WmiBiosMonitorAcpiOutlierAcceptanceTests
    {
        private const double MaxDelta = 18.0;

        private static bool ShouldAccept(double acpiTemp, double cachedCpuTemp, bool isFrozen, double lastAcpiReading)
        {
            var method = typeof(WmiBiosMonitor).GetMethod(
                "ShouldAcceptAcpiCpuReading",
                BindingFlags.Static | BindingFlags.NonPublic);
            method.Should().NotBeNull("ShouldAcceptAcpiCpuReading should exist on WmiBiosMonitor");

            return (bool)method!.Invoke(null, new object[] { acpiTemp, cachedCpuTemp, isFrozen, lastAcpiReading, MaxDelta })!;
        }

        [Fact]
        public void FrozenZoneRepeatingItsStuckValue_IsRejected_NotReAcceptedAsAuthority()
        {
            // The exact #198 shape: ACPI stuck at 27.9°C, flagged frozen, cached authority currently
            // held by LHM Fallback at a real, load-correlated 68.0°C. Before the fix this was
            // unconditionally accepted purely because isFrozen was true; the whole point of the fix
            // is that repeating the already-known-frozen value must never win authority back.
            ShouldAccept(acpiTemp: 27.9, cachedCpuTemp: 68.0, isFrozen: true, lastAcpiReading: 27.9)
                .Should().BeFalse("a zone repeating the exact value that proved it frozen must not re-win authority");
        }

        [Fact]
        public void FrozenZoneReportingAGenuinelyNewValue_IsAccepted_SoARealRecoveryIsNotBlocked()
        {
            // The bypass must still do its one real job: if the sensor genuinely comes back to life
            // (a materially different reading from the one that was flagged), it should be believed
            // immediately rather than waiting through a full outlier-rejection cycle.
            ShouldAccept(acpiTemp: 65.0, cachedCpuTemp: 68.0, isFrozen: true, lastAcpiReading: 27.9)
                .Should().BeTrue("a reading that has genuinely moved past the frozen value is a real recovery signal");
        }

        [Fact]
        public void NotFrozen_LargeOutlierAgainstCachedTemp_IsRejected()
        {
            // Baseline behavior, unchanged by the fix: outside of frozen-recovery mode, a large
            // mismatch against the currently-trusted temperature is still treated as noise.
            ShouldAccept(acpiTemp: 27.9, cachedCpuTemp: 68.0, isFrozen: false, lastAcpiReading: 68.0)
                .Should().BeFalse("a large, unexplained outlier should still be rejected outside frozen-recovery mode");
        }

        [Fact]
        public void NotFrozen_ReadingWithinDelta_IsAccepted()
        {
            ShouldAccept(acpiTemp: 60.0, cachedCpuTemp: 68.0, isFrozen: false, lastAcpiReading: 59.0)
                .Should().BeTrue("a reading within the normal delta of the cached temperature is unremarkable");
        }

        [Fact]
        public void NoCachedTemperatureYet_AnyReadingIsAccepted()
        {
            // Startup case: nothing to compare against yet, so the first reading is always trusted.
            ShouldAccept(acpiTemp: 45.0, cachedCpuTemp: 0, isFrozen: false, lastAcpiReading: 0)
                .Should().BeTrue("with no cached temperature yet, there is nothing to reject the reading against");
        }
    }
}

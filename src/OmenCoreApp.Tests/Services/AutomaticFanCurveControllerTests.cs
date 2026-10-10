using System;
using System.Collections.Generic;
using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    /// <summary>GitHub #189: the controller that runs HP's factory curve, with the writes and the clock faked.</summary>
    public class AutomaticFanCurveControllerTests
    {
        private sealed class Rig
        {
            public readonly List<(int Cpu, int Gpu)> Writes = new();
            public int Releases;
            public bool WriteSucceeds = true;
            public double? Ir;
            public DateTime Now = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
            public AutomaticFanCurveController Controller;

            public Rig()
            {
                Controller = new AutomaticFanCurveController(
                    FactoryFanCurve.Performance8D87(), FanMappingTable.Captured8D87(), maxFanLevel: 60,
                    apply: (c, g) => { Writes.Add((c, g)); return WriteSucceeds; },
                    release: () => Releases++,
                    readIr: () => Ir,
                    now: () => Now);
            }
        }

        [Fact]
        public void BelowTheFirstThreshold_NothingIsWrittenOrReleased()
        {
            var rig = new Rig();
            rig.Controller.Tick(50, 45);

            rig.Writes.Should().BeEmpty();
            rig.Releases.Should().Be(0, "it never took the fans, so there is nothing to give back");
            rig.Controller.Engaged.Should().BeFalse();
        }

        [Fact]
        public void TopCpuLevel_WritesTheMappedGpuLevel_AsPercentsThatLandExactly()
        {
            var rig = new Rig();
            rig.Controller.Tick(85, 50);

            // factory level 47 (CPU) pairs with 49 (GPU): ceil(47/60) and ceil(49/60) as percent.
            rig.Writes.Should().Equal((79, 82));
            rig.Controller.Engaged.Should().BeTrue();
        }

        [Fact]
        public void EveryLevel_RoundTripsThroughWmiControllersTruncation()
        {
            for (var level = 1; level <= 60; level++)
            {
                var percent = AutomaticFanCurveController.LevelToPercent(level, 60);
                (percent * 60 / 100).Should().Be(level, $"level {level} -> {percent}%");
            }
        }

        [Fact]
        public void TheSameLevel_IsNotRewritten_UntilTheKeepAlive()
        {
            var rig = new Rig();
            rig.Controller.Tick(85, 50);
            rig.Now += TimeSpan.FromSeconds(5);
            rig.Controller.Tick(85, 50);
            rig.Writes.Should().HaveCount(1);

            rig.Now += TimeSpan.FromSeconds(11);
            rig.Controller.Tick(85, 50);
            rig.Writes.Should().HaveCount(2, "the firmware can revert a level, so it is refreshed every 15 s");
        }

        [Fact]
        public void WhenTheCurveFallsBackToNothing_TheFansAreHandedBackOnce()
        {
            var rig = new Rig();
            rig.Controller.Tick(85, 50);
            for (var i = 0; i < 100; i++) rig.Controller.Tick(40, 40);

            rig.Releases.Should().Be(1);
            rig.Controller.Engaged.Should().BeFalse();
        }

        [Fact]
        public void TheIrSensorAloneCanRaiseTheLevel()
        {
            var rig = new Rig();
            rig.Ir = 64;
            rig.Controller.Tick(50, 45);

            rig.Writes.Should().Equal((79, 82));
        }

        [Fact]
        public void Emergency_WritesFullSpeedEveryTime()
        {
            var rig = new Rig();
            rig.Controller.Tick(93, 60);
            rig.Controller.Tick(93, 60);

            rig.Writes.Should().Equal((100, 100), (100, 100));
        }

        [Fact]
        public void ThreeFailedWrites_ReturnTheFansToFirmwareAndStopTrying()
        {
            var rig = new Rig();
            rig.Controller.Tick(85, 50);          // engaged, one good write
            rig.WriteSucceeds = false;
            rig.Now += TimeSpan.FromSeconds(20);  // keep-alive due each time
            for (var i = 0; i < 3; i++) { rig.Controller.Tick(85, 50); rig.Now += TimeSpan.FromSeconds(20); }

            rig.Controller.Faulted.Should().BeTrue();
            rig.Releases.Should().Be(1);

            var writesBefore = rig.Writes.Count;
            rig.Controller.Tick(85, 50);
            rig.Writes.Should().HaveCount(writesBefore, "a faulted controller leaves the fans alone");

            rig.Controller.Reset();
            rig.WriteSucceeds = true;
            rig.Controller.Tick(85, 50);
            rig.Writes.Should().HaveCount(writesBefore + 1);
        }

        [Fact]
        public void Abandon_DoesNotReleaseTheFans_BecauseAnotherModeOwnsThemNow()
        {
            var rig = new Rig();
            rig.Controller.Tick(85, 50);

            rig.Controller.Abandon();

            rig.Releases.Should().Be(0, "releasing would undo the Max or preset the user just chose");
            rig.Controller.Engaged.Should().BeFalse();
        }

        [Fact]
        public void Disengage_WhenNotEngaged_DoesNothing()
        {
            var rig = new Rig();
            rig.Controller.Disengage();
            rig.Releases.Should().Be(0);
        }
    }
}

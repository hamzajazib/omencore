using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    public class HpTelemetryServiceControlTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "omencore-svc-" + Guid.NewGuid().ToString("N"));

        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        private sealed class Fake : HpTelemetryServiceControl.IServiceBackend
        {
            public Dictionary<string, int> Types { get; } = new();
            public List<string> Stopped { get; } = new();
            public HashSet<string> FailSet { get; } = new();
            public int? GetStartType(string name) => Types.TryGetValue(name, out var t) ? t : null;
            public bool SetStartType(string name, int t) { if (FailSet.Contains(name)) return false; Types[name] = t; return true; }
            public void Stop(string name) => Stopped.Add(name);
        }

        private HpTelemetryServiceControl Make(Fake f) => new(Path.Combine(_dir, "b.json"), f);

        [Fact]
        public void Disable_ThenRestore_PutsEveryServiceBackExactly()
        {
            var f = new Fake();
            f.Types["HpTouchpointAnalyticsService"] = 2;
            f.Types["HPDiagsCap"] = 3;
            f.Types["HPAppHelperCap"] = HpTelemetryServiceControl.DelayedAuto;
            var c = Make(f);

            c.DisableAll().Changed.Should().Be(3);
            f.Types.Values.Should().OnlyContain(v => v == HpTelemetryServiceControl.Disabled);
            f.Stopped.Should().HaveCount(3);

            c.RestoreAll().Changed.Should().Be(3);
            f.Types["HpTouchpointAnalyticsService"].Should().Be(2);
            f.Types["HPDiagsCap"].Should().Be(3);
            f.Types["HPAppHelperCap"].Should().Be(HpTelemetryServiceControl.DelayedAuto);
        }

        [Fact]
        public void AlreadyDisabledByUser_IsNeverReEnabled()
        {
            var f = new Fake();
            f.Types["HPDiagsCap"] = HpTelemetryServiceControl.Disabled;
            var c = Make(f);

            c.DisableAll().Changed.Should().Be(0);
            c.RestoreAll().Changed.Should().Be(0);
            f.Types["HPDiagsCap"].Should().Be(HpTelemetryServiceControl.Disabled);
        }

        [Fact]
        public void DisableTwice_KeepsTheTrueOriginal()
        {
            var f = new Fake();
            f.Types["HPDiagsCap"] = 2;
            var c = Make(f);
            c.DisableAll();
            c.DisableAll();
            c.RestoreAll();
            f.Types["HPDiagsCap"].Should().Be(2, "the second disable must not record 'Disabled' as the original");
        }

        [Fact]
        public void FailedChange_IsCountedAndNotLeftInTheBackup()
        {
            var f = new Fake();
            f.Types["HPDiagsCap"] = 2;
            f.FailSet.Add("HPDiagsCap");
            var c = Make(f);

            c.DisableAll().Failed.Should().Be(1);
            f.FailSet.Clear();
            c.RestoreAll().Changed.Should().Be(0);
        }

        [Fact]
        public void AbsentServices_AreSkipped() =>
            Make(new Fake()).DisableAll().Should().Be(new HpTelemetryServiceControl.Result(0, 0, HpTelemetryServiceControl.TelemetryServices.Length));
    }
}

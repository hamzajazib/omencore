using System;
using System.Collections.Generic;
using FluentAssertions;
using OmenCore.Models;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    public class NvidiaPowerServiceTests
    {
        private sealed class Lease : IDisposable
        {
            private readonly List<string> _calls;
            public Lease(List<string> calls) => _calls = calls;
            public void Dispose() => _calls.Add("release");
        }

        [Fact]
        public void CurrentWrite_RunsBetweenPrepareAndRelease()
        {
            var calls = new List<string>();
            NvidiaPowerService.GuardCurrent(() => { calls.Add("prepare"); return new Lease(calls); },
                () => { calls.Add("write"); return 0; });
            string.Join(",", calls).Should().Be("prepare,write,release");
        }

        [Fact]
        public void CurrentWrite_NeverRuns_WhenPreparationFails()
        {
            var wrote = false;
            var act = () => NvidiaPowerService.GuardCurrent(
                () => throw new InvalidOperationException("HP performance mode was refused"),
                () => { wrote = true; return 0; });
            act.Should().Throw<InvalidOperationException>();
            wrote.Should().BeFalse("a failed HP preparation must stop the driver write");
        }

        [Fact]
        public void LeaseIsReleased_EvenWhenTheWriteThrows()
        {
            var calls = new List<string>();
            var act = () => NvidiaPowerService.GuardCurrent(() => new Lease(calls), () => throw new InvalidOperationException("driver"));
            act.Should().Throw<InvalidOperationException>();
            calls.Should().Equal("release");
        }

        [Fact]
        public void PowerUnlock_IsOffByDefault() =>
            new FeaturePreferences().NvidiaPowerUnlockEnabled.Should().BeFalse();
    }
}

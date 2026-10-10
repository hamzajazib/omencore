using System.Collections.Generic;
using FluentAssertions;
using OmenCore.Hardware;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    public class GpuEngineLoadSamplerTests
    {
        private static double Agg(params (string name, double value)[] items)
        {
            var list = new List<KeyValuePair<string, double>>();
            foreach (var (n, v) in items) list.Add(new(n, v));
            return GpuEngineLoadSampler.Aggregate(list);
        }

        [Fact]
        public void BusiestThreeDEngine_WinsOverCopyAndVideo() =>
            Agg(("pid_1_engtype_3D", 40), ("pid_2_engtype_3D", 12), ("pid_3_engtype_Copy", 70)).Should().Be(40);

        [Fact]
        public void WithNo3DActivity_CopyAndVideoAreSummed() =>
            Agg(("pid_1_engtype_Copy", 10), ("pid_2_engtype_VideoDecode", 5.5), ("pid_3_engtype_3D", 0)).Should().Be(15.5);

        [Fact]
        public void ResultIsClampedAndNonFiniteValuesAreIgnored()
        {
            Agg(("a_engtype_3D", 250)).Should().Be(100);
            Agg(("a_engtype_3D", double.NaN), ("b_engtype_Copy", -3)).Should().Be(0);
        }

        [Theory]
        [InlineData("pid_4_luid_0x0_phys_0_eng_0_engtype_3D", true)]
        [InlineData("pid_4_luid_0x0_phys_0_eng_1_engtype_VideoProcessing", true)]
        [InlineData("pid_4_luid_0x0_phys_0_eng_2_engtype_Security", false)]
        public void OnlyTheEnginesTheOldCountersCoveredAreTracked(string name, bool tracked) =>
            GpuEngineLoadSampler.IsTrackedEngine(name).Should().Be(tracked);

        [Fact]
        public void ReadingNeverThrows_EvenWithoutTheCounterCategory()
        {
            var s = new GpuEngineLoadSampler();
            s.ReadLoadPercent().Should().BeInRange(0, 100);
            s.ReadLoadPercent().Should().BeInRange(0, 100);
        }
    }
}

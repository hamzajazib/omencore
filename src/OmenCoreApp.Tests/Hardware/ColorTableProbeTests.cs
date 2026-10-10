using System.Linq;
using FluentAssertions;
using OmenCore.Hardware;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    public class ColorTableProbeTests
    {
        [Fact]
        public void EveryVariant_Is128Bytes_AndCarriesTheRequestedColour()
        {
            foreach (var v in ColorTableProbe.Variants)
            {
                var t = v.Build(0x11, 0x22, 0x33);
                t.Should().HaveCount(128, v.Id);
                var at = t.ToList().IndexOf(0x11);
                at.Should().BeGreaterThan(0, v.Id);
                t.Skip(at).Take(3).Should().Equal(0x11, 0x22, 0x33);
            }
        }

        [Fact]
        public void BaselineMatchesWhatReleaseFourFourOneSends()
        {
            var t = ColorTableProbe.Variants[0].Build(0xFF, 0, 0);
            t[0].Should().Be(1, "declared zone count");
            t.Skip(1).Take(24).Should().OnlyContain(b => b == 0, "the 24-byte pad");
            t.Skip(25).Take(3).Should().Equal(0xFF, 0, 0);
        }

        [Fact]
        public void EachVariantChangesExactlyOneThingFromTheBaseline()
        {
            var baseline = ColorTableProbe.Variants[0].Build(0xAA, 0xBB, 0xCC);
            foreach (var v in ColorTableProbe.Variants.Skip(1))
            {
                var t = v.Build(0xAA, 0xBB, 0xCC);
                t.Should().NotEqual(baseline, v.Id);
                var differing = Enumerable.Range(0, 128).Count(i => t[i] != baseline[i]);
                differing.Should().BeLessThanOrEqualTo(7, $"{v.Id} should be a small, single-idea change");
            }
        }

        [Fact]
        public void VariantIds_AreUnique() =>
            ColorTableProbe.Variants.Select(v => v.Id).Should().OnlyHaveUniqueItems();
    }
}

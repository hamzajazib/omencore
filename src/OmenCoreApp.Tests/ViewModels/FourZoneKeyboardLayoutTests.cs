using System.Linq;
using FluentAssertions;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    public class FourZoneKeyboardLayoutTests
    {
        [Fact]
        public void Keys_AreGeneratedOnce_AndAreNotEmpty()
        {
            FourZoneKeyboardLayout.Keys.Should().NotBeEmpty();
            // Constant geometry: the same list instance every time, not rebuilt per call.
            FourZoneKeyboardLayout.Keys.Should().BeSameAs(FourZoneKeyboardLayout.Keys);
        }

        [Fact]
        public void EveryZone_HasAtLeastOneKey()
        {
            var zonesPresent = FourZoneKeyboardLayout.Keys.Select(k => k.ZoneIndex).Distinct().OrderBy(z => z).ToList();

            zonesPresent.Should().BeEquivalentTo(new[] { 1, 2, 3, 4 },
                "the whole point of this layout is a clickable key in every one of the four zones");
        }

        [Fact]
        public void EveryKey_HasAPositiveSizeAndFitsWithinTheCanvas()
        {
            foreach (var key in FourZoneKeyboardLayout.Keys)
            {
                key.Width.Should().BeGreaterThan(0, $"key '{key.Label}' must have a drawable width");
                key.Height.Should().BeGreaterThan(0, $"key '{key.Label}' must have a drawable height");
                key.X.Should().BeGreaterThanOrEqualTo(0, $"key '{key.Label}' must not start left of the canvas");
                key.Y.Should().BeGreaterThanOrEqualTo(0, $"key '{key.Label}' must not start above the canvas");
                (key.X + key.Width).Should().BeLessThanOrEqualTo(FourZoneKeyboardLayout.CanvasWidth + 0.01,
                    $"key '{key.Label}' must not extend past the canvas width");
                (key.Y + key.Height).Should().BeLessThanOrEqualTo(FourZoneKeyboardLayout.CanvasHeight + 0.01,
                    $"key '{key.Label}' must not extend past the canvas height");
            }
        }

        [Fact]
        public void EveryKey_HasAValidZoneIndex()
        {
            FourZoneKeyboardLayout.Keys.Should().OnlyContain(k => k.ZoneIndex >= 1 && k.ZoneIndex <= 4);
        }

        [Fact]
        public void CommonKeys_AreAssignedToTheExpectedZone()
        {
            // Spot-check a handful of keys against LightingView.xaml's own pre-existing zone
            // hints (e.g. Zone 1's "TAB Q W E R T", Zone 4's "ENTER"), so the synthetic layout
            // doesn't silently drift from what the rest of the UI already documents.
            FindKey("Esc").ZoneIndex.Should().Be(1);
            FindKey("Tab").ZoneIndex.Should().Be(1);
            FindKey("Enter").ZoneIndex.Should().Be(4);
            FindKey("Backspace").ZoneIndex.Should().Be(4);
        }

        private static FourZoneKeyVisual FindKey(string label) =>
            FourZoneKeyboardLayout.Keys.First(k => k.Label == label);
    }
}

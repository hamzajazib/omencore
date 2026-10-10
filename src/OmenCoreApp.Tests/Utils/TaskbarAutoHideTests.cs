using FluentAssertions;
using OmenCore.Utils;
using Xunit;

namespace OmenCoreApp.Tests.Utils
{
    public class TaskbarAutoHideTests
    {
        [Fact]
        public void WithoutAutoHide_TheWorkAreaIsUntouched() =>
            TaskbarAutoHide.Fit(0, 0, 1920, 1080, false, TaskbarAutoHide.Edge.Bottom).Should().Be((0, 0, 1920, 1080));

        [Theory]
        [InlineData(TaskbarAutoHide.Edge.Bottom, 0, 0, 1920, 1078)]
        [InlineData(TaskbarAutoHide.Edge.Top, 0, 2, 1920, 1078)]
        [InlineData(TaskbarAutoHide.Edge.Left, 2, 0, 1918, 1080)]
        [InlineData(TaskbarAutoHide.Edge.Right, 0, 0, 1918, 1080)]
        public void AutoHide_LeavesAStripOnTheTaskbarEdge(TaskbarAutoHide.Edge edge, int l, int t, int w, int h) =>
            TaskbarAutoHide.Fit(0, 0, 1920, 1080, true, edge).Should().Be((l, t, w, h));
    }
}

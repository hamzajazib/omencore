using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    /// <summary>
    /// Covers <c>MainViewModel.AppendLogLineAndRebuildBuffer</c>, the log-buffer bookkeeping picked
    /// up standalone from community PR #147 (its other two changes had real bugs found on review
    /// and were not merged - a tray-icon change-detection cache that never populates in the default
    /// configuration, and a dashboard uptime timer that can't restart once paused).
    ///
    /// The original code rebuilt the whole displayed buffer with <c>string.Join</c> on every single
    /// log line; this replaces that with an incrementally-maintained <c>StringBuilder</c> that only
    /// pays for a full rebuild when the line cap is actually exceeded. Output must be byte-identical
    /// to the old <c>string.Join(lines, "\n")</c> behavior in every case - this is a performance
    /// change, not a behavior change.
    /// </summary>
    public class MainViewModelLogBufferTests
    {
        [Fact]
        public void FirstLine_ProducesJustThatLine_WithNoTrailingNewline()
        {
            var lines = new Queue<string>();
            var builder = new StringBuilder();

            var result = MainViewModel.AppendLogLineAndRebuildBuffer("line one", lines, builder, maxLines: 200);

            result.Should().Be("line one");
        }

        [Fact]
        public void MultipleLinesUnderTheCap_AreJoinedWithNewlines_MatchingStringJoinBehavior()
        {
            var lines = new Queue<string>();
            var builder = new StringBuilder();

            MainViewModel.AppendLogLineAndRebuildBuffer("first", lines, builder, maxLines: 200);
            MainViewModel.AppendLogLineAndRebuildBuffer("second", lines, builder, maxLines: 200);
            var result = MainViewModel.AppendLogLineAndRebuildBuffer("third", lines, builder, maxLines: 200);

            result.Should().Be("first\nsecond\nthird", "this must match the old string.Join(lines, \"\\n\") output exactly");
        }

        [Fact]
        public void ExceedingTheCap_DropsTheOldestLine_SameAsTheQueueAlwaysDid()
        {
            var lines = new Queue<string>();
            var builder = new StringBuilder();

            MainViewModel.AppendLogLineAndRebuildBuffer("one", lines, builder, maxLines: 2);
            MainViewModel.AppendLogLineAndRebuildBuffer("two", lines, builder, maxLines: 2);
            var result = MainViewModel.AppendLogLineAndRebuildBuffer("three", lines, builder, maxLines: 2);

            lines.Should().Equal("two", "three");
            result.Should().Be("two\nthree", "the oldest line must be dropped once the cap is exceeded");
        }

        [Fact]
        public void ManyLinesPastTheCap_KeepsOnlyTheMostRecent_AndBufferStaysInSync()
        {
            var lines = new Queue<string>();
            var builder = new StringBuilder();
            string result = string.Empty;

            for (var i = 1; i <= 250; i++)
            {
                result = MainViewModel.AppendLogLineAndRebuildBuffer($"line {i}", lines, builder, maxLines: 200);
            }

            lines.Should().HaveCount(200);
            lines.Peek().Should().Be("line 51", "the 200 most recent lines out of 250 start at line 51");
            result.Should().StartWith("line 51\n").And.EndWith("line 250");
            result.Should().NotContain("line 50\n", "anything older than the cap must not survive in the displayed buffer");
        }

        [Fact]
        public void BufferNeverEndsWithATrailingNewline_UnlikeAppendingRawWithoutTrimming()
        {
            var lines = new Queue<string>();
            var builder = new StringBuilder();

            var result = MainViewModel.AppendLogLineAndRebuildBuffer("only line", lines, builder, maxLines: 200);

            result.Should().NotEndWith("\n");
        }
    }
}

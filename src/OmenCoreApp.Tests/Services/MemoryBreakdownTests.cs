using System.Text;
using FluentAssertions;
using OmenCore.Services.Diagnostics;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    public class MemoryBreakdownTests
    {
        [Fact]
        public void Breakdown_SplitsManagedHeapFromNativeAndNamesTheLargestModules()
        {
            var sb = new StringBuilder();
            DiagnosticExportService.AppendMemoryBreakdown(sb);
            var text = sb.ToString();

            text.Should().Contain("[Memory Breakdown]");
            text.Should().Contain("Gen2:").And.Contain("LOH:");
            text.Should().Contain("NativeEstimateMB");
            text.Should().Contain("LargestModules:");
        }
    }
}

using System;
using System.IO;
using FluentAssertions;
using NvpwrControlBlackwell;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    public class NvidiaStatePathTests
    {
        [Fact]
        public void ValidationCaches_LiveOutsideTheInstallFolder_SoUpdatesKeepThem()
        {
            StatePaths.Dir.Should().StartWith(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
            StatePaths.Dir.Should().NotStartWith(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        }
    }
}

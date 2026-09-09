using System;
using System.IO;
using System.Reflection;
using FluentAssertions;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    [Collection("Config Isolation")]
    public class UpdateViewModelTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string? _previousConfigDir;

        public UpdateViewModelTests()
        {
            _previousConfigDir = Environment.GetEnvironmentVariable("OMENCORE_CONFIG_DIR");
            _tempDir = Path.Combine(Path.GetTempPath(), "OmenCoreTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDir);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", _tempDir);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", _previousConfigDir);
        }

        private static UpdateViewModel CreateViewModel(LoggingService logging, ConfigurationService configService, AppConfig config)
        {
            // CheckOnStartup defaults to true, which would fire a real network call to GitHub's
            // API from the constructor - not something a unit test should do on every run.
            config.Updates ??= new UpdatePreferences();
            config.Updates.CheckOnStartup = false;
            return new UpdateViewModel(logging, configService, config);
        }

        [Fact]
        public void Constructor_SetsAppVersionLabel_ImmediatelySynchronously()
        {
            var logging = new LoggingService();
            var configService = new ConfigurationService();
            var config = configService.Load();

            using var vm = CreateViewModel(logging, configService, config);

            vm.AppVersionLabel.Should().StartWith("v");
        }

        [Fact]
        public void CanInstallUpdate_IsFalse_WithNoAvailableUpdate()
        {
            var logging = new LoggingService();
            var configService = new ConfigurationService();
            var config = configService.Load();

            using var vm = CreateViewModel(logging, configService, config);

            vm.InstallUpdateCommand.CanExecute(null).Should().BeFalse("no update has been discovered yet");
        }

        [Fact]
        public void CanOpenReleaseNotes_IsFalse_WithNoAvailableUpdate()
        {
            var logging = new LoggingService();
            var configService = new ConfigurationService();
            var config = configService.Load();

            using var vm = CreateViewModel(logging, configService, config);

            vm.OpenReleaseNotesCommand.CanExecute(null).Should().BeFalse("there is no changelog URL to open yet");
        }

        [Fact]
        public void AutoUpdateService_IsExposed_ForExternalCallers()
        {
            var logging = new LoggingService();
            var configService = new ConfigurationService();
            var config = configService.Load();

            using var vm = CreateViewModel(logging, configService, config);

            vm.AutoUpdateService.Should().NotBeNull("MainViewModel.ReportModelAsync reads this directly");
            vm.AutoUpdateService.GetCurrentVersion().Should().NotBeNull();
        }

        [Theory]
        [InlineData(30, "30s")]
        [InlineData(90, "1m 30s")]
        [InlineData(3661, "1h 1m")]
        public void FormatTimeSpan_FormatsHoursMinutesSeconds(int totalSeconds, string expected)
        {
            var method = typeof(UpdateViewModel).GetMethod("FormatTimeSpan", BindingFlags.NonPublic | BindingFlags.Static);
            method.Should().NotBeNull();

            var result = (string)method!.Invoke(null, new object[] { TimeSpan.FromSeconds(totalSeconds) })!;

            result.Should().Be(expected);
        }
    }
}

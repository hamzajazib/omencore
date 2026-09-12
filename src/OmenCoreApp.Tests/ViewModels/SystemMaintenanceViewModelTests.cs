using System;
using System.IO;
using FluentAssertions;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    [Collection("Config Isolation")]
    public class SystemMaintenanceViewModelTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string? _previousConfigDir;

        public SystemMaintenanceViewModelTests()
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

        private static SystemMaintenanceViewModel CreateViewModel(LoggingService logging)
        {
            var gpuSwitchService = new GpuSwitchService(logging);
            var cleanupService = new OmenGamingHubCleanupService(logging);
            var restoreService = new SystemRestoreService(logging);
            return new SystemMaintenanceViewModel(gpuSwitchService, cleanupService, restoreService, logging);
        }

        [Fact]
        public void Constructor_PopulatesGpuSwitchModes_AndDetectsCurrentModeSynchronously()
        {
            // GpuSwitchModes / DetectGpuMode() used to run inline in SystemControlViewModel's own
            // constructor; extraction moved them into this sub-VM's constructor unchanged.
            var logging = new LoggingService();
            var vm = CreateViewModel(logging);

            vm.GpuSwitchModes.Should().BeEquivalentTo(new[]
            {
                GpuSwitchMode.Hybrid,
                GpuSwitchMode.Discrete,
                GpuSwitchMode.Integrated
            });
            vm.CurrentGpuMode.Should().NotBeNullOrWhiteSpace();
            vm.CurrentGpuMode.Should().NotBe("Detecting...", "DetectGpuMode() should have run synchronously in the constructor");
        }

        [Fact]
        public void Constructor_WithNoWmiBios_LeavesDisplayOverdriveUnsupported()
        {
            var logging = new LoggingService();
            var vm = CreateViewModel(logging);

            vm.DisplayOverdriveSupported.Should().BeFalse();
        }

        [Fact]
        public void Commands_AreConstructed_AndNonNull()
        {
            var logging = new LoggingService();
            var vm = CreateViewModel(logging);

            vm.SwitchGpuModeCommand.Should().NotBeNull();
            vm.RunCleanupCommand.Should().NotBeNull();
            vm.CreateRestorePointCommand.Should().NotBeNull();
        }

        [Fact]
        public void RunCleanupCommand_CanExecute_WhenNotInProgress()
        {
            var logging = new LoggingService();
            var vm = CreateViewModel(logging);

            vm.CleanupInProgress.Should().BeFalse();
            vm.RunCleanupCommand.CanExecute(null).Should().BeTrue();
        }

        [Fact]
        public void CleanupOptions_DefaultToSameValues_AsBeforeExtraction()
        {
            var logging = new LoggingService();
            var vm = CreateViewModel(logging);

            vm.CleanupUninstallApp.Should().BeTrue();
            vm.CleanupRemoveServices.Should().BeTrue();
            vm.CleanupRegistryEntries.Should().BeFalse();
            vm.CleanupRemoveLegacyInstallers.Should().BeTrue();
            vm.CleanupRemoveFiles.Should().BeTrue();
            vm.CleanupKillProcesses.Should().BeTrue();
        }

        [Fact]
        public void Dispose_DoesNotThrow()
        {
            var logging = new LoggingService();
            var vm = CreateViewModel(logging);

            var act = () => vm.Dispose();

            act.Should().NotThrow();
        }
    }
}

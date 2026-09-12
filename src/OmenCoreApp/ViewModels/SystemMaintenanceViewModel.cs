using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using OmenCore.Hardware;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Utils;

namespace OmenCore.ViewModels
{
    /// <summary>
    /// Four small, mutually-independent maintenance features extracted out of
    /// SystemControlViewModel: GPU mode switching (Hybrid/Discrete/Integrated), display panel
    /// overdrive, the OMEN Gaming Hub cleanup wizard, and manual system-restore-point creation.
    /// None of them touch the fan/EC/undervolt/GPU-OC write paths or the tuning-safety rollback
    /// system that ties those together, which is what makes this a clean, low-risk slice to pull
    /// out on its own.
    /// </summary>
    public class SystemMaintenanceViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly LoggingService _logging;
        private readonly GpuSwitchService _gpuSwitchService;
        private readonly OmenGamingHubCleanupService _cleanupService;
        private readonly SystemRestoreService _restoreService;
        private readonly HpWmiBios? _wmiBios;

        public event PropertyChangedEventHandler? PropertyChanged;

        #region GPU Mode Switching

        private string _currentGpuMode = "Detecting...";
        public string CurrentGpuMode
        {
            get => _currentGpuMode;
            private set
            {
                if (_currentGpuMode != value)
                {
                    _currentGpuMode = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool GpuModeSwitchingSupported => _gpuSwitchService.IsSupported;

        public bool GpuModeSwitchingUnsupported => !GpuModeSwitchingSupported;

        public string GpuModeSwitchingStatusText => GpuModeSwitchingSupported
            ? "BIOS WMI GPU switching is available."
            : $"GPU mode switching is unavailable through OmenCore: {_gpuSwitchService.UnsupportedReason}";

        public ObservableCollection<GpuSwitchMode> GpuSwitchModes { get; } = new();
        public GpuSwitchMode? SelectedGpuMode { get; set; }
        public ICommand SwitchGpuModeCommand { get; }

        #endregion

        #region Display Overdrive

        private bool _displayOverdriveEnabled;
        public bool DisplayOverdriveEnabled
        {
            get => _displayOverdriveEnabled;
            set
            {
                if (_displayOverdriveEnabled != value)
                {
                    _displayOverdriveEnabled = value;
                    OnPropertyChanged(nameof(DisplayOverdriveEnabled));
                    // Apply immediately when toggled
                    _ = SetDisplayOverdriveAsync(value);
                }
            }
        }

        private bool _displayOverdriveSupported;
        public bool DisplayOverdriveSupported
        {
            get => _displayOverdriveSupported;
            private set
            {
                if (_displayOverdriveSupported != value)
                {
                    _displayOverdriveSupported = value;
                    OnPropertyChanged(nameof(DisplayOverdriveSupported));
                }
            }
        }

        #endregion

        #region OMEN Cleanup

        public ObservableCollection<string> OmenCleanupSteps { get; } = new();
        public bool CleanupUninstallApp { get; set; } = true;
        public bool CleanupRemoveServices { get; set; } = true;
        public bool CleanupRegistryEntries { get; set; } = false;
        public bool CleanupRemoveLegacyInstallers { get; set; } = true;
        public bool CleanupRemoveFiles { get; set; } = true;
        public bool CleanupKillProcesses { get; set; } = true;
        public string CleanupStatusText => CleanupStatus;
        public ICommand RunCleanupCommand { get; }

        private bool _cleanupInProgress;
        public bool CleanupInProgress
        {
            get => _cleanupInProgress;
            private set
            {
                if (_cleanupInProgress != value)
                {
                    _cleanupInProgress = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasCleanupSteps));
                    (RunCleanupCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        private bool _cleanupComplete;
        public bool CleanupComplete
        {
            get => _cleanupComplete;
            private set
            {
                if (_cleanupComplete != value)
                {
                    _cleanupComplete = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool HasCleanupSteps => OmenCleanupSteps.Count > 0;

        private string _cleanupStatus = "Status: Not checked";
        public string CleanupStatus
        {
            get => _cleanupStatus;
            private set
            {
                if (_cleanupStatus != value)
                {
                    _cleanupStatus = value;
                    OnPropertyChanged();
                }
            }
        }

        #endregion

        #region Restore Point

        public ICommand CreateRestorePointCommand { get; }

        #endregion

        // Mirrors ViewModelBase.ExecuteWithLoadingAsync (this class doesn't inherit
        // ViewModelBase, matching the newer sub-VM convention established for UpdateViewModel /
        // GpuClampViewModel) so the three async operations below keep the exact busy-state
        // behavior they had on SystemControlViewModel, even though nothing currently binds to it.
        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsNotLoading));
                }
            }
        }

        public bool IsNotLoading => !IsLoading;

        private string _loadingMessage = "Loading...";
        public string LoadingMessage
        {
            get => _loadingMessage;
            set
            {
                if (_loadingMessage != value)
                {
                    _loadingMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        private async Task ExecuteWithLoadingAsync(Func<Task> operation, string? loadingMessage = null)
        {
            try
            {
                if (loadingMessage != null)
                {
                    LoadingMessage = loadingMessage;
                }
                IsLoading = true;
                await operation();
            }
            finally
            {
                IsLoading = false;
            }
        }

        public SystemMaintenanceViewModel(
            GpuSwitchService gpuSwitchService,
            OmenGamingHubCleanupService cleanupService,
            SystemRestoreService restoreService,
            LoggingService logging,
            HpWmiBios? wmiBios = null)
        {
            _gpuSwitchService = gpuSwitchService;
            _cleanupService = cleanupService;
            _restoreService = restoreService;
            _logging = logging;
            _wmiBios = wmiBios;

            SwitchGpuModeCommand = new AsyncRelayCommand(_ => SwitchGpuModeAsync());
            RunCleanupCommand = new AsyncRelayCommand(_ => RunCleanupAsync(), _ => !CleanupInProgress);
            CreateRestorePointCommand = new AsyncRelayCommand(_ => CreateRestorePointAsync());

            GpuSwitchModes.Add(GpuSwitchMode.Hybrid);
            GpuSwitchModes.Add(GpuSwitchMode.Discrete);
            GpuSwitchModes.Add(GpuSwitchMode.Integrated);

            DetectGpuMode();
            DetectDisplayOverdrive();
        }

        private void DetectGpuMode()
        {
            try
            {
                var mode = _gpuSwitchService.DetectCurrentMode();
                CurrentGpuMode = mode switch
                {
                    GpuSwitchMode.Hybrid => "Hybrid (MSHybrid/Optimus)",
                    GpuSwitchMode.Discrete => "Discrete GPU Only",
                    GpuSwitchMode.Integrated => "Integrated GPU Only",
                    _ => "Unknown"
                };
                _logging.Info($"Detected GPU mode: {CurrentGpuMode}");
            }
            catch (Exception ex)
            {
                _logging.ErrorWithContext(
                    component: "SystemMaintenanceViewModel",
                    operation: "DetectGpuMode",
                    message: "Failed to detect GPU mode",
                    ex: ex);
                CurrentGpuMode = "Detection Failed";
            }
        }

        private async Task SwitchGpuModeAsync()
        {
            if (SelectedGpuMode == null)
            {
                _logging.Warn("No GPU mode selected");
                return;
            }

            // Check if GPU mode switching is supported BEFORE attempting
            if (!_gpuSwitchService.IsSupported)
            {
                var reason = _gpuSwitchService.UnsupportedReason;
                _logging.Info($"GPU mode switching not available: {reason}");
                return;
            }

            await ExecuteWithLoadingAsync(() => {
                var targetMode = SelectedGpuMode.Value;
                _logging.Info($"⚡ Attempting to switch GPU mode to: {targetMode}");

                var success = _gpuSwitchService.Switch(targetMode);

                if (success)
                {
                    _logging.Info($"✓ GPU mode switch initiated. System restart required to apply changes.");

                    // Show restart prompt
                    var result = System.Windows.MessageBox.Show(
                        $"GPU mode has been set to {targetMode}.\n\n" +
                        "A system restart is required for changes to take effect.\n\n" +
                        "Would you like to restart now?",
                        "Restart Required - OmenCore",
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Question);

                    if (result == System.Windows.MessageBoxResult.Yes)
                    {
                        // Restart the system
                        System.Diagnostics.Process.Start("shutdown", "/r /t 0");
                    }
                }
                else
                {
                    _logging.Error("✗ GPU mode switch failed");
                    System.Windows.MessageBox.Show(
                        "Failed to switch GPU mode. Please check the logs for details.",
                        "GPU Mode Switch Failed",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }

                return Task.CompletedTask;
            });
        }

        private async Task SetDisplayOverdriveAsync(bool enabled)
        {
            if (_wmiBios == null) return;

            await Task.Run(() =>
            {
                try
                {
                    var success = _wmiBios.SetDisplayOverdrive(enabled);
                    if (!success)
                    {
                        _logging.Warn($"Display overdrive toggle failed");
                        // Revert UI
                        _displayOverdriveEnabled = !enabled;
                        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() => OnPropertyChanged(nameof(DisplayOverdriveEnabled)));
                    }
                }
                catch (Exception ex)
                {
                    _logging.Error($"Display overdrive error: {ex.Message}", ex);
                    _displayOverdriveEnabled = !enabled;
                    System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() => OnPropertyChanged(nameof(DisplayOverdriveEnabled)));
                }
            });
        }

        private void DetectDisplayOverdrive()
        {
            if (_wmiBios == null || !_wmiBios.IsAvailable) return;

            try
            {
                var status = _wmiBios.GetDisplayOverdrive();
                if (status.HasValue)
                {
                    DisplayOverdriveSupported = true;
                    _displayOverdriveEnabled = status.Value;
                    OnPropertyChanged(nameof(DisplayOverdriveEnabled));
                    _logging.Info($"✓ Display overdrive supported. Current: {(status.Value ? "enabled" : "disabled")}");
                }
                else
                {
                    DisplayOverdriveSupported = false;
                    _logging.Info("Display overdrive not supported on this model");
                }
            }
            catch (Exception ex)
            {
                DisplayOverdriveSupported = false;
                _logging.Warn($"Display overdrive detection failed: {ex.Message}");
            }
        }

        private async Task RunCleanupAsync()
        {
            OmenCleanupSteps.Clear();
            _cleanupService.StepCompleted += OnStepCompleted;

            try
            {
                await ExecuteWithLoadingAsync(async () =>
                {
                    var options = new OmenCleanupOptions
                    {
                        RemoveStorePackage = CleanupUninstallApp,
                        RemoveServicesAndTasks = CleanupRemoveServices,
                        RemoveRegistryTraces = CleanupRegistryEntries,
                        RemoveLegacyInstallers = CleanupRemoveLegacyInstallers,
                        RemoveResidualFiles = CleanupRemoveFiles,
                        KillRunningProcesses = CleanupKillProcesses,
                        DryRun = false,
                        PreserveFirewallRules = true
                    };

                    var result = await _cleanupService.CleanupAsync(options);
                    CleanupStatus = result.Success ? "✓ Cleanup complete - restart recommended" : "⚠ Cleanup failed";
                    CleanupComplete = result.Success;
                }, "Running HP Omen cleanup...");
            }
            finally
            {
                _cleanupService.StepCompleted -= OnStepCompleted;
                CleanupInProgress = false;
            }
        }

        private void OnStepCompleted(string step)
        {
            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                OmenCleanupSteps.Add(step);
                CleanupStatus = step;
                OnPropertyChanged(nameof(HasCleanupSteps));
            });
        }

        private async Task CreateRestorePointAsync()
        {
            await ExecuteWithLoadingAsync(async () =>
            {
                _logging.Info("Creating system restore point...");
                var result = await _restoreService.CreateRestorePointAsync("OmenCore - Before System Changes");

                if (result.Success)
                {
                    _logging.Info($"✓ System restore point created successfully (Sequence: {result.SequenceNumber})");
                }
                else
                {
                    _logging.Error($"✗ Failed to create restore point: {result.Message}");
                }
            }, "Creating system restore point...");
        }

        public void Dispose()
        {
            // Nothing persistent to unhook: _cleanupService.StepCompleted is subscribed and
            // unsubscribed entirely within RunCleanupAsync's own try/finally.
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

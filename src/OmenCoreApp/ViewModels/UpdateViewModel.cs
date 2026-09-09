using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Utils;

namespace OmenCore.ViewModels
{
    /// <summary>
    /// App-update checking, downloading, and installing — extracted out of MainViewModel since
    /// it's a clean, self-contained feature with its own service (AutoUpdateService). Constructed
    /// eagerly (not lazily like tab-scoped sub-VMs) because its bindings live in always-visible
    /// window chrome (title-bar version label, update banner), not inside a lazily-created tab.
    /// </summary>
    public class UpdateViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly LoggingService _logging;
        private readonly ConfigurationService _configService;
        private readonly AppConfig _config;
        private readonly AutoUpdateService _autoUpdateService;

        private readonly AsyncRelayCommand _installUpdateCommand;
        private readonly AsyncRelayCommand _checkForUpdatesCommand;
        private readonly RelayCommand _openReleaseNotesCommand;

        private VersionInfo? _availableUpdate;
        private bool _updateBannerVisible;
        private string _updateBannerMessage = string.Empty;
        private bool _updateDownloadInProgress;
        private double _updateDownloadProgress;
        private string _updateDownloadStatus = string.Empty;
        private bool _updateInstallBlocked;
        private string _appVersionLabel = "v0.0.0";

        public event PropertyChangedEventHandler? PropertyChanged;

        public UpdateViewModel(LoggingService logging, ConfigurationService configService, AppConfig config, AutoUpdateService? autoUpdateService = null)
        {
            _logging = logging;
            _configService = configService;
            _config = config;

            _autoUpdateService = autoUpdateService ?? new AutoUpdateService(_logging);
            _autoUpdateService.DownloadProgressChanged += OnUpdateDownloadProgressChanged;
            _autoUpdateService.UpdateCheckCompleted += OnBackgroundUpdateCheckCompleted;
            AppVersionLabel = $"v{_autoUpdateService.GetCurrentVersion()}";

            _checkForUpdatesCommand = new AsyncRelayCommand(_ => CheckForUpdatesBannerAsync(true), _ => !_updateDownloadInProgress);
            CheckForUpdatesCommand = _checkForUpdatesCommand;
            _installUpdateCommand = new AsyncRelayCommand(_ => InstallUpdateAsync(), _ => CanInstallUpdate());
            InstallUpdateCommand = _installUpdateCommand;
            _openReleaseNotesCommand = new RelayCommand(_ => OpenReleaseNotes(), _ => CanOpenReleaseNotes());
            OpenReleaseNotesCommand = _openReleaseNotesCommand;

            var updatePrefs = _config.Updates ?? new UpdatePreferences();
            _autoUpdateService.ConfigureBackgroundChecks(updatePrefs);
            if (updatePrefs.CheckOnStartup)
            {
                _ = CheckForUpdatesBannerAsync();
            }
        }

        /// <summary>Exposed for the one external call site (MainViewModel.ReportModelAsync) that needs the current version string.</summary>
        public AutoUpdateService AutoUpdateService => _autoUpdateService;

        public string AppVersionLabel
        {
            get => _appVersionLabel;
            private set
            {
                if (_appVersionLabel != value)
                {
                    _appVersionLabel = value;
                    OnPropertyChanged(nameof(AppVersionLabel));
                }
            }
        }

        public bool UpdateBannerVisible
        {
            get => _updateBannerVisible;
            private set
            {
                if (_updateBannerVisible != value)
                {
                    _updateBannerVisible = value;
                    OnPropertyChanged(nameof(UpdateBannerVisible));
                }
            }
        }

        public string UpdateBannerMessage
        {
            get => _updateBannerMessage;
            private set
            {
                if (_updateBannerMessage != value)
                {
                    _updateBannerMessage = value;
                    OnPropertyChanged(nameof(UpdateBannerMessage));
                }
            }
        }

        public bool UpdateDownloadInProgress
        {
            get => _updateDownloadInProgress;
            private set
            {
                if (_updateDownloadInProgress != value)
                {
                    _updateDownloadInProgress = value;
                    OnPropertyChanged(nameof(UpdateDownloadInProgress));
                }
            }
        }

        public double UpdateDownloadProgress
        {
            get => _updateDownloadProgress;
            private set
            {
                if (Math.Abs(_updateDownloadProgress - value) > 0.01)
                {
                    _updateDownloadProgress = value;
                    OnPropertyChanged(nameof(UpdateDownloadProgress));
                }
            }
        }

        public string UpdateDownloadStatus
        {
            get => _updateDownloadStatus;
            private set
            {
                if (_updateDownloadStatus != value)
                {
                    _updateDownloadStatus = value;
                    OnPropertyChanged(nameof(UpdateDownloadStatus));
                }
            }
        }

        public ICommand InstallUpdateCommand { get; }
        public ICommand CheckForUpdatesCommand { get; }
        public ICommand OpenReleaseNotesCommand { get; }

        private async Task CheckForUpdatesBannerAsync(bool showStatus = false)
        {
            try
            {
                if (showStatus)
                {
                    UpdateBannerVisible = true;
                    UpdateBannerMessage = "Checking for updates...";
                }

                var result = await _autoUpdateService.CheckForUpdatesAsync();

                // Update last check time
                if (_config.Updates != null)
                {
                    _config.Updates.LastCheckTime = DateTime.Now;
                    _configService.Save(_config);
                }

                if (result.UpdateAvailable && result.LatestVersion != null)
                {
                    // Check if version is skipped
                    if (_config.Updates?.SkippedVersion == result.LatestVersion.VersionString)
                    {
                        _logging.Info($"Update v{result.LatestVersion.VersionString} available but skipped by user");
                        return;
                    }

                    _availableUpdate = result.LatestVersion;
                    _updateInstallBlocked = false;
                    UpdateBannerMessage = $"Update available: v{_availableUpdate.VersionString} (Current {AppVersionLabel})";
                    UpdateBannerVisible = true;
                }
                else
                {
                    _availableUpdate = null;
                    _updateInstallBlocked = false;
                    if (showStatus)
                    {
                        UpdateBannerMessage = "You are running the latest version.";
                        UpdateBannerVisible = true;
                        // Auto-hide after 3 seconds
                        _ = AutoHideLatestVersionBannerAsync();
                    }
                    else
                    {
                        UpdateBannerVisible = false;
                        UpdateBannerMessage = string.Empty;
                    }
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"Update check failed: {ex.Message}");
            }
            finally
            {
                RefreshUpdateCommands();
            }
        }

        private async Task AutoHideLatestVersionBannerAsync()
        {
            try
            {
                await Task.Delay(3000);
                if (UpdateBannerMessage == "You are running the latest version.")
                {
                    UpdateBannerVisible = false;
                    UpdateBannerMessage = string.Empty;
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to auto-hide update banner: {ex.Message}");
            }
        }

        private async Task InstallUpdateAsync()
        {
            if (_availableUpdate == null)
            {
                return;
            }

            try
            {
                UpdateDownloadInProgress = true;
                UpdateDownloadProgress = 0;
                UpdateBannerMessage = $"Downloading v{_availableUpdate.VersionString} ({_availableUpdate.FileSizeFormatted})";
                UpdateDownloadStatus = "Initializing download...";

                _logging.Info($"Starting update download: v{_availableUpdate.VersionString}");

                var installerPath = await _autoUpdateService.DownloadUpdateAsync(_availableUpdate);

                if (installerPath == null)
                {
                    var hashMissing = string.IsNullOrWhiteSpace(_availableUpdate?.Sha256Hash);
                    UpdateBannerMessage = hashMissing
                        ? "Update requires manual download (missing SHA256 in release notes)."
                        : "Download failed. Check Release Notes for manual download.";
                    UpdateDownloadStatus = hashMissing ? "Install blocked until SHA256 is provided" : "Download failed";
                    _updateInstallBlocked = hashMissing;
                    _logging.Warn("Update download unavailable; missing hash or download error");
                    RefreshUpdateCommands();
                    return;
                }

                UpdateBannerMessage = "Installing update...";
                UpdateDownloadStatus = "Launching installer...";

                _logging.Info($"Installing update from {System.IO.Path.GetFileName(installerPath)}");

                var installResult = await _autoUpdateService.InstallUpdateAsync(installerPath);

                if (!installResult.Success)
                {
                    UpdateBannerMessage = installResult.Message;
                    UpdateDownloadStatus = "Installation failed";
                    _logging.ErrorWithContext(
                        component: "UpdateViewModel",
                        operation: "InstallUpdateAsync",
                        message: $"Update installation failed: {installResult.Message}");
                }
                else
                {
                    _logging.Info("Update installer launched - Application will restart");
                }
            }
            catch (System.Security.SecurityException ex)
            {
                _logging.ErrorWithContext(
                    component: "UpdateViewModel",
                    operation: "InstallUpdateAsync.Security",
                    message: "Update security verification failed",
                    ex: ex);
                UpdateBannerMessage = "Security verification failed";
                UpdateDownloadStatus = "Hash verification failed - update rejected for security";
            }
            catch (Exception ex)
            {
                _logging.ErrorWithContext(
                    component: "UpdateViewModel",
                    operation: "InstallUpdateAsync",
                    message: "Update installation failed",
                    ex: ex);
                UpdateBannerMessage = $"Update failed: {ex.Message}";
                UpdateDownloadStatus = "Error occurred";
            }
            finally
            {
                UpdateDownloadInProgress = false;
                UpdateDownloadProgress = 0;
                RefreshUpdateCommands();
            }
        }

        private void OnUpdateDownloadProgressChanged(object? sender, UpdateDownloadProgress progress)
        {
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                UpdateDownloadProgress = progress.ProgressPercent;
                UpdateDownloadStatus = $"{progress.ProgressPercent:F1}% • {progress.DownloadSpeedMbps:F2} MB/s • {FormatTimeSpan(progress.EstimatedTimeRemaining)} remaining";
            });
        }

        private void OnBackgroundUpdateCheckCompleted(object? sender, UpdateCheckResult result)
        {
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                if (result.UpdateAvailable && result.LatestVersion != null)
                {
                    _availableUpdate = result.LatestVersion;
                    _updateInstallBlocked = false;
                    UpdateBannerVisible = true;
                    UpdateBannerMessage = $"v{result.LatestVersion.VersionString} is now available";
                    _logging.Info($"Background check found update: v{result.LatestVersion.VersionString}");
                    RefreshUpdateCommands();
                }
            });
        }

        private static string FormatTimeSpan(TimeSpan span)
        {
            if (span.TotalHours >= 1)
                return $"{span.Hours}h {span.Minutes}m";
            if (span.TotalMinutes >= 1)
                return $"{span.Minutes}m {span.Seconds}s";
            return $"{span.Seconds}s";
        }

        private bool CanOpenReleaseNotes() => _availableUpdate != null && !string.IsNullOrWhiteSpace(_availableUpdate.ChangelogUrl);

        private bool CanInstallUpdate() => _availableUpdate != null && !_updateDownloadInProgress && !_updateInstallBlocked;

        private void OpenReleaseNotes()
        {
            if (!CanOpenReleaseNotes())
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _availableUpdate!.ChangelogUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logging.ErrorWithContext(
                    component: "UpdateViewModel",
                    operation: "OpenReleaseNotes",
                    message: "Failed to open release notes",
                    ex: ex);
            }
        }

        private void RefreshUpdateCommands()
        {
            _installUpdateCommand.RaiseCanExecuteChanged();
            _openReleaseNotesCommand.RaiseCanExecuteChanged();
            _checkForUpdatesCommand.RaiseCanExecuteChanged();
        }

        public void Dispose()
        {
            _autoUpdateService.DownloadProgressChanged -= OnUpdateDownloadProgressChanged;
            _autoUpdateService.UpdateCheckCompleted -= OnBackgroundUpdateCheckCompleted;
            _autoUpdateService.Dispose();
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

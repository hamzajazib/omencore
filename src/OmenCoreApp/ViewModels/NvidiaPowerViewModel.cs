using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using OmenCore.Services;
using OmenCore.Utils;

namespace OmenCore.ViewModels
{
    /// <summary>
    /// Tuning panel for the opt-in NVIDIA laptop GPU power unlock. Nothing is read or written until the user
    /// turns it on, and every write is confirmed first.
    /// </summary>
    public class NvidiaPowerViewModel : INotifyPropertyChanged
    {
        private readonly ConfigurationService _config;
        private readonly NvidiaPowerService _service;
        private readonly LoggingService _logging;
        private NvidiaPowerService.Status? _status;
        private bool _busy;
        private string _statusText = "";
        private string _resultText = "";
        private int _selectedTarget;

        public NvidiaPowerViewModel(ConfigurationService config, NvidiaPowerService service, LoggingService logging)
        {
            _config = config;
            _service = service;
            _logging = logging;
            RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync(), _ => Enabled && !_busy);
            ApplyMaxCommand = new AsyncRelayCommand(_ => RunAsync("MAX", w => _service.ApplyMax(w), true),
                _ => Enabled && !_busy && SelectedTarget > 0 && _status?.MaxReady == true);
            ApplyCurrentCommand = new AsyncRelayCommand(_ => RunAsync("CURRENT", w => _service.ApplyCurrent(w), false),
                _ => Enabled && !_busy && SelectedTarget > 0 && _status?.CurrentReady == true);
            RemoveMaxCommand = new AsyncRelayCommand(_ => RunAsync("remove MAX override", _ => _service.RemoveMax(), true),
                _ => Enabled && !_busy && _status?.MaxReady == true);
            ResolveVbiosCommand = new AsyncRelayCommand(_ => ResolveAsync(null), _ => Enabled && !_busy);
            ResolveVbiosFromFileCommand = new AsyncRelayCommand(_ =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Select a VBIOS dump", Filter = "VBIOS dump (*.rom;*.bin)|*.rom;*.bin|All files|*.*" };
                return dlg.ShowDialog() == true ? ResolveAsync(dlg.FileName) : Task.CompletedTask;
            }, _ => Enabled && !_busy);
            if (Enabled) _ = RefreshAsync();
        }

        public ICommand ResolveVbiosCommand { get; }
        public ICommand ResolveVbiosFromFileCommand { get; }

        private async Task ResolveAsync(string? romPath)
        {
            _busy = true; RaiseCommands();
            try
            {
                var r = await Task.Run(() => romPath == null ? _service.ResolveVbiosAuto() : _service.ResolveVbiosFromRom(romPath));
                ResultText = (r.Success ? "Done: " : "Not resolved: ") + r.Message;
            }
            finally
            {
                _busy = false;
                await RefreshAsync();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<int> Targets { get; } = new();
        public ICommand RefreshCommand { get; }
        public ICommand ApplyMaxCommand { get; }
        public ICommand ApplyCurrentCommand { get; }
        public ICommand RemoveMaxCommand { get; }

        public bool Enabled
        {
            get => _config.Config.Features?.NvidiaPowerUnlockEnabled == true;
            set
            {
                if (Enabled == value) return;
                _config.Config.Features ??= new OmenCore.Models.FeaturePreferences();
                _config.Config.Features.NvidiaPowerUnlockEnabled = value;
                _config.Save(_config.Config);
                OnPropertyChanged();
                if (value) _ = RefreshAsync(); else StatusText = "";
                RaiseCommands();
            }
        }

        public string StatusText { get => _statusText; private set { _statusText = value; OnPropertyChanged(); } }
        public string ResultText { get => _resultText; private set { _resultText = value; OnPropertyChanged(); } }

        public int SelectedTarget
        {
            get => _selectedTarget;
            set { _selectedTarget = value; OnPropertyChanged(); RaiseCommands(); }
        }

        private Task RefreshAsync() => Task.Run(() =>
        {
            NvidiaPowerService.Status s;
            try { s = _service.Assess(); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or DllNotFoundException)
            {
                Post(() => StatusText = "Could not read the NVIDIA driver: " + ex.Message);
                return;
            }
            Post(() =>
            {
                _status = s;
                Targets.Clear();
                foreach (var t in s.Targets) Targets.Add(t);
                if (!Targets.Contains(SelectedTarget)) SelectedTarget = Targets.FirstOrDefault();
                var live = s.LiveCurrentW.HasValue && s.LiveMaxW.HasValue
                    ? $"Live CURRENT {s.LiveCurrentW:0.#} W, live MAX {s.LiveMaxW:0.#} W. " : "";
                StatusText = $"{(string.IsNullOrEmpty(s.Gpu) ? "NVIDIA GPU" : s.Gpu)}: {live}" +
                             $"MAX writes {(s.MaxReady ? "ready" : "not ready")}, CURRENT writes {(s.CurrentReady ? "ready" : "not ready")}. {s.Reason}";
                RaiseCommands();
            });
        });

        private async Task RunAsync(string what, Func<int, NvidiaPowerService.Outcome> op, bool mayNeedReboot)
        {
            var watts = SelectedTarget;
            var prompt = what.StartsWith("remove", StringComparison.Ordinal)
                ? "Remove the MAX override? Windows must be restarted afterwards to restore the factory limit."
                : $"Apply {what} {watts} W?\n\nThis changes the GPU's power limit beyond the laptop maker's setting. " +
                  "Higher power means more heat, and the cooling was not designed for it. " +
                  (mayNeedReboot ? "MAX needs a Windows restart to take effect. " : "CURRENT resets on reboot. ") +
                  "Only continue if you understand the risk.";
            if (MessageBox.Show(prompt, "NVIDIA power", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;

            _busy = true; RaiseCommands();
            try
            {
                var r = await Task.Run(() => op(watts));
                ResultText = (r.Success ? "Done: " : "Not applied: ") + r.Message + (r.RebootRequired ? " Restart Windows to finish." : "");
                _logging.Info("NVIDIA power panel: " + ResultText);
            }
            finally
            {
                _busy = false;
                await RefreshAsync();
            }
        }

        private void RaiseCommands()
        {
            foreach (var c in new[] { RefreshCommand, ApplyMaxCommand, ApplyCurrentCommand, RemoveMaxCommand, ResolveVbiosCommand, ResolveVbiosFromFileCommand })
                (c as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }

        private static void Post(Action a)
        {
            var d = Application.Current?.Dispatcher;
            if (d == null || d.CheckAccess()) a(); else d.BeginInvoke(a);
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

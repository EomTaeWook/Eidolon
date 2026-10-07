using System.Collections.ObjectModel;
using Dignus.Log;
using Eidolon.App.Services;
using Eidolon.App.Localization;
using Eidolon.App.Presenters;
using Eidolon.Core.Domain;
using Eidolon.Core.Infrastructure;

namespace Eidolon.App.ViewModels
{
    public class StudioSession : ObservableObject, IDisposable
    {
        private readonly SettingsStore _settingsStore;
        private readonly StringHelper _strings;
        private readonly StudioWorkPresenter _work;
        private readonly ComfyEngine _engine;
        private readonly string _dataDirectory;
        private readonly CancellationTokenSource _appLifetime = new CancellationTokenSource();
        private DesktopSettings _settings = new DesktopSettings();
        private bool _maintenanceWork;
        private bool _isBusy;
        private bool _initialized;
        private bool _closing;
        private string _status = string.Empty;
        private string _error = string.Empty;
        private double _percent;
        private bool _indeterminate;
        public ObservableCollection<QueuedWorkItem> PendingRequests { get; private set; } = new ObservableCollection<QueuedWorkItem>();
        public AsyncCommand ClearPendingCommand { get; private set; }
        public DesktopSettings SettingsDraft { get; private set; } = new DesktopSettings();

        public bool IsBusy
        {
            get
            {
                return _isBusy;
            }
            private set
            {
                if (Set(ref _isBusy, value) == true)
                {
                    Raise(nameof(IsIdle));
                    RefreshCommands();
                }
            }
        }

        public bool IsIdle
        {
            get
            {
                return _isBusy == false && _work.IsIdle == true && _initialized == true && _closing == false;
            }
        }

        public bool CanQueue
        {
            get
            {
                return _initialized == true && _closing == false && _maintenanceWork == false && _work.HasScheduledWork == false;
            }
        }

        public bool CanEditGenerationInputs
        {
            get
            {
                return _initialized == true && _closing == false;
            }
        }

        public bool HasPendingRequests
        {
            get
            {
                return PendingRequests.Count > 0;
            }
        }

        public string QueueSummary
        {
            get
            {
                return _strings.Format("EidolonText245", PendingRequests.Count);
            }
        }

        public string ActiveRequestTitle
        {
            get
            {
                return _work.CurrentTitle;
            }
        }

        public string Status
        {
            get
            {
                return _status;
            }
            internal set
            {
                if (Set(ref _status, value) == true)
                {
                    LogHelper.Info(value);
                }
            }
        }

        public string Error
        {
            get
            {
                return _error;
            }
            internal set
            {
                Set(ref _error, value);
                Raise(nameof(HasError));
            }
        }

        public bool HasError
        {
            get
            {
                return string.IsNullOrEmpty(_error) == false;
            }
        }

        public double Percent
        {
            get
            {
                return _percent;
            }
            internal set
            {
                Set(ref _percent, value);
            }
        }

        public bool Indeterminate
        {
            get
            {
                return _indeterminate;
            }
            internal set
            {
                Set(ref _indeterminate, value);
            }
        }

        public bool HasLocalModelRuntime
        {
            get
            {
                if (Path.IsPathFullyQualified(_settings.InstallDirectory) == false)
                {
                    return false;
                }
                RuntimeLayout layout = new RuntimeLayout(_settings.InstallDirectory);
                return File.Exists(layout.ComfyPython) == true && File.Exists(Path.Combine(layout.ComfyDirectory, "main.py")) == true;
            }
        }

        internal Task WorkAsync(Func<CancellationToken, Task> action)
        {
            if (IsIdle == false)
            {
                return Task.CompletedTask;
            }
            _maintenanceWork = true;
            Raise(nameof(CanQueue));
            _work.Enqueue(_strings.GetString("EidolonText227"), token => ExecuteMaintenanceAsync(action, token));
            return Task.CompletedTask;
        }

        internal async Task ExecuteMaintenanceAsync(Func<CancellationToken, Task> action, CancellationToken token)
        {
            _maintenanceWork = true;
            Raise(nameof(CanQueue));
            RefreshCommands();
            try
            {
                await action(token);
            }
            finally
            {
                _maintenanceWork = false;
                Raise(nameof(CanQueue));
                RefreshCommands();
            }
        }

        private void RefreshQueue()
        {
            if (_closing == true)
            {
                return;
            }
            PendingRequests.Clear();
            foreach (string title in _work.GetPendingTitles())
            {
                PendingRequests.Add(new QueuedWorkItem(title));
            }
            Raise(nameof(HasPendingRequests));
            Raise(nameof(QueueSummary));
            Raise(nameof(ActiveRequestTitle));
            Raise(nameof(IsIdle));
            Raise(nameof(CanQueue));

            RefreshCommands();
        }

        private Task ClearPendingAsync()
        {
            _work.ClearPending();
            return Task.CompletedTask;
        }

        private void OnWorkStarted()
        {
            IsBusy = true;
            Error = string.Empty;
            Percent = 0;
            Indeterminate = true;
            Status = _strings.GetString("EidolonText227");

        }

        private void OnWorkFinished(StudioWorkState state, Exception failure)
        {
            if (state == StudioWorkState.Cancelled)
            {
                Status = _strings.GetString("EidolonText228");
            }
            if (state == StudioWorkState.Failed)
            {
                Status = _strings.GetString("EidolonText229");
                Error = _strings.GetExceptionMessage(failure);
            }
            IsBusy = false;
            Indeterminate = false;
            RefreshAvailability();
        }

        internal DesktopSettings CreateActiveSettings(DesktopSettings settings)
        {
            DesktopSettings active = settings.Copy();
            EngineConnection connection = _engine.Connection;
            if (connection != null && connection.UsesLocalAssets == false &&
                connection.ServerAddress == ComfyServerAddress.Root(settings))
            {
                active.UseServerAssets = true;
                active.ServerAddress = connection.ServerAddress;
            }
            return active;
        }

        public event Action AvailabilityChanged;

        public StudioSession(SettingsStore settingsStore, ComfyEngine engine, StudioWorkPresenter work,
            StringHelper strings, string dataDirectory)
        {
            _settingsStore = settingsStore;
            _engine = engine;
            _work = work;
            _strings = strings;
            _dataDirectory = dataDirectory;
            Status = _strings.GetString("EidolonText214");
            _work.QueueChanged += RefreshQueue;
            _work.Started += OnWorkStarted;
            _work.ProgressChanged += OnWorkProgress;
            _work.Finished += OnWorkFinished;
            ClearPendingCommand = new AsyncCommand(ClearPendingAsync,
                () => CanQueue == true && HasPendingRequests == true, ReportError);
        }

        public DesktopSettings Settings
        {
            get
            {
                return _settings;
            }
            private set
            {
                Set(ref _settings, value);
            }
        }
        public CancellationToken Lifetime
        {
            get
            {
                return _appLifetime.Token;
            }
        }
        public string DataDirectory
        {
            get
            {
                return _dataDirectory;
            }
        }
        public bool IsClosing
        {
            get
            {
                return _closing;
            }
        }
        public bool IsInitialized
        {
            get
            {
                return _initialized;
            }
            internal set
            {
                _initialized = value;
                RefreshAvailability();
            }
        }
        public bool MaintenanceWork
        {
            get
            {
                return _maintenanceWork;
            }
            internal set
            {
                _maintenanceWork = value;
                RefreshAvailability();
            }
        }
        public bool IsEngineConnected
        {
            get
            {
                return _engine.Connection != null;
            }
        }

        internal void LoadSettings()
        {
            Settings = _settingsStore.Load();
            SettingsDraft = Settings.Copy();
            Raise(nameof(SettingsDraft));
        }

        internal void SaveSettings(DesktopSettings settings)
        {
            _settingsStore.Save(settings);
            Settings = settings;
            RefreshAvailability();
        }

        internal void ReportError(Exception error)
        {
            LogHelper.Error(error);
            Error = _strings.GetExceptionMessage(error);
        }

        internal void RefreshAvailability()
        {
            Raise(nameof(IsIdle));
            Raise(nameof(CanQueue));
            Raise(nameof(CanEditGenerationInputs));
            Raise(nameof(IsEngineConnected));
            ClearPendingCommand.Refresh();
            AvailabilityChanged?.Invoke();
        }

        private void RefreshCommands()
        {
            RefreshAvailability();
        }

        private void OnWorkProgress(WorkProgress progress)
        {
            Status = _strings.Format(progress.Code, progress.Arguments);
            Percent = progress.Percent;
            Indeterminate = progress.IsIndeterminate;
        }

        internal void Localize()
        {
            Status = _strings.TranslateMessage(Status);
            Raise(nameof(QueueSummary));
        }

        internal void BeginClosing()
        {
            _closing = true;
            _appLifetime.Cancel();
            RefreshAvailability();
        }

        public void Dispose()
        {
            _work.QueueChanged -= RefreshQueue;
            _work.Started -= OnWorkStarted;
            _work.ProgressChanged -= OnWorkProgress;
            _work.Finished -= OnWorkFinished;
            _appLifetime.Dispose();
        }
    }
}

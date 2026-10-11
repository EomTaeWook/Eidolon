using System.ComponentModel;
using Avalonia.Threading;
using Dignus.Log;
using Eidolon.App.Localization;
using Eidolon.App.Presenters;
using Eidolon.App.Services;

namespace Eidolon.App.ViewModels
{
    public class AppUpdateViewModel : StudioPanelViewModel
    {
        private readonly ApplicationUpdateService _updates;
        private readonly ApplicationUpdateInstaller _installer;
        private readonly ApplicationUpdateRestartPresenter _restart;
        private ApplicationUpdateCheck _check;
        private ApplicationUpdateInstallPlan _plan;
        private VerifiedApplicationUpdate _download;
        private CancellationTokenSource _requestLifetime;
        private Task _checking = Task.CompletedTask;
        private Exception _failure;
        private bool _busy;
        private bool _installing;
        private bool _executingInstall;
        private bool _readyToRestart;
        private bool _updateRestart;
        private string _statusName = "EidolonText672";
        private double _percentage;

        public AppUpdateViewModel(StudioSession session, StudioNavigationViewModel navigation, StudioWorkPresenter work,
            StringHelper strings, DesktopDialogs dialogs, ApplicationUpdateService updates,
            ApplicationUpdateInstaller installer, ApplicationUpdateRestartPresenter restart)
            : base(session, navigation, work, strings, dialogs)
        {
            _updates = updates;
            _installer = installer;
            _restart = restart;
            CheckCommand = Command(CheckAsync, CanCheck);
            InstallCommand = Command(QueueInstallAsync, CanInstall);
            CancelCommand = Command(CancelAsync, CanCancel);
            OpenReleaseCommand = Command(OpenReleaseAsync, () => Session.IsClosing == false);
            _work.Finished += OnWorkFinished;
            _work.QueueChanged += OnQueueChanged;
        }

        public AsyncCommand CheckCommand { get; private set; }
        public AsyncCommand InstallCommand { get; private set; }
        public AsyncCommand CancelCommand { get; private set; }
        public AsyncCommand OpenReleaseCommand { get; private set; }

        public string CurrentVersionCaption
        {
            get
            {
                if (ApplicationReleaseVersion.TryParse(_updates.CurrentVersion, out ApplicationReleaseVersion version) == true)
                {
                    return _strings.Format("EidolonText667", version.Text);
                }
                return _strings.Format("EidolonText667", _updates.CurrentVersion);
            }
        }

        public bool IsBusy
        {
            get
            {
                return _busy;
            }
        }

        public bool IsIndeterminate
        {
            get
            {
                return _statusName != "EidolonText695";
            }
        }

        public double Percentage
        {
            get
            {
                return _percentage;
            }
        }

        public bool HasUpdate
        {
            get
            {
                if (_check == null)
                {
                    return false;
                }
                return _check.HasUpdate;
            }
        }

        public string StatusCaption
        {
            get
            {
                if (_failure != null)
                {
                    return _strings.GetExceptionMessage(_failure);
                }
                if (_statusName == "EidolonText675")
                {
                    return _strings.Format(_statusName, _check.Release.Version);
                }
                if (_statusName == "EidolonText695")
                {
                    return _strings.Format(_statusName, _percentage);
                }
                return _strings.GetString(_statusName);
            }
        }

        public string InstallHint
        {
            get
            {
                if (HasUpdate == false)
                {
                    return string.Empty;
                }
                if (_check.InstallBlockedCode != ApplicationUpdateMessageCode.None)
                {
                    return _strings.Format(_check.InstallBlockedCode);
                }
                if (_busy == false)
                {
                    if (_work.HasScheduledWork == true)
                    {
                        return _strings.Format(ApplicationUpdateMessageCode.WorkPending);
                    }
                    if (Session.IsIdle == false)
                    {
                        return _strings.Format(ApplicationUpdateMessageCode.WorkPending);
                    }
                }
                return string.Empty;
            }
        }

        private bool CanCheck()
        {
            if (_busy == true)
            {
                return false;
            }
            if (Session.IsClosing == true)
            {
                return false;
            }
            return Session.IsInitialized;
        }

        private bool CanInstall()
        {
            if (CanCheck() == false)
            {
                return false;
            }
            if (_check == null)
            {
                return false;
            }
            if (_check.CanInstall == false)
            {
                return false;
            }
            if (_work.HasScheduledWork == true)
            {
                return false;
            }
            return Session.IsIdle;
        }

        private bool CanCancel()
        {
            if (_busy == false)
            {
                return false;
            }
            if (_readyToRestart == true)
            {
                return false;
            }
            return Session.IsClosing == false;
        }

        private Task CheckAsync()
        {
            _checking = CheckCoreAsync();
            return _checking;
        }

        private async Task CheckCoreAsync()
        {
            _busy = true;
            _failure = null;
            _statusName = "EidolonText671";
            _requestLifetime = CancellationTokenSource.CreateLinkedTokenSource(Session.Lifetime);
            RefreshState();
            try
            {
                _check = await _updates.CheckAsync(_requestLifetime.Token);
                _statusName = "EidolonText673";
                if (_check.HasStableRelease == false)
                {
                    _statusName = "EidolonText674";
                }
                if (_check.HasUpdate == true)
                {
                    _statusName = "EidolonText675";
                }
            }
            catch (OperationCanceledException) when (_requestLifetime.IsCancellationRequested == true)
            {
                _statusName = "EidolonText698";
            }
            catch (Exception error)
            {
                LogHelper.Error(error);
                _failure = error;
                _check = null;
            }
            finally
            {
                _busy = false;
                _requestLifetime.Dispose();
                _requestLifetime = null;
                RefreshState();
            }
        }

        private Task QueueInstallAsync()
        {
            _busy = true;
            _installing = true;
            _failure = null;
            _percentage = 0;
            _statusName = "EidolonText695";
            _requestLifetime = CancellationTokenSource.CreateLinkedTokenSource(Session.Lifetime);
            Session.MaintenanceWork = true;
            RefreshState();
            try
            {
                _work.Enqueue(_strings.GetString("EidolonText669"), token => Session.ExecuteMaintenanceAsync(PrepareInstallAsync, token), OnDiscarded);
            }
            catch
            {
                _busy = false;
                _installing = false;
                Session.MaintenanceWork = false;
                _requestLifetime.Dispose();
                _requestLifetime = null;
                RefreshState();
                throw;
            }
            return Task.CompletedTask;
        }

        private async Task PrepareInstallAsync(CancellationToken token)
        {
            _executingInstall = true;
            using CancellationTokenSource lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _requestLifetime.Token);
            try
            {
                if (lifetime.IsCancellationRequested == true)
                {
                    _statusName = "EidolonText698";
                    ReportProgress(ApplicationUpdateMessageCode.Cancelled, 0, false);
                    return;
                }
                IProgress<ApplicationUpdateProgress> progress = new Progress<ApplicationUpdateProgress>(value =>
                {
                    if (Session.IsClosing == true)
                    {
                        return;
                    }
                    if (lifetime.IsCancellationRequested == true)
                    {
                        return;
                    }
                    if (_executingInstall == false)
                    {
                        return;
                    }
                    if (_statusName != "EidolonText695")
                    {
                        return;
                    }
                    _percentage = value.Percentage;
                    ReportProgress(ApplicationUpdateMessageCode.Downloading, value.Percentage, false, value.Percentage);
                    RefreshState();
                });
                _download = await _updates.DownloadAsync(_check.Release, progress, lifetime.Token);
                _statusName = "EidolonText696";
                ReportProgress(ApplicationUpdateMessageCode.Preparing);
                RefreshState();
                _plan = await _installer.PrepareAsync(_download, lifetime.Token);
                await _installer.StartHelperAsync(_plan, lifetime.Token);
                lifetime.Token.ThrowIfCancellationRequested();
                _readyToRestart = true;
                _statusName = "EidolonText697";
                ReportProgress(ApplicationUpdateMessageCode.ReadyToRestart, 100, false);
            }
            catch
            {
                await DiscardPreparedAsync();
                throw;
            }
            finally
            {
                _requestLifetime.Dispose();
                _requestLifetime = null;
            }
        }

        private void ReportProgress(ApplicationUpdateMessageCode code, double percent = 0, bool isIndeterminate = true, params object[] arguments)
        {
            if (Session.IsClosing == true)
            {
                return;
            }
            Session.Status = _strings.Format(code, arguments);
            Session.Percent = percent;
            Session.Indeterminate = isIndeterminate;
        }

        private void OnWorkFinished(StudioWorkState state, Exception failure)
        {
            if (_executingInstall == false)
            {
                return;
            }
            _executingInstall = false;
            if (state == StudioWorkState.Cancelled)
            {
                _statusName = "EidolonText698";
            }
            if (state == StudioWorkState.Failed)
            {
                _failure = failure;
            }
            if (_statusName == "EidolonText698")
            {
                Session.Status = _strings.Format(ApplicationUpdateMessageCode.Cancelled);
                Session.Error = string.Empty;
            }
            if (_readyToRestart == false)
            {
                _busy = false;
                _installing = false;
            }
            else
            {
                Session.MaintenanceWork = true;
            }
            RefreshState();
        }

        private async void OnQueueChanged()
        {
            if (_readyToRestart == false)
            {
                return;
            }
            if (Session.IsClosing == true)
            {
                return;
            }
            if (Session.IsIdle == false)
            {
                return;
            }
            try
            {
                _restart.RequestRestart(_plan);
                _plan = null;
                _download = null;
                _readyToRestart = false;
            }
            catch (Exception error)
            {
                LogHelper.Error(error);
                _failure = error;
                await DiscardPreparedAsync();
                _busy = false;
                _installing = false;
                Session.MaintenanceWork = false;
                RefreshState();
            }
        }

        private void OnDiscarded()
        {
            Dispatcher.UIThread.Post(() =>
            {
                _busy = false;
                _installing = false;
                _statusName = "EidolonText698";
                _requestLifetime?.Dispose();
                _requestLifetime = null;
                Session.MaintenanceWork = false;
                RefreshState();
            });
        }

        private async Task CancelAsync()
        {
            if (_installing == true)
            {
                await _work.CancelCurrentAsync();
            }
            if (_requestLifetime == null)
            {
                return;
            }
            await _requestLifetime.CancelAsync();
        }

        private Task OpenReleaseAsync()
        {
            string address = "https://github.com/EomTaeWook/Eidolon/releases";
            if (_check != null)
            {
                if (_check.HasStableRelease == true)
                {
                    address = _check.Release.ReleasePage.AbsoluteUri;
                }
            }
            if (_failure is ApplicationUpdateException failure)
            {
                if (failure.ReleasePage != null)
                {
                    address = failure.ReleasePage.AbsoluteUri;
                }
            }
            return _dialogs.OpenLinkAsync(address);
        }

        internal void ShowStartupFailure(string[] args)
        {
            if (args == null)
            {
                return;
            }
            if (args.Length < 3)
            {
                return;
            }
            if (args[0] != "--eidolon-update-cleanup")
            {
                return;
            }
            _updateRestart = true;
            if (args.Length != 4)
            {
                return;
            }
            if (Enum.TryParse(args[3], out ApplicationUpdateMessageCode code) == true)
            {
                if (code == ApplicationUpdateMessageCode.ReplaceFailed)
                {
                    _failure = new ApplicationUpdateException(code);
                }
                if (code == ApplicationUpdateMessageCode.LaunchFailed)
                {
                    _failure = new ApplicationUpdateException(code);
                }
            }
        }

        internal void OpenAfterRestart()
        {
            if (_updateRestart == false)
            {
                return;
            }
            Navigation.SelectedSettingsTab = 0;
            Navigation.SelectedTab = 4;
            if (_failure != null)
            {
                Session.ReportError(_failure);
            }
        }

        internal async Task CloseAsync()
        {
            _requestLifetime?.Cancel();
            await _checking;
        }

        internal async Task DiscardPreparedAsync()
        {
            ApplicationUpdateInstallPlan plan = _plan;
            VerifiedApplicationUpdate download = _download;
            _plan = null;
            _download = null;
            _readyToRestart = false;
            try
            {
                await _installer.CancelAsync(plan);
            }
            catch (Exception error)
            {
                LogHelper.Error(error);
            }
            try
            {
                if (download != null)
                {
                    await _updates.DiscardAsync(download);
                }
            }
            catch (Exception error)
            {
                LogHelper.Error(error);
            }
        }

        protected override void OnSessionPropertyChanged(object sender, PropertyChangedEventArgs args)
        {
            Raise(nameof(InstallHint));
        }

        private void RefreshState()
        {
            if (Session.IsClosing == true)
            {
                return;
            }
            Raise(nameof(IsBusy));
            Raise(nameof(IsIndeterminate));
            Raise(nameof(Percentage));
            Raise(nameof(HasUpdate));
            Raise(nameof(StatusCaption));
            Raise(nameof(InstallHint));
            RefreshCommands();
        }

        public override void Localize()
        {
            Raise(nameof(CurrentVersionCaption));
            RefreshState();
            base.Localize();
        }

        public override void Dispose()
        {
            _work.Finished -= OnWorkFinished;
            _work.QueueChanged -= OnQueueChanged;
            base.Dispose();
        }
    }
}

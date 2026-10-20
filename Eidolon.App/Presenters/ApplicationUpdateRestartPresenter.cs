using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Dignus.DependencyInjection.Attributes;
using Dignus.Log;
using Eidolon.App.Services;

namespace Eidolon.App.Presenters
{
    [Injectable(Dignus.DependencyInjection.LifeScope.Singleton)]
    public class ApplicationUpdateRestartPresenter
    {
        private readonly ApplicationUpdateInstaller _installer;
        private readonly IClassicDesktopStyleApplicationLifetime _desktop;
        private ApplicationUpdateInstallPlan _plan;
        private bool _windowClosed;
        private MainWindow _window;

        public ApplicationUpdateRestartPresenter(ApplicationUpdateInstaller installer, IClassicDesktopStyleApplicationLifetime desktop)
        {
            _installer = installer;
            _desktop = desktop;
        }

        public void RequestRestart(ApplicationUpdateInstallPlan plan)
        {
            if (_desktop.MainWindow is not MainWindow window)
            {
                throw new ApplicationUpdateException(ApplicationUpdateMessageCode.CloseUnavailable);
            }
            if (_plan != null)
            {
                throw new ApplicationUpdateException(ApplicationUpdateMessageCode.PrepareFailed);
            }
            _installer.EnsureHelperReady(plan);
            _plan = plan;
            _window = window;
            _window.Closed += OnWindowClosed;
            Dispatcher.UIThread.Post(() => _window.Close());
        }

        private void OnWindowClosed(object sender, EventArgs args)
        {
            _window.Closed -= OnWindowClosed;
            _windowClosed = _window.ShutdownCompleted;
        }

        public async Task CompleteShutdownAsync()
        {
            if (_plan == null)
            {
                return;
            }
            try
            {
                if (_windowClosed == false)
                {
                    await _installer.CancelAsync(_plan).ConfigureAwait(false);
                    return;
                }
                await _installer.AuthorizeAfterShutdownAsync(_plan).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                LogHelper.Error(error);
                await _installer.CancelAsync(_plan).ConfigureAwait(false);
            }
        }
    }
}

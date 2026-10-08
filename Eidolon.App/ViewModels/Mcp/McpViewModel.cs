using Avalonia.Threading;
using Dignus.Log;
using Eidolon.App.Localization;
using Eidolon.App.Mcp;
using Eidolon.App.Presenters;
using Eidolon.App.Services;
using Eidolon.Mcp;

namespace Eidolon.App.ViewModels
{
    public class McpViewModel : StudioPanelViewModel
    {
        private readonly McpHttpServer _server;

        public McpViewModel(StudioSession session, StudioNavigationViewModel navigation, StudioWorkPresenter work,
            StringHelper strings, DesktopDialogs dialogs, McpService tools) : base(session, navigation, work, strings, dialogs)
        {
            _server = new McpHttpServer(new McpServerOptions
            {
                Instructions = "Eidolon queues local image generation and style edits. Image tools return acceptance, not completion. Poll status and read result paths. Instruction overrides apply only to their request."
            }, tools.CreateTools(), error => LogHelper.Error(error));
            _server.StateChanged += OnServerStateChanged;
            StartCommand = Command(StartAsync, () => Session.IsInitialized == true && Session.IsClosing == false && IsRunning == false);
            StopCommand = Command(StopAsync, () => Session.IsClosing == false && IsRunning == true);
        }

        public AsyncCommand StartCommand { get; private set; }
        public AsyncCommand StopCommand { get; private set; }

        public string Endpoint
        {
            get
            {
                return _server.Endpoint.AbsoluteUri;
            }
        }

        public bool IsRunning
        {
            get
            {
                return _server.IsRunning;
            }
        }

        public string StatusCaption
        {
            get
            {
                if (IsRunning == true)
                {
                    return _strings.GetString("EidolonText598");
                }
                return _strings.GetString("EidolonText599");
            }
        }

        private async Task StartAsync()
        {
            try
            {
                await _server.StartAsync(Session.Lifetime);
            }
            catch (System.Net.HttpListenerException error) when (error.NativeErrorCode == 5)
            {
                LogHelper.Error(error);
                throw new InvalidOperationException(_strings.GetString("EidolonText602"), error);
            }
            RefreshState();
        }

        private async Task StopAsync()
        {
            await _server.StopAsync();
            RefreshState();
        }

        private void OnServerStateChanged()
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (Session.IsClosing == false)
                {
                    RefreshState();
                }
            });
        }

        private void RefreshState()
        {
            Raise(nameof(IsRunning));
            Raise(nameof(StatusCaption));
            RefreshCommands();
        }

        public override void Localize()
        {
            RefreshState();
            base.Localize();
        }

        internal async Task CloseAsync()
        {
            _server.StateChanged -= OnServerStateChanged;
            await _server.DisposeAsync();
        }
    }
}

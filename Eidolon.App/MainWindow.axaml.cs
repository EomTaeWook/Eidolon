using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Eidolon.App.Localization;
using Eidolon.App.ViewModels;

namespace Eidolon.App
{
    public partial class MainWindow : Window
    {
        private readonly StudioViewModel _viewModel;
        private readonly StringHelper _strings;
        private bool _closeRequested;
        private bool _canClose;

        public MainWindow(StudioViewModel viewModel, StringHelper strings)
        {
            _strings = strings;
            _viewModel = viewModel;
            AvaloniaXamlLoader.Load(this);
            DataContext = _viewModel;
            Opened += OnOpened;
            Closing += OnClosing;
        }

        private async void OnOpened(object sender, EventArgs arguments)
        {
            await _viewModel.InitializeAsync();
        }

        private async void OnClosing(object sender, WindowClosingEventArgs arguments)
        {
            if (_canClose == true)
            {
                return;
            }
            arguments.Cancel = true;
            if (_closeRequested == true)
            {
                return;
            }
            _closeRequested = true;
            IsEnabled = false;
            Title = _strings.GetString("EidolonText201");
            try
            {
                await _viewModel.DisposeAsync();
            }
            finally
            {
                _canClose = true;
                Close();
            }
        }
    }
}

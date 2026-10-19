using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Eidolon.App.Localization;
using Eidolon.App.Models.Windows;
using Eidolon.App.Services.Windows;
using Eidolon.App.ViewModels;
using System.Runtime.InteropServices;

namespace Eidolon.App
{
    public partial class MainWindow : Window
    {
        private readonly StudioViewModel _viewModel;
        private readonly StringHelper _strings;
        private bool _closeRequested;
        private bool _canClose;
        private bool _shutdownCompleted;

        internal bool ShutdownCompleted
        {
            get
            {
                return _shutdownCompleted;
            }
        }

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
            SetWindowsTaskbarIdentity(typeof(App).Assembly.GetName().Name);
            await _viewModel.InitializeAsync();
        }

        private void SetWindowsTaskbarIdentity(string applicationUserModelId)
        {
            if (OperatingSystem.IsWindows() == false)
            {
                return;
            }
            Guid propertyStoreInterfaceId = typeof(IWindowsPropertyStore).GUID;
            SHGetPropertyStoreForWindow(TryGetPlatformHandle().Handle, ref propertyStoreInterfaceId, out IWindowsPropertyStore propertyStore);
            WindowsPropertyKey propertyKey = new WindowsPropertyKey { FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), PropertyId = 5 };
            WindowsPropertyValue propertyValue = new WindowsPropertyValue();
            try
            {
                if (string.IsNullOrEmpty(applicationUserModelId) == false)
                {
                    propertyValue.ValueType = 31;
                    propertyValue.StringPointer = Marshal.StringToCoTaskMemUni(applicationUserModelId);
                }
                Marshal.ThrowExceptionForHR(propertyStore.SetValue(ref propertyKey, ref propertyValue));
            }
            finally
            {
                PropVariantClear(ref propertyValue);
            }
        }

        [DllImport("shell32.dll", ExactSpelling = true, PreserveSig = false)]
        private static extern void SHGetPropertyStoreForWindow(IntPtr windowHandle, ref Guid propertyStoreInterfaceId, out IWindowsPropertyStore propertyStore);

        [DllImport("ole32.dll", ExactSpelling = true)]
        private static extern int PropVariantClear(ref WindowsPropertyValue propertyValue);

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
                _shutdownCompleted = true;
            }
            finally
            {
                try
                {
                    SetWindowsTaskbarIdentity(string.Empty);
                }
                finally
                {
                    _canClose = true;
                    Close();
                }
            }
        }
    }
}

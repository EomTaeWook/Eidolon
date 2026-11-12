namespace Eidolon.App.ViewModels
{
    public class StudioNavigationViewModel : ObservableObject
    {
        private int _selectedTab;
        private int _selectedEnvironmentTab;
        private int _selectedSettingsTab;
        private int _selectedEngineToolTab;
        public AsyncCommand ShowEditingCommand { get; private set; }
        public AsyncCommand ShowGenerationCommand { get; private set; }
        public AsyncCommand ShowResultsCommand { get; private set; }
        public AsyncCommand ShowTrainingCommand { get; private set; }
        public AsyncCommand ShowGenerationEnvironmentCommand { get; private set; }
        public AsyncCommand ShowEngineSettingsCommand { get; private set; }
        public AsyncCommand ShowBlenderSettingsCommand
        {
            get;
            private set;
        }
        public AsyncCommand ShowEngineCommand { get; private set; }
        public AsyncCommand ShowModelsCommand { get; private set; }
        public AsyncCommand ShowSettingsCommand { get; private set; }
        public AsyncCommand ShowHelpCommand { get; private set; }
        public AsyncCommand ShowAssetCreationCommand { get; private set; }
        public AsyncCommand ShowModelingCommand
        {
            get;
            private set;
        }

        public int SelectedTab
        {
            get
            {
                return _selectedTab;
            }
            set
            {
                if (Set(ref _selectedTab, value) == true)
                {
                    Raise(nameof(IsGenerationView));
                    Raise(nameof(IsEditingView));
                    Raise(nameof(IsGenerationWorkspace));
                    Raise(nameof(IsResultsView));
                    Raise(nameof(IsTrainingView));
                    Raise(nameof(IsGenerationEnvironmentView));
                    Raise(nameof(IsSettingsView));
                    Raise(nameof(IsHelpView));
                    Raise(nameof(IsAssetCreationView));
                    Raise(nameof(IsModelingView));
                }
            }
        }

        public bool IsGenerationView
        {
            get
            {
                return SelectedTab == 0;
            }
        }

        public bool IsResultsView
        {
            get
            {
                return SelectedTab == 6;
            }
        }

        public bool IsEditingView
        {
            get
            {
                return SelectedTab == 1;
            }
        }

        public bool IsGenerationWorkspace
        {
            get
            {
                return IsGenerationView == true || IsEditingView == true;
            }
        }

        public int SelectedEnvironmentTab
        {
            get
            {
                return _selectedEnvironmentTab;
            }
            set
            {
                Set(ref _selectedEnvironmentTab, value);
            }
        }

        public int SelectedSettingsTab
        {
            get
            {
                return _selectedSettingsTab;
            }
            set
            {
                Set(ref _selectedSettingsTab, value);
            }
        }

        public int SelectedEngineToolTab
        {
            get
            {
                return _selectedEngineToolTab;
            }
            set
            {
                Set(ref _selectedEngineToolTab, value);
            }
        }

        public bool IsTrainingView
        {
            get
            {
                return SelectedTab == 2;
            }
        }

        public bool IsGenerationEnvironmentView
        {
            get
            {
                return SelectedTab == 3;
            }
        }

        public bool IsSettingsView
        {
            get
            {
                return SelectedTab == 4;
            }
        }

        public bool IsHelpView
        {
            get
            {
                return SelectedTab == 5;
            }
        }

        public bool IsAssetCreationView
        {
            get
            {
                return SelectedTab == 7;
            }
        }

        public bool IsModelingView
        {
            get
            {
                return SelectedTab == 8;
            }
        }

        private readonly Func<int, Task> _navigate;

        public StudioNavigationViewModel(Func<int, Task> navigate, Action<Exception> onError)
        {
            _navigate = navigate;
            ShowGenerationCommand = new AsyncCommand(() => NavigateAsync(0), () => true, onError);
            ShowEditingCommand = new AsyncCommand(() => NavigateAsync(1), () => true, onError);
            ShowResultsCommand = new AsyncCommand(() => NavigateAsync(6), () => true, onError);
            ShowTrainingCommand = new AsyncCommand(() => NavigateAsync(2), () => true, onError);
            ShowGenerationEnvironmentCommand = new AsyncCommand(() => NavigateAsync(3), () => true, onError);
            ShowEngineSettingsCommand = new AsyncCommand(OpenEngineSettingsAsync, () => true, onError);
            ShowBlenderSettingsCommand = new AsyncCommand(() => OpenEngineSettingsAsync(1), () => true, onError);
            ShowEngineCommand = ShowEngineSettingsCommand;
            ShowModelsCommand = new AsyncCommand(OpenModelsAsync, () => true, onError);
            ShowSettingsCommand = new AsyncCommand(() => NavigateAsync(4), () => true, onError);
            ShowHelpCommand = new AsyncCommand(() => NavigateAsync(5), () => true, onError);
            ShowAssetCreationCommand = new AsyncCommand(OpenAssetCreationAsync, () => true, onError);
            ShowModelingCommand = new AsyncCommand(() => NavigateAsync(8), () => true, onError);
        }

        private Task NavigateAsync(int tab)
        {
            return _navigate(tab);
        }

        internal Task OpenEngineSettingsAsync()
        {
            return OpenEngineSettingsAsync(0);
        }

        private Task OpenEngineSettingsAsync(int engineToolTab)
        {
            SelectedEngineToolTab = engineToolTab;
            SelectedSettingsTab = 1;
            return NavigateAsync(4);
        }

        internal Task OpenModelsAsync()
        {
            SelectedEnvironmentTab = 2;
            return NavigateAsync(3);
        }

        internal Task OpenAssetCreationAsync()
        {
            return NavigateAsync(7);
        }

        internal Task OpenTrainingDataAsync()
        {
            return NavigateAsync(2);
        }
    }
}

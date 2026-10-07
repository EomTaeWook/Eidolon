namespace Eidolon.App.ViewModels
{
    public class StudioNavigationViewModel : ObservableObject
    {
        private int _selectedTab;
        private int _selectedEngineTab;
        public AsyncCommand ShowEditingCommand { get; private set; }
        public AsyncCommand ShowGenerationCommand { get; private set; }
        public AsyncCommand ShowResultsCommand { get; private set; }
        public AsyncCommand ShowTrainingCommand { get; private set; }
        public AsyncCommand ShowEngineCommand { get; private set; }
        public AsyncCommand ShowSettingsCommand { get; private set; }
        public AsyncCommand ShowHelpCommand { get; private set; }

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
                    Raise(nameof(IsEngineView));
                    Raise(nameof(IsSettingsView));
                    Raise(nameof(IsHelpView));
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

        public int SelectedEngineTab
        {
            get
            {
                return _selectedEngineTab;
            }
            set
            {
                Set(ref _selectedEngineTab, value);
            }
        }

        public bool IsTrainingView
        {
            get
            {
                return SelectedTab == 2;
            }
        }

        public bool IsEngineView
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

        private readonly Func<int, Task> _navigate;

        public StudioNavigationViewModel(Func<int, Task> navigate, Action<Exception> onError)
        {
            _navigate = navigate;
            ShowGenerationCommand = new AsyncCommand(() => NavigateAsync(0), () => true, onError);
            ShowEditingCommand = new AsyncCommand(() => NavigateAsync(1), () => true, onError);
            ShowResultsCommand = new AsyncCommand(() => NavigateAsync(6), () => true, onError);
            ShowTrainingCommand = new AsyncCommand(() => NavigateAsync(2), () => true, onError);
            ShowEngineCommand = new AsyncCommand(() => NavigateAsync(3), () => true, onError);
            ShowSettingsCommand = new AsyncCommand(() => NavigateAsync(4), () => true, onError);
            ShowHelpCommand = new AsyncCommand(() => NavigateAsync(5), () => true, onError);
        }

        private Task NavigateAsync(int tab)
        {
            return _navigate(tab);
        }
    }
}

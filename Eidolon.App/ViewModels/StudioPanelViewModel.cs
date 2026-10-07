using System.ComponentModel;
using Dignus.Log;
using Eidolon.App.Services;
using Eidolon.App.Localization;
using Eidolon.App.Presenters;

namespace Eidolon.App.ViewModels
{
    public abstract class StudioPanelViewModel : ObservableObject, IDisposable
    {
        protected readonly StudioWorkPresenter _work;
        protected readonly StringHelper _strings;
        protected readonly DesktopDialogs _dialogs;
        protected readonly List<AsyncCommand> _commands = new List<AsyncCommand>();
        public StudioSession Session { get; private set; }
        public StudioNavigationViewModel Navigation { get; private set; }

        protected StudioPanelViewModel(StudioSession session, StudioNavigationViewModel navigation,
            StudioWorkPresenter work, StringHelper strings, DesktopDialogs dialogs)
        {
            Session = session;
            Navigation = navigation;
            _work = work;
            _strings = strings;
            _dialogs = dialogs;
            Session.AvailabilityChanged += RefreshCommands;
            Session.PropertyChanged += OnSessionPropertyChanged;
            Navigation.PropertyChanged += OnNavigationPropertyChanged;
        }

        protected AsyncCommand Command(Func<Task> action, Func<bool> canExecute = null)
        {
            if (canExecute == null)
            {
                canExecute = () => Session.IsIdle;
            }
            AsyncCommand command = new AsyncCommand(action, canExecute, OnCommandError);
            _commands.Add(command);
            return command;
        }

        protected void OnCommandError(Exception error)
        {
            LogHelper.Error(error);
            Session.Error = _strings.GetExceptionMessage(error);
        }

        protected virtual void OnSessionPropertyChanged(object sender, PropertyChangedEventArgs args)
        {
        }

        protected virtual void OnNavigationPropertyChanged(object sender, PropertyChangedEventArgs args)
        {
        }

        protected virtual void RefreshCommands()
        {
            foreach (AsyncCommand command in _commands)
            {
                command.Refresh();
            }
        }

        public virtual void Localize()
        {
            RefreshCommands();
        }

        public virtual void Dispose()
        {
            Session.AvailabilityChanged -= RefreshCommands;
            Session.PropertyChanged -= OnSessionPropertyChanged;
            Navigation.PropertyChanged -= OnNavigationPropertyChanged;
        }
    }
}

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Eidolon.App.ViewModels
{
    public class AsyncCommand : ICommand
    {
        private readonly Func<object, Task> _execute;
        private readonly Func<bool> _canExecute;
        private readonly Action<Exception> _onError;
        private bool _running;

        public event EventHandler CanExecuteChanged;

        public AsyncCommand(Func<Task> execute, Func<bool> canExecute, Action<Exception> onError)
            : this(parameter => execute(), canExecute, onError)
        {
        }

        public AsyncCommand(Func<object, Task> execute, Func<bool> canExecute, Action<Exception> onError)
        {
            _execute = execute;
            _canExecute = canExecute;
            _onError = onError;
        }

        public bool CanExecute(object parameter)
        {
            return _running == false && _canExecute() == true;
        }

        public async void Execute(object parameter)
        {
            if (CanExecute(parameter) == false)
            {
                return;
            }
            _running = true;
            Refresh();
            try
            {
                await _execute(parameter);
            }
            catch (Exception error)
            {
                _onError(error);
            }
            finally
            {
                _running = false;
                Refresh();
            }
        }

        public void Refresh()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

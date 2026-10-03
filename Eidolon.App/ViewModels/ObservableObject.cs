using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Eidolon.App.ViewModels
{
    public class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableObject()
        {
        }

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value) == true)
            {
                return false;
            }
            field = value;
            Raise(propertyName);
            return true;
        }

        protected void Raise([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

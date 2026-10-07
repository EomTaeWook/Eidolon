using System.Globalization;

namespace Eidolon.App.ViewModels
{
    public class TrainingStepItem : ObservableObject
    {
        private string _title;
        private bool _isCurrent;
        private bool _isAvailable;

        public TrainingStepItem(TrainingStep step, string title, AsyncCommand selectCommand)
        {
            Step = step;
            _title = title;
            SelectCommand = selectCommand;
        }

        public TrainingStep Step { get; private set; }
        public AsyncCommand SelectCommand { get; private set; }

        public string Number
        {
            get
            {
                return ((int)Step + 1).ToString(CultureInfo.InvariantCulture);
            }
        }

        public string Title
        {
            get
            {
                return _title;
            }
            internal set
            {
                Set(ref _title, value);
            }
        }

        public bool IsCurrent
        {
            get
            {
                return _isCurrent;
            }
            internal set
            {
                Set(ref _isCurrent, value);
            }
        }

        public bool IsAvailable
        {
            get
            {
                return _isAvailable;
            }
            internal set
            {
                Set(ref _isAvailable, value);
            }
        }
    }
}

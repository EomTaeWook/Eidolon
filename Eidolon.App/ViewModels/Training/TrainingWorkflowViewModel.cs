using Eidolon.App.Localization;

namespace Eidolon.App.ViewModels
{
    public class TrainingWorkflowViewModel : ObservableObject
    {
        private readonly StringHelper _strings;
        private readonly Func<TrainingStep, bool> _canComplete;
        private readonly Func<TrainingStep, string> _hint;
        private readonly Func<bool> _canNavigate;
        private TrainingStep _currentStep;
        private TrainingStep _furthestStep;

        public TrainingWorkflowViewModel(StringHelper strings, Func<TrainingStep, bool> canComplete,
            Func<TrainingStep, string> hint, Func<bool> canNavigate, Action<Exception> onError)
        {
            _strings = strings;
            _canComplete = canComplete;
            _hint = hint;
            _canNavigate = canNavigate;
            PreviousCommand = new AsyncCommand(PreviousAsync,
                () => _canNavigate() == true && IsImagesStep == false, onError);
            NextCommand = new AsyncCommand(NextAsync,
                () => _canNavigate() == true && IsTrainingStep == false && _canComplete(_currentStep) == true, onError);
            foreach (TrainingStep step in Enum.GetValues<TrainingStep>())
            {
                AsyncCommand select = new AsyncCommand(() => SelectAsync(step),
                    () => CanSelect(step) == true, onError);
                Steps.Add(new TrainingStepItem(step, StepTitle(step), select));
            }
            Refresh();
        }

        public List<TrainingStepItem> Steps { get; private set; } = new List<TrainingStepItem>();
        public AsyncCommand PreviousCommand { get; private set; }
        public AsyncCommand NextCommand { get; private set; }

        public bool IsImagesStep
        {
            get
            {
                return _currentStep == TrainingStep.Images;
            }
        }

        public bool IsIdentityStep
        {
            get
            {
                return _currentStep == TrainingStep.Identity;
            }
        }

        public bool IsTrainingStep
        {
            get
            {
                return _currentStep == TrainingStep.Training;
            }
        }

        public string Title
        {
            get
            {
                return StepTitle(_currentStep);
            }
        }

        public string Hint
        {
            get
            {
                return _hint(_currentStep);
            }
        }

        public string PositionCaption
        {
            get
            {
                return _strings.Format("EidolonText488", (int)_currentStep + 1, Steps.Count);
            }
        }

        private string StepTitle(TrainingStep step)
        {
            switch (step)
            {
                case TrainingStep.Images:
                    return _strings.GetString("EidolonText483");
                case TrainingStep.Identity:
                    return _strings.GetString("EidolonText484");
                case TrainingStep.Training:
                    return _strings.GetString("EidolonText487");
                default:
                    throw new ArgumentOutOfRangeException(nameof(step));
            }
        }

        private bool CanSelect(TrainingStep target)
        {
            if (_canNavigate() == false)
            {
                return false;
            }
            if ((int)target <= (int)_currentStep)
            {
                return true;
            }
            if ((int)target > (int)_furthestStep)
            {
                return false;
            }
            foreach (TrainingStep step in Enum.GetValues<TrainingStep>())
            {
                if ((int)step >= (int)target)
                {
                    break;
                }
                if (_canComplete(step) == false)
                {
                    return false;
                }
            }
            return true;
        }

        private Task PreviousAsync()
        {
            _currentStep = (TrainingStep)((int)_currentStep - 1);
            Refresh();
            return Task.CompletedTask;
        }

        private Task NextAsync()
        {
            _currentStep = (TrainingStep)((int)_currentStep + 1);
            if ((int)_currentStep > (int)_furthestStep)
            {
                _furthestStep = _currentStep;
            }
            Refresh();
            return Task.CompletedTask;
        }

        private Task SelectAsync(TrainingStep step)
        {
            _currentStep = step;
            Refresh();
            return Task.CompletedTask;
        }

        internal void ShowImages()
        {
            _currentStep = TrainingStep.Images;
            Refresh();
        }

        internal void Refresh()
        {
            foreach (TrainingStep step in Enum.GetValues<TrainingStep>())
            {
                if ((int)step >= (int)_currentStep)
                {
                    break;
                }
                if (_canComplete(step) == false)
                {
                    _currentStep = step;
                    break;
                }
            }
            foreach (TrainingStepItem item in Steps)
            {
                item.IsCurrent = item.Step == _currentStep;
                item.IsAvailable = CanSelect(item.Step);
                item.SelectCommand.Refresh();
            }
            Raise(nameof(IsImagesStep));
            Raise(nameof(IsIdentityStep));
            Raise(nameof(IsTrainingStep));
            Raise(nameof(Title));
            Raise(nameof(Hint));
            Raise(nameof(PositionCaption));
            PreviousCommand.Refresh();
            NextCommand.Refresh();
        }

        internal void Localize()
        {
            foreach (TrainingStepItem item in Steps)
            {
                item.Title = StepTitle(item.Step);
            }
            Refresh();
        }
    }
}

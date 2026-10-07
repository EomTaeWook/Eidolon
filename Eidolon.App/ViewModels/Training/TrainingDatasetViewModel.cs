using Eidolon.App.Localization;
using Eidolon.App.Presenters;
using Eidolon.App.Services;
using Eidolon.Core.Application;
using Eidolon.Core.Domain;

namespace Eidolon.App.ViewModels
{
    public class TrainingDatasetViewModel : StudioPanelViewModel
    {
        private readonly StudioService _studio;
        private readonly Func<TrainingInput> _snapshotInput;
        private readonly Func<bool> _canPrepare;
        private string _path = string.Empty;
        private int _revision;
        private int _preparedRevision = -1;
        private bool _isPreparing;
        private string _requestTitle = string.Empty;

        public TrainingDatasetViewModel(StudioSession session, StudioNavigationViewModel navigation,
            StudioWorkPresenter work, StringHelper strings, DesktopDialogs dialogs, StudioService studio,
            Func<TrainingInput> snapshotInput, Func<bool> canPrepare) : base(session, navigation, work, strings, dialogs)
        {
            _studio = studio;
            _snapshotInput = snapshotInput;
            _canPrepare = canPrepare;
            CreateCommand = Command(CreateAsync, () => CanPrepare == true);
            OpenCommand = Command(() => _dialogs.OpenFolderAsync(_path),
                () => Session.IsClosing == false && HasPrepared == true);
            _work.QueueChanged += OnQueueChanged;
        }

        public AsyncCommand CreateCommand { get; private set; }
        public AsyncCommand OpenCommand { get; private set; }

        public string Path
        {
            get
            {
                return _path;
            }
        }

        public bool HasPrepared
        {
            get
            {
                return string.IsNullOrEmpty(_path) == false;
            }
        }

        public bool IsCurrent
        {
            get
            {
                return HasPrepared == true && _preparedRevision == _revision;
            }
        }

        public bool IsPreparing
        {
            get
            {
                return _isPreparing;
            }
        }

        public bool CanPrepare
        {
            get
            {
                return Session.CanQueue == true && _isPreparing == false && _canPrepare() == true;
            }
        }

        internal void Invalidate()
        {
            _revision++;
            Raise(nameof(IsCurrent));
            RefreshCommands();
        }

        internal void RefreshInputs()
        {
            RefreshCommands();
        }

        private async Task CreateAsync()
        {
            if (CanPrepare == false)
            {
                return;
            }
            TrainingInput input = _snapshotInput();
            int revision = _revision;
            bool queued = false;
            _isPreparing = true;
            RefreshState();
            try
            {
                string parentDirectory = await _dialogs.PickFolderAsync(_strings.GetString("EidolonText472"));
                if (string.IsNullOrEmpty(parentDirectory) == true || Session.CanQueue == false)
                {
                    return;
                }
                _requestTitle = _strings.GetString("EidolonText471") + " · " + input.Name;
                _work.Enqueue(_requestTitle, async token =>
                {
                    try
                    {
                        string path = await _studio.PrepareTrainingDatasetAsync(input, parentDirectory, _work.Progress, token);
                        _path = path;
                        _preparedRevision = revision;
                        Raise(nameof(Path));
                        Raise(nameof(HasPrepared));
                        Raise(nameof(IsCurrent));
                        Session.Status = _strings.Format("EidolonText473", input.Images.Count);
                    }
                    finally
                    {
                        FinishPreparation();
                    }
                });
                queued = true;
            }
            finally
            {
                if (queued == false)
                {
                    FinishPreparation();
                }
            }
        }

        private void FinishPreparation()
        {
            _isPreparing = false;
            _requestTitle = string.Empty;
            RefreshState();
        }

        private void OnQueueChanged()
        {
            if (_isPreparing == true && string.IsNullOrEmpty(_requestTitle) == false
                && _work.CurrentTitle != _requestTitle
                && _work.GetPendingTitles().Contains(_requestTitle) == false)
            {
                FinishPreparation();
            }
        }

        private void RefreshState()
        {
            Raise(nameof(IsPreparing));
            RefreshCommands();
        }

        protected override void RefreshCommands()
        {
            Raise(nameof(CanPrepare));
            base.RefreshCommands();
        }

        public override void Dispose()
        {
            _work.QueueChanged -= OnQueueChanged;
            base.Dispose();
        }
    }
}

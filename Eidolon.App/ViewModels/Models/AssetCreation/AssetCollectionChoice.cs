using Eidolon.App.Localization;
using Eidolon.Core.Domain;

namespace Eidolon.App.ViewModels
{
    public class AssetCollectionChoice : ObservableObject
    {
        private readonly JobState _state;
        private string _stateCaption = string.Empty;

        public AssetCollectionChoice(AssetCollection collection, StringHelper strings)
        {
            Id = collection.Id;
            Caption = collection.CreatedAtUtc.ToLocalTime().ToString("MM/dd HH:mm") + " · " + collection.Prompt;
            _state = collection.State;
            Localize(strings);
        }

        public string Id { get; private set; }
        public string Caption { get; private set; }

        public string StateCaption
        {
            get
            {
                return _stateCaption;
            }
        }

        internal void Localize(StringHelper strings)
        {
            Set(ref _stateCaption, GetStateCaption(_state, strings), nameof(StateCaption));
        }

        internal static string GetStateCaption(JobState state, StringHelper strings)
        {
            switch (state)
            {
                case JobState.Completed:
                    return strings.GetString("EidolonText210");
                case JobState.Cancelled:
                    return strings.GetString("EidolonText211");
                case JobState.Interrupted:
                    return strings.GetString("EidolonText212");
                case JobState.Running:
                    return strings.GetString("EidolonText625");
                case JobState.Failed:
                    return strings.GetString("EidolonText216");
                default:
                    return strings.GetString("EidolonText624");
            }
        }
    }
}

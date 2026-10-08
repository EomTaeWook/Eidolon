using Eidolon.Core.Domain;

namespace Eidolon.App.ViewModels
{
    public class AssetCollectionChoice
    {
        public AssetCollectionChoice(AssetCollection collection)
        {
            Id = collection.Id;
            Caption = collection.CreatedAtUtc.ToLocalTime().ToString("MM/dd HH:mm") + " · " + collection.Prompt;
        }

        public string Id { get; private set; }
        public string Caption { get; private set; }
    }
}

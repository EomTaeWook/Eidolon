using Eidolon.Core.Domain;
using Eidolon.App.Localization;

namespace Eidolon.App.ViewModels
{
    public class AssetItem : ObservableObject
    {
        private readonly StringHelper _strings;
        private bool _isSelected;
        public ModelAsset Asset { get; private set; }
        public string Label { get; private set; }
        public string Detail { get; private set; }
        public bool HasTriggerWord
        {
            get
            {
                return string.IsNullOrWhiteSpace(Asset.TriggerWord) == false;
            }
        }

        public string TriggerCaption
        {
            get
            {
                return _strings.Format("EidolonText340", Asset.TriggerWord);
            }
        }
        public bool IsSelected
        {
            get
            {
                return _isSelected;
            }
            set
            {
                Set(ref _isSelected, value);
            }
        }

        public AssetItem(ModelAsset asset, StringHelper strings)
        {
            _strings = strings;
            Asset = asset;
            Localize();
        }

        public void Localize()
        {
            ModelAsset asset = Asset;
            Label = asset.Name;
            string kind = _strings.GetString("EidolonText208");
            if (asset.Kind == AssetKind.Lora)
            {
                kind = "LoRA";
            }
            string family = _strings.GetString("EidolonText209");
            if (asset.Family == ModelFamily.StableDiffusion15)
            {
                family = "SD 1.5";
            }
            if (asset.Family == ModelFamily.Sdxl)
            {
                family = "SDXL";
            }
            Detail = kind + " · " + family;
            if (string.IsNullOrWhiteSpace(asset.TriggerWord) == false)
            {
                Detail += " · " + asset.TriggerWord;
            }
            Raise(nameof(Detail));
            Raise(nameof(HasTriggerWord));
            Raise(nameof(TriggerCaption));
        }
    }
}

using Eidolon.App.Localization;
using Eidolon.Core.Domain;

namespace Eidolon.App.ViewModels
{
    public class RuntimeModuleItem : ObservableObject
    {
        private readonly StringHelper _strings;
        private readonly RuntimeModule _module;

        public string Label { get; private set; } = string.Empty;
        public string Version { get; private set; } = string.Empty;
        public string Status { get; private set; } = string.Empty;
        public string Location
        {
            get
            {
                return _module.Location;
            }
        }
        public bool HasVersion
        {
            get
            {
                return _module.State != RuntimeModuleState.NotInstalled;
            }
        }

        public RuntimeModuleItem(RuntimeModule module, StringHelper strings)
        {
            _module = module;
            _strings = strings;
            Localize();
        }

        public void Localize()
        {
            switch (_module.Kind)
            {
                case RuntimeModuleKind.ComfyUi:
                    Label = "ComfyUI";
                    break;
                case RuntimeModuleKind.GenerationPython:
                    Label = _strings.GetString("EidolonText282");
                    break;
                case RuntimeModuleKind.Torch:
                    Label = "PyTorch";
                    break;
                case RuntimeModuleKind.TorchVision:
                    Label = "torchvision";
                    break;
                case RuntimeModuleKind.SdScripts:
                    Label = "sd-scripts";
                    break;
                case RuntimeModuleKind.TrainingPython:
                    Label = _strings.GetString("EidolonText283");
                    break;
                case RuntimeModuleKind.Uv:
                    Label = "uv";
                    break;
                case RuntimeModuleKind.CustomNode:
                    Label = _module.Name;
                    break;
            }
            Version = _module.Version;
            if (HasVersion == true && string.IsNullOrWhiteSpace(Version) == true)
            {
                Version = _strings.GetString("EidolonText279");
            }
            switch (_module.State)
            {
                case RuntimeModuleState.NotInstalled:
                    Status = _strings.GetString("EidolonText277");
                    break;
                case RuntimeModuleState.Installed:
                    Status = _strings.GetString("EidolonText276");
                    break;
                case RuntimeModuleState.Disabled:
                    Status = _strings.GetString("EidolonText278");
                    break;
            }
            Raise(nameof(Label));
            Raise(nameof(Version));
            Raise(nameof(Status));
        }
    }
}

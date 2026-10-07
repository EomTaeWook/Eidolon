using Eidolon.App.Localization;
using Eidolon.Core.Domain;
using System.Globalization;

namespace Eidolon.App.ViewModels
{
    public class ModelDownloadItem : ObservableObject
    {
        private readonly StringHelper _strings;
        private bool _isInstalled;
        private bool _isInstalling;

        public ModelDownload Model { get; private set; }
        public AsyncCommand InstallCommand { get; private set; }
        public AsyncCommand DeleteCommand { get; private set; }
        public string Name
        {
            get
            {
                return Model.Name;
            }
        }
        public string Detail
        {
            get
            {
                string family = "SDXL";
                if (Model.Family == ModelFamily.StableDiffusion15)
                {
                    family = "SD 1.5";
                }
                return family + " · " + (Model.SizeBytes / 1000000000.0).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
            }
        }
        public string Description
        {
            get
            {
                if (Model.FileName == "sd_xl_base_1.0.safetensors")
                {
                    return _strings.GetString("EidolonText345");
                }
                if (Model.Family == ModelFamily.StableDiffusion15)
                {
                    return _strings.GetString("EidolonText346");
                }
                if (Model.FileName == "Illustrious-XL-v0.1.safetensors")
                {
                    return _strings.GetString("EidolonText350");
                }
                return _strings.GetString("EidolonText347");
            }
        }
        public string InstallCaption
        {
            get
            {
                if (IsInstalling == true)
                {
                    return _strings.GetString("EidolonText354");
                }
                return _strings.GetString("EidolonText351");
            }
        }
        public bool IsInstalling
        {
            get
            {
                return _isInstalling;
            }
            set
            {
                if (Set(ref _isInstalling, value) == true)
                {
                    Raise(nameof(InstallCaption));
                }
            }
        }
        public bool IsInstalled
        {
            get
            {
                return _isInstalled;
            }
            set
            {
                Set(ref _isInstalled, value);
            }
        }

        public ModelDownloadItem(ModelDownload model, StringHelper strings, AsyncCommand installCommand, AsyncCommand deleteCommand)
        {
            Model = model;
            _strings = strings;
            InstallCommand = installCommand;
            DeleteCommand = deleteCommand;
        }

        public void Localize()
        {
            Raise(nameof(Description));
            Raise(nameof(InstallCaption));
        }
    }
}

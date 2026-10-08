using Avalonia.Media.Imaging;
using Eidolon.App.Localization;
using Eidolon.Core.Domain;

namespace Eidolon.App.ViewModels
{
    public class AssetFrameItem : ObservableObject, IDisposable
    {
        private string _prompt;
        private Bitmap _image;
        private readonly StringHelper _strings;

        public AssetFrameItem(AssetFrame frame, StringHelper strings)
        {
            Frame = frame;
            _strings = strings;
            _prompt = frame.Prompt;
            Localize(strings);
        }

        public void Localize(StringHelper strings)
        {
            AssetFrame frame = Frame;
            Caption = strings.Format("EidolonText620", frame.Number);
            if (frame.Label == "front")
            {
                Caption = strings.GetString("EidolonText637");
            }
            if (frame.Label == "side")
            {
                Caption = strings.GetString("EidolonText638");
            }
            if (frame.Label == "back")
            {
                Caption = strings.GetString("EidolonText639");
            }
            if (frame.Label == "top")
            {
                Caption = strings.GetString("EidolonText640");
            }
            StateCaption = strings.GetString("EidolonText624");
            if (frame.State == JobState.Completed)
            {
                StateCaption = strings.GetString("EidolonText210");
                if (HasImage == false)
                {
                    StateCaption = strings.GetString("EidolonText626");
                }
            }
            if (frame.State == JobState.Running)
            {
                StateCaption = strings.GetString("EidolonText625");
            }
            if (frame.State == JobState.Failed || frame.State == JobState.Cancelled || frame.State == JobState.Interrupted)
            {
                StateCaption = strings.GetString("EidolonText626");
            }
            Raise(nameof(Caption));
            Raise(nameof(StateCaption));
            Raise(nameof(ErrorCaption));
        }

        public AssetFrame Frame { get; private set; }
        public string Caption { get; private set; }
        public string StateCaption { get; private set; }
        public string ErrorCaption
        {
            get
            {
                if (Frame.ErrorCode == StudioMessageCode.None)
                {
                    return string.Empty;
                }
                return _strings.Format(Frame.ErrorCode, Frame.ErrorArguments);
            }
        }
        public string Prompt
        {
            get
            {
                return _prompt;
            }
            set
            {
                Set(ref _prompt, value);
            }
        }
        public Bitmap Image
        {
            get
            {
                return _image;
            }
        }
        public bool HasImage
        {
            get
            {
                return _image != null;
            }
        }
        public void SetImage(Bitmap image)
        {
            Bitmap previous = _image;
            _image = image;
            Raise(nameof(Image));
            Raise(nameof(HasImage));
            Localize(_strings);
            previous?.Dispose();
        }
        public void Dispose()
        {
            SetImage(null);
        }
    }
}

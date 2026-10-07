using Eidolon.App.Localization;

namespace Eidolon.App.ViewModels
{
    public class CodexModelChoice : ObservableObject
    {
        private readonly string _displayName;
        private string _label;

        public CodexModelChoice(string model, string displayName, StringHelper strings)
        {
            Model = model;
            _displayName = displayName;
            Localize(strings);
        }

        public string Model { get; private set; }

        public string Label
        {
            get
            {
                return _label;
            }
            private set
            {
                Set(ref _label, value);
            }
        }

        public void Localize(StringHelper strings)
        {
            if (string.IsNullOrEmpty(Model) == true)
            {
                Label = strings.GetString("EidolonText551");
                return;
            }
            Label = _displayName;
            if (string.IsNullOrWhiteSpace(Label) == true)
            {
                Label = Model;
            }
        }
    }
}

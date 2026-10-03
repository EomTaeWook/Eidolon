using Eidolon.Core.Domain;
using Eidolon.App.Localization;

namespace Eidolon.App.ViewModels
{
    public class KindChoice : ObservableObject
    {
        public AssetKind Value { get; private set; }
        public string Label { get; private set; }

        public KindChoice(AssetKind value, string label)
        {
            Value = value;
            Label = label;
        }

        public void Localize(StringHelper strings)
        {
            Label = strings.TranslateMessage(Label);
            Raise(nameof(Label));
        }
    }
}

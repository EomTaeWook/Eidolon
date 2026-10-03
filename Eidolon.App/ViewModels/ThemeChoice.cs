using Eidolon.Core.Domain;
using Eidolon.App.Localization;

namespace Eidolon.App.ViewModels
{
    public class ThemeChoice : ObservableObject
    {
        public AppTheme Value { get; private set; }
        public string Label { get; private set; }

        public ThemeChoice(AppTheme value, string label)
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

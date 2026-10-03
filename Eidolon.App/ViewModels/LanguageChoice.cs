using Eidolon.Core.Domain;
using Eidolon.App.Localization;

namespace Eidolon.App.ViewModels
{
    public class LanguageChoice
    {
        public AppLanguage Value { get; private set; }
        public string Label { get; private set; }

        public LanguageChoice(AppLanguage value, string label)
        {
            Value = value;
            Label = label;
        }
    }
}

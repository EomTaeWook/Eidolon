using Eidolon.Core.Domain;

namespace Eidolon.App.Localization
{
    public class StringLanguageSelection
    {
        public AppLanguage Language { get; }

        public StringLanguageSelection(AppLanguage language)
        {
            Language = language;
        }
    }
}

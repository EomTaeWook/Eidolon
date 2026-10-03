using Avalonia;
using Eidolon.App.Localization;
using Eidolon.Core.Domain;

namespace Eidolon.App.Services
{
    public class LanguageService
    {
        private readonly Application _application;
        private readonly StringHelper _strings;

        public LanguageService(Application application, StringHelper strings)
        {
            _application = application;
            _strings = strings;
        }

        public void Apply(AppLanguage language)
        {
            _strings.Language = language;
            foreach (string name in _strings.Names)
            {
                _application.Resources[name] = _strings.GetString(name);
            }
        }
    }
}

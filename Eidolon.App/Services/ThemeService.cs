using Avalonia;
using Avalonia.Styling;
using Eidolon.Core.Domain;
using Eidolon.App.Localization;

namespace Eidolon.App.Services
{
    public class ThemeService
    {
        private readonly Application _application;

        public ThemeService(Application application)
        {
            _application = application;
        }

        public void Apply(AppTheme theme)
        {
            if (theme == AppTheme.Light)
            {
                _application.RequestedThemeVariant = ThemeVariant.Light;
            }
            else
            {
                _application.RequestedThemeVariant = ThemeVariant.Dark;
            }
        }
    }
}

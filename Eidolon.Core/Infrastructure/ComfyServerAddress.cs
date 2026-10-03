using Eidolon.Core.Domain;

namespace Eidolon.Core.Infrastructure
{
    public static class ComfyServerAddress
    {
        public static string Root(StudioSettings settings)
        {
            settings.Validate();
            return new Uri(settings.ServerAddress.Trim()).AbsoluteUri.TrimEnd('/');
        }

        public static bool CanStartLocally(StudioSettings settings)
        {
            if (Uri.TryCreate(settings.ServerAddress, UriKind.Absolute, out Uri address) == false)
            {
                return false;
            }
            return address.IsLoopback == true && address.Scheme == Uri.UriSchemeHttp &&
                address.AbsolutePath == "/" && address.Port >= 1024 && address.Port <= 65535 &&
                string.IsNullOrEmpty(address.UserInfo) == true && string.IsNullOrEmpty(address.Query) == true &&
                string.IsNullOrEmpty(address.Fragment) == true;
        }

        public static bool UsesServerAssets(StudioSettings settings)
        {
            return settings.UseServerAssets == true || CanStartLocally(settings) == false;
        }

        public static string Route(StudioSettings settings, string route)
        {
            return Root(settings) + "/" + route;
        }

        public static bool IsExternalAsset(ModelAsset asset)
        {
            if (Uri.TryCreate(asset.RuntimeRoot, UriKind.Absolute, out Uri address) == false)
            {
                return false;
            }
            return address.Scheme == Uri.UriSchemeHttp || address.Scheme == Uri.UriSchemeHttps;
        }
    }
}

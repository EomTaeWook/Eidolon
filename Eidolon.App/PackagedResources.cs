using System.Resources;
using System.Text;

namespace Eidolon.App
{
    internal static class PackagedResources
    {
        public static Stream Open(string resourceName)
        {
            Stream stream = typeof(PackagedResources).Assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                throw new MissingManifestResourceException(resourceName);
            }
            return stream;
        }

        public static string ReadText(string resourceName)
        {
            using Stream stream = Open(resourceName);
            using StreamReader reader = new StreamReader(stream, Encoding.UTF8, true);
            return reader.ReadToEnd();
        }
    }
}

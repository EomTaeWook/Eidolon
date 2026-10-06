namespace Eidolon.Core.Domain
{
    public class ModelDownload
    {
        public string Name { get; private set; }
        public ModelFamily Family { get; private set; }
        public string FileName { get; private set; }
        public long SizeBytes { get; private set; }
        public string DownloadUrl { get; private set; }
        public string Sha256 { get; private set; }
        public string LicenseUrl { get; private set; }
        public string LicenseFileName { get; private set; }
        public string ModelCardUrl { get; private set; }
        public string TermsOfUseUrl { get; private set; }

        public ModelDownload(string name, ModelFamily family, string repository, string revision,
            string fileName, long sizeBytes, string sha256, string licenseUrl, string licenseFileName,
            string termsOfUseFileName = "")
        {
            Name = name;
            Family = family;
            FileName = fileName;
            SizeBytes = sizeBytes;
            string source = "https://huggingface.co/" + repository + "/resolve/" + revision + "/";
            DownloadUrl = source + fileName;
            ModelCardUrl = source + "README.md";
            Sha256 = sha256;
            LicenseUrl = licenseUrl;
            LicenseFileName = licenseFileName;
            TermsOfUseUrl = string.Empty;
            if (string.IsNullOrEmpty(termsOfUseFileName) == false)
            {
                TermsOfUseUrl = source + termsOfUseFileName;
            }
        }
    }
}

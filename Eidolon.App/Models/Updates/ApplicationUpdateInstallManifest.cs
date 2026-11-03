namespace Eidolon.App.Services
{
    public class ApplicationUpdateInstallManifest
    {
        public ApplicationUpdateInstallManifest()
        {
        }

        public int OriginalPid { get; set; }
        public long OriginalStartTicks { get; set; }
        public string InstallPath { get; set; }
        public string WorkingDirectory { get; set; }
        public string InstalledSha256 { get; set; }
        public string CandidateSha256 { get; set; }
        public long CandidateSize { get; set; }
        public string Nonce { get; set; }
        public string CandidateVersion { get; set; }
        public string InstalledBundleSha256 { get; set; }
        public string PreparedBundleSha256 { get; set; }
    }
}

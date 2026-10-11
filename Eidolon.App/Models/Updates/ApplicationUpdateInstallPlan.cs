using System.Diagnostics;

namespace Eidolon.App.Services
{
    public class ApplicationUpdateInstallPlan
    {
        internal ApplicationUpdateInstallPlan(string directory, string nonce, VerifiedApplicationUpdate download)
        {
            Directory = directory;
            Nonce = nonce;
            Download = download;
        }

        internal VerifiedApplicationUpdate Download { get; }
        internal string Directory { get; }
        internal string Nonce { get; }
        internal Process Helper { get; set; }
    }
}

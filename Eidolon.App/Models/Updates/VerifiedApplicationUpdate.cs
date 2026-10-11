namespace Eidolon.App.Services
{
    public class VerifiedApplicationUpdate
    {
        internal VerifiedApplicationUpdate(ApplicationUpdateRelease release, string directory, string path)
        {
            Release = release;
            Directory = directory;
            FilePath = path;
        }

        public ApplicationUpdateRelease Release { get; }
        internal string Directory { get; }
        internal string FilePath { get; }
    }
}

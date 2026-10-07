namespace Eidolon.Core.Infrastructure
{
    public class RuntimeManifest
    {
        public RuntimeManifest()
        {
        }

        public string Owner { get; set; } = "Eidolon";
        public int SchemaVersion { get; set; } = 1;
        public string State { get; set; } = "Installing";
        public bool UseCpu { get; set; }
        public string ComfyVersion { get; set; } = RuntimeLayout.ComfyVersion;
        public string TrainingVersion { get; set; } = RuntimeLayout.TrainingVersion;
        public string Error { get; set; } = string.Empty;
    }
}

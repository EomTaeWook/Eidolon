namespace Eidolon.Modeling
{
    public class ModelingInput
    {
        public ModelingInput(string CodexExecutablePath, string CodexModel, string BlenderExecutablePath, string OutputDirectory, string SourceBlendPath, string Instructions, IReadOnlyList<ModelingReference> References, int maximumTriangleCount = 0, bool showBlenderWindow = false, ModelingAgent agent = ModelingAgent.Codex, string partsBlendPath = "", string engineInstallDirectory = "")
        {
            ArgumentOutOfRangeException.ThrowIfNegative(maximumTriangleCount);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumTriangleCount, 1_000_000);
            this.CodexExecutablePath = CodexExecutablePath;
            this.CodexModel = CodexModel;
            this.BlenderExecutablePath = BlenderExecutablePath;
            EngineInstallDirectory = engineInstallDirectory;
            this.OutputDirectory = OutputDirectory;
            this.SourceBlendPath = SourceBlendPath;
            PartsBlendPath = partsBlendPath;
            this.Instructions = Instructions;
            this.References = References.ToArray();
            MaximumTriangleCount = maximumTriangleCount;
            ShowBlenderWindow = showBlenderWindow;
            Agent = agent;
        }

        public string CodexExecutablePath
        {
            get;
        }

        public string CodexModel
        {
            get;
        }

        public string BlenderExecutablePath
        {
            get;
        }

        public string EngineInstallDirectory
        {
            get;
        }

        public string OutputDirectory
        {
            get;
        }

        public string SourceBlendPath
        {
            get;
        }

        public string PartsBlendPath
        {
            get;
        }

        public string Instructions
        {
            get;
        }

        public IReadOnlyList<ModelingReference> References
        {
            get;
        }

        public int MaximumTriangleCount
        {
            get;
        }

        public bool ShowBlenderWindow
        {
            get;
        }

        public ModelingAgent Agent
        {
            get;
        }
    }
}

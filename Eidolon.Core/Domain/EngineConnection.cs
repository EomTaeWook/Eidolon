namespace Eidolon.Core.Domain
{
    public class EngineConnection
    {
        public string ServerAddress { get; private set; }
        public bool OwnsProcess { get; private set; }
        public bool UsesLocalAssets { get; private set; }

        public EngineConnection(string serverAddress, bool ownsProcess, bool usesLocalAssets)
        {
            ServerAddress = serverAddress;
            OwnsProcess = ownsProcess;
            UsesLocalAssets = usesLocalAssets;
        }
    }
}

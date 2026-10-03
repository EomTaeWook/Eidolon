namespace Eidolon.Core.Domain
{
    public class AssetCatalog
    {
        public AssetCatalog()
        {
        }

        public int SchemaVersion { get; set; } = 1;
        public List<ModelAsset> Assets { get; set; } = new List<ModelAsset>();
    }
}

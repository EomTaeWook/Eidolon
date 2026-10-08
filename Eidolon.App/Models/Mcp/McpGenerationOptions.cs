using Eidolon.App.Services;
using Eidolon.Core.Domain;

namespace Eidolon.App.Mcp
{
    public class McpGenerationOptions
    {
        public McpGenerationOptions()
        {
        }

        public DesktopSettings Settings { get; set; }
        public ModelAsset Model { get; set; }
        public List<ModelAsset> Loras { get; set; } = new List<ModelAsset>();
        public long Seed { get; set; }
        public bool RemoveBackground { get; set; }
    }
}

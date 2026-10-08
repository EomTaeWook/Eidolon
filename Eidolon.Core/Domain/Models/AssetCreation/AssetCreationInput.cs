namespace Eidolon.Core.Domain
{
    public class AssetCreationInput
    {
        public const int MaximumFrames = 32;
        public const int MaximumFrameSize = 2048;
        public const int DefaultSpriteFrameSize = 256;
        public const int DefaultViewFrameSize = 1024;
        public const long MaximumSheetPixels = 64 * 1000 * 1000;

        public AssetCreationInput()
        {
        }

        public AssetCreationKind Kind { get; set; }
        public string Prompt { get; set; } = string.Empty;
        public string ActionPrompt { get; set; } = string.Empty;
        public int FrameCount { get; set; } = 8;
        public List<string> FrameDescriptions { get; set; } = new List<string>();
        public GenerationReferenceInput Reference { get; set; }
        public long Seed { get; set; }
        public bool RemoveBackground { get; set; } = true;
        public bool PixelArt { get; set; }
        public int FrameWidth { get; set; } = DefaultSpriteFrameSize;
        public int FrameHeight { get; set; } = DefaultSpriteFrameSize;
        public int Columns { get; set; } = 4;
        public int FramesPerSecond { get; set; } = 10;
    }
}

namespace Eidolon.Core.Domain
{
    public class AssetCreationInput
    {
        public const int MaximumFrames = 32;
        public const int MaximumFrameSize = 2048;
        public const int DefaultSpriteFrameSize = 256;
        public const int DefaultViewFrameSize = 1024;
        public const int ViewCount = 4;
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

        public void Validate()
        {
            if (Kind != AssetCreationKind.SpriteAnimation)
            {
                if (Kind != AssetCreationKind.FourViews)
                {
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
            }
            if (Prompt == null)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (Prompt.Length > 16000)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (ActionPrompt == null)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (ActionPrompt.Length > 2000)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (FrameDescriptions == null)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (Reference == null)
            {
                throw new StudioException(StudioMessageCode.InvalidReferenceImage);
            }
            if (Reference.ImageData == null)
            {
                throw new StudioException(StudioMessageCode.InvalidReferenceImage);
            }
            if (Reference.ImageData.Length == 0)
            {
                throw new StudioException(StudioMessageCode.InvalidReferenceImage);
            }
            if (Reference.ImageData.Length > GenerationReferenceInput.MaximumImageBytes)
            {
                throw new StudioException(StudioMessageCode.InvalidReferenceImage);
            }
            if (double.IsFinite(Reference.ChangeStrength) == false)
            {
                throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
            }
            if (Reference.ChangeStrength < 0.05)
            {
                throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
            }
            if (Reference.ChangeStrength > 0.95)
            {
                throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
            }
            if (Seed < 0)
            {
                throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
            }
            if (FrameCount < 2)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (FrameCount > MaximumFrames)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (FrameWidth < 16)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (FrameWidth > MaximumFrameSize)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (FrameHeight < 16)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (FrameHeight > MaximumFrameSize)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (Columns < 1)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (Columns > MaximumFrames)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (FramesPerSecond < 1)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (FramesPerSecond > 60)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (FrameDescriptions.Count > 0)
            {
                if (FrameDescriptions.Count != FrameCount)
                {
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
                foreach (string description in FrameDescriptions)
                {
                    if (string.IsNullOrWhiteSpace(description) == true)
                    {
                        throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                    }
                    if (description.Length > 4000)
                    {
                        throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                    }
                }
            }
            int count = ViewCount;
            if (Kind == AssetCreationKind.SpriteAnimation)
            {
                count = FrameCount;
            }
            int columns = Math.Min(Columns, count);
            int rows = (count + columns - 1) / columns;
            if ((long)FrameWidth * FrameHeight * columns * rows > MaximumSheetPixels)
            {
                throw new StudioException(StudioMessageCode.AssetSheetTooLarge);
            }
        }
    }
}

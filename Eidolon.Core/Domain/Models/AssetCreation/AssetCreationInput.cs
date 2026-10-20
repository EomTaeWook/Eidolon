namespace Eidolon.Core.Domain
{
    public class AssetCreationInput
    {
        public const int MaximumFrames = 32;
        public const int MaximumFrameSize = 2048;
        public const int DefaultViewFrameSize = 1024;
        public const int ViewCount = 5;
        public const long MaximumSheetPixels = 64 * 1000 * 1000;

        public AssetCreationInput()
        {
        }

        public AssetCreationKind Kind { get; set; } = AssetCreationKind.FiveViews;
        public string Prompt { get; set; } = string.Empty;
        public GenerationReferenceInput Reference { get; set; }
        public long Seed { get; set; }
        public bool RemoveBackground { get; set; } = true;
        public bool PixelArt { get; set; }
        public int FrameWidth { get; set; } = DefaultViewFrameSize;
        public int FrameHeight { get; set; } = DefaultViewFrameSize;
        public int Columns { get; set; } = ViewCount;

        public void Validate()
        {
            if (Kind != AssetCreationKind.FiveViews)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (Prompt == null)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (Prompt.Length > 16000)
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
            int columnCount = Math.Min(Columns, ViewCount);
            int rowCount = (ViewCount + columnCount - 1) / columnCount;
            if ((long)FrameWidth * FrameHeight * columnCount * rowCount > MaximumSheetPixels)
            {
                throw new StudioException(StudioMessageCode.AssetSheetTooLarge);
            }
        }
    }
}

namespace Eidolon.Core.Domain
{
    public class AssetSheetManifest
    {
        public AssetSheetManifest()
        {
        }

        public string Format { get; set; } = "Eidolon.AssetSheet";
        public int SchemaVersion { get; set; } = 1;
        public string CollectionId { get; set; } = string.Empty;
        public AssetCreationKind Kind { get; set; }
        public string SheetFile { get; set; } = "Sheet.png";
        public int Width { get; set; }
        public int Height { get; set; }
        public int FrameWidth { get; set; }
        public int FrameHeight { get; set; }
        public int FramesPerSecond { get; set; }
        public bool Loop { get; set; }
        public bool PixelArt { get; set; }
        public string CoordinateOrigin { get; set; } = "top-left";
        public List<AssetSheetFrame> Frames { get; set; } = new List<AssetSheetFrame>();
    }

    public class AssetSheetFrame
    {
        public AssetSheetFrame()
        {
        }

        public int Number { get; set; }
        public string Label { get; set; } = string.Empty;
        public string File { get; set; } = string.Empty;
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public double PivotX { get; set; } = 0.5;
        public double PivotY { get; set; } = 1.0;
        public string SourceJobId { get; set; } = string.Empty;
        public long Seed { get; set; }
    }
}

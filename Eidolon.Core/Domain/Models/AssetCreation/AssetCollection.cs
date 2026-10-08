namespace Eidolon.Core.Domain
{
    public class AssetCollection
    {
        public const string DocumentFormat = "Eidolon.AssetCollection";

        public AssetCollection()
        {
        }

        public string Format { get; set; } = DocumentFormat;
        public int SchemaVersion { get; set; } = 1;
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public AssetCreationKind Kind { get; set; }
        public JobState State { get; set; } = JobState.Preparing;
        public string Prompt { get; set; } = string.Empty;
        public string ActionPrompt { get; set; } = string.Empty;
        public StudioSettings Settings { get; set; } = new StudioSettings();
        public bool UseServerAssets { get; set; }
        public ModelAsset Model { get; set; } = new ModelAsset();
        public List<ModelAsset> Loras { get; set; } = new List<ModelAsset>();
        public string ReferenceName { get; set; } = string.Empty;
        public double ChangeStrength { get; set; } = 0.45;
        public long Seed { get; set; }
        public bool RemoveBackground { get; set; }
        public bool PixelArt { get; set; }
        public int FrameWidth { get; set; }
        public int FrameHeight { get; set; }
        public int Columns { get; set; }
        public int FramesPerSecond { get; set; }
        public List<AssetFrame> Frames { get; set; } = new List<AssetFrame>();
        public DateTimeOffset CreatedAtUtc { get; set; }
        public StudioMessageCode ErrorCode { get; set; }
        public object[] ErrorArguments { get; set; } = Array.Empty<object>();
    }

    public class AssetFrame
    {
        public AssetFrame()
        {
        }

        public int Number { get; set; }
        public string Label { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
        public JobState State { get; set; } = JobState.Preparing;
        public string ImagePath { get; set; } = string.Empty;
        public string SourceJobId { get; set; } = string.Empty;
        public long Seed { get; set; }
        public StudioMessageCode ErrorCode { get; set; }
        public long ImageSeed { get; set; }
        public object[] ErrorArguments { get; set; } = Array.Empty<object>();
    }
}

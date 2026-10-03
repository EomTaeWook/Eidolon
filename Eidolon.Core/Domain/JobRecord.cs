namespace Eidolon.Core.Domain
{
    public class JobRecord
    {
        public JobRecord()
        {
        }

        public int SchemaVersion { get; set; } = 1;
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public JobKind Kind { get; set; }
        public JobState State { get; set; } = JobState.Preparing;
        public string Title { get; set; } = string.Empty;
        public string UserPrompt { get; set; } = string.Empty;
        public string PositivePrompt { get; set; } = string.Empty;
        public string NegativePrompt { get; set; } = string.Empty;
        public ModelAsset Model { get; set; } = new ModelAsset();
        public List<ModelAsset> Loras { get; set; } = new List<ModelAsset>();
        public long Seed { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Steps { get; set; }
        public double Guidance { get; set; }
        public string Sampler { get; set; } = string.Empty;
        public string Scheduler { get; set; } = string.Empty;
        public string EnginePromptId { get; set; } = string.Empty;
        public string TriggerWord { get; set; } = string.Empty;
        public string DatasetDescription { get; set; } = string.Empty;
        public int DatasetImageCount { get; set; }
        public List<string> ImageFiles { get; set; } = new List<string>();
        public string TrainedFile { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public StudioMessageCode ErrorCode { get; set; }
        public object[] ErrorArguments { get; set; } = Array.Empty<object>();
        public DateTimeOffset StartedAtUtc { get; set; }
        public DateTimeOffset FinishedAtUtc { get; set; }
    }
}

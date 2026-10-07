namespace Eidolon.Core.Domain
{
    public class GenerationMetadata
    {
        public const string DocumentFormat = "Eidolon.GenerationImage";

        public GenerationMetadata()
        {
        }

        public GenerationMetadata(JobRecord job, DateTimeOffset generatedAtUtc)
        {
            Format = DocumentFormat;
            SourceJobId = job.Id;
            Title = job.Title;
            UserPrompt = job.UserPrompt;
            BasePositivePrompt = job.BasePositivePrompt;
            HasBasePositivePrompt = job.HasBasePositivePrompt;
            PositivePrompt = job.PositivePrompt;
            NegativePrompt = job.NegativePrompt;
            RemoveBackground = job.RemoveBackground;
            GenerationBackend = job.GenerationBackend;
            CodexModel = job.CodexModel;
            ReferenceMode = job.ReferenceMode;
            ReferenceImageName = job.ReferenceImageName;
            ReferenceImagePath = job.ReferenceImagePath;
            Denoise = job.Denoise;
            Model = job.Model.Copy();
            Loras = job.Loras.Select(lora => lora.Copy()).ToList();
            Seed = job.Seed;
            Width = job.Width;
            Height = job.Height;
            Steps = job.Steps;
            Guidance = job.Guidance;
            Sampler = job.Sampler;
            Scheduler = job.Scheduler;
            GeneratedAtUtc = generatedAtUtc;
        }

        public string Format { get; set; } = string.Empty;
        public int SchemaVersion { get; set; } = 1;
        public string SourceJobId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string UserPrompt { get; set; } = string.Empty;
        public string BasePositivePrompt { get; set; } = string.Empty;
        public bool HasBasePositivePrompt { get; set; }
        public string PositivePrompt { get; set; } = string.Empty;
        public string NegativePrompt { get; set; } = string.Empty;
        public bool RemoveBackground { get; set; }
        public GenerationBackend GenerationBackend { get; set; } = GenerationBackend.ComfyUI;
        public string CodexModel { get; set; } = string.Empty;
        public GenerationReferenceMode ReferenceMode { get; set; } = GenerationReferenceMode.None;
        public string ReferenceImageName { get; set; } = string.Empty;
        public string ReferenceImagePath { get; set; } = string.Empty;
        public double Denoise { get; set; } = 1.0;
        public ModelAsset Model { get; set; } = new ModelAsset();
        public List<ModelAsset> Loras { get; set; } = new List<ModelAsset>();
        public long Seed { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Steps { get; set; }
        public double Guidance { get; set; }
        public string Sampler { get; set; } = string.Empty;
        public string Scheduler { get; set; } = string.Empty;
        public DateTimeOffset GeneratedAtUtc { get; set; }
    }
}

namespace Eidolon.Core.Domain
{
    public class TrainingInput
    {
        public TrainingInput()
        {
        }

        public ModelAsset Model { get; set; }
        public ModelAsset ResumeLora { get; set; }
        public int Steps { get; set; } = TrainingPreset.MaxSteps;
        public string ImageDirectory { get; set; } = string.Empty;
        public List<string> ImageFiles { get; set; } = new List<string>();
        public string Name { get; set; } = string.Empty;
        public string TriggerWord { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}

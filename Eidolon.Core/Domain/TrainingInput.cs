namespace Eidolon.Core.Domain
{
    public class TrainingInput
    {
        public TrainingInput()
        {
        }

        public ModelAsset Model { get; set; }
        public ModelAsset ResumeLora { get; set; }
        public string ImageDirectory { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string TriggerWord { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}

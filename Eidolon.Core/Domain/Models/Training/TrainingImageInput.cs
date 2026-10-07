namespace Eidolon.Core.Domain
{
    public class TrainingImageInput
    {
        public TrainingImageInput()
        {
        }

        public string FilePath { get; set; } = string.Empty;
        public TrainingBackground Background { get; set; } = TrainingBackground.Original;

        public TrainingImageInput Copy()
        {
            return new TrainingImageInput
            {
                FilePath = FilePath,
                Background = Background
            };
        }
    }
}

namespace Eidolon.Core.Domain
{
    public class TrainingPreset
    {
        public TrainingPreset()
        {
        }

        public const int MaxSteps = 1000;
        public const int QuickMaxSteps = 300;
        public const int MinimumSteps = 1;
        public const int MaximumSteps = 100000;
        public const int NetworkDimension = 16;
        public const int NetworkAlpha = 16;
        public const int Repeats = 10;
        public const string LearningRate = "0.0001";
        public const string Optimizer = "AdamW";
        public const string Precision = "fp16";
    }
}

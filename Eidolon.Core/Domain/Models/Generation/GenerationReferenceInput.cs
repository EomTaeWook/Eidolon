namespace Eidolon.Core.Domain
{
    public class GenerationReferenceInput
    {
        public const int MaximumImageBytes = 32 * 1024 * 1024;
        public const int MaximumImagePixels = 40 * 1000 * 1000;

        public GenerationReferenceInput()
        {
        }

        public byte[] ImageData { get; set; } = Array.Empty<byte>();
        public string ImageName { get; set; } = string.Empty;
        public GenerationReferenceMode Mode { get; set; } = GenerationReferenceMode.Reimagine;
        public double ChangeStrength { get; set; } = 0.65;
    }
}

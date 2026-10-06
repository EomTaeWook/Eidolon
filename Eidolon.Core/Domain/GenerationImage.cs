namespace Eidolon.Core.Domain
{
    public class GenerationImage
    {
        public GenerationImage()
        {
        }

        public string FilePath { get; set; } = string.Empty;
        public DateTimeOffset CreatedAtUtc { get; set; }
        public GenerationMetadata Metadata { get; set; }
        public Exception MetadataError { get; set; }
    }
}

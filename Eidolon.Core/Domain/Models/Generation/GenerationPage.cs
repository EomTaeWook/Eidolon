namespace Eidolon.Core.Domain
{
    public class GenerationPage
    {
        public GenerationPage()
        {
        }

        public int Number { get; set; } = 1;
        public int PageCount { get; set; } = 1;
        public int TotalCount { get; set; }
        public string LatestImagePath { get; set; } = string.Empty;
        public List<GenerationImage> Images { get; set; } = new List<GenerationImage>();
    }
}

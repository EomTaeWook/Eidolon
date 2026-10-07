namespace Eidolon.App.Services
{
    public class PromptPreset
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string PositivePrompt { get; set; } = string.Empty;
        public string NegativePrompt { get; set; } = string.Empty;

        public PromptPreset()
        {
        }

        public PromptPreset Copy()
        {
            return new PromptPreset
            {
                Id = Id,
                Name = Name,
                PositivePrompt = PositivePrompt,
                NegativePrompt = NegativePrompt
            };
        }
    }
}

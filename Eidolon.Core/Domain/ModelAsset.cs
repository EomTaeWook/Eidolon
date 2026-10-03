namespace Eidolon.Core.Domain
{
    public class ModelAsset
    {
        public ModelAsset()
        {
        }

        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public AssetKind Kind { get; set; }
        public ModelFamily Family { get; set; }
        public string Name { get; set; } = string.Empty;
        public string EngineName { get; set; } = string.Empty;
        public string RuntimeRoot { get; set; } = string.Empty;
        public string TriggerWord { get; set; } = string.Empty;
        public DateTimeOffset AddedAtUtc { get; set; }

        public ModelAsset Copy()
        {
            return new ModelAsset
            {
                Id = Id,
                Kind = Kind,
                Family = Family,
                Name = Name,
                EngineName = EngineName,
                RuntimeRoot = RuntimeRoot,
                TriggerWord = TriggerWord,
                AddedAtUtc = AddedAtUtc
            };
        }
    }
}

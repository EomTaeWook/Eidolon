namespace Eidolon.Core.Domain
{
    public enum AssetCreationKind
    {
        SpriteAnimation = 0,
        // Preserve the kinds of existing three-view collections when reading saved jobs.
        CharacterViews = 1,
        ObjectViews = 2,
        FourViews = 3
    }
}

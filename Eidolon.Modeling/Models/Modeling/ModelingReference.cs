namespace Eidolon.Modeling
{
    public class ModelingReference
    {
        public ModelingReference(string Direction, string ImagePath)
        {
            this.Direction = Direction;
            this.ImagePath = ImagePath;
        }

        public string Direction
        {
            get;
        }

        public string ImagePath
        {
            get;
        }
    }
}

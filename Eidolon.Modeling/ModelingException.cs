namespace Eidolon.Modeling
{
    public class ModelingException : Exception
    {
        public ModelingException(string directoryPath, Exception innerException) : base("The Blender modeling request failed.", innerException)
        {
            DirectoryPath = directoryPath;
        }

        public string DirectoryPath
        {
            get;
        }
    }
}

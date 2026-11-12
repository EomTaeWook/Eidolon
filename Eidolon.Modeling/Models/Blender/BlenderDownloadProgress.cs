namespace Eidolon.Modeling
{
    public class BlenderDownloadProgress
    {
        public BlenderDownloadProgress(double Percent, bool IsIndeterminate)
        {
            this.Percent = Percent;
            this.IsIndeterminate = IsIndeterminate;
        }

        public double Percent
        {
            get;
            init;
        }

        public bool IsIndeterminate
        {
            get;
            init;
        }
    }
}

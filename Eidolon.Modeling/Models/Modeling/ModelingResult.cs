namespace Eidolon.Modeling
{
    public class ModelingResult
    {
        public ModelingResult(string directoryPath, string blendPath, string glbPath, IReadOnlyList<string> renderPaths, bool refinementFailed, int triangleCount = 0, string refinementReviewReason = "")
        {
            DirectoryPath = directoryPath;
            BlendPath = blendPath;
            GlbPath = glbPath;
            RenderPaths = renderPaths;
            RefinementFailed = refinementFailed;
            TriangleCount = triangleCount;
            RefinementReviewReason = refinementReviewReason;
        }

        public string DirectoryPath
        {
            get;
        }

        public string BlendPath
        {
            get;
        }

        public string GlbPath
        {
            get;
        }

        public IReadOnlyList<string> RenderPaths
        {
            get;
        }

        public bool RefinementFailed
        {
            get;
        }

        public int TriangleCount
        {
            get;
        }

        public string RefinementReviewReason
        {
            get;
        }
    }
}

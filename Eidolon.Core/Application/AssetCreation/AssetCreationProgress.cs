using Eidolon.Core.Domain;

namespace Eidolon.Core.Application
{
    internal class AssetCreationProgress : IProgress<WorkProgress>
    {
        private readonly IProgress<WorkProgress> _progress;
        private readonly int _index;
        private readonly int _count;
        private readonly bool _singleFrame;

        internal AssetCreationProgress(IProgress<WorkProgress> progress, int index, int count, bool singleFrame)
        {
            _progress = progress;
            _index = index;
            _count = count;
            _singleFrame = singleFrame;
        }

        public void Report(WorkProgress value)
        {
            double percent = (_index + Math.Clamp(value.Percent, 0, 100) / 100) / _count * 100;
            if (_singleFrame == true)
            {
                percent = Math.Clamp(value.Percent, 0, 100);
            }
            _progress.Report(new WorkProgress(StudioMessageCode.AssetFrameGenerating, percent, value.IsIndeterminate,
                _index + 1, _count));
        }
    }
}

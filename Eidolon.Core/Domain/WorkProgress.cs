namespace Eidolon.Core.Domain
{
    public class WorkProgress
    {
        public StudioMessageCode Code { get; private set; }
        public object[] Arguments { get; private set; }
        public double Percent { get; private set; }
        public bool IsIndeterminate { get; private set; }

        public WorkProgress(StudioMessageCode code, double percent = 0, bool isIndeterminate = true, params object[] arguments)
        {
            Code = code;
            Arguments = arguments;
            Percent = percent;
            IsIndeterminate = isIndeterminate;
        }
    }
}

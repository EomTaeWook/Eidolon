namespace Eidolon.Core.Domain
{
    public class StudioException : Exception
    {
        public StudioMessageCode Code { get; private set; }
        public object[] Arguments { get; private set; }

        public StudioException(StudioMessageCode code, params object[] arguments) : this(code, null, arguments)
        {
        }

        public StudioException(StudioMessageCode code, Exception innerException, params object[] arguments)
            : base(code.ToString(), innerException)
        {
            Code = code;
            Arguments = arguments;
        }
    }
}

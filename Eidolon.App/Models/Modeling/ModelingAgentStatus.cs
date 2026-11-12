namespace Eidolon.App.Models.Modeling
{
    public enum ModelingAgentStatus
    {
        Unknown,
        Checking,
        Missing,
        LoginRequired,
        Ready,
        Failed,
        TimedOut,
        UnsupportedCommand
    }
}

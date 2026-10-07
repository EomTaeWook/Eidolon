namespace Eidolon.Core.Domain
{
    public enum JobState
    {
        Preparing,
        Running,
        Completed,
        Failed,
        Cancelled,
        Interrupted,
        RegistrationFailed
    }
}

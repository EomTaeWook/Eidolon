namespace Eidolon.Modeling
{
    public class ModelingProcessRequest
    {
        public ModelingProcessRequest(string executable, IReadOnlyList<string> arguments, string workingDirectory, string logPath, string standardInput, bool showWindow = false, Action<string> standardOutputReceived = null)
        {
            Executable = executable;
            Arguments = arguments;
            WorkingDirectory = workingDirectory;
            LogPath = logPath;
            StandardInput = standardInput;
            ShowWindow = showWindow;
            StandardOutputReceived = standardOutputReceived;
        }

        public string Executable
        {
            get;
        }

        public IReadOnlyList<string> Arguments
        {
            get;
        }

        public string WorkingDirectory
        {
            get;
        }

        public string LogPath
        {
            get;
        }

        public string StandardInput
        {
            get;
        }

        public bool ShowWindow
        {
            get;
        }

        public Action<string> StandardOutputReceived
        {
            get;
        }
    }
}

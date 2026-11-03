namespace Eidolon.Core.Infrastructure
{
    public class CodexExecutableLocator
    {
        public string Find(string configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath) == false)
            {
                string path = configuredPath.Trim();
                if (Path.IsPathFullyQualified(path) == false)
                {
                    return string.Empty;
                }
                if (IsExecutable(path) == false)
                {
                    return string.Empty;
                }
                return Path.GetFullPath(path);
            }
            string executableName = "codex.exe";
            if (OperatingSystem.IsMacOS() == true)
            {
                executableName = "codex";
            }
            if (OperatingSystem.IsMacOS() == false)
            {
                string installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "OpenAI", "Codex", "bin", "codex.exe");
                if (IsExecutable(installed) == true)
                {
                    return installed;
                }
            }
            string searchPath = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrWhiteSpace(searchPath) == false)
            {
                foreach (string entry in searchPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    string directory = entry.Trim().Trim('"');
                    if (Path.IsPathFullyQualified(directory) == false)
                    {
                        continue;
                    }
                    string candidate = Path.Combine(directory, executableName);
                    if (IsExecutable(candidate) == true)
                    {
                        return candidate;
                    }
                }
            }
            if (OperatingSystem.IsMacOS() == true)
            {
                foreach (string directory in new string[] { "/opt/homebrew/bin", "/usr/local/bin" })
                {
                    string candidate = Path.Combine(directory, executableName);
                    if (IsExecutable(candidate) == true)
                    {
                        return candidate;
                    }
                }
            }
            return string.Empty;
        }

        private bool IsExecutable(string path)
        {
            if (File.Exists(path) == false)
            {
                return false;
            }
            if (OperatingSystem.IsMacOS() == false)
            {
                return Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase);
            }
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    return false;
                }
                UnixFileMode permissions = File.GetUnixFileMode(path);
                if ((permissions & UnixFileMode.UserExecute) != 0)
                {
                    return true;
                }
                if ((permissions & UnixFileMode.GroupExecute) != 0)
                {
                    return true;
                }
                return (permissions & UnixFileMode.OtherExecute) != 0;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}

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
                if (File.Exists(path) == false)
                {
                    return string.Empty;
                }
                if (Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) == false)
                {
                    return string.Empty;
                }
                return Path.GetFullPath(path);
            }
            string installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "OpenAI", "Codex", "bin", "codex.exe");
            if (File.Exists(installed) == true)
            {
                return installed;
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
                    string candidate = Path.Combine(directory, "codex.exe");
                    if (File.Exists(candidate) == true)
                    {
                        return candidate;
                    }
                }
            }
            return string.Empty;
        }
    }
}

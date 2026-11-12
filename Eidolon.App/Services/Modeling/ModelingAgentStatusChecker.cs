using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Eidolon.App.Models.Modeling;
using Eidolon.Core.Infrastructure;
using Eidolon.Modeling;

namespace Eidolon.App.Services
{
    public class ModelingAgentStatusChecker
    {
        private readonly ProcessRunner _processRunner;

        public ModelingAgentStatusChecker(ProcessRunner processRunner)
        {
            _processRunner = processRunner;
        }

        public async Task<ModelingAgentStatus> CheckAsync(ModelingAgent agent, string executablePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(executablePath) == false)
            {
                return ModelingAgentStatus.Missing;
            }
            string[] arguments = new string[] { "login", "status" };
            if (agent == ModelingAgent.Claude)
            {
                arguments = new string[] { "auth", "status" };
            }
            try
            {
                ProcessStartInfo startInfo = _processRunner.CreateStartInfo(executablePath, arguments, Path.GetTempPath());
                var result = await RunStatusProcessAsync(startInfo, cancellationToken);
                return GetAgentStatus(agent, result.ExitCode, result.StandardOutput, result.StandardError);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == true)
            {
                throw;
            }
            catch (TimeoutException)
            {
                return ModelingAgentStatus.TimedOut;
            }
            catch (Exception)
            {
                return ModelingAgentStatus.Failed;
            }
        }

        public async Task<List<string>> ListWslDistributionsAsync(CancellationToken cancellationToken)
        {
            ProcessStartInfo startInfo = CreateWslStartInfo(new string[] { "--list", "--quiet" });
            startInfo.StandardOutputEncoding = Encoding.Unicode;
            startInfo.StandardErrorEncoding = Encoding.Unicode;
            var result = await RunStatusProcessAsync(startInfo, cancellationToken, 65536);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException("WSL distribution query failed.");
            }
            List<string> distributions = new List<string>();
            foreach (string line in result.StandardOutput.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string distribution = line.Trim(' ', '\t', '\0', '\uFEFF');
                if (string.IsNullOrEmpty(distribution) == true)
                {
                    continue;
                }
                if (distribution.Any(character => char.IsControl(character)) == true)
                {
                    throw new InvalidDataException("Invalid WSL distribution name.");
                }
                if (distributions.Contains(distribution, StringComparer.Ordinal) == false)
                {
                    distributions.Add(distribution);
                }
            }
            return distributions;
        }

        public async Task<ModelingAgentStatus> CheckWslAsync(ModelingAgent agent, string distribution, CancellationToken cancellationToken, IReadOnlyCollection<string> registeredDistributions = null)
        {
            using CancellationTokenSource timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCancellation.CancelAfter(TimeSpan.FromSeconds(8));
            try
            {
                IReadOnlyCollection<string> distributions = registeredDistributions;
                if (distributions == null)
                {
                    distributions = await ListWslDistributionsAsync(timeoutCancellation.Token);
                }
                if (distributions.Contains(distribution, StringComparer.Ordinal) == false)
                {
                    return ModelingAgentStatus.Failed;
                }
                string commandName = "codex";
                string statusCommand = "login";
                if (agent == ModelingAgent.Claude)
                {
                    commandName = "claude";
                    statusCommand = "auth";
                }
                const string locateExecutable = """
                    command_path=$(command -v "$1" 2>/dev/null)
                    case "$command_path" in /*) ;; *) command_path= ;; esac
                    for candidate in "$command_path" "$HOME/.local/bin/$1" "$HOME/.npm-global/bin/$1" "$HOME"/.nvm/versions/node/*/bin/"$1"; do
                        case "$candidate" in ''|/mnt/[a-zA-Z]/*|*.exe|*.cmd|*.bat) continue ;; esac
                        if [ -f "$candidate" ]; then
                            if [ -x "$candidate" ]; then
                                printf '%s\n' "$candidate"
                                exit 0
                            fi
                        fi
                    done
                    exit 0
                    """;
                ProcessStartInfo locateStartInfo = CreateWslStartInfo(new string[] { "--distribution", distribution, "--cd", "~", "--exec", "sh", "-c", locateExecutable.Replace("\r\n", "\n"), "sh", commandName });
                var locatedExecutable = await RunStatusProcessAsync(locateStartInfo, timeoutCancellation.Token);
                if (locatedExecutable.ExitCode != 0)
                {
                    return ModelingAgentStatus.Failed;
                }
                string executablePath = locatedExecutable.StandardOutput.Trim();
                if (string.IsNullOrEmpty(executablePath) == true)
                {
                    return ModelingAgentStatus.Missing;
                }
                if (executablePath.StartsWith("/", StringComparison.Ordinal) == false)
                {
                    return ModelingAgentStatus.Failed;
                }
                if (executablePath.Any(character => char.IsControl(character)) == true)
                {
                    return ModelingAgentStatus.Failed;
                }
                const string runStatus = "PATH=\"${1%/*}:$PATH\"; export PATH; exec \"$@\"";
                ProcessStartInfo statusStartInfo = CreateWslStartInfo(new string[] { "--distribution", distribution, "--cd", "~", "--exec", "sh", "-c", runStatus, "sh", executablePath, statusCommand, "status" });
                var result = await RunStatusProcessAsync(statusStartInfo, timeoutCancellation.Token);
                return GetAgentStatus(agent, result.ExitCode, result.StandardOutput, result.StandardError);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == true)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return ModelingAgentStatus.TimedOut;
            }
            catch (TimeoutException)
            {
                return ModelingAgentStatus.TimedOut;
            }
            catch (Exception)
            {
                return ModelingAgentStatus.Failed;
            }
        }

        private ProcessStartInfo CreateWslStartInfo(IEnumerable<string> arguments)
        {
            if (OperatingSystem.IsWindows() == false)
            {
                throw new PlatformNotSupportedException("WSL readiness is available on Windows only.");
            }
            string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe");
            if (File.Exists(executable) == false)
            {
                throw new FileNotFoundException("WSL is unavailable.");
            }
            return _processRunner.CreateStartInfo(executable, arguments, Path.GetTempPath());
        }

        private ModelingAgentStatus GetAgentStatus(ModelingAgent agent, int exitCode, string standardOutput, string standardError)
        {
            if (exitCode == 0)
            {
                return ModelingAgentStatus.Ready;
            }
            if (IsUnsupportedCommand(standardOutput) == true)
            {
                return ModelingAgentStatus.UnsupportedCommand;
            }
            if (IsUnsupportedCommand(standardError) == true)
            {
                return ModelingAgentStatus.UnsupportedCommand;
            }
            if (exitCode == 1)
            {
                if (agent == ModelingAgent.Codex)
                {
                    if (standardError.Trim().Equals("Not logged in", StringComparison.Ordinal) == true)
                    {
                        return ModelingAgentStatus.LoginRequired;
                    }
                }
                if (agent == ModelingAgent.Claude)
                {
                    if (IsClaudeLoggedOut(standardOutput) == true)
                    {
                        return ModelingAgentStatus.LoginRequired;
                    }
                }
            }
            return ModelingAgentStatus.Failed;
        }

        private async Task<(int ExitCode, string StandardOutput, string StandardError)> RunStatusProcessAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken, int maximumOutputLength = 8192)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using CancellationTokenSource timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCancellation.CancelAfter(TimeSpan.FromSeconds(8));
            using Process process = new Process();
            using WindowsProcessLifetime processLifetime = new WindowsProcessLifetime(true);
            Task<string> standardOutputTask = Task.FromResult(string.Empty);
            Task<string> standardErrorTask = Task.FromResult(string.Empty);
            try
            {
                process.StartInfo = startInfo;
                process.StartInfo.RedirectStandardInput = true;
                if (process.Start() == false)
                {
                    throw new InvalidOperationException("Status process could not start.");
                }
                processLifetime.Attach(process);
                process.StandardInput.Close();
                standardOutputTask = ReadStatusOutputAsync(process.StandardOutput, timeoutCancellation.Token, maximumOutputLength);
                standardErrorTask = ReadStatusOutputAsync(process.StandardError, timeoutCancellation.Token, maximumOutputLength);
                await process.WaitForExitAsync(timeoutCancellation.Token);
                string[] output = await Task.WhenAll(standardOutputTask, standardErrorTask);
                cancellationToken.ThrowIfCancellationRequested();
                return (process.ExitCode, output[0], output[1]);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == true)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("Status query timed out.");
            }
            finally
            {
                timeoutCancellation.Cancel();
                processLifetime.Dispose();
                StopStatusProcess(process);
                try
                {
                    await Task.WhenAll(standardOutputTask, standardErrorTask);
                }
                catch (Exception)
                {
                }
            }
        }

        private async Task<string> ReadStatusOutputAsync(StreamReader reader, CancellationToken cancellationToken, int maximumOutputLength)
        {
            char[] buffer = new char[1024];
            StringBuilder output = new StringBuilder();
            while (true)
            {
                int length = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (length == 0)
                {
                    return output.ToString();
                }
                if (output.Length + length > maximumOutputLength)
                {
                    throw new InvalidDataException("Status output exceeded the limit.");
                }
                output.Append(buffer, 0, length);
            }
        }

        private bool IsUnsupportedCommand(string output)
        {
            foreach (string marker in new string[] { "unrecognized subcommand", "unknown command", "unknown option", "unexpected argument" })
            {
                if (output.Contains(marker, StringComparison.OrdinalIgnoreCase) == true)
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsClaudeLoggedOut(string output)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(output);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }
                if (document.RootElement.TryGetProperty("authMethod", out JsonElement authenticationMethod) == false)
                {
                    return false;
                }
                if (authenticationMethod.ValueKind != JsonValueKind.String)
                {
                    return false;
                }
                return authenticationMethod.GetString() == "none";
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private void StopStatusProcess(Process process)
        {
            try
            {
                if (process.HasExited == false)
                {
                    process.Kill(true);
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
        }
    }
}

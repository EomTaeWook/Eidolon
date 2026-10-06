using Eidolon.Core.Application;
using System.Diagnostics;
using System.Text;
using Dignus.Log;
using Eidolon.Core.Domain;

namespace Eidolon.Core.Infrastructure
{
    public class ProcessRunner
    {
        public ProcessRunner()
        {
        }
        public async Task RunAsync(string executable, IEnumerable<string> arguments, string workingDirectory,
            string logPath, CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string> environment = null, Action<string> lineReceived = null,
            bool stopWithParent = false)
        {
            ProcessStartInfo start = CreateStartInfo(executable, arguments, workingDirectory, environment);
            using Process process = new Process { StartInfo = start };
            using ProcessLogFile log = new ProcessLogFile(logPath);
            using SemaphoreSlim logGate = new SemaphoreSlim(1, 1);
            using WindowsProcessLifetime parentLifetime = new WindowsProcessLifetime(stopWithParent);
            cancellationToken.ThrowIfCancellationRequested();
            if (process.Start() == false)
            {
                throw new StudioException(StudioMessageCode.ProcessStartFailed);
            }
            Task stdout = Task.CompletedTask;
            Task stderr = Task.CompletedTask;
            try
            {
                parentLifetime.Attach(process);
                LogHelper.Info($"Process started: {Path.GetFileName(executable)}. Log: {logPath}");
                stdout = ReadAsync(process.StandardOutput);
                stderr = ReadAsync(process.StandardError);
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                parentLifetime.Dispose();
                await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (process.ExitCode != 0)
                {
                    throw new StudioException(StudioMessageCode.ProcessFailed, process.ExitCode, logPath);
                }
            }
            finally
            {
                parentLifetime.Dispose();
                if (process.HasExited == false)
                {
                    try
                    {
                        process.Kill(true);
                    }
                    catch (InvalidOperationException) when (process.HasExited == true)
                    {
                    }
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }
                await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            }

            async Task ReadAsync(StreamReader reader)
            {
                char[] buffer = new char[1024];
                StringBuilder pending = new StringBuilder();
                try
                {
                    int count;
                    while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        for (int index = 0; index < count; index++)
                        {
                            char character = buffer[index];
                            if (character == '\r' || character == '\n')
                            {
                                if (pending.Length > 0)
                                {
                                    await ReportLineAsync(pending.ToString()).ConfigureAwait(false);
                                    pending.Clear();
                                }
                            }
                            else
                            {
                                pending.Append(character);
                                if (pending.Length >= 8192)
                                {
                                    await ReportLineAsync(pending.ToString()).ConfigureAwait(false);
                                    pending.Clear();
                                }
                            }
                        }
                    }
                    if (pending.Length > 0)
                    {
                        await ReportLineAsync(pending.ToString()).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == true)
                {
                }
            }

            async Task ReportLineAsync(string line)
            {
                await logGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    log.WriteLine(line);
                }
                finally
                {
                    logGate.Release();
                }
                if (lineReceived != null)
                {
                    lineReceived.Invoke(line);
                }
            }
        }

        public ProcessStartInfo CreateStartInfo(string executable, IEnumerable<string> arguments,
            string workingDirectory, IReadOnlyDictionary<string, string> environment = null)
        {
            ProcessStartInfo start = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (string argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }
            start.Environment["PYTHONUTF8"] = "1";
            start.Environment["PYTHONUNBUFFERED"] = "1";
            if (environment != null)
            {
                foreach (KeyValuePair<string, string> entry in environment)
                {
                    start.Environment[entry.Key] = entry.Value;
                }
            }
            return start;
        }
    }
}

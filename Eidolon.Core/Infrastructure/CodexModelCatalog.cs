using System.Text;
using System.Text.Json;
using Eidolon.Core.Domain;

namespace Eidolon.Core.Infrastructure
{
    public class CodexModelCatalog
    {
        private readonly ProcessRunner _processes;
        private readonly CodexExecutableLocator _executables;

        public CodexModelCatalog(ProcessRunner processes, CodexExecutableLocator executables)
        {
            _processes = processes;
            _executables = executables;
        }

        public async Task<List<CodexModelInfo>> ReadAsync(string configuredPath, string dataDirectory,
            CancellationToken token)
        {
            string executable = _executables.Find(configuredPath);
            if (string.IsNullOrEmpty(executable) == true)
            {
                throw new StudioException(StudioMessageCode.CodexExecutableNotFound);
            }
            StringBuilder output = new StringBuilder();
            string logPath = Path.Combine(dataDirectory, "Logs", "CodexModels.log");
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await _processes.RunAsync(executable, new string[] { "debug", "models" }, dataDirectory,
                logPath, timeout.Token, stopWithParent: true,
                standardOutputChunkReceived: chunk => output.Append(chunk)).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            CodexModelCatalogData catalog = JsonSerializer.Deserialize<CodexModelCatalogData>(output.ToString());
            if (catalog == null)
            {
                throw new JsonException();
            }
            if (catalog.Models == null)
            {
                throw new JsonException();
            }
            List<CodexModelInfo> models = new List<CodexModelInfo>();
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            foreach (CodexModelInfo model in catalog.Models)
            {
                if (model == null)
                {
                    continue;
                }
                if (string.IsNullOrWhiteSpace(model.Model) == true)
                {
                    continue;
                }
                if (model.Visibility != "list")
                {
                    continue;
                }
                if (model.InputModalities != null)
                {
                    if (model.InputModalities.Count > 0)
                    {
                        if (model.InputModalities.Contains("image") == false)
                        {
                            continue;
                        }
                    }
                }
                if (names.Add(model.Model) == true)
                {
                    models.Add(model);
                }
            }
            return models;
        }
    }
}

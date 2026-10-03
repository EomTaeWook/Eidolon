using Eidolon.Core.Application;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Eidolon.Core.Domain;
using Dignus.Log;

namespace Eidolon.Core.Infrastructure
{
    public class ComfyEngine : IAsyncDisposable
    {
        private readonly HttpClient _http;
        private readonly ProcessRunner _runner;
        private readonly RuntimeInstaller _installer;
        private readonly ComfyWorkflowBuilder _workflow;
        private readonly FileDownloader _downloader;
        private readonly TimeProvider _time;
        private readonly SemaphoreSlim _lifecycleGate = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _engineLifetime;
        private Task _engineTask;
        private string _activeRoot = string.Empty;
        private int _activePort;
        private bool _activeCpu;

        public EngineConnection Connection { get; private set; }

        public ComfyEngine(HttpClient http, ProcessRunner runner, RuntimeInstaller installer,
            ComfyWorkflowBuilder workflow, FileDownloader downloader, TimeProvider time)
        {
            _http = http;
            _runner = runner;
            _installer = installer;
            _workflow = workflow;
            _downloader = downloader;
            _time = time;
        }

        public async Task EnsureReadyAsync(StudioSettings settings, IProgress<WorkProgress> progress,
            CancellationToken cancellationToken)
        {
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Uri server = new Uri(ComfyServerAddress.Root(settings));
                RuntimeLayout layout = null;
                if (string.IsNullOrWhiteSpace(settings.InstallDirectory) == false)
                {
                    layout = new RuntimeLayout(settings.InstallDirectory);
                }
                if (_engineTask != null && _engineTask.IsCompleted == false)
                {
                    if (layout != null && Connection != null && Connection.ServerAddress == ComfyServerAddress.Root(settings) &&
                        _activeRoot == layout.Root && _activePort == server.Port && _activeCpu == settings.UseCpu)
                    {
                        return;
                    }
                    await StopInternalAsync().ConfigureAwait(false);
                }
                else if (_engineTask != null)
                {
                    await StopInternalAsync().ConfigureAwait(false);
                }
                Connection = null;
                progress.Report(new WorkProgress(StudioMessageCode.ServerConnecting));
                if (await TryConnectExistingAsync(settings, cancellationToken).ConfigureAwait(false) == true)
                {
                    progress.Report(new WorkProgress(StudioMessageCode.ServerConnected, 100, false));
                    return;
                }
                if (ComfyServerAddress.CanStartLocally(settings) == false)
                {
                    throw new StudioException(StudioMessageCode.InvalidComfyServer);
                }
                IPAddress listenAddress = IPAddress.Loopback;
                if (IPAddress.TryParse(server.DnsSafeHost, out IPAddress parsedAddress) == true)
                {
                    listenAddress = parsedAddress;
                }
                TcpListener probe = new TcpListener(listenAddress, server.Port);
                try
                {
                    probe.Start();
                }
                catch (SocketException error)
                {
                    throw new StudioException(StudioMessageCode.EnginePortOccupied, error, server.Port);
                }
                finally
                {
                    probe.Stop();
                }
                if (layout == null)
                {
                    throw new StudioException(StudioMessageCode.InstallDirectoryRequired);
                }
                layout.EnsureInstalled();
                progress.Report(new WorkProgress(StudioMessageCode.EngineStarting));
                _engineLifetime?.Dispose();
                _engineLifetime = new CancellationTokenSource();
                List<string> arguments = new List<string>
                {
                    "-u", "main.py", "--listen", listenAddress.ToString(), "--port", server.Port.ToString(),
                    "--disable-auto-launch", "--extra-model-paths-config", Path.Combine(layout.Root, "ModelPaths.yaml")
                };
                if (settings.UseCpu == true)
                {
                    arguments.Add("--cpu");
                }
                _activeRoot = layout.Root;
                _activePort = server.Port;
                _activeCpu = settings.UseCpu;
                _engineTask = _runner.RunAsync(layout.ComfyPython, arguments, layout.ComfyDirectory,
                    Path.Combine(layout.LogDirectory, "Engine.log"), _engineLifetime.Token,
                    _installer.EnvironmentFor(layout), stopWithParent: true);
                DateTimeOffset deadlineAtUtc = _time.GetUtcNow().AddMinutes(3);
                while (_time.GetUtcNow() < deadlineAtUtc)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_engineTask.IsCompleted == true)
                    {
                        await _engineTask.ConfigureAwait(false);
                        throw new StudioException(StudioMessageCode.EngineExitedDuringStartup);
                    }
                    using CancellationTokenSource requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    requestTimeout.CancelAfter(TimeSpan.FromSeconds(2));
                    try
                    {
                        using HttpResponseMessage response = await _http.GetAsync(Address(settings, "system_stats"), requestTimeout.Token).ConfigureAwait(false);
                        if (response.IsSuccessStatusCode == true)
                        {
                            Connection = new EngineConnection(ComfyServerAddress.Root(settings), true, true);
                            progress.Report(new WorkProgress(StudioMessageCode.EngineReady, 100, false));
                            return;
                        }
                    }
                    catch (HttpRequestException)
                    {
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == false)
                    {
                    }
                    await Task.Delay(TimeSpan.FromMilliseconds(750), _time, cancellationToken).ConfigureAwait(false);
                }
                throw new StudioException(StudioMessageCode.EngineStartupTimedOut);
            }
            catch
            {
                await StopInternalAsync().ConfigureAwait(false);
                throw;
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        public async Task GenerateAsync(StudioSettings settings, JobRecord job, JobStore jobs,
            IProgress<WorkProgress> progress, CancellationToken cancellationToken)
        {
            await EnsureReadyAsync(settings, progress, cancellationToken).ConfigureAwait(false);
            string clientId = Guid.NewGuid().ToString("N");
            using ClientWebSocket socket = new ClientWebSocket();
            using CancellationTokenSource socketLifetime = new CancellationTokenSource();
            Task socketTask = Task.CompletedTask;
            try
            {
                using CancellationTokenSource connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                try
                {
                    UriBuilder socketAddress = new UriBuilder(Address(settings, "ws"));
                    string httpScheme = socketAddress.Scheme;
                    socketAddress.Scheme = "ws";
                    if (httpScheme == "https")
                    {
                        socketAddress.Scheme = "wss";
                    }
                    socketAddress.Query = "clientId=" + clientId;
                    await socket.ConnectAsync(socketAddress.Uri, connectTimeout.Token).ConfigureAwait(false);
                    socketTask = ReceiveProgressAsync(socket, job, progress, socketLifetime.Token);
                }
                catch (Exception error) when (error is WebSocketException || error is OperationCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress.Report(new WorkProgress(StudioMessageCode.ProgressConnectionUnavailable));
                }
                string promptId = Guid.ParseExact(job.Id, "N").ToString("D");
                JsonObject payload = new JsonObject
                {
                    ["prompt"] = _workflow.Build(job),
                    ["client_id"] = clientId,
                    ["prompt_id"] = promptId
                };
                // Persist our known ID before submission, so cancellation can address an accepted request even if its response is lost.
                job.EnginePromptId = promptId;
                jobs.Save(job);
                JsonObject response = await PostAsync(settings, "prompt", payload, cancellationToken).ConfigureAwait(false);
                job.EnginePromptId = response["prompt_id"].GetValue<string>();
                job.State = JobState.Running;
                jobs.Save(job);
                progress.Report(new WorkProgress(StudioMessageCode.GenerationQueued));
                DateTimeOffset deadlineAtUtc = _time.GetUtcNow().AddMinutes(30);
                while (_time.GetUtcNow() < deadlineAtUtc)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    JsonObject history = await GetAsync(settings, "history/" + Uri.EscapeDataString(job.EnginePromptId), cancellationToken).ConfigureAwait(false);
                    if (history[job.EnginePromptId] is JsonObject completed)
                    {
                        JsonObject status = completed["status"] as JsonObject;
                        if (status != null && status["status_str"]?.GetValue<string>() == "error")
                        {
                            string detail = string.Empty;
                            if (status["messages"] is JsonArray messages)
                            {
                                foreach (JsonNode item in messages)
                                {
                                    if (item is JsonArray pair && pair.Count >= 2 && pair[0]?.GetValue<string>() == "execution_error")
                                    {
                                        detail = pair[1]?["exception_message"]?.GetValue<string>() ?? detail;
                                    }
                                }
                            }
                            throw new StudioException(StudioMessageCode.GenerationFailed, detail);
                        }
                        if (completed["outputs"]?["7"]?["images"] is JsonArray images && images.Count > 0)
                        {
                            int index = 0;
                            foreach (JsonNode image in images)
                            {
                                index++;
                                string relative = Path.Combine("Images", index.ToString("D2") + ".png");
                                string filename = image["filename"].GetValue<string>();
                                string subfolder = image["subfolder"]?.GetValue<string>() ?? string.Empty;
                                string type = image["type"]?.GetValue<string>() ?? "output";
                                string url = Address(settings, "view?filename=" + Uri.EscapeDataString(filename) +
                                    "&subfolder=" + Uri.EscapeDataString(subfolder) + "&type=" + Uri.EscapeDataString(type));
                                await _downloader.DownloadAsync(url, jobs.ImagePath(job, relative), string.Empty,
                                    progress, cancellationToken).ConfigureAwait(false);
                                job.ImageFiles.Add(relative);
                                jobs.Save(job);
                            }
                            return;
                        }
                        if (status != null && status["completed"]?.GetValue<bool>() == true)
                        {
                            throw new StudioException(StudioMessageCode.GenerationOutputMissing);
                        }
                    }
                    await Task.Delay(TimeSpan.FromSeconds(1), _time, cancellationToken).ConfigureAwait(false);
                }
                throw new StudioException(StudioMessageCode.GenerationTimedOut);
            }
            catch
            {
                if (string.IsNullOrEmpty(job.EnginePromptId) == false)
                {
                    try
                    {
                        await CancelAsync(settings, job.EnginePromptId).ConfigureAwait(false);
                    }
                    catch (Exception cancelError)
                    {
                        if (Connection == null || Connection.OwnsProcess == false)
                        {
                            throw new StudioException(StudioMessageCode.ExternalCancellationFailed, cancelError, cancelError.Message);
                        }
                        else
                        {
                            progress.Report(new WorkProgress(StudioMessageCode.EngineCancellationFailed, 0, true, cancelError.Message));
                            await StopAsync().ConfigureAwait(false);
                        }
                    }
                }
                throw;
            }
            finally
            {
                socketLifetime.Cancel();
                socket.Abort();
                try
                {
                    await socketTask.ConfigureAwait(false);
                }
                catch (Exception error) when (error is OperationCanceledException || error is WebSocketException)
                {
                }
            }
        }

        private async Task CancelAsync(StudioSettings settings, string id)
        {
            using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await PostAsync(settings, "api/jobs/" + Uri.EscapeDataString(id) + "/cancel", new JsonObject(), timeout.Token).ConfigureAwait(false);
        }

        private async Task<bool> TryConnectExistingAsync(StudioSettings settings, CancellationToken token)
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                await ConnectExistingAsync(settings, timeout.Token).ConfigureAwait(false);
                return true;
            }
            catch (Exception error) when (error is HttpRequestException || error is System.Text.Json.JsonException ||
                error is OperationCanceledException || error is StudioException || error is InvalidOperationException)
            {
                token.ThrowIfCancellationRequested();
                return false;
            }
        }

        private async Task ConnectExistingAsync(StudioSettings settings, CancellationToken token)
        {
            using HttpResponseMessage response = await _http.GetAsync(Address(settings, "system_stats"), token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            string content = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            JsonObject stats = JsonNode.Parse(content) as JsonObject;
            if (stats == null || stats["system"] is not JsonObject || stats["devices"] is not JsonArray)
            {
                throw new StudioException(StudioMessageCode.InvalidComfyServer);
            }
            bool ownsProcess = _engineTask != null && _engineTask.IsCompleted == false;
            bool usesLocalAssets = ownsProcess;
            if (ComfyServerAddress.CanStartLocally(settings) == true &&
                string.IsNullOrWhiteSpace(settings.InstallDirectory) == false && stats["system"]["argv"] is JsonArray arguments)
            {
                RuntimeLayout layout = new RuntimeLayout(settings.InstallDirectory);
                string modelPaths = Path.Combine(layout.Root, "ModelPaths.yaml");
                for (int index = 0; index + 1 < arguments.Count; index++)
                {
                    if (arguments[index]?.GetValue<string>() == "--extra-model-paths-config" &&
                        string.Equals(arguments[index + 1]?.GetValue<string>(), modelPaths, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        usesLocalAssets = true;
                        break;
                    }
                }
            }
            Connection = new EngineConnection(ComfyServerAddress.Root(settings), ownsProcess, usesLocalAssets);
        }

        private async Task ReceiveProgressAsync(ClientWebSocket socket, JobRecord job,
            IProgress<WorkProgress> progress, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[8192];
            while (socket.State == WebSocketState.Open)
            {
                using MemoryStream message = new MemoryStream();
                WebSocketReceiveResult part;
                do
                {
                    part = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                    if (part.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }
                    if (part.MessageType == WebSocketMessageType.Text)
                    {
                        message.Write(buffer, 0, part.Count);
                        if (message.Length > 1024 * 1024)
                        {
                            throw new StudioException(StudioMessageCode.ProgressMessageTooLarge);
                        }
                    }
                }
                while (part.EndOfMessage == false);
                if (part.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }
                JsonObject data = JsonNode.Parse(message.ToArray()) as JsonObject;
                if (data?["data"]?["prompt_id"]?.GetValue<string>() != job.EnginePromptId)
                {
                    continue;
                }
                string type = data["type"].GetValue<string>();
                if (type == "progress")
                {
                    int value = data["data"]["value"].GetValue<int>();
                    int maximum = data["data"]["max"].GetValue<int>();
                    if (maximum > 0)
                    {
                        progress.Report(new WorkProgress(StudioMessageCode.GenerationProgress, value * 100.0 / maximum, false, value, maximum));
                    }
                }
                if (type == "executing")
                {
                    progress.Report(new WorkProgress(StudioMessageCode.ProcessingModel));
                }
            }
        }

        private async Task<JsonObject> GetAsync(StudioSettings settings, string route, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await _http.GetAsync(Address(settings, route), cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            string text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return JsonNode.Parse(text).AsObject();
        }

        private async Task<JsonObject> PostAsync(StudioSettings settings, string route, JsonObject payload,
            CancellationToken cancellationToken)
        {
            using StringContent content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await _http.PostAsync(Address(settings, route), content, cancellationToken).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode == false)
            {
                throw new StudioException(StudioMessageCode.EngineRequestFailed, (int)response.StatusCode, text);
            }
            if (string.IsNullOrWhiteSpace(text) == true)
            {
                return new JsonObject();
            }
            return JsonNode.Parse(text).AsObject();
        }

        private string Address(StudioSettings settings, string route)
        {
            return ComfyServerAddress.Route(settings, route);
        }

        public async Task RefreshExternalModelsAsync(StudioSettings settings, AssetLibrary assets, CancellationToken token)
        {
            List<string> checkpoints = await GetModelNamesAsync(settings, "checkpoints", token).ConfigureAwait(false);
            List<string> loras = await GetModelNamesAsync(settings, "loras", token).ConfigureAwait(false);
            await assets.SynchronizeExternalAsync(settings, checkpoints, loras, token).ConfigureAwait(false);
        }

        private async Task<List<string>> GetModelNamesAsync(StudioSettings settings, string folder, CancellationToken token)
        {
            using HttpResponseMessage response = await _http.GetAsync(Address(settings, "models/" + folder), token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                string node = "CheckpointLoaderSimple";
                string field = "ckpt_name";
                if (folder == "loras")
                {
                    node = "LoraLoader";
                    field = "lora_name";
                }
                JsonObject definition = await GetAsync(settings, "object_info/" + node, token).ConfigureAwait(false);
                JsonArray alternatives = definition[node]["input"]["required"][field][0].AsArray();
                return alternatives.Select(name => name.GetValue<string>()).ToList();
            }
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            JsonArray names = JsonNode.Parse(json).AsArray();
            return names.Select(name => name.GetValue<string>()).ToList();
        }

        public async Task StopAsync()
        {
            await _lifecycleGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await StopInternalAsync().ConfigureAwait(false);
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        private async Task StopInternalAsync()
        {
            Connection = null;
            if (_engineTask == null)
            {
                return;
            }
            _engineLifetime.Cancel();
            try
            {
                await _engineTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception error)
            {
                LogHelper.Error(error);
            }
            finally
            {
                _engineLifetime.Dispose();
                _engineLifetime = null;
                _engineTask = null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
            _lifecycleGate.Dispose();
        }
    }
}

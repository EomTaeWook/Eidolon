using Eidolon.Core.Application;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Eidolon.Core.Domain;

namespace Eidolon.Core.Infrastructure
{
    public class RuntimeInstaller
    {
        private readonly FileDownloader _downloader;
        private readonly ProcessRunner _processes;
        private readonly AtomicJsonFile _json;

        public RuntimeInstaller(FileDownloader downloader, ProcessRunner processes, AtomicJsonFile json)
        {
            _downloader = downloader;
            _processes = processes;
            _json = json;
        }

        public async Task InstallAsync(StudioSettings settings, IProgress<WorkProgress> progress,
            CancellationToken cancellationToken)
        {
            if (OperatingSystem.IsWindows() == false || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            {
                throw new StudioException(StudioMessageCode.UnsupportedInstallationPlatform);
            }
            settings.Validate();
            RuntimeLayout layout = new RuntimeLayout(settings.InstallDirectory);
            string marker = Path.Combine(layout.Root, "Runtime.json");
            RuntimeManifest previous = _json.Read<RuntimeManifest>(marker);
            if (previous == null && Directory.Exists(layout.Root) == true)
            {
                if (Directory.EnumerateFileSystemEntries(layout.Root).Any() == true)
                {
                    throw new StudioException(StudioMessageCode.InstallationDirectoryOccupied);
                }
            }
            if (previous != null)
            {
                if (previous.Owner != "Eidolon" || previous.SchemaVersion != 1)
                {
                    throw new StudioException(StudioMessageCode.InvalidInstallationOwner);
                }
            }
            Directory.CreateDirectory(layout.Root);
            RuntimeManifest manifest = new RuntimeManifest { UseCpu = settings.UseCpu };
            _json.Write(marker, manifest);
            try
            {
                string downloads = Path.Combine(layout.Root, "Downloads");
                string uvZip = Path.Combine(downloads, "uv-" + RuntimeLayout.UvVersion + ".zip");
                progress.Report(new WorkProgress(StudioMessageCode.DownloadingInstaller));
                await _downloader.DownloadAsync("https://releases.astral.sh/github/uv/releases/download/" + RuntimeLayout.UvVersion + "/uv-x86_64-pc-windows-msvc.zip",
                    uvZip, "ea1397797a0ca15f63516dd0f49c2dde9776db9be5861cab152ebe8ad199894d", progress, cancellationToken).ConfigureAwait(false);
                string tools = Path.Combine(layout.Root, "Tools");
                if (File.Exists(layout.UvExecutable) == false)
                {
                    await ExtractAsync(uvZip, tools, false, cancellationToken).ConfigureAwait(false);
                }
                Dictionary<string, string> environment = EnvironmentFor(layout);
                string installLog = Path.Combine(layout.LogDirectory, "Install.log");
                progress.Report(new WorkProgress(StudioMessageCode.InstallingPython));
                await _processes.RunAsync(layout.UvExecutable,
                    new[] { "python", "install", RuntimeLayout.PythonVersion, "--no-bin", "--no-registry" }, layout.Root,
                    installLog, cancellationToken, environment).ConfigureAwait(false);
                progress.Report(new WorkProgress(StudioMessageCode.DownloadingEngines));
                await InstallSourceAsync("Comfy-Org/ComfyUI", RuntimeLayout.ComfyVersion, layout.ComfyDirectory).ConfigureAwait(false);
                await InstallSourceAsync("kohya-ss/sd-scripts", RuntimeLayout.TrainingVersion, layout.TrainingDirectory).ConfigureAwait(false);
                progress.Report(new WorkProgress(StudioMessageCode.PreparingPythonEnvironments));
                foreach (string name in new[] { "ComfyUI", "Training" })
                {
                    string venv = Path.Combine(layout.Root, "Environments", name);
                    if (File.Exists(Path.Combine(venv, "Scripts", "python.exe")) == false)
                    {
                        await _processes.RunAsync(layout.UvExecutable,
                            new[] { "venv", "--seed", "--managed-python", "--python", RuntimeLayout.PythonVersion, venv },
                            layout.Root, installLog, cancellationToken, environment).ConfigureAwait(false);
                    }
                }
                string index = "https://download.pytorch.org/whl/cu128";
                if (settings.UseCpu == true)
                {
                    index = "https://download.pytorch.org/whl/cpu";
                }
                string constraint = Path.Combine(layout.Root, "TorchConstraints.txt");
                string suffix = "+cu128";
                if (settings.UseCpu == true)
                {
                    suffix = "+cpu";
                }
                await File.WriteAllTextAsync(constraint,
                    "torch==" + RuntimeLayout.TorchVersion + suffix + "\ntorchvision==" + RuntimeLayout.VisionVersion + suffix + "\n",
                    cancellationToken).ConfigureAwait(false);
                int environmentIndex = 0;
                foreach (KeyValuePair<string, string> entry in new Dictionary<string, string>
                {
                    { layout.ComfyPython, layout.ComfyDirectory },
                    { layout.TrainingPython, layout.TrainingDirectory }
                })
                {
                    environmentIndex++;
                    progress.Report(new WorkProgress(StudioMessageCode.InstallingDependencies, 0, true, environmentIndex));
                    await _processes.RunAsync(layout.UvExecutable,
                        new[] { "pip", "install", "--python", entry.Key, "--reinstall-package", "torch", "--reinstall-package", "torchvision",
                            "torch==" + RuntimeLayout.TorchVersion + suffix, "torchvision==" + RuntimeLayout.VisionVersion + suffix, "--index-url", index },
                        entry.Value, installLog, cancellationToken, environment).ConfigureAwait(false);
                    await _processes.RunAsync(layout.UvExecutable,
                        new[] { "pip", "install", "--python", entry.Key, "--constraint", constraint, "-r", "requirements.txt" },
                        entry.Value, installLog, cancellationToken, environment).ConfigureAwait(false);
                    await _processes.RunAsync(entry.Key,
                        new[] { "-c", "import torch, safetensors, PIL; print('Python dependencies ready; CUDA:', torch.cuda.is_available())" },
                        entry.Value, installLog, cancellationToken, environment).ConfigureAwait(false);
                }
                progress.Report(new WorkProgress(StudioMessageCode.ConnectingModelDirectories));
                Directory.CreateDirectory(layout.ModelsDirectory);
                Directory.CreateDirectory(layout.LorasDirectory);
                string config = "eidolon:\n    base_path: " + QuoteYaml(Path.Combine(layout.Root, "Models")) + "\n    checkpoints: checkpoints\n    loras: loras\n";
                await File.WriteAllTextAsync(Path.Combine(layout.Root, "ModelPaths.yaml"), config,
                    new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
                manifest.State = "Ready";
                _json.Write(marker, manifest);
                progress.Report(new WorkProgress(StudioMessageCode.InstallationCompleted, 100, false));
            }
            catch (Exception error)
            {
                manifest.State = "Failed";
                if (error is OperationCanceledException)
                {
                    manifest.State = "Cancelled";
                }
                manifest.Error = error.Message;
                _json.Write(marker, manifest);
                throw;
            }

            async Task InstallSourceAsync(string repository, string version, string destination)
            {
                if (Directory.Exists(destination) == true)
                {
                    return;
                }
                string archivePath = Path.Combine(layout.Root, "Downloads", Path.GetFileName(destination) + ".zip");
                await _downloader.DownloadAsync("https://github.com/" + repository + "/archive/refs/tags/" + version + ".zip",
                    archivePath, string.Empty, progress, cancellationToken).ConfigureAwait(false);
                await ExtractAsync(archivePath, destination, true, cancellationToken).ConfigureAwait(false);
            }
        }

        public Dictionary<string, string> EnvironmentFor(RuntimeLayout layout)
        {
            return new Dictionary<string, string>
            {
                { "UV_PYTHON_INSTALL_DIR", Path.Combine(layout.Root, "Python") },
                { "UV_CACHE_DIR", Path.Combine(layout.Root, "Cache", "uv") },
                { "UV_NO_PROGRESS", "1" },
                { "HF_HOME", Path.Combine(layout.Root, "Cache", "HuggingFace") }
            };
        }

        private string QuoteYaml(string value)
        {
            return "'" + value.Replace("'", "''") + "'";
        }

        private async Task ExtractAsync(string archivePath, string destination, bool stripRoot,
            CancellationToken cancellationToken)
        {
            string staging = destination + ".staging-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(staging);
            using ZipArchive archive = ZipFile.OpenRead(archivePath);
            long expandedBytes = 0;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                expandedBytes += entry.Length;
                if (expandedBytes > 2L * 1024 * 1024 * 1024)
                {
                    throw new StudioException(StudioMessageCode.ArchiveTooLarge);
                }
                if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                {
                    throw new StudioException(StudioMessageCode.ArchiveLinkUnsupported);
                }
                string relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                if (stripRoot == true)
                {
                    int separator = relative.IndexOf(Path.DirectorySeparatorChar);
                    if (separator < 0)
                    {
                        continue;
                    }
                    relative = relative.Substring(separator + 1);
                }
                if (string.IsNullOrEmpty(relative) == true)
                {
                    continue;
                }
                string path = Path.GetFullPath(Path.Combine(staging, relative));
                if (path.StartsWith(Path.GetFullPath(staging) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) == false)
                {
                    throw new StudioException(StudioMessageCode.ArchivePathInvalid);
                }
                if (entry.Name.Length == 0)
                {
                    Directory.CreateDirectory(path);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                await using Stream input = entry.Open();
                await using FileStream output = File.Create(path);
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            Directory.Move(staging, destination);
        }
    }
}

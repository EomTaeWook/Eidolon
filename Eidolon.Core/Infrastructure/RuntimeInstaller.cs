using Eidolon.Core.Application;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Eidolon.Core.Domain;

namespace Eidolon.Core.Infrastructure
{
    public class RuntimeInstaller
    {
        private readonly FileDownloader _downloader;
        private readonly ProcessRunner _processes;
        private readonly AtomicJsonFile _json;
        private readonly RuntimeModuleReader _modules;

        public RuntimeInstaller(FileDownloader downloader, ProcessRunner processes, AtomicJsonFile json, RuntimeModuleReader modules)
        {
            _downloader = downloader;
            _processes = processes;
            _json = json;
            _modules = modules;
        }

        public void EnsureInstalled(RuntimeLayout runtimeLayout, bool needsTraining = false)
        {
            if (OperatingSystem.IsWindows() == false)
            {
                throw new StudioException(StudioMessageCode.UnsupportedRuntimePlatform);
            }
            if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
            {
                throw new StudioException(StudioMessageCode.UnsupportedRuntimePlatform);
            }
            RuntimeManifest runtimeManifest = _json.Read<RuntimeManifest>(Path.Combine(runtimeLayout.Root, "Runtime.json"));
            if (runtimeManifest == null)
            {
                throw new StudioException(StudioMessageCode.RuntimeNotInstalled);
            }
            if (runtimeManifest.State != "Ready")
            {
                throw new StudioException(StudioMessageCode.RuntimeIncomplete);
            }
            if (runtimeManifest.Owner != "Eidolon")
            {
                throw new StudioException(StudioMessageCode.UnsupportedRuntimeSchema);
            }
            if (runtimeManifest.SchemaVersion != 1)
            {
                throw new StudioException(StudioMessageCode.UnsupportedRuntimeSchema);
            }
            if (runtimeManifest.ComfyVersion != RuntimeLayout.ComfyVersion)
            {
                throw new StudioException(StudioMessageCode.RuntimeVersionMismatch);
            }
            if (runtimeManifest.TrainingVersion != RuntimeLayout.TrainingVersion)
            {
                throw new StudioException(StudioMessageCode.RuntimeVersionMismatch);
            }
            if (File.Exists(runtimeLayout.ComfyPython) == false)
            {
                throw new StudioException(StudioMessageCode.GenerationRuntimeMissing);
            }
            if (File.Exists(Path.Combine(runtimeLayout.ComfyDirectory, "main.py")) == false)
            {
                throw new StudioException(StudioMessageCode.GenerationRuntimeMissing);
            }
            if (needsTraining == true)
            {
                if (File.Exists(runtimeLayout.TrainingPython) == false)
                {
                    throw new StudioException(StudioMessageCode.TrainingRuntimeMissing);
                }
            }
        }

        public IReadOnlyList<RuntimeModule> ReadModules(string installDirectory, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(installDirectory) == true)
            {
                return Array.Empty<RuntimeModule>();
            }
            RuntimeLayout layout = new(installDirectory);
            RuntimeManifest manifest = _json.Read<RuntimeManifest>(Path.Combine(layout.Root, "Runtime.json"));
            if (manifest != null && (manifest.Owner != "Eidolon" || manifest.SchemaVersion != 1))
            {
                throw new StudioException(StudioMessageCode.InvalidInstallationOwner);
            }
            return _modules.Read(layout, manifest, token);
        }

        public async Task InstallAsync(StudioSettings settings, IProgress<WorkProgress> progress, CancellationToken cancellationToken)
        {
            if (OperatingSystem.IsWindows() == false)
            {
                throw new StudioException(StudioMessageCode.UnsupportedInstallationPlatform);
            }
            if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
            {
                throw new StudioException(StudioMessageCode.UnsupportedInstallationPlatform);
            }
            settings.Validate();
            RuntimeLayout layout = new(settings.InstallDirectory);
            string marker = Path.Combine(layout.Root, "Runtime.json");
            EnsurePromptConverterPath(marker);
            EnsurePromptConverterPath(marker + ".bak");
            RuntimeManifest previous = _json.Read<RuntimeManifest>(marker);
            if (previous == null)
            {
                if (Directory.Exists(layout.Root) == true)
                {
                    if (Directory.EnumerateFileSystemEntries(layout.Root).Any() == true)
                    {
                        throw new StudioException(StudioMessageCode.InstallationDirectoryOccupied);
                    }
                }
            }
            if (previous != null)
            {
                if (previous.Owner != "Eidolon")
                {
                    throw new StudioException(StudioMessageCode.InvalidInstallationOwner);
                }
                if (previous.SchemaVersion != 1)
                {
                    throw new StudioException(StudioMessageCode.InvalidInstallationOwner);
                }
            }
            Directory.CreateDirectory(layout.Root);
            RuntimeManifest manifest = new() { UseCpu = settings.UseCpu };
            if (previous != null)
            {
                manifest.PromptConverterInstalled = previous.PromptConverterInstalled;
                manifest.PromptConverterVersion = previous.PromptConverterVersion;
                manifest.PromptConverterFiles = previous.PromptConverterFiles;
            }
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
                progress.Report(new WorkProgress(StudioMessageCode.DownloadingOutlineNodes));
                string outlineDirectory = layout.OutlineNodesDirectory;
                if (Directory.Exists(outlineDirectory) == false && Directory.Exists(outlineDirectory + ".disabled") == true)
                {
                    outlineDirectory += ".disabled";
                }
                await InstallSourceAsync("Fannovel16/comfyui_controlnet_aux", RuntimeLayout.OutlineNodeRevision,
                    outlineDirectory, false).ConfigureAwait(false);
                foreach (string name in new[] { "__init__.py", "requirements.txt" })
                {
                    if (File.Exists(Path.Combine(outlineDirectory, name)) == false)
                    {
                        throw new StudioException(StudioMessageCode.CustomNodeFileMissing, Path.Combine(outlineDirectory, name));
                    }
                }
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
                progress.Report(new WorkProgress(StudioMessageCode.InstallingOutlineDependencies));
                await _processes.RunAsync(layout.UvExecutable,
                    new[] { "pip", "install", "--python", layout.ComfyPython, "--constraint", constraint, "-r", "requirements.txt" },
                    outlineDirectory, installLog, cancellationToken, environment).ConfigureAwait(false);
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

            async Task InstallSourceAsync(string repository, string version, string destination, bool useTag = true)
            {
                if (Directory.Exists(destination) == true)
                {
                    return;
                }
                string reference = "refs/tags/" + version;
                string archiveName = Path.GetFileName(destination);
                if (useTag == false)
                {
                    reference = version;
                    archiveName += "-" + version;
                }
                string archivePath = Path.Combine(layout.Root, "Downloads", archiveName + ".zip");
                await _downloader.DownloadAsync("https://github.com/" + repository + "/archive/" + reference + ".zip",
                    archivePath, string.Empty, progress, cancellationToken).ConfigureAwait(false);
                await ExtractAsync(archivePath, destination, true, cancellationToken).ConfigureAwait(false);
            }
        }

        public bool IsPromptConverterInstalled(string installDirectory)
        {
            if (OperatingSystem.IsWindows() == false)
            {
                return false;
            }
            if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(installDirectory) == true)
            {
                return false;
            }
            RuntimeLayout runtimeLayout = new(installDirectory);
            string manifestPath = Path.Combine(runtimeLayout.Root, "Runtime.json");
            EnsurePromptConverterPath(manifestPath);
            RuntimeManifest runtimeManifest = _json.Read<RuntimeManifest>(manifestPath);
            if (runtimeManifest == null)
            {
                return false;
            }
            if (runtimeManifest.Owner != "Eidolon")
            {
                throw new StudioException(StudioMessageCode.InvalidInstallationOwner);
            }
            if (runtimeManifest.SchemaVersion != 1)
            {
                throw new StudioException(StudioMessageCode.InvalidInstallationOwner);
            }
            if (runtimeManifest.PromptConverterInstalled == false)
            {
                return false;
            }
            if (runtimeManifest.PromptConverterVersion != RuntimeLayout.PromptConverterVersion)
            {
                return false;
            }
            EnsurePromptConverterPath(runtimeLayout.LogDirectory);
            return HasPromptConverterFiles(runtimeLayout, runtimeManifest);
        }

        public async Task InstallPromptConverterAsync(StudioSettings settings, IProgress<WorkProgress> progress, CancellationToken cancellationToken)
        {
            if (OperatingSystem.IsWindows() == false)
            {
                throw new StudioException(StudioMessageCode.UnsupportedInstallationPlatform);
            }
            if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
            {
                throw new StudioException(StudioMessageCode.UnsupportedInstallationPlatform);
            }
            settings.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            RuntimeLayout runtimeLayout = new(settings.InstallDirectory);
            string manifestPath = Path.Combine(runtimeLayout.Root, "Runtime.json");
            EnsurePromptConverterPath(manifestPath);
            EnsurePromptConverterPath(manifestPath + ".bak");
            RuntimeManifest runtimeManifest = _json.Read<RuntimeManifest>(manifestPath);
            if (runtimeManifest == null)
            {
                if (Directory.Exists(runtimeLayout.Root) == true)
                {
                    if (Directory.EnumerateFileSystemEntries(runtimeLayout.Root).Any() == true)
                    {
                        throw new StudioException(StudioMessageCode.InstallationDirectoryOccupied);
                    }
                }
                runtimeManifest = new RuntimeManifest();
            }
            if (runtimeManifest.Owner != "Eidolon")
            {
                throw new StudioException(StudioMessageCode.InvalidInstallationOwner);
            }
            if (runtimeManifest.SchemaVersion != 1)
            {
                throw new StudioException(StudioMessageCode.InvalidInstallationOwner);
            }
            string executableDirectory = Path.GetDirectoryName(runtimeLayout.PromptConverterExecutable);
            string modelDirectory = Path.GetDirectoryName(runtimeLayout.PromptConverterModelPath);
            foreach (string componentDirectory in new[] { executableDirectory, modelDirectory })
            {
                EnsurePromptConverterPath(componentDirectory);
                if (string.IsNullOrEmpty(runtimeManifest.PromptConverterVersion) == true)
                {
                    if (Directory.Exists(componentDirectory) == true)
                    {
                        if (Directory.EnumerateFileSystemEntries(componentDirectory).Any() == true)
                        {
                            throw new StudioException(StudioMessageCode.InstallationDirectoryOccupied);
                        }
                    }
                }
            }
            if (string.IsNullOrEmpty(runtimeManifest.PromptConverterVersion) == false)
            {
                if (runtimeManifest.PromptConverterVersion != RuntimeLayout.PromptConverterVersion)
                {
                    throw new StudioException(StudioMessageCode.RuntimeVersionMismatch);
                }
            }
            runtimeManifest.PromptConverterInstalled = false;
            runtimeManifest.PromptConverterVersion = RuntimeLayout.PromptConverterVersion;
            runtimeManifest.PromptConverterFiles = new List<string>();
            _json.Write(manifestPath, runtimeManifest);
            progress.Report(new WorkProgress(StudioMessageCode.InstallingPromptConverter));
            try
            {
                string archivePath = Path.Combine(runtimeLayout.Root, "Downloads", "llama-" + RuntimeLayout.PromptConverterVersion + "-bin-win-cpu-x64.zip");
                EnsurePromptConverterPath(archivePath);
                EnsurePromptConverterPath(archivePath + ".part");
                await _downloader.DownloadAsync(RuntimeLayout.PromptConverterArchiveUrl, archivePath, RuntimeLayout.PromptConverterArchiveSha256, progress, cancellationToken).ConfigureAwait(false);
                EnsurePromptConverterPath(executableDirectory);
                if (Directory.Exists(executableDirectory) == false)
                {
                    await ExtractAsync(archivePath, executableDirectory, false, cancellationToken).ConfigureAwait(false);
                }
                using (ZipArchive archive = ZipFile.OpenRead(archivePath))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (entry.Name.Length == 0)
                        {
                            continue;
                        }
                        if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                        {
                            throw new StudioException(StudioMessageCode.ArchiveLinkUnsupported);
                        }
                        string extractedPath = Path.GetFullPath(Path.Combine(executableDirectory, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                        if (extractedPath.StartsWith(executableDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) == false)
                        {
                            throw new StudioException(StudioMessageCode.ArchivePathInvalid);
                        }
                        EnsurePromptConverterPath(extractedPath);
                        if (File.Exists(extractedPath) == false)
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(extractedPath));
                            await using Stream entryStream = entry.Open();
                            await using FileStream extractedStream = new(extractedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                            await entryStream.CopyToAsync(extractedStream, cancellationToken).ConfigureAwait(false);
                        }
                        if (new FileInfo(extractedPath).Length != entry.Length)
                        {
                            throw new StudioException(StudioMessageCode.PromptConverterNotInstalled);
                        }
                        await using (Stream archiveStream = entry.Open())
                        await using (FileStream installedStream = File.OpenRead(extractedPath))
                        {
                            byte[] archiveChecksum = await SHA256.HashDataAsync(archiveStream, cancellationToken).ConfigureAwait(false);
                            byte[] installedChecksum = await SHA256.HashDataAsync(installedStream, cancellationToken).ConfigureAwait(false);
                            if (archiveChecksum.AsSpan().SequenceEqual(installedChecksum) == false)
                            {
                                throw new StudioException(StudioMessageCode.DownloadChecksumMismatch, Path.GetFileName(extractedPath));
                            }
                        }
                        runtimeManifest.PromptConverterFiles.Add(Path.GetRelativePath(runtimeLayout.Root, extractedPath));
                    }
                }
                EnsurePromptConverterPath(runtimeLayout.PromptConverterModelPath);
                EnsurePromptConverterPath(runtimeLayout.PromptConverterModelPath + ".part");
                await _downloader.DownloadAsync(RuntimeLayout.PromptConverterModelUrl, runtimeLayout.PromptConverterModelPath, RuntimeLayout.PromptConverterModelSha256, progress, cancellationToken).ConfigureAwait(false);
                await DownloadNoticeAsync("https://huggingface.co/bartowski/Qwen_Qwen3-4B-Instruct-2507-GGUF/resolve/" + RuntimeLayout.PromptConverterModelRevision + "/README.md", Path.Combine(modelDirectory, "README.md"), "139ebb15c2601ca66b0d2820127b1bfaf491b084a582f9fa08286ba7daeeea82").ConfigureAwait(false);
                await DownloadNoticeAsync("https://huggingface.co/Qwen/Qwen3-4B-Instruct-2507/resolve/" + RuntimeLayout.PromptConverterOriginalModelRevision + "/README.md", Path.Combine(modelDirectory, "Qwen.README.md"), "8e3dd0c3b5b11897cc71092ccfe517bb7a9783479baa3665aad73c8d1a2041cd").ConfigureAwait(false);
                await DownloadNoticeAsync("https://huggingface.co/Qwen/Qwen3-4B-Instruct-2507/resolve/" + RuntimeLayout.PromptConverterOriginalModelRevision + "/LICENSE", Path.Combine(modelDirectory, "LICENSE"), "832dd9e00a68dd83b3c3fb9f5588dad7dcf337a0db50f7d9483f310cd292e92e").ConfigureAwait(false);
                await DownloadNoticeAsync("https://raw.githubusercontent.com/ggml-org/llama.cpp/" + RuntimeLayout.PromptConverterRevision + "/LICENSE", Path.Combine(executableDirectory, "llama.cpp.LICENSE"), "94f29bbed6a22c35b992c5c6ebf0e7c92f13b836b90f36f461c9cf2f0f1d010d").ConfigureAwait(false);
                runtimeManifest.PromptConverterFiles.Add(Path.GetRelativePath(runtimeLayout.Root, runtimeLayout.PromptConverterModelPath));
                if (HasPromptConverterFiles(runtimeLayout, runtimeManifest) == false)
                {
                    throw new StudioException(StudioMessageCode.PromptConverterNotInstalled);
                }
                cancellationToken.ThrowIfCancellationRequested();
                EnsurePromptConverterPath(manifestPath);
                EnsurePromptConverterPath(manifestPath + ".bak");
                runtimeManifest.PromptConverterInstalled = true;
                _json.Write(manifestPath, runtimeManifest);
                progress.Report(new WorkProgress(StudioMessageCode.PromptConverterReady, 100, false));
            }
            catch
            {
                runtimeManifest.PromptConverterInstalled = false;
                EnsurePromptConverterPath(manifestPath);
                EnsurePromptConverterPath(manifestPath + ".bak");
                _json.Write(manifestPath, runtimeManifest);
                throw;
            }

            async Task DownloadNoticeAsync(string sourceUrl, string destinationPath, string expectedSha256)
            {
                EnsurePromptConverterPath(destinationPath);
                EnsurePromptConverterPath(destinationPath + ".part");
                await _downloader.DownloadAsync(sourceUrl, destinationPath, expectedSha256, progress, cancellationToken).ConfigureAwait(false);
                runtimeManifest.PromptConverterFiles.Add(Path.GetRelativePath(runtimeLayout.Root, destinationPath));
            }
        }

        private bool HasPromptConverterFiles(RuntimeLayout runtimeLayout, RuntimeManifest runtimeManifest)
        {
            if (runtimeManifest.PromptConverterFiles == null)
            {
                return false;
            }
            if (runtimeManifest.PromptConverterFiles.Count == 0)
            {
                return false;
            }
            string executableDirectory = Path.GetDirectoryName(runtimeLayout.PromptConverterExecutable);
            string modelDirectory = Path.GetDirectoryName(runtimeLayout.PromptConverterModelPath);
            foreach (string requiredPath in new[] { runtimeLayout.PromptConverterExecutable, runtimeLayout.PromptConverterModelPath, Path.Combine(executableDirectory, "llama.cpp.LICENSE"), Path.Combine(modelDirectory, "README.md"), Path.Combine(modelDirectory, "Qwen.README.md"), Path.Combine(modelDirectory, "LICENSE") })
            {
                if (runtimeManifest.PromptConverterFiles.Contains(Path.GetRelativePath(runtimeLayout.Root, requiredPath), StringComparer.OrdinalIgnoreCase) == false)
                {
                    return false;
                }
                EnsurePromptConverterPath(requiredPath);
                if (File.Exists(requiredPath) == false)
                {
                    return false;
                }
                if (new FileInfo(requiredPath).Length == 0)
                {
                    return false;
                }
            }
            if (new FileInfo(runtimeLayout.PromptConverterModelPath).Length != RuntimeLayout.PromptConverterModelBytes)
            {
                return false;
            }
            foreach (string relativePath in runtimeManifest.PromptConverterFiles)
            {
                if (string.IsNullOrWhiteSpace(relativePath) == true)
                {
                    return false;
                }
                string installedPath = Path.GetFullPath(Path.Combine(runtimeLayout.Root, relativePath));
                bool ownedPath = installedPath.StartsWith(executableDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                if (ownedPath == false)
                {
                    ownedPath = installedPath.StartsWith(modelDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                }
                if (ownedPath == false)
                {
                    throw new StudioException(StudioMessageCode.InvalidInstallationOwner);
                }
                EnsurePromptConverterPath(installedPath);
                if (File.Exists(installedPath) == false)
                {
                    return false;
                }
            }
            return true;
        }

        private void EnsurePromptConverterPath(string path)
        {
            string currentPath = Path.GetFullPath(path);
            while (string.IsNullOrEmpty(currentPath) == false)
            {
                try
                {
                    if ((File.GetAttributes(currentPath) & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new StudioException(StudioMessageCode.InvalidInstallationOwner);
                    }
                }
                catch (FileNotFoundException)
                {
                }
                catch (DirectoryNotFoundException)
                {
                }
                currentPath = Path.GetDirectoryName(currentPath);
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

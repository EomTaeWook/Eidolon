using Eidolon.Core.Application;
using Eidolon.Core.Domain;

namespace Eidolon.Core.Infrastructure
{
    public class AssetLibrary
    {
        private readonly AtomicJsonFile _json;
        private readonly SafetensorsInspector _inspector;
        private readonly FileDownloader _downloader;
        private readonly TimeProvider _time;
        private readonly string _path;
        private readonly IReadOnlyList<ModelDownload> _downloads;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        public IReadOnlyList<ModelDownload> Downloads
        {
            get
            {
                return _downloads;
            }
        }

        public AssetLibrary(string dataDirectory, AtomicJsonFile json, SafetensorsInspector inspector,
            FileDownloader downloader, TimeProvider time)
        {
            _path = Path.Combine(dataDirectory, "Assets.json");
            _json = json;
            _inspector = inspector;
            _downloader = downloader;
            _time = time;
            const string sdxlLicense = "https://huggingface.co/stabilityai/stable-diffusion-xl-base-1.0/resolve/462165984030d82259a11f4367a4eed129e94a7b/LICENSE.md";
            _downloads = Array.AsReadOnly(new ModelDownload[]
            {
                new ModelDownload("SDXL Base 1.0", ModelFamily.Sdxl,
                    "stabilityai/stable-diffusion-xl-base-1.0", "462165984030d82259a11f4367a4eed129e94a7b",
                    "sd_xl_base_1.0.safetensors", 6938078334,
                    "31e35c80fc4829d14f90153f4c74cd59c90b779f6afe05a74cd6120b893f7e5b",
                    sdxlLicense, "License-SDXL.md"),
                new ModelDownload("Stable Diffusion 1.5", ModelFamily.StableDiffusion15,
                    "stable-diffusion-v1-5/stable-diffusion-v1-5", "451f4fe16113bff5a5d2269ed5ad43b0592e9a14",
                    "v1-5-pruned-emaonly.safetensors", 4265146304,
                    "6ce0161689b3853acaa03779ec93eafe75a02f4ced659bee03f50797806fa2fa",
                    "https://huggingface.co/spaces/CompVis/stable-diffusion-license/resolve/14d42d09bffd871b1666a084fc954a50cff72ac0/license.txt",
                    "License-SD15.txt"),
                new ModelDownload("RealVisXL 4.0", ModelFamily.Sdxl,
                    "SG161222/RealVisXL_V4.0", "26dfe44930964cd70d0a817b6d1cc945c130e38d",
                    "RealVisXL_V4.0.safetensors", 6938040706,
                    "912c9dc74f5855175c31a7993f863a043ac8dcc31732b324cd05d75cd7e16844",
                    sdxlLicense, "License-RealVisXL.md"),
                new ModelDownload("Illustrious XL 0.1", ModelFamily.Sdxl,
                    "OnomaAIResearch/Illustrious-xl-early-release-v0", "dca0dac303e6dc4b0c31d8001bc685b89b5d0204",
                    "Illustrious-XL-v0.1.safetensors", 6938040760,
                    "3e15ba00387db678ab4a099f75771c4f5ac67fda9e7100a01d263eaf30145aa9",
                    "https://freedevproject.org/faipl-1.0-sd/", "License-Illustrious.html", "TERM_OF_USE")
            });
        }

        public async Task<List<ModelAsset>> ListAsync(StudioSettings settings)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                AssetCatalog catalog = Load();
                if (ComfyServerAddress.UsesServerAssets(settings) == true)
                {
                    string server = ComfyServerAddress.Root(settings);
                    return catalog.Assets.Where(asset => asset.RuntimeRoot == server)
                        .Select(asset => asset.Copy()).OrderBy(asset => asset.Kind).ThenBy(asset => asset.Name).ToList();
                }
                if (string.IsNullOrWhiteSpace(settings.InstallDirectory) == true)
                {
                    return new List<ModelAsset>();
                }
                RuntimeLayout layout = new RuntimeLayout(settings.InstallDirectory);
                return catalog.Assets.Where(asset => asset.RuntimeRoot.Equals(layout.Root, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(layout.AssetPath(asset)) == true)
                    .Select(asset => asset.Copy()).OrderBy(asset => asset.Kind).ThenBy(asset => asset.Name).ToList();
            }
            finally
            {
                _gate.Release();
            }
        }

        public Task<ModelAsset> ImportAsync(StudioSettings settings, string sourcePath,
            ModelFamily family, string triggerWord, CancellationToken cancellationToken)
        {
            return ImportAsync(settings, sourcePath, AssetKind.Checkpoint, false, family, triggerWord, cancellationToken);
        }

        public Task<ModelAsset> ImportAsync(StudioSettings settings, string sourcePath, AssetKind kind,
            ModelFamily family, string triggerWord, CancellationToken cancellationToken)
        {
            return ImportAsync(settings, sourcePath, kind, true, family, triggerWord, cancellationToken);
        }

        private async Task<ModelAsset> ImportAsync(StudioSettings settings, string sourcePath, AssetKind kind,
            bool requireKind, ModelFamily family, string triggerWord, CancellationToken cancellationToken)
        {
            if (ComfyServerAddress.UsesServerAssets(settings) == true)
            {
                throw new StudioException(StudioMessageCode.ExternalAssetImportUnsupported);
            }
            RuntimeLayout layout = new RuntimeLayout(settings.InstallDirectory);
            layout.EnsureInstalled();
            if (Path.GetExtension(sourcePath).Equals(".safetensors", StringComparison.OrdinalIgnoreCase) == false)
            {
                throw new StudioException(StudioMessageCode.SafetensorsRequired);
            }
            ModelFamily detected = await Task.Run(() =>
            {
                if (requireKind == true)
                {
                    return _inspector.Inspect(sourcePath, kind);
                }
                return _inspector.Inspect(sourcePath, out kind);
            }, cancellationToken).ConfigureAwait(false);
            if (detected != ModelFamily.Unknown)
            {
                if (family != ModelFamily.Unknown && detected != family)
                {
                    throw new StudioException(StudioMessageCode.WeightsFamilyMismatch);
                }
                family = detected;
            }
            string name = Path.GetFileName(sourcePath);
            string directory = layout.ModelsDirectory;
            if (kind == AssetKind.Lora)
            {
                directory = layout.LorasDirectory;
            }
            Directory.CreateDirectory(directory);
            string destination = Path.Combine(directory, name);
            bool isSameFile = Path.GetFullPath(sourcePath).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase);
            if (isSameFile == false)
            {
                if (File.Exists(destination) == true)
                {
                    name = Path.GetFileNameWithoutExtension(sourcePath) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".safetensors";
                    destination = Path.Combine(directory, name);
                }
                string temporary = destination + ".importing";
                try
                {
                    await using (FileStream source = File.OpenRead(sourcePath))
                    await using (FileStream target = File.Create(temporary))
                    {
                        await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    File.Move(temporary, destination);
                }
                finally
                {
                    if (File.Exists(temporary) == true)
                    {
                        File.Delete(temporary);
                    }
                }
            }
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                AssetCatalog catalog = Load();
                ModelAsset asset = catalog.Assets.FirstOrDefault(item => item.RuntimeRoot.Equals(layout.Root, StringComparison.OrdinalIgnoreCase)
                    && item.Kind == kind && item.EngineName == name);
                if (asset == null)
                {
                    asset = new ModelAsset { AddedAtUtc = _time.GetUtcNow() };
                    catalog.Assets.Add(asset);
                }
                asset.Name = Path.GetFileNameWithoutExtension(name);
                asset.Kind = kind;
                asset.Family = family;
                asset.EngineName = name;
                asset.RuntimeRoot = layout.Root;
                asset.TriggerWord = triggerWord.Trim();
                _json.Write(_path, catalog);
                return asset.Copy();
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task ScanAsync(StudioSettings settings, IProgress<WorkProgress> progress,
            CancellationToken cancellationToken)
        {
            RuntimeLayout layout = new RuntimeLayout(settings.InstallDirectory);
            layout.EnsureInstalled();
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                AssetCatalog catalog = Load();
                catalog.Assets.RemoveAll(asset => asset.RuntimeRoot.Equals(layout.Root, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(layout.AssetPath(asset)) == false);
                foreach (AssetKind kind in Enum.GetValues<AssetKind>())
                {
                    string directory = layout.ModelsDirectory;
                    if (kind == AssetKind.Lora)
                    {
                        directory = layout.LorasDirectory;
                    }
                    Directory.CreateDirectory(directory);
                    foreach (string path in Directory.EnumerateFiles(directory, "*.safetensors", SearchOption.AllDirectories))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string engineName = Path.GetRelativePath(directory, path);
                        if (catalog.Assets.Any(asset => asset.RuntimeRoot.Equals(layout.Root, StringComparison.OrdinalIgnoreCase)
                            && asset.Kind == kind && asset.EngineName == engineName) == true)
                        {
                            continue;
                        }
                        progress.Report(new WorkProgress(StudioMessageCode.InspectingModel, 0, true, Path.GetFileName(path)));
                        ModelFamily family = _inspector.Inspect(path, kind);
                        catalog.Assets.Add(new ModelAsset
                        {
                            Name = Path.GetFileNameWithoutExtension(path),
                            EngineName = engineName,
                            RuntimeRoot = layout.Root,
                            Family = family,
                            Kind = kind,
                            AddedAtUtc = _time.GetUtcNow()
                        });
                    }
                }
                _json.Write(_path, catalog);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task UpdateAsync(ModelAsset asset, ModelFamily family, string triggerWord, string displayName = "")
        {
            if (family == ModelFamily.Unknown)
            {
                throw new StudioException(StudioMessageCode.AssetFamilyRequired);
            }
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                AssetCatalog catalog = Load();
                ModelAsset stored = catalog.Assets.Single(item => item.Id == asset.Id);
                if (ComfyServerAddress.IsExternalAsset(stored) == false)
                {
                    RuntimeLayout layout = new RuntimeLayout(Path.GetDirectoryName(stored.RuntimeRoot));
                    ModelFamily detected = _inspector.Inspect(layout.AssetPath(stored), stored.Kind);
                    if (detected != ModelFamily.Unknown && detected != family)
                    {
                        throw new StudioException(StudioMessageCode.InvalidAssetFamily);
                    }
                }
                stored.Family = family;
                stored.TriggerWord = triggerWord.Trim();
                if (string.IsNullOrWhiteSpace(displayName) == false)
                {
                    stored.Name = displayName.Trim();
                }
                _json.Write(_path, catalog);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task RemoveAsync(string id)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                AssetCatalog catalog = Load();
                catalog.Assets.RemoveAll(asset => asset.Id == id);
                _json.Write(_path, catalog);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task DeleteDownloadedAsync(StudioSettings settings, string id, CancellationToken cancellationToken)
        {
            if (ComfyServerAddress.UsesServerAssets(settings) == true)
            {
                throw new StudioException(StudioMessageCode.ExternalAssetImportUnsupported);
            }
            RuntimeLayout layout = new RuntimeLayout(settings.InstallDirectory);
            layout.EnsureInstalled();
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                AssetCatalog catalog = Load();
                ModelAsset asset = catalog.Assets.FirstOrDefault(item => item.Id == id);
                if (asset == null)
                {
                    throw new StudioException(StudioMessageCode.ModelRequired);
                }
                if (asset.Kind != AssetKind.Checkpoint)
                {
                    throw new StudioException(StudioMessageCode.CheckpointRequired);
                }
                if (asset.RuntimeRoot.Equals(layout.Root, StringComparison.OrdinalIgnoreCase) == false)
                {
                    throw new StudioException(StudioMessageCode.AssetSourceMismatch);
                }
                if (Downloads.Any(item => item.FileName.Equals(asset.EngineName, StringComparison.OrdinalIgnoreCase) == true) == false)
                {
                    throw new StudioException(StudioMessageCode.AssetPathInvalid);
                }
                string path = layout.AssetPath(asset);
                DirectoryInfo directory = new DirectoryInfo(layout.ModelsDirectory);
                while (directory != null)
                {
                    if (directory.Exists == true && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new StudioException(StudioMessageCode.AssetPathInvalid);
                    }
                    directory = directory.Parent;
                }
                cancellationToken.ThrowIfCancellationRequested();
                File.Delete(path);
                File.Delete(path + ".part");
                catalog.Assets.RemoveAll(item => item.Id == id);
                _json.Write(_path, catalog);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<ModelAsset> DownloadAsync(StudioSettings settings, string fileName, IProgress<WorkProgress> progress,
            CancellationToken cancellationToken)
        {
            if (ComfyServerAddress.UsesServerAssets(settings) == true)
            {
                throw new StudioException(StudioMessageCode.ExternalAssetImportUnsupported);
            }
            ModelDownload model = Downloads.Single(item => item.FileName == fileName);
            RuntimeLayout layout = new RuntimeLayout(settings.InstallDirectory);
            layout.EnsureInstalled();
            string destination = Path.Combine(layout.ModelsDirectory, model.FileName);
            await _downloader.DownloadAsync(model.DownloadUrl, destination,
                model.Sha256, progress, cancellationToken).ConfigureAwait(false);
            await _downloader.DownloadAsync(model.LicenseUrl, Path.Combine(layout.Root, "Models", model.LicenseFileName),
                string.Empty, progress, cancellationToken).ConfigureAwait(false);
            await _downloader.DownloadAsync(model.ModelCardUrl,
                Path.Combine(layout.Root, "Models", Path.GetFileNameWithoutExtension(model.FileName) + "-README.md"),
                string.Empty, progress, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(model.TermsOfUseUrl) == false)
            {
                await _downloader.DownloadAsync(model.TermsOfUseUrl,
                    Path.Combine(layout.Root, "Models", Path.GetFileNameWithoutExtension(model.FileName) + "-Terms.txt"),
                    string.Empty, progress, cancellationToken).ConfigureAwait(false);
            }
            return await ImportAsync(settings, destination, AssetKind.Checkpoint, model.Family,
                string.Empty, cancellationToken).ConfigureAwait(false);
        }

        public async Task SynchronizeExternalAsync(StudioSettings settings, IReadOnlyList<string> checkpoints,
            IReadOnlyList<string> loras, CancellationToken token)
        {
            string server = ComfyServerAddress.Root(settings);
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                AssetCatalog catalog = Load();
                foreach (AssetKind kind in Enum.GetValues<AssetKind>())
                {
                    IReadOnlyList<string> names = checkpoints;
                    if (kind == AssetKind.Lora)
                    {
                        names = loras;
                    }
                    catalog.Assets.RemoveAll(asset => asset.RuntimeRoot == server && asset.Kind == kind && names.Contains(asset.EngineName) == false);
                    foreach (string name in names)
                    {
                        token.ThrowIfCancellationRequested();
                        if (catalog.Assets.Any(asset => asset.RuntimeRoot == server && asset.Kind == kind && asset.EngineName == name) == true)
                        {
                            continue;
                        }
                        catalog.Assets.Add(new ModelAsset
                        {
                            RuntimeRoot = server,
                            EngineName = name,
                            Name = Path.GetFileNameWithoutExtension(name),
                            Kind = kind,
                            Family = ModelFamily.Unknown,
                            AddedAtUtc = _time.GetUtcNow()
                        });
                    }
                }
                _json.Write(_path, catalog);
            }
            finally
            {
                _gate.Release();
            }
        }

        private AssetCatalog Load()
        {
            AssetCatalog catalog = _json.Read<AssetCatalog>(_path);
            if (catalog == null)
            {
                return new AssetCatalog();
            }
            if (catalog.SchemaVersion != 1)
            {
                throw new StudioException(StudioMessageCode.UnsupportedAssetSchema);
            }
            if (catalog.Assets == null)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCatalog);
            }
            return catalog;
        }
    }
}

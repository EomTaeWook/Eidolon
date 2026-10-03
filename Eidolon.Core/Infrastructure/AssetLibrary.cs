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
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        public AssetLibrary(string dataDirectory, AtomicJsonFile json, SafetensorsInspector inspector,
            FileDownloader downloader, TimeProvider time)
        {
            _path = Path.Combine(dataDirectory, "Assets.json");
            _json = json;
            _inspector = inspector;
            _downloader = downloader;
            _time = time;
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

        public async Task<ModelAsset> ImportAsync(StudioSettings settings, string sourcePath, AssetKind kind,
            ModelFamily family, string triggerWord, CancellationToken cancellationToken)
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
            ModelFamily detected = await Task.Run(() => _inspector.Inspect(sourcePath, kind), cancellationToken).ConfigureAwait(false);
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

        public async Task<ModelAsset> DownloadStarterAsync(StudioSettings settings, IProgress<WorkProgress> progress,
            CancellationToken cancellationToken)
        {
            RuntimeLayout layout = new RuntimeLayout(settings.InstallDirectory);
            layout.EnsureInstalled();
            string destination = Path.Combine(layout.ModelsDirectory, "sd_xl_base_1.0.safetensors");
            const string repository = "https://huggingface.co/stabilityai/stable-diffusion-xl-base-1.0/resolve/462165984030d82259a11f4367a4eed129e94a7b/";
            await _downloader.DownloadAsync(repository + "sd_xl_base_1.0.safetensors", destination,
                "31e35c80fc4829d14f90153f4c74cd59c90b779f6afe05a74cd6120b893f7e5b", progress, cancellationToken).ConfigureAwait(false);
            await _downloader.DownloadAsync(repository + "LICENSE.md", Path.Combine(layout.Root, "Models", "License-SDXL.md"),
                string.Empty, progress, cancellationToken).ConfigureAwait(false);
            return await ImportAsync(settings, destination, AssetKind.Checkpoint, ModelFamily.Sdxl,
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

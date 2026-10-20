using Dignus.Log;
using Eidolon.Core.Application;
using Eidolon.Core.Domain;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Eidolon.Core.Infrastructure
{
    public class JobStore
    {
        private readonly AtomicJsonFile _json;
        private readonly RuntimeInstaller _runtimeInstaller;
        private readonly string _directory;
        private readonly string _imagesDirectory;
        private readonly object _gate = new object();

        public JobStore(string dataDirectory, AtomicJsonFile atomicJsonFile, RuntimeInstaller runtimeInstaller)
        {
            _json = atomicJsonFile;
            _runtimeInstaller = runtimeInstaller;
            _directory = Path.GetFullPath(Path.Combine(dataDirectory, "Jobs"));
            _imagesDirectory = Path.GetFullPath(Path.Combine(dataDirectory, "Images"));
        }

        public string DirectoryFor(string id)
        {
            if (Guid.TryParseExact(id, "N", out _) == false)
            {
                throw new StudioException(StudioMessageCode.InvalidJobId);
            }
            return Path.Combine(_directory, id);
        }

        internal string TrainingDatasetDirectory(JobRecord job, RuntimeLayout layout)
        {
            DirectoryFor(job.Id);
            string path = Path.GetFullPath(Path.Combine(layout.TrainingDataDirectory, job.Id));
            EnsureRegularDirectoryParents(path);
            if (Directory.Exists(path) == true)
            {
                throw new StudioException(StudioMessageCode.InvalidJobDirectory);
            }
            job.DatasetDirectory = path;
            Save(job);
            return path;
        }

        public int ClearTrainingDatasets(StudioSettings settings, CancellationToken token)
        {
            RuntimeLayout layout = new RuntimeLayout(settings.InstallDirectory);
            _runtimeInstaller.EnsureInstalled(layout);
            string root = Path.GetFullPath(layout.TrainingDataDirectory);
            EnsureRegularDirectoryParents(root);
            if (Directory.Exists(root) == false)
            {
                return 0;
            }
            lock (_gate)
            {
                List<string> targets = new List<string>();
                foreach (string directory in Directory.EnumerateDirectories(root))
                {
                    token.ThrowIfCancellationRequested();
                    if (Guid.TryParseExact(Path.GetFileName(directory), "N", out _) == false)
                    {
                        continue;
                    }
                    string path = Path.GetFullPath(directory);
                    if (string.Equals(Path.GetDirectoryName(path), root, StringComparison.OrdinalIgnoreCase) == false)
                    {
                        throw new StudioException(StudioMessageCode.InvalidJobDirectory);
                    }
                    EnsureRegularDirectoryTree(path, token);
                    targets.Add(path);
                }
                foreach (string target in targets)
                {
                    token.ThrowIfCancellationRequested();
                    EnsureRegularDirectoryParents(target);
                    Directory.Delete(target, true);
                }
                return targets.Count;
            }
        }

        private void EnsureRegularDirectoryParents(string path)
        {
            DirectoryInfo directory = new DirectoryInfo(path);
            while (directory != null)
            {
                if (directory.Exists == true)
                {
                    if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new StudioException(StudioMessageCode.InvalidJobDirectory);
                    }
                }
                directory = directory.Parent;
            }
        }

        private void EnsureRegularDirectoryTree(string directory, CancellationToken token)
        {
            EnsureRegularDirectoryParents(directory);
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                token.ThrowIfCancellationRequested();
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new StudioException(StudioMessageCode.InvalidJobDirectory);
                }
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    EnsureRegularDirectoryTree(path, token);
                }
            }
        }

        public void Save(JobRecord job)
        {
            lock (_gate)
            {
                _json.Write(Path.Combine(DirectoryFor(job.Id), "Job.json"), job);
            }
        }

        public JobRecord Load(string id)
        {
            lock (_gate)
            {
                JobRecord job = _json.Read<JobRecord>(Path.Combine(DirectoryFor(id), "Job.json"));
                if (job == null)
                {
                    throw new StudioException(StudioMessageCode.InvalidGenerationRecord);
                }
                if (job.SchemaVersion != 1)
                {
                    throw new StudioException(StudioMessageCode.UnsupportedJobSchema);
                }
                if (job.Id != id)
                {
                    throw new StudioException(StudioMessageCode.InvalidJobDirectory);
                }
                return job;
            }
        }

        public string OutputDirectory(string generationDirectory)
        {
            if (string.IsNullOrWhiteSpace(generationDirectory) == true)
            {
                return _imagesDirectory;
            }
            if (Path.IsPathFullyQualified(generationDirectory) == false)
            {
                throw new StudioException(StudioMessageCode.InvalidGenerationDirectory);
            }
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(generationDirectory.Trim()));
        }

        public void SaveAssetCollection(AssetCollection collection)
        {
            lock (_gate)
            {
                _json.Write(Path.Combine(DirectoryFor(collection.Id), "AssetCollection.json"), collection);
            }
        }

        public void DeleteAssetCollection(string collectionId, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                string directory = Path.GetFullPath(DirectoryFor(collectionId));
                if (string.Equals(Path.GetDirectoryName(directory), _directory, StringComparison.OrdinalIgnoreCase) == false)
                {
                    throw new StudioException(StudioMessageCode.InvalidJobDirectory);
                }
                EnsureRegularDirectoryTree(directory, cancellationToken);
                AssetCollection collection = LoadAssetCollection(collectionId);
                if (collection.Kind == AssetCreationKind.SpriteAnimation)
                {
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
                if (collection.State == JobState.Preparing)
                {
                    throw new StudioException(StudioMessageCode.GenerationRecordBusy);
                }
                if (collection.State == JobState.Running)
                {
                    throw new StudioException(StudioMessageCode.GenerationRecordBusy);
                }
                if (File.Exists(Path.Combine(directory, "Job.json")) == true)
                {
                    throw new StudioException(StudioMessageCode.InvalidJobDirectory);
                }
                cancellationToken.ThrowIfCancellationRequested();
                EnsureRegularDirectoryParents(directory);
                Directory.Delete(directory, true);
            }
        }

        public AssetCollection LoadAssetCollection(string collectionId)
        {
            lock (_gate)
            {
                AssetCollection collection = _json.Read<AssetCollection>(Path.Combine(DirectoryFor(collectionId), "AssetCollection.json"));
                if (collection == null)
                {
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
                if (collection.Id != collectionId)
                {
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
                if (collection.Format != AssetCollection.DocumentFormat)
                {
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
                if (collection.SchemaVersion != 1)
                {
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
                if (collection.Frames == null)
                {
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
                if (collection.Model == null)
                {
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
                if (collection.Loras == null)
                {
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
                if (collection.Settings == null)
                {
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
                if (Enum.IsDefined(collection.Kind) == false)
                {
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
                if (collection.Frames.Count < 2)
                {
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
                if (collection.Frames.Count > AssetCreationInput.MaximumFrames)
                {
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
                foreach (AssetFrame frame in collection.Frames)
                {
                    if (frame == null)
                    {
                        throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                    }
                    if (frame.ImagePath == null)
                    {
                        throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                    }
                    if (frame.Prompt == null)
                    {
                        throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                    }
                    if (frame.Label == null)
                    {
                        throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                    }
                    if (frame.Seed < 0)
                    {
                        throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                    }
                    if (frame.ImageSeed < 0)
                    {
                        throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                    }
                    if (frame.ErrorArguments == null)
                    {
                        throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                    }
                    if (Enum.IsDefined(frame.State) == false)
                    {
                        throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                    }
                    if (Enum.IsDefined(frame.ErrorCode) == false)
                    {
                        throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                    }
                }
                if (collection.Loras.Any(lora => lora == null) == true)
                {
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
                if (string.IsNullOrWhiteSpace(collection.Prompt) == true)
                {
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
                if (collection.Seed < 0)
                {
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
                if (Enum.IsDefined(collection.State) == false)
                {
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
                if (Enum.IsDefined(collection.ErrorCode) == false)
                {
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
                if (collection.ErrorArguments == null)
                {
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
                if (collection.FrameWidth < 16)
                {
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
                if (collection.FrameWidth > AssetCreationInput.MaximumFrameSize)
                {
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
                if (collection.FrameHeight < 16)
                {
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
                if (collection.FrameHeight > AssetCreationInput.MaximumFrameSize)
                {
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
                if (collection.Columns < 1)
                {
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
                if (collection.Columns > AssetCreationInput.MaximumFrames)
                {
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
                if (double.IsFinite(collection.ChangeStrength) == false)
                {
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
                if (collection.ChangeStrength < 0.05)
                {
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
                if (collection.ChangeStrength > 0.95)
                {
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
                if (collection.Kind == AssetCreationKind.FiveViews)
                {
                    if (collection.Frames.Count != AssetCreationInput.ViewCount)
                    {
                        throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                    }
                }
                if (collection.Kind == AssetCreationKind.FourViews)
                {
                    if (collection.Frames.Count != 4)
                    {
                        throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                    }
                }
                if (collection.Kind == AssetCreationKind.CharacterViews)
                {
                    if (collection.Frames.Count != 3)
                    {
                        throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                    }
                }
                if (collection.Kind == AssetCreationKind.ObjectViews)
                {
                    if (collection.Frames.Count != 3)
                    {
                        throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                    }
                }
                for (int frameIndex = 0; frameIndex < collection.Frames.Count; frameIndex++)
                {
                    if (collection.Frames[frameIndex].Number != frameIndex + 1)
                    {
                        throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                    }
                }
                collection.Settings.UseServerAssets = collection.UseServerAssets;
                return collection;
            }
        }

        public List<AssetCollection> LoadAssetCollections(CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                List<AssetCollection> collections = new List<AssetCollection>();
                if (Directory.Exists(_directory) == false)
                {
                    return collections;
                }
                foreach (string directory in Directory.EnumerateDirectories(_directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (File.Exists(Path.Combine(directory, "AssetCollection.json")) == false)
                    {
                        continue;
                    }
                    try
                    {
                        AssetCollection collection = LoadAssetCollection(Path.GetFileName(directory));
                        if (collection.Kind == AssetCreationKind.SpriteAnimation)
                        {
                            continue;
                        }
                        collections.Add(collection);
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        LogHelper.Error(error);
                    }
                }
                return collections.OrderByDescending(collection => collection.CreatedAtUtc).ToList();
            }
        }

        public void WriteAssetSheetManifest(string path, AssetSheetManifest manifest)
        {
            lock (_gate)
            {
                _json.WriteNew(path, manifest);
            }
        }

        public GenerationPage LoadGenerationPage(string generationDirectory, int number, int pageSize, CancellationToken token, bool includeImages = true, bool includeAssetCollections = true, bool groupAssetCollections = false)
        {
            if (number < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(number));
            }
            if (pageSize < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(pageSize));
            }
            lock (_gate)
            {
                List<FileInfo> files = ReadImageFiles(generationDirectory, token);
                if (includeImages == true)
                {
                    if (includeAssetCollections == true)
                    {
                        if (groupAssetCollections == false)
                        {
                            GenerationPage rawPage = new GenerationPage();
                            rawPage.TotalCount = files.Count;
                            rawPage.PageCount = Math.Max(1, (files.Count + pageSize - 1) / pageSize);
                            rawPage.Number = Math.Min(number, rawPage.PageCount);
                            if (files.Count > 0)
                            {
                                rawPage.LatestImagePath = files[0].FullName;
                            }
                            foreach (FileInfo file in files.Skip((rawPage.Number - 1) * pageSize).Take(pageSize))
                            {
                                GenerationImage image = FindGenerationImage(generationDirectory, file.FullName, token);
                                if (image != null)
                                {
                                    rawPage.Images.Add(image);
                                }
                            }
                            return rawPage;
                        }
                    }
                }
                List<GenerationImage> visibleImages = new List<GenerationImage>();
                List<GenerationImage> collectionImages = new List<GenerationImage>();
                foreach (FileInfo file in files)
                {
                    token.ThrowIfCancellationRequested();
                    GenerationImage image = FindGenerationImage(generationDirectory, file.FullName, token);
                    if (image == null)
                    {
                        continue;
                    }
                    bool belongsToCollection = false;
                    if (image.Metadata != null)
                    {
                        belongsToCollection = Guid.TryParseExact(image.Metadata.AssetCollectionId, "N", out _);
                        if (image.Metadata.AssetFrameNumber < 1)
                        {
                            belongsToCollection = false;
                        }
                        if (image.Metadata.AssetFrameNumber > AssetCreationInput.ViewCount)
                        {
                            belongsToCollection = false;
                        }
                        if (string.IsNullOrWhiteSpace(image.Metadata.AssetFrameLabel) == true)
                        {
                            belongsToCollection = false;
                        }
                    }
                    if (belongsToCollection == true)
                    {
                        if (includeAssetCollections == true)
                        {
                            collectionImages.Add(image);
                        }
                        continue;
                    }
                    if (includeImages == true)
                    {
                        visibleImages.Add(image);
                    }
                }
                if (groupAssetCollections == true)
                {
                    foreach (IGrouping<string, GenerationImage> collectionGroup in collectionImages.GroupBy(image => image.Metadata.AssetCollectionId, StringComparer.OrdinalIgnoreCase))
                    {
                        token.ThrowIfCancellationRequested();
                        List<GenerationImage> members = collectionGroup.OrderByDescending(image => image.CreatedAtUtc).ThenBy(image => image.FilePath, StringComparer.OrdinalIgnoreCase).ToList();
                        GenerationImage representative = members.FirstOrDefault(image => string.Equals(image.Metadata.AssetFrameLabel, "front", StringComparison.OrdinalIgnoreCase));
                        if (representative == null)
                        {
                            representative = members.FirstOrDefault(image => image.Metadata.AssetFrameNumber == 1);
                        }
                        if (representative == null)
                        {
                            representative = members[0];
                        }
                        visibleImages.Add(new GenerationImage
                        {
                            FilePath = representative.FilePath,
                            CreatedAtUtc = members[0].CreatedAtUtc,
                            Metadata = representative.Metadata,
                            MetadataError = representative.MetadataError,
                            CollectionImages = members
                        });
                    }
                }
                else
                {
                    visibleImages.AddRange(collectionImages);
                }
                visibleImages = visibleImages.OrderByDescending(image => image.CreatedAtUtc).ThenBy(image => image.FilePath, StringComparer.OrdinalIgnoreCase).ToList();
                GenerationPage page = new GenerationPage();
                page.TotalCount = visibleImages.Count;
                page.PageCount = Math.Max(1, (visibleImages.Count + pageSize - 1) / pageSize);
                page.Number = Math.Min(number, page.PageCount);
                if (visibleImages.Count > 0)
                {
                    page.LatestImagePath = visibleImages[0].FilePath;
                }
                page.Images = visibleImages.Skip((page.Number - 1) * pageSize).Take(pageSize).ToList();
                return page;
            }
        }

        public GenerationImage FindGenerationImage(string generationDirectory, string path, CancellationToken token)
        {
            lock (_gate)
            {
                token.ThrowIfCancellationRequested();
                path = ValidateImagePath(generationDirectory, path);
                if (File.Exists(path) == false)
                {
                    return null;
                }
                GenerationImage image = new GenerationImage
                {
                    FilePath = path,
                    CreatedAtUtc = new DateTimeOffset(File.GetCreationTimeUtc(path))
                };
                try
                {
                    image.Metadata = ReadGenerationMetadata(path);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    image.MetadataError = error;
                    LogHelper.Error(error);
                }
                return image;
            }
        }

        public List<string> LoadGenerationPaths(string generationDirectory, CancellationToken token)
        {
            lock (_gate)
            {
                return ReadImageFiles(generationDirectory, token).Select(file => file.FullName).ToList();
            }
        }

        internal GenerationMetadata ReadGenerationMetadata(string imagePath)
        {
            lock (_gate)
            {
                GenerationMetadata metadata = _json.Read<GenerationMetadata>(Path.ChangeExtension(imagePath, ".json"));
                if (metadata == null)
                {
                    return null;
                }
                if (metadata.Format != GenerationMetadata.DocumentFormat)
                {
                    throw new StudioException(StudioMessageCode.InvalidGenerationMetadata);
                }
                if (metadata.SchemaVersion != 1)
                {
                    throw new StudioException(StudioMessageCode.InvalidGenerationMetadata);
                }
                if (metadata.Model == null)
                {
                    throw new StudioException(StudioMessageCode.InvalidGenerationMetadata);
                }
                if (metadata.Loras == null)
                {
                    throw new StudioException(StudioMessageCode.InvalidGenerationMetadata);
                }
                if (metadata.Loras.Any(lora => lora == null) == true)
                {
                    throw new StudioException(StudioMessageCode.InvalidGenerationMetadata);
                }
                if (metadata.UserPrompt == null)
                {
                    throw new StudioException(StudioMessageCode.InvalidGenerationMetadata);
                }
                if (metadata.PositivePrompt == null)
                {
                    throw new StudioException(StudioMessageCode.InvalidGenerationMetadata);
                }
                if (metadata.NegativePrompt == null)
                {
                    throw new StudioException(StudioMessageCode.InvalidGenerationMetadata);
                }
                if (metadata.BasePositivePrompt == null)
                {
                    throw new StudioException(StudioMessageCode.InvalidGenerationMetadata);
                }
                if (Enum.IsDefined(metadata.GenerationBackend) == false)
                {
                    throw new StudioException(StudioMessageCode.InvalidGenerationMetadata);
                }
                if (metadata.ReferenceMode != GenerationReferenceMode.None)
                {
                    if (metadata.ReferenceMode != GenerationReferenceMode.Reimagine)
                    {
                        if (metadata.ReferenceMode != GenerationReferenceMode.Restyle)
                        {
                            throw new StudioException(StudioMessageCode.InvalidGenerationMetadata);
                        }
                    }
                    if (string.IsNullOrWhiteSpace(metadata.ReferenceImagePath) == true)
                    {
                        throw new StudioException(StudioMessageCode.InvalidGenerationMetadata);
                    }
                    if (metadata.GenerationBackend == GenerationBackend.ComfyUI)
                    {
                        if (double.IsFinite(metadata.Denoise) == false)
                        {
                            throw new StudioException(StudioMessageCode.InvalidGenerationMetadata);
                        }
                        if (metadata.Denoise < 0.05)
                        {
                            throw new StudioException(StudioMessageCode.InvalidGenerationMetadata);
                        }
                        if (metadata.Denoise > 0.95)
                        {
                            throw new StudioException(StudioMessageCode.InvalidGenerationMetadata);
                        }
                    }
                }
                return metadata;
            }
        }

        internal void SaveAssetFrameMetadata(AssetCollection collection, AssetFrame frame, JobRecord sourceJob, string imagePath, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (collection.Kind == AssetCreationKind.SpriteAnimation)
                {
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
                if (Guid.TryParseExact(collection.Id, "N", out _) == false)
                {
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
                if (frame.Number < 1)
                {
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
                if (frame.Number > collection.Frames.Count)
                {
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
                if (collection.Frames[frame.Number - 1] != frame)
                {
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
                if (string.IsNullOrWhiteSpace(frame.Label) == true)
                {
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
                if (sourceJob.Kind != JobKind.Generation)
                {
                    throw new StudioException(StudioMessageCode.InvalidGenerationRecord);
                }
                if (sourceJob.State != JobState.Completed)
                {
                    throw new StudioException(StudioMessageCode.InvalidGenerationRecord);
                }
                if (string.IsNullOrWhiteSpace(sourceJob.OutputDirectory) == true)
                {
                    throw new StudioException(StudioMessageCode.InvalidGenerationDirectory);
                }
                string outputDirectory = OutputDirectory(sourceJob.OutputDirectory);
                string publishedImagePath = ValidateImagePath(outputDirectory, imagePath);
                EnsureRegularDirectoryParents(outputDirectory);
                if (File.Exists(publishedImagePath) == false)
                {
                    throw new StudioException(StudioMessageCode.InvalidJobImagePath);
                }
                if ((File.GetAttributes(publishedImagePath) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new StudioException(StudioMessageCode.InvalidJobImagePath);
                }
                bool matchesSourceImage = false;
                foreach (string relativeImagePath in sourceJob.ImageFiles)
                {
                    if (string.Equals(ImagePath(sourceJob, relativeImagePath), publishedImagePath, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        matchesSourceImage = true;
                        break;
                    }
                }
                if (matchesSourceImage == false)
                {
                    throw new StudioException(StudioMessageCode.InvalidGenerationRecord);
                }
                string metadataPath = Path.ChangeExtension(publishedImagePath, ".json");
                if (File.Exists(metadataPath) == false)
                {
                    throw new StudioException(StudioMessageCode.InvalidGenerationMetadata);
                }
                if ((File.GetAttributes(metadataPath) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new StudioException(StudioMessageCode.InvalidJobImagePath);
                }
                string metadataBackupPath = metadataPath + ".bak";
                if (File.Exists(metadataBackupPath) == true)
                {
                    if ((File.GetAttributes(metadataBackupPath) & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new StudioException(StudioMessageCode.InvalidJobImagePath);
                    }
                }
                GenerationMetadata metadata = ReadGenerationMetadata(publishedImagePath);
                if (metadata == null)
                {
                    throw new StudioException(StudioMessageCode.InvalidGenerationMetadata);
                }
                if (metadata.SourceJobId != sourceJob.Id)
                {
                    throw new StudioException(StudioMessageCode.InvalidGenerationMetadata);
                }
                if (string.IsNullOrEmpty(metadata.AssetCollectionId) == false)
                {
                    if (metadata.AssetCollectionId != collection.Id)
                    {
                        throw new StudioException(StudioMessageCode.InvalidGenerationMetadata);
                    }
                }
                if (metadata.AssetFrameNumber != 0)
                {
                    if (metadata.AssetFrameNumber != frame.Number)
                    {
                        throw new StudioException(StudioMessageCode.InvalidGenerationMetadata);
                    }
                }
                if (string.IsNullOrEmpty(metadata.AssetFrameLabel) == false)
                {
                    if (metadata.AssetFrameLabel != frame.Label)
                    {
                        throw new StudioException(StudioMessageCode.InvalidGenerationMetadata);
                    }
                }
                bool needsUpdate = false;
                if (metadata.AssetCollectionId != collection.Id)
                {
                    needsUpdate = true;
                }
                if (metadata.AssetFrameNumber != frame.Number)
                {
                    needsUpdate = true;
                }
                if (metadata.AssetFrameLabel != frame.Label)
                {
                    needsUpdate = true;
                }
                if (needsUpdate == false)
                {
                    return;
                }
                JsonObject metadataDocument = _json.Read<JsonObject>(metadataPath);
                if (metadataDocument == null)
                {
                    throw new StudioException(StudioMessageCode.InvalidGenerationMetadata);
                }
                metadataDocument[nameof(GenerationMetadata.AssetCollectionId)] = collection.Id;
                metadataDocument[nameof(GenerationMetadata.AssetFrameNumber)] = frame.Number;
                metadataDocument[nameof(GenerationMetadata.AssetFrameLabel)] = frame.Label;
                cancellationToken.ThrowIfCancellationRequested();
                _json.Write(metadataPath, metadataDocument);
            }
        }

        public void DeleteGenerationImages(string generationDirectory, IReadOnlyList<string> paths, CancellationToken token)
        {
            lock (_gate)
            {
                string root = OutputDirectory(generationDirectory);
                if (Directory.Exists(root) == false)
                {
                    return;
                }
                if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new StudioException(StudioMessageCode.InvalidGenerationDirectory);
                }
                List<string> targets = paths.Select(path => ValidateImagePath(root, path))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                foreach (string target in targets)
                {
                    token.ThrowIfCancellationRequested();
                    EnsureRegularFile(target);
                    EnsureRegularFile(Path.ChangeExtension(target, ".json"));
                }
                foreach (string target in targets)
                {
                    token.ThrowIfCancellationRequested();
                    File.Delete(target);
                    File.Delete(Path.ChangeExtension(target, ".json"));
                }
            }
        }

        private void EnsureRegularFile(string path)
        {
            if (Directory.Exists(path) == true)
            {
                throw new StudioException(StudioMessageCode.InvalidJobImagePath);
            }
            if (File.Exists(path) == true && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new StudioException(StudioMessageCode.InvalidJobImagePath);
            }
        }

        private string ValidateImagePath(string generationDirectory, string path)
        {
            string root = OutputDirectory(generationDirectory);
            if (Path.IsPathFullyQualified(path) == false)
            {
                throw new StudioException(StudioMessageCode.InvalidJobImagePath);
            }
            path = Path.GetFullPath(path);
            if (string.Equals(Path.GetDirectoryName(path), root, StringComparison.OrdinalIgnoreCase) == false)
            {
                throw new StudioException(StudioMessageCode.InvalidJobImagePath);
            }
            if (string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase) == false)
            {
                throw new StudioException(StudioMessageCode.InvalidJobImagePath);
            }
            return path;
        }

        private List<FileInfo> ReadImageFiles(string generationDirectory, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string root = OutputDirectory(generationDirectory);
            List<FileInfo> files = new List<FileInfo>();
            if (Directory.Exists(root) == false)
            {
                return files;
            }
            foreach (FileInfo file in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.TopDirectoryOnly))
            {
                token.ThrowIfCancellationRequested();
                if (string.Equals(file.Extension, ".png", StringComparison.OrdinalIgnoreCase) == true
                    && (file.Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    files.Add(file);
                }
            }
            return files.OrderByDescending(file => file.CreationTimeUtc)
                .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public void MarkInterrupted(CancellationToken token)
        {
            lock (_gate)
            {
                foreach (AssetCollection collection in LoadAssetCollections(token))
                {
                    if (collection.State == JobState.Preparing || collection.State == JobState.Running)
                    {
                        collection.State = JobState.Interrupted;
                        foreach (AssetFrame frame in collection.Frames.Where(frame => frame.State == JobState.Running))
                        {
                            frame.State = JobState.Interrupted;
                        }
                        SaveAssetCollection(collection);
                    }
                }
                foreach (JobHeader header in ReadHeaders(token))
                {
                    token.ThrowIfCancellationRequested();
                    if (header.State == JobState.Preparing || header.State == JobState.Running)
                    {
                        JobRecord job = Load(header.Id);
                        job.State = JobState.Interrupted;
                        job.Error = string.Empty;
                        job.ErrorCode = StudioMessageCode.JobInterrupted;
                        Save(job);
                    }
                }
            }
        }

        public void MigrateGenerationMetadata(string generationDirectory, CancellationToken token)
        {
            lock (_gate)
            {
                foreach (JobHeader header in ReadHeaders(token))
                {
                    token.ThrowIfCancellationRequested();
                    if (header.Kind != JobKind.Generation)
                    {
                        continue;
                    }
                    if (header.HasImageMetadata == true)
                    {
                        continue;
                    }
                    if (header.State == JobState.Preparing)
                    {
                        continue;
                    }
                    if (header.State == JobState.Running)
                    {
                        continue;
                    }
                    try
                    {
                        JobRecord job = Load(header.Id);
                        List<string> publishedFiles = new List<string>();
                        bool legacy = string.IsNullOrWhiteSpace(job.OutputDirectory);
                        string output = job.OutputDirectory;
                        if (legacy == true)
                        {
                            output = OutputDirectory(generationDirectory);
                        }
                        int imageIndex = 0;
                        foreach (string file in job.ImageFiles)
                        {
                            token.ThrowIfCancellationRequested();
                            int currentIndex = imageIndex;
                            imageIndex++;
                            string source = ImagePath(job, file);
                            if (File.Exists(source) == false)
                            {
                                continue;
                            }
                            string destination = source;
                            if (legacy == true)
                            {
                                string name = job.Id + "_" + currentIndex.ToString(CultureInfo.InvariantCulture) + ".png";
                                destination = Path.Combine(output, name);
                                Directory.CreateDirectory(output);
                                string legacyMetadataPath = Path.ChangeExtension(destination, ".json");
                                if (File.Exists(legacyMetadataPath) == true)
                                {
                                    GenerationMetadata existing = _json.Read<GenerationMetadata>(legacyMetadataPath);
                                    if (existing.Format != GenerationMetadata.DocumentFormat)
                                    {
                                        throw new StudioException(StudioMessageCode.InvalidGenerationRecord);
                                    }
                                    if (existing.SourceJobId != job.Id)
                                    {
                                        throw new StudioException(StudioMessageCode.InvalidGenerationRecord);
                                    }
                                }
                                else
                                {
                                    if (File.Exists(destination) == true)
                                    {
                                        throw new StudioException(StudioMessageCode.InvalidGenerationRecord);
                                    }
                                    _json.WriteNew(legacyMetadataPath, new GenerationMetadata(job, job.StartedAtUtc));
                                }
                                if (File.Exists(destination) == false)
                                {
                                    string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                                    try
                                    {
                                        File.Copy(source, temporary, false);
                                        File.SetCreationTimeUtc(temporary, File.GetCreationTimeUtc(source));
                                        token.ThrowIfCancellationRequested();
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
                            }
                            string metadataPath = Path.ChangeExtension(destination, ".json");
                            if (File.Exists(metadataPath) == false)
                            {
                                DateTimeOffset generatedAtUtc = job.FinishedAtUtc;
                                if (generatedAtUtc == default)
                                {
                                    generatedAtUtc = job.StartedAtUtc;
                                }
                                _json.WriteNew(metadataPath, new GenerationMetadata(job, generatedAtUtc));
                            }
                            publishedFiles.Add(Path.GetFileName(destination));
                        }
                        if (legacy == true)
                        {
                            job.OutputDirectory = output;
                            job.ImageFiles = publishedFiles;
                        }
                        job.HasImageMetadata = true;
                        Save(job);
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        LogHelper.Error(error);
                    }
                }
                MigrateAssetFrameMetadata(generationDirectory, token);
            }
        }

        private void MigrateAssetFrameMetadata(string generationDirectory, CancellationToken cancellationToken)
        {
            string outputDirectory = OutputDirectory(generationDirectory);
            if (Directory.Exists(outputDirectory) == false)
            {
                return;
            }
            foreach (AssetCollection collection in LoadAssetCollections(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    string collectionDirectory = DirectoryFor(collection.Id);
                    EnsureRegularDirectoryParents(collectionDirectory);
                    string collectionPath = Path.Combine(collectionDirectory, "AssetCollection.json");
                    if ((File.GetAttributes(collectionPath) & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    LogHelper.Error(error);
                    continue;
                }
                foreach (AssetFrame frame in collection.Frames)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (Guid.TryParseExact(frame.SourceJobId, "N", out _) == false)
                    {
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(frame.ImagePath) == true)
                    {
                        continue;
                    }
                    try
                    {
                        string imagePath = ValidateImagePath(outputDirectory, frame.ImagePath);
                        if (File.Exists(imagePath) == false)
                        {
                            continue;
                        }
                        string sourceJobDirectory = DirectoryFor(frame.SourceJobId);
                        EnsureRegularDirectoryParents(sourceJobDirectory);
                        string sourceJobPath = Path.Combine(sourceJobDirectory, "Job.json");
                        if (File.Exists(sourceJobPath) == false)
                        {
                            continue;
                        }
                        if ((File.GetAttributes(sourceJobPath) & FileAttributes.ReparsePoint) != 0)
                        {
                            continue;
                        }
                        JobRecord sourceJob = Load(frame.SourceJobId);
                        if (string.IsNullOrWhiteSpace(sourceJob.OutputDirectory) == true)
                        {
                            continue;
                        }
                        if (string.Equals(OutputDirectory(sourceJob.OutputDirectory), outputDirectory, StringComparison.OrdinalIgnoreCase) == false)
                        {
                            continue;
                        }
                        SaveAssetFrameMetadata(collection, frame, sourceJob, imagePath, cancellationToken);
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        LogHelper.Error(error);
                    }
                }
            }
        }

        private List<JobHeader> ReadHeaders(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            List<JobHeader> headers = new List<JobHeader>();
            if (Directory.Exists(_directory) == false)
            {
                return headers;
            }
            foreach (string directory in Directory.EnumerateDirectories(_directory))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    JobHeader header = _json.Read<JobHeader>(Path.Combine(directory, "Job.json"));
                    if (header == null)
                    {
                        continue;
                    }
                    if (header.SchemaVersion != 1)
                    {
                        throw new StudioException(StudioMessageCode.UnsupportedJobSchema);
                    }
                    if (string.Equals(Path.GetFullPath(directory), DirectoryFor(header.Id), StringComparison.OrdinalIgnoreCase) == false)
                    {
                        throw new StudioException(StudioMessageCode.InvalidJobDirectory);
                    }
                    headers.Add(header);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    LogHelper.Error(error);
                }
            }
            return headers;
        }

        public string ImagePath(JobRecord job, string relativePath)
        {
            string directory = DirectoryFor(job.Id);
            if (string.IsNullOrWhiteSpace(job.OutputDirectory) == false)
            {
                directory = OutputDirectory(job.OutputDirectory);
            }
            return ResolveImagePath(directory, relativePath);
        }

        public string WorkingImagePath(JobRecord job, string relativePath)
        {
            return ResolveImagePath(DirectoryFor(job.Id), relativePath);
        }

        public async Task PublishImageAsync(JobRecord job, string workingFile, bool removeWorkingFile,
            DateTimeOffset generatedAtUtc, CancellationToken token)
        {
            string source = WorkingImagePath(job, workingFile);
            string relative = generatedAtUtc.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture)
                + "_" + Guid.NewGuid().ToString("N") + ".png";
            string destination = ImagePath(job, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            string metadataPath = Path.ChangeExtension(destination, ".json");
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            bool metadataWritten = false;
            bool imagePublished = false;
            try
            {
                await using (FileStream input = File.OpenRead(source))
                await using (FileStream output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 1024 * 1024, FileOptions.Asynchronous))
                {
                    await input.CopyToAsync(output, token).ConfigureAwait(false);
                    await output.FlushAsync(token).ConfigureAwait(false);
                    output.Flush(true);
                }
                token.ThrowIfCancellationRequested();
                lock (_gate)
                {
                    token.ThrowIfCancellationRequested();
                    _json.WriteNew(metadataPath, new GenerationMetadata(job, generatedAtUtc));
                    metadataWritten = true;
                    File.Move(temporary, destination);
                    imagePublished = true;
                    job.ImageFiles.Add(relative);
                    job.HasImageMetadata = true;
                    Save(job);
                }
                if (removeWorkingFile == true)
                {
                    File.Delete(source);
                }
            }
            finally
            {
                if (metadataWritten == true && imagePublished == false)
                {
                    File.Delete(metadataPath);
                }
                if (File.Exists(temporary) == true)
                {
                    File.Delete(temporary);
                }
            }
        }

        private string ResolveImagePath(string directory, string relativePath)
        {
            directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            string prefix = directory;
            if (Path.EndsInDirectorySeparator(prefix) == false)
            {
                prefix += Path.DirectorySeparatorChar;
            }
            string path = Path.GetFullPath(Path.Combine(directory, relativePath));
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == false)
            {
                throw new StudioException(StudioMessageCode.InvalidJobImagePath);
            }
            return path;
        }

        public void SetOutputDirectory(JobRecord job, string generationDirectory)
        {
            job.OutputDirectory = OutputDirectory(generationDirectory);
        }
    }
}

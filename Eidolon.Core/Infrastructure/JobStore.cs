using Dignus.Log;
using Eidolon.Core.Application;
using Eidolon.Core.Domain;
using System.Globalization;

namespace Eidolon.Core.Infrastructure
{
    public class JobStore
    {
        private readonly AtomicJsonFile _json;
        private readonly string _directory;
        private readonly string _imagesDirectory;
        private readonly object _gate = new object();

        public JobStore(string dataDirectory, AtomicJsonFile json)
        {
            _json = json;
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

        public GenerationPage LoadGenerationPage(string generationDirectory, int number, int pageSize,
            CancellationToken token)
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
                GenerationPage page = new GenerationPage();
                page.TotalCount = files.Count;
                page.PageCount = Math.Max(1, (files.Count + pageSize - 1) / pageSize);
                page.Number = Math.Min(number, page.PageCount);
                if (files.Count > 0)
                {
                    page.LatestImagePath = files[0].FullName;
                }
                foreach (FileInfo file in files.Skip((page.Number - 1) * pageSize).Take(pageSize))
                {
                    GenerationImage image = FindGenerationImage(generationDirectory, file.FullName, token);
                    if (image != null)
                    {
                        page.Images.Add(image);
                    }
                }
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
                    GenerationMetadata metadata = _json.Read<GenerationMetadata>(Path.ChangeExtension(path, ".json"));
                    if (metadata != null)
                    {
                        if (metadata.Format != GenerationMetadata.DocumentFormat || metadata.SchemaVersion != 1)
                        {
                            throw new StudioException(StudioMessageCode.InvalidGenerationRecord);
                        }
                        if (metadata.Model == null || metadata.Loras == null || metadata.Loras.Any(lora => lora == null) == true)
                        {
                            throw new StudioException(StudioMessageCode.InvalidGenerationRecord);
                        }
                        if (metadata.UserPrompt == null || metadata.PositivePrompt == null || metadata.NegativePrompt == null
                            || metadata.BasePositivePrompt == null)
                        {
                            throw new StudioException(StudioMessageCode.InvalidGenerationRecord);
                        }
                        if (metadata.ReferenceMode != GenerationReferenceMode.None
                            && (metadata.ReferenceMode != GenerationReferenceMode.Reimagine && metadata.ReferenceMode != GenerationReferenceMode.Restyle
                                || string.IsNullOrWhiteSpace(metadata.ReferenceImagePath) == true
                                || double.IsFinite(metadata.Denoise) == false || metadata.Denoise < 0.05 || metadata.Denoise > 0.95))
                        {
                            throw new StudioException(StudioMessageCode.InvalidGenerationRecord);
                        }
                        image.Metadata = metadata;
                    }
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
                    if (header.Kind != JobKind.Generation || header.HasImageMetadata == true
                        || header.State == JobState.Preparing || header.State == JobState.Running)
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
                                    if (existing.Format != GenerationMetadata.DocumentFormat || existing.SourceJobId != job.Id)
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

        private class JobHeader
        {
            public JobHeader()
            {
            }

            public int SchemaVersion { get; set; } = 1;
            public string Id { get; set; } = string.Empty;
            public JobKind Kind { get; set; }
            public JobState State { get; set; }
            public bool HasImageMetadata { get; set; }
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

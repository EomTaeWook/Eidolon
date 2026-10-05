using Eidolon.Core.Application;
using System.Text.Json;
using System.Text.Json.Serialization;
using Eidolon.Core.Domain;

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

        public void DeleteGenerations(IReadOnlyList<string> ids)
        {
            lock (_gate)
            {
                List<JobRecord> jobs = ids.Distinct().Select(Load).ToList();
                foreach (JobRecord job in jobs)
                {
                    if (job.Kind != JobKind.Generation)
                    {
                        throw new StudioException(StudioMessageCode.InvalidGenerationRecord);
                    }
                    if (job.State == JobState.Preparing || job.State == JobState.Running)
                    {
                        throw new StudioException(StudioMessageCode.GenerationRecordBusy);
                    }
                }
                foreach (JobRecord job in jobs)
                {
                    string directory = Path.GetFullPath(DirectoryFor(job.Id));
                    if (string.Equals(Path.GetDirectoryName(directory), _directory, StringComparison.OrdinalIgnoreCase) == false)
                    {
                        throw new StudioException(StudioMessageCode.InvalidJobDirectory);
                    }
                    PreserveLegacyImages(job, directory);
                    Directory.Delete(directory, true);
                }
            }
        }

        public List<JobRecord> LoadAll(bool markInterrupted = false)
        {
            lock (_gate)
            {
                List<JobRecord> jobs = new List<JobRecord>();
                if (Directory.Exists(_directory) == false)
                {
                    return jobs;
                }
                foreach (string directory in Directory.EnumerateDirectories(_directory))
                {
                    JobRecord job = _json.Read<JobRecord>(Path.Combine(directory, "Job.json"));
                    if (job == null)
                    {
                        continue;
                    }
                    if (job.SchemaVersion != 1)
                    {
                        throw new StudioException(StudioMessageCode.UnsupportedJobSchema);
                    }
                    if (Path.GetFullPath(directory) != DirectoryFor(job.Id))
                    {
                        throw new StudioException(StudioMessageCode.InvalidJobDirectory);
                    }
                    if (markInterrupted == true && (job.State == JobState.Preparing || job.State == JobState.Running))
                    {
                        job.State = JobState.Interrupted;
                        job.Error = string.Empty;
                        job.ErrorCode = StudioMessageCode.JobInterrupted;
                        _json.Write(Path.Combine(directory, "Job.json"), job);
                    }
                    jobs.Add(job);
                }
                return jobs.OrderByDescending(job => job.StartedAtUtc).ToList();
            }
        }

        public string ImagePath(JobRecord job, string relativePath)
        {
            string directory = DirectoryFor(job.Id);
            if (string.IsNullOrWhiteSpace(job.OutputDirectory) == false)
            {
                if (Path.IsPathFullyQualified(job.OutputDirectory) == false)
                {
                    throw new StudioException(StudioMessageCode.InvalidGenerationDirectory);
                }
                directory = Path.GetFullPath(job.OutputDirectory);
            }
            return ResolveImagePath(directory, relativePath);
        }

        public string WorkingImagePath(JobRecord job, string relativePath)
        {
            return ResolveImagePath(DirectoryFor(job.Id), relativePath);
        }

        public async Task PublishImageAsync(JobRecord job, string workingFile, bool removeWorkingFile,
            CancellationToken token)
        {
            string source = WorkingImagePath(job, workingFile);
            string relative = Guid.NewGuid().ToString("N") + ".png";
            string destination = ImagePath(job, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
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
                File.Move(temporary, destination);
                try
                {
                    job.ImageFiles.Add(relative);
                    Save(job);
                }
                catch
                {
                    job.ImageFiles.Remove(relative);
                    File.Delete(destination);
                    throw;
                }
                if (removeWorkingFile == true)
                {
                    File.Delete(source);
                }
            }
            finally
            {
                if (File.Exists(temporary) == true)
                {
                    File.Delete(temporary);
                }
            }
        }

        private void PreserveLegacyImages(JobRecord job, string directory)
        {
            string prefix = Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar;
            if (job.ImageFiles.Any(file => ImagePath(job, file).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) == false)
            {
                return;
            }
            Directory.CreateDirectory(_imagesDirectory);
            List<string> copies = new List<string>();
            List<string> previousFiles = job.ImageFiles;
            string previousDirectory = job.OutputDirectory;
            try
            {
                foreach (string file in previousFiles)
                {
                    string source = ImagePath(job, file);
                    if (File.Exists(source) == false)
                    {
                        continue;
                    }
                    string destination = Path.Combine(_imagesDirectory, Guid.NewGuid().ToString("N") + ".png");
                    copies.Add(destination);
                    File.Copy(source, destination, false);
                }
                job.OutputDirectory = _imagesDirectory;
                job.ImageFiles = copies.Select(Path.GetFileName).ToList();
                Save(job);
            }
            catch
            {
                job.OutputDirectory = previousDirectory;
                job.ImageFiles = previousFiles;
                foreach (string copy in copies)
                {
                    File.Delete(copy);
                }
                throw;
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
            if (string.IsNullOrWhiteSpace(generationDirectory) == true)
            {
                job.OutputDirectory = _imagesDirectory;
                return;
            }
            if (Path.IsPathFullyQualified(generationDirectory) == false)
            {
                throw new StudioException(StudioMessageCode.InvalidGenerationDirectory);
            }
            job.OutputDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(generationDirectory.Trim()));
        }
    }
}

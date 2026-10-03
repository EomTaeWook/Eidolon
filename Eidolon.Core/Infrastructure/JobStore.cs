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
        private readonly object _gate = new object();

        public JobStore(string dataDirectory, AtomicJsonFile json)
        {
            _json = json;
            _directory = Path.Combine(dataDirectory, "Jobs");
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
            string path = Path.GetFullPath(Path.Combine(directory, relativePath));
            if (path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) == false)
            {
                throw new StudioException(StudioMessageCode.InvalidJobImagePath);
            }
            return path;
        }
    }
}

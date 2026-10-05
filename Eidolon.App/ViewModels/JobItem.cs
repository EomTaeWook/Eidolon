using Eidolon.Core.Domain;
using Eidolon.App.Localization;

namespace Eidolon.App.ViewModels
{
    public class JobItem : ObservableObject
    {
        private readonly StringHelper _strings;
        public JobRecord Job { get; private set; }
        public string Label { get; private set; }
        public string Detail { get; private set; }
        public string Description { get; private set; }
        public string AppliedLoras
        {
            get
            {
                return string.Join(", ", Job.Loras.Select(lora => lora.Name));
            }
        }
        public bool HasAppliedLoras
        {
            get
            {
                return Job.Loras.Count > 0;
            }
        }

        public JobItem(JobRecord job, StringHelper strings)
        {
            _strings = strings;
            Job = job;
            Localize();
        }

        public void Localize()
        {
            JobRecord job = Job;
            Label = job.Title;
            if (Label.Length > 70)
            {
                Label = Label.Substring(0, 70) + "…";
            }
            string state;
            switch (job.State)
            {
                case JobState.Completed:
                    state = _strings.GetString("EidolonText210");
                    break;
                case JobState.Cancelled:
                    state = _strings.GetString("EidolonText211");
                    break;
                case JobState.Interrupted:
                    state = _strings.GetString("EidolonText212");
                    break;
                case JobState.RegistrationFailed:
                    state = _strings.GetString("EidolonText213");
                    break;
                case JobState.Preparing:
                    state = _strings.GetString("EidolonText214");
                    break;
                case JobState.Running:
                    state = _strings.GetString("EidolonText215");
                    break;
                default:
                    state = _strings.GetString("EidolonText216");
                    break;
            }
            Detail = job.StartedAtUtc.ToLocalTime().ToString("MM/dd HH:mm") + " · " + state + " · " + job.Model.Name;
            Description = job.Title + "\n" + Detail;
            if (job.Kind == JobKind.Generation)
            {
                Description += _strings.Format("EidolonText217", job.Width, job.Height, job.Seed, job.PositivePrompt, job.NegativePrompt);
                if (job.Loras.Count > 0)
                {
                    Description += "\nLoRA: " + string.Join(", ", job.Loras.Select(lora => lora.Name));
                }
            }
            else
            {
                Description += _strings.Format("EidolonText218", job.DatasetImageCount, job.TriggerWord);
                if (job.Loras.Count > 0)
                {
                    Description += "\n" + _strings.GetString("EidolonText262") + ": " + job.Loras[0].Name;
                }
            }
            if (job.ErrorCode != StudioMessageCode.None || string.IsNullOrWhiteSpace(job.Error) == false)
            {
                Description += "\n" + _strings.GetJobError(job);
            }
            Raise(nameof(Detail));
            Raise(nameof(Description));
        }
    }
}

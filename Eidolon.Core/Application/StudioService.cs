using Eidolon.Core.Domain;
using Eidolon.Core.Infrastructure;
using System.Text.RegularExpressions;

namespace Eidolon.Core.Application
{
    public class StudioService
    {
        private readonly AssetLibrary _assets;
        private readonly JobStore _jobs;
        private readonly ComfyEngine _engine;
        private readonly LoraTrainer _trainer;
        private readonly TimeProvider _time;
        private readonly ISeedProvider _seeds;
        private readonly BackgroundRemovalService _backgroundRemoval;
        private readonly SemaphoreSlim _gpuGate = new SemaphoreSlim(1, 1);

        public StudioService(AssetLibrary assets, JobStore jobs, ComfyEngine engine,
            LoraTrainer trainer, TimeProvider time, ISeedProvider seeds, BackgroundRemovalService backgroundRemoval)
        {
            _assets = assets;
            _jobs = jobs;
            _engine = engine;
            _trainer = trainer;
            _time = time;
            _seeds = seeds;
            _backgroundRemoval = backgroundRemoval;
        }

        public async Task<JobRecord> GenerateAsync(StudioSettings settings, string prompt, ModelAsset model,
            IReadOnlyList<ModelAsset> loras, bool removeBackground, IProgress<WorkProgress> progress, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(prompt) == true)
            {
                throw new StudioException(StudioMessageCode.PromptRequired);
            }
            ValidateModel(settings, model);
            foreach (ModelAsset lora in loras)
            {
                if (lora.Kind != AssetKind.Lora)
                {
                    throw new StudioException(StudioMessageCode.LoraRequired);
                }
                if (lora.Family != model.Family)
                {
                    throw new StudioException(StudioMessageCode.ModelFamilyMismatch);
                }
                ValidateFile(settings, lora);
            }
            GenerationPreset preset = new GenerationPreset(model.Family);
            JobRecord job = new JobRecord
            {
                Kind = JobKind.Generation,
                Title = prompt.Trim(),
                UserPrompt = prompt.Trim(),
                PositivePrompt = ComposePrompt(settings.PositivePrompt, prompt, loras),
                NegativePrompt = settings.NegativePrompt.Trim(),
                RemoveBackground = removeBackground,
                Model = model.Copy(),
                Loras = loras.Select(lora => lora.Copy()).ToList(),
                Seed = _seeds.Next(),
                Width = preset.Resolution,
                Height = preset.Resolution,
                Steps = preset.Steps,
                Guidance = preset.Guidance,
                Sampler = preset.Sampler,
                Scheduler = preset.Scheduler,
                StartedAtUtc = _time.GetUtcNow()
            };
            _jobs.SetOutputDirectory(job, settings.GenerationDirectory);
            await _gpuGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                _jobs.Save(job);
                await _engine.GenerateAsync(settings.Copy(), job, _jobs, progress, token).ConfigureAwait(false);
                foreach (string imageFile in job.OriginalImageFiles)
                {
                    await PublishImageAsync(job, imageFile, progress, token).ConfigureAwait(false);
                }
                job.State = JobState.Completed;
                job.FinishedAtUtc = _time.GetUtcNow();
                _jobs.Save(job);
                progress.Report(new WorkProgress(StudioMessageCode.GenerationCompleted, 100, false));
                return job;
            }
            catch (Exception error)
            {
                FinishFailure(job, error);
                throw;
            }
            finally
            {
                _gpuGate.Release();
            }
        }

        public async Task<ModelAsset> TrainAsync(StudioSettings settings, TrainingInput input,
            IProgress<WorkProgress> progress, CancellationToken token)
        {
            if (ComfyServerAddress.UsesServerAssets(settings) == true ||
                (_engine.Connection != null && _engine.Connection.OwnsProcess == false))
            {
                throw new StudioException(StudioMessageCode.LocalTrainingRuntimeRequired);
            }
            ValidateModel(settings, input.Model);
            if (input.ResumeLora != null)
            {
                if (input.ResumeLora.Kind != AssetKind.Lora)
                {
                    throw new StudioException(StudioMessageCode.LoraRequired);
                }
                if (input.ResumeLora.Family != input.Model.Family)
                {
                    throw new StudioException(StudioMessageCode.ModelFamilyMismatch);
                }
                ValidateFile(settings, input.ResumeLora);
            }
            if (Directory.Exists(input.ImageDirectory) == false)
            {
                throw new StudioException(StudioMessageCode.DatasetRequired);
            }
            if (string.IsNullOrWhiteSpace(input.Name) == true)
            {
                throw new StudioException(StudioMessageCode.LoraNameRequired);
            }
            if (string.IsNullOrWhiteSpace(input.TriggerWord) == true)
            {
                throw new StudioException(StudioMessageCode.TriggerRequired);
            }
            if (input.TriggerWord.Any(character => char.IsWhiteSpace(character) || character == ',') == true)
            {
                throw new StudioException(StudioMessageCode.InvalidTrigger);
            }
            if (input.Steps < TrainingPreset.MinimumSteps || input.Steps > TrainingPreset.MaximumSteps)
            {
                throw new StudioException(StudioMessageCode.InvalidTrainingSteps,
                    TrainingPreset.MinimumSteps, TrainingPreset.MaximumSteps);
            }
            JobRecord job = new JobRecord
            {
                Kind = JobKind.Training,
                Title = input.Name.Trim(),
                Model = input.Model.Copy(),
                TriggerWord = input.TriggerWord.Trim(),
                DatasetDescription = input.Description.Trim(),
                QuickTraining = input.Steps == TrainingPreset.QuickMaxSteps,
                Seed = _seeds.Next(),
                Steps = input.Steps,
                StartedAtUtc = _time.GetUtcNow()
            };
            if (input.ResumeLora != null)
            {
                job.Loras.Add(input.ResumeLora.Copy());
            }
            await _gpuGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                _jobs.Save(job);
                progress.Report(new WorkProgress(StudioMessageCode.ReleasingEngineMemory));
                await _engine.StopAsync().ConfigureAwait(false);
                string trainedPath = await _trainer.TrainAsync(settings.Copy(), job, input,
                    progress, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                job.TrainedFile = Path.GetRelativePath(_jobs.DirectoryFor(job.Id), trainedPath);
                _jobs.Save(job);
                progress.Report(new WorkProgress(StudioMessageCode.RegisteringTrainedLora));
                ModelAsset asset;
                try
                {
                    asset = await _assets.ImportAsync(settings, trainedPath, AssetKind.Lora, job.Model.Family,
                        job.TriggerWord, CancellationToken.None).ConfigureAwait(false);
                    await _assets.UpdateAsync(asset, asset.Family, asset.TriggerWord, job.Title).ConfigureAwait(false);
                    asset.Name = job.Title;
                }
                catch (Exception error)
                {
                    job.State = JobState.RegistrationFailed;
                    job.Error = error.Message;
                    job.ErrorCode = StudioMessageCode.LoraRegistrationFailed;
                    job.ErrorArguments = new object[] { error.Message };
                    job.FinishedAtUtc = _time.GetUtcNow();
                    _jobs.Save(job);
                    throw new StudioException(StudioMessageCode.LoraRegistrationFailed, error, error.Message);
                }
                job.State = JobState.Completed;
                job.FinishedAtUtc = _time.GetUtcNow();
                _jobs.Save(job);
                progress.Report(new WorkProgress(StudioMessageCode.TrainingCompleted, 100, false));
                return asset;
            }
            catch (Exception error)
            {
                if (job.State != JobState.RegistrationFailed)
                {
                    FinishFailure(job, error);
                }
                throw;
            }
            finally
            {
                _gpuGate.Release();
            }
        }

        private async Task PublishImageAsync(JobRecord job, string imageFile,
            IProgress<WorkProgress> progress, CancellationToken token)
        {
            bool removeWorkingFile = false;
            string workingFile = imageFile;
            if (job.RemoveBackground == true)
            {
                workingFile = Path.Combine("Processed", Guid.NewGuid().ToString("N") + ".png");
                await _backgroundRemoval.RemoveAsync(_jobs.WorkingImagePath(job, imageFile),
                    _jobs.WorkingImagePath(job, workingFile), progress, token).ConfigureAwait(false);
                removeWorkingFile = true;
            }
            await _jobs.PublishImageAsync(job, workingFile, removeWorkingFile, token).ConfigureAwait(false);
        }

        private void ValidateModel(StudioSettings settings, ModelAsset model)
        {
            settings.Validate();
            if (model == null)
            {
                throw new StudioException(StudioMessageCode.ModelRequired);
            }
            if (model.Kind != AssetKind.Checkpoint)
            {
                throw new StudioException(StudioMessageCode.CheckpointRequired);
            }
            if (model.Family == ModelFamily.Unknown)
            {
                throw new StudioException(StudioMessageCode.ModelFamilyRequired);
            }
            ValidateFile(settings, model);
        }

        private void ValidateFile(StudioSettings settings, ModelAsset asset)
        {
            if (ComfyServerAddress.UsesServerAssets(settings) == true)
            {
                if (asset.RuntimeRoot != ComfyServerAddress.Root(settings))
                {
                    throw new StudioException(StudioMessageCode.AssetSourceMismatch);
                }
                return;
            }
            RuntimeLayout layout = new RuntimeLayout(settings.InstallDirectory);
            layout.EnsureInstalled();
            if (asset.RuntimeRoot.Equals(layout.Root, StringComparison.OrdinalIgnoreCase) == false)
            {
                throw new StudioException(StudioMessageCode.AssetSourceMismatch);
            }
            if (File.Exists(layout.AssetPath(asset)) == false)
            {
                throw new StudioException(StudioMessageCode.ModelFileMissing);
            }
        }

        private string ComposePrompt(string positive, string prompt, IReadOnlyList<ModelAsset> loras)
        {
            List<string> parts = new List<string>();
            if (string.IsNullOrWhiteSpace(positive) == false)
            {
                parts.Add(positive.Trim());
            }
            parts.Add(prompt.Trim());
            foreach (ModelAsset lora in loras)
            {
                if (string.IsNullOrWhiteSpace(lora.TriggerWord) == false)
                {
                    foreach (string trigger in lora.TriggerWord.Split(',',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        string combined = string.Join(", ", parts);
                        string pattern = @"(?<![\p{L}\p{N}_])" + Regex.Escape(trigger) + @"(?![\p{L}\p{N}_])";
                        if (Regex.IsMatch(combined, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) == false)
                        {
                            parts.Add(trigger);
                        }
                    }
                }
            }
            return string.Join(", ", parts);
        }

        private void FinishFailure(JobRecord job, Exception error)
        {
            job.State = JobState.Failed;
            job.Error = error.Message;
            if (error is StudioException failure)
            {
                job.ErrorCode = failure.Code;
                job.ErrorArguments = failure.Arguments;
            }
            if (error is OperationCanceledException)
            {
                job.State = JobState.Cancelled;
                job.Error = string.Empty;
                job.ErrorCode = StudioMessageCode.JobCancelled;
            }
            job.FinishedAtUtc = _time.GetUtcNow();
            _jobs.Save(job);
        }
    }
}

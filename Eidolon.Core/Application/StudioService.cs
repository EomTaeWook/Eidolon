using Eidolon.Core.Domain;
using Eidolon.Core.Infrastructure;
using System.Text.RegularExpressions;
using SkiaSharp;
using Dignus.Log;

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
            IReadOnlyList<ModelAsset> loras, bool removeBackground, long seed, IProgress<WorkProgress> progress, CancellationToken token)
        {
            return await GenerateAsync(settings, prompt, model, loras, removeBackground, seed, null, progress, token).ConfigureAwait(false);
        }

        public async Task<JobRecord> GenerateAsync(StudioSettings settings, string prompt, ModelAsset model,
            IReadOnlyList<ModelAsset> loras, bool removeBackground, long seed, GenerationReferenceInput reference,
            IProgress<WorkProgress> progress, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(prompt) == true)
            {
                throw new StudioException(StudioMessageCode.PromptRequired);
            }
            if (seed < 0)
            {
                throw new StudioException(StudioMessageCode.InvalidGenerationSeed, 0, long.MaxValue);
            }
            if (reference != null)
            {
                if (reference.Mode != GenerationReferenceMode.Reimagine && reference.Mode != GenerationReferenceMode.Restyle
                    || double.IsFinite(reference.ChangeStrength) == false || reference.ChangeStrength < 0.05 || reference.ChangeStrength > 0.95)
                {
                    throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
                }
                if (reference.ImageData == null || reference.ImageData.Length == 0
                    || reference.ImageData.Length > GenerationReferenceInput.MaximumImageBytes)
                {
                    throw new StudioException(StudioMessageCode.InvalidReferenceImage);
                }
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
                BasePositivePrompt = settings.PositivePrompt.Trim(),
                HasBasePositivePrompt = true,
                PositivePrompt = ComposePrompt(settings.PositivePrompt, prompt, loras),
                NegativePrompt = settings.NegativePrompt.Trim(),
                RemoveBackground = removeBackground,
                Model = model.Copy(),
                Loras = loras.Select(lora => lora.Copy()).ToList(),
                Seed = seed,
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
                if (reference != null)
                {
                    progress.Report(new WorkProgress(StudioMessageCode.PreparingReferenceImage));
                    await PrepareReferenceImageAsync(job, reference, preset, token).ConfigureAwait(false);
                    _jobs.Save(job);
                }
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

        private async Task PrepareReferenceImageAsync(JobRecord job, GenerationReferenceInput reference,
            GenerationPreset preset, CancellationToken token)
        {
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                using SKMemoryStream input = new SKMemoryStream(reference.ImageData);
                using SKCodec codec = SKCodec.Create(input);
                if (codec == null || codec.Info.Width < 1 || codec.Info.Height < 1
                    || (long)codec.Info.Width * codec.Info.Height > GenerationReferenceInput.MaximumImagePixels)
                {
                    throw new StudioException(StudioMessageCode.InvalidReferenceImage);
                }
                using SKBitmap source = SKBitmap.Decode(codec);
                if (source == null)
                {
                    throw new StudioException(StudioMessageCode.InvalidReferenceImage);
                }
                int sourceWidth = source.Width;
                int sourceHeight = source.Height;
                if (codec.EncodedOrigin == SKEncodedOrigin.LeftTop || codec.EncodedOrigin == SKEncodedOrigin.RightTop
                    || codec.EncodedOrigin == SKEncodedOrigin.RightBottom || codec.EncodedOrigin == SKEncodedOrigin.LeftBottom)
                {
                    sourceWidth = source.Height;
                    sourceHeight = source.Width;
                }
                double scale = (double)preset.Resolution / Math.Max(sourceWidth, sourceHeight);
                job.Width = Math.Max(64, (int)Math.Round(sourceWidth * scale / 8) * 8);
                job.Height = Math.Max(64, (int)Math.Round(sourceHeight * scale / 8) * 8);
                using SKBitmap prepared = new SKBitmap(job.Width, job.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
                using (SKCanvas canvas = new SKCanvas(prepared))
                {
                    canvas.Clear(SKColors.White);
                    canvas.Scale((float)job.Width / sourceWidth, (float)job.Height / sourceHeight);
                    switch (codec.EncodedOrigin)
                    {
                        case SKEncodedOrigin.TopRight:
                            canvas.Translate(source.Width, 0);
                            canvas.Scale(-1, 1);
                            break;
                        case SKEncodedOrigin.BottomRight:
                            canvas.Translate(source.Width, source.Height);
                            canvas.RotateDegrees(180);
                            break;
                        case SKEncodedOrigin.BottomLeft:
                            canvas.Translate(0, source.Height);
                            canvas.Scale(1, -1);
                            break;
                        case SKEncodedOrigin.LeftTop:
                            canvas.RotateDegrees(90);
                            canvas.Scale(1, -1);
                            break;
                        case SKEncodedOrigin.RightTop:
                            canvas.Translate(source.Height, 0);
                            canvas.RotateDegrees(90);
                            break;
                        case SKEncodedOrigin.RightBottom:
                            canvas.Translate(source.Height, source.Width);
                            canvas.RotateDegrees(90);
                            canvas.Scale(-1, 1);
                            break;
                        case SKEncodedOrigin.LeftBottom:
                            canvas.Translate(0, source.Width);
                            canvas.RotateDegrees(270);
                            break;
                    }
                    using SKImage image = SKImage.FromBitmap(source);
                    canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear));
                }
                token.ThrowIfCancellationRequested();
                string destination = _jobs.WorkingImagePath(job, Path.Combine("Inputs", "Reference.png"));
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                using SKImage output = SKImage.FromBitmap(prepared);
                using SKData encoded = output.Encode(SKEncodedImageFormat.Png, 100);
                if (encoded == null)
                {
                    throw new StudioException(StudioMessageCode.InvalidReferenceImage);
                }
                using (FileStream stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write))
                {
                    encoded.SaveTo(stream);
                }
                job.ReferenceMode = reference.Mode;
                job.ReferenceImageName = reference.ImageName;
                job.ReferenceImagePath = destination;
                job.Denoise = reference.ChangeStrength;
                token.ThrowIfCancellationRequested();
            }, token).ConfigureAwait(false);
        }

        public async Task<string> PrepareTrainingDatasetAsync(TrainingInput input, string parentDirectory,
            IProgress<WorkProgress> progress, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (input.Images.Count == 0)
            {
                throw new StudioException(StudioMessageCode.TrainingImagesMissing);
            }
            if (Directory.Exists(parentDirectory) == false)
            {
                throw new StudioException(StudioMessageCode.DatasetRequired);
            }
            foreach (TrainingImageInput image in input.Images)
            {
                if (Enum.IsDefined(typeof(TrainingBackground), image.Background) == false)
                {
                    throw new StudioException(StudioMessageCode.InvalidTrainingBackground);
                }
            }
            string name = "Eidolon-Training-" + _time.GetUtcNow().ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" +
                Guid.NewGuid().ToString("N").Substring(0, 8);
            string directory = Path.Combine(Path.GetFullPath(parentDirectory), name);
            await _trainer.PrepareDatasetAsync(input, directory, progress, token).ConfigureAwait(false);
            return directory;
        }

        public async Task<ModelAsset> TrainAsync(StudioSettings settings, TrainingInput input,
            IProgress<WorkProgress> progress, IProgress<Exception> engineRecoveryErrors,
            CancellationToken token, CancellationToken lifetimeToken)
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
            if (input.Images.Count > 0)
            {
                foreach (TrainingImageInput image in input.Images)
                {
                    if (File.Exists(image.FilePath) == false)
                    {
                        throw new StudioException(StudioMessageCode.TrainingImagesMissing);
                    }
                    if (Enum.IsDefined(typeof(TrainingBackground), image.Background) == false)
                    {
                        throw new StudioException(StudioMessageCode.InvalidTrainingBackground);
                    }
                }
            }
            else if (Directory.Exists(input.ImageDirectory) == false)
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
            if (Enum.IsDefined(typeof(TrainingBackground), input.Background) == false)
            {
                throw new StudioException(StudioMessageCode.InvalidTrainingBackground);
            }
            JobRecord job = new JobRecord
            {
                Kind = JobKind.Training,
                Title = input.Name.Trim(),
                Model = input.Model.Copy(),
                TriggerWord = input.TriggerWord.Trim(),
                DatasetDescription = input.Description.Trim(),
                DatasetBackground = input.Background,
                DatasetImages = input.Images.Select(image => image.Copy()).ToList(),
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
                try
                {
                    if (lifetimeToken.IsCancellationRequested == false)
                    {
                        try
                        {
                            await _engine.EnsureReadyAsync(settings, progress, lifetimeToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested == true)
                        {
                        }
                        catch (Exception error)
                        {
                            LogHelper.Error(error);
                            engineRecoveryErrors.Report(error);
                        }
                    }
                }
                finally
                {
                    _gpuGate.Release();
                }
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
            await _jobs.PublishImageAsync(job, workingFile, removeWorkingFile, _time.GetUtcNow(), token).ConfigureAwait(false);
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

        public string GetBasePositivePrompt(GenerationMetadata job)
        {
            if (job.HasBasePositivePrompt == true)
            {
                return job.BasePositivePrompt;
            }
            if (string.IsNullOrWhiteSpace(job.UserPrompt) == true)
            {
                throw new StudioException(StudioMessageCode.InvalidGenerationRecord);
            }
            int start = 0;
            while (start < job.PositivePrompt.Length)
            {
                int index = job.PositivePrompt.IndexOf(job.UserPrompt, start, StringComparison.Ordinal);
                if (index < 0)
                {
                    break;
                }
                string positive = string.Empty;
                bool hasBoundary = index == 0;
                if (index >= 2 && job.PositivePrompt.Substring(index - 2, 2) == ", ")
                {
                    positive = job.PositivePrompt.Substring(0, index - 2);
                    hasBoundary = true;
                }
                if (hasBoundary == true)
                {
                    List<string> parts = new List<string>();
                    if (string.IsNullOrWhiteSpace(positive) == false)
                    {
                        parts.Add(positive.Trim());
                    }
                    parts.Add(job.UserPrompt.Trim());
                    AppendLoraTriggers(parts, job.Loras);
                    if (string.Join(", ", parts) == job.PositivePrompt)
                    {
                        return positive;
                    }
                }
                start = index + 1;
            }
            throw new StudioException(StudioMessageCode.InvalidGenerationRecord);
        }

        private string ComposePrompt(string positive, string prompt, IReadOnlyList<ModelAsset> loras)
        {
            List<string> description = new List<string>();
            description.Add(prompt.Trim());
            if (string.IsNullOrWhiteSpace(positive) == false)
            {
                description.Add(positive.Trim());
            }
            List<string> parts = new List<string>();
            AppendLoraTriggers(parts, loras, string.Join(", ", description));
            parts.AddRange(description);
            return string.Join(", ", parts);
        }

        private void AppendLoraTriggers(List<string> parts, IReadOnlyList<ModelAsset> loras, string existingPrompt = "")
        {
            foreach (ModelAsset lora in loras)
            {
                if (string.IsNullOrWhiteSpace(lora.TriggerWord) == false)
                {
                    foreach (string trigger in lora.TriggerWord.Split(',',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        string combined = string.Join(", ", parts);
                        if (string.IsNullOrWhiteSpace(existingPrompt) == false)
                        {
                            combined += ", " + existingPrompt;
                        }
                        string pattern = @"(?<![\p{L}\p{N}_])" + Regex.Escape(trigger) + @"(?![\p{L}\p{N}_])";
                        if (Regex.IsMatch(combined, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) == false)
                        {
                            parts.Add(trigger);
                        }
                    }
                }
            }
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

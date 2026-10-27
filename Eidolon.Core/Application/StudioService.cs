using Eidolon.Core.Domain;
using Eidolon.Core.Infrastructure;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
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
        private readonly CodexImageEngine _codex;
        private readonly RuntimeInstaller _runtimeInstaller;
        private readonly SemaphoreSlim _gpuGate = new(1, 1);

        public StudioService(AssetLibrary assetLibrary, JobStore jobStore, ComfyEngine comfyEngine, LoraTrainer loraTrainer, TimeProvider timeProvider, ISeedProvider seedProvider, BackgroundRemovalService backgroundRemovalService, CodexImageEngine codexImageEngine, RuntimeInstaller runtimeInstaller)
        {
            _assets = assetLibrary;
            _jobs = jobStore;
            _engine = comfyEngine;
            _trainer = loraTrainer;
            _time = timeProvider;
            _seeds = seedProvider;
            _backgroundRemoval = backgroundRemovalService;
            _codex = codexImageEngine;
            _runtimeInstaller = runtimeInstaller;
        }

        public async Task<JobRecord> GenerateAsync(StudioSettings settings, string prompt, ModelAsset model,
            IReadOnlyList<ModelAsset> loras, bool removeBackground, long seed, IProgress<WorkProgress> progress, CancellationToken token)
        {
            return await GenerateAsync(settings, prompt, model, loras, removeBackground, seed, null, progress, token).ConfigureAwait(false);
        }

        public async Task<JobRecord> GenerateAsync(StudioSettings settings, string prompt, ModelAsset model, IReadOnlyList<ModelAsset> loras, bool removeBackground, long seed, GenerationReferenceInput reference, IProgress<WorkProgress> progress, CancellationToken token, string convertedPrompt = "")
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
                if (reference.Mode != GenerationReferenceMode.Reimagine)
                {
                    if (reference.Mode != GenerationReferenceMode.Restyle)
                    {
                        if (reference.Mode != GenerationReferenceMode.Inpaint)
                        {
                            throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
                        }
                    }
                }
                if (double.IsFinite(reference.ChangeStrength) == false)
                {
                    throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
                }
                if (reference.ChangeStrength < 0.05)
                {
                    throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
                }
                if (reference.ChangeStrength > 0.95)
                {
                    throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
                }
                if (reference.ImageData == null)
                {
                    throw new StudioException(StudioMessageCode.InvalidReferenceImage);
                }
                if (reference.ImageData.Length == 0)
                {
                    throw new StudioException(StudioMessageCode.InvalidReferenceImage);
                }
                if (reference.ImageData.Length > GenerationReferenceInput.MaximumImageBytes)
                {
                    throw new StudioException(StudioMessageCode.InvalidReferenceImage);
                }
                if (reference.Mode == GenerationReferenceMode.Inpaint)
                {
                    if (reference.MaskData == null)
                    {
                        throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
                    }
                    if (reference.MaskData.Length == 0)
                    {
                        throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
                    }
                    if (reference.MaskData.Length > GenerationReferenceInput.MaximumImageBytes)
                    {
                        throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
                    }
                }
            }
            string generationPrompt = prompt;
            if (string.IsNullOrWhiteSpace(convertedPrompt) == false)
            {
                generationPrompt = convertedPrompt;
            }
            if (settings.GenerationBackend == GenerationBackend.Codex)
            {
                return await GenerateCodexAsync(settings, prompt, removeBackground, reference, progress, token, generationPrompt).ConfigureAwait(false);
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
            GenerationPreset preset = new(model.Family);
            JobRecord job = new()
            {
                Kind = JobKind.Generation,
                Title = prompt.Trim(),
                UserPrompt = prompt.Trim(),
                BasePositivePrompt = settings.PositivePrompt.Trim(),
                HasBasePositivePrompt = true,
                PositivePrompt = ComposePrompt(settings.PositivePrompt, generationPrompt, loras),
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

        private async Task<JobRecord> GenerateCodexAsync(StudioSettings settings, string prompt, bool removeBackground, GenerationReferenceInput reference, IProgress<WorkProgress> progress, CancellationToken token, string generationPrompt)
        {
            settings.Validate();
            JobRecord job = new()
            {
                Kind = JobKind.Generation,
                GenerationBackend = GenerationBackend.Codex,
                CodexModel = settings.CodexModel.Trim(),
                Title = prompt.Trim(),
                UserPrompt = prompt.Trim(),
                BasePositivePrompt = settings.PositivePrompt,
                HasBasePositivePrompt = true,
                PositivePrompt = ComposePrompt(settings.PositivePrompt, generationPrompt, Array.Empty<ModelAsset>()),
                NegativePrompt = settings.NegativePrompt,
                RemoveBackground = removeBackground,
                Model = new ModelAsset { Name = "Codex", EngineName = "image_gen" },
                StartedAtUtc = _time.GetUtcNow()
            };
            _jobs.SetOutputDirectory(job, settings.GenerationDirectory);
            try
            {
                _jobs.Save(job);
                if (reference != null)
                {
                    progress.Report(new WorkProgress(StudioMessageCode.PreparingReferenceImage));
                    await PrepareReferenceImageAsync(job, reference, null, token).ConfigureAwait(false);
                    _jobs.Save(job);
                }
                await _codex.GenerateAsync(settings.Copy(), job, _jobs, progress, token).ConfigureAwait(false);
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
        }

        private async Task PrepareReferenceImageAsync(JobRecord job, GenerationReferenceInput reference,
            GenerationPreset preset, CancellationToken token)
        {
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                using SKMemoryStream input = new(reference.ImageData);
                using SKCodec codec = SKCodec.Create(input);
                if (codec == null)
                {
                    throw new StudioException(StudioMessageCode.InvalidReferenceImage);
                }
                if (codec.Info.Width < 1)
                {
                    throw new StudioException(StudioMessageCode.InvalidReferenceImage);
                }
                if (codec.Info.Height < 1)
                {
                    throw new StudioException(StudioMessageCode.InvalidReferenceImage);
                }
                if ((long)codec.Info.Width * codec.Info.Height > GenerationReferenceInput.MaximumImagePixels)
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
                switch (codec.EncodedOrigin)
                {
                    case SKEncodedOrigin.LeftTop:
                    case SKEncodedOrigin.RightTop:
                    case SKEncodedOrigin.RightBottom:
                    case SKEncodedOrigin.LeftBottom:
                        sourceWidth = source.Height;
                        sourceHeight = source.Width;
                        break;
                }
                job.Width = sourceWidth;
                job.Height = sourceHeight;
                if (job.GenerationBackend == GenerationBackend.ComfyUI)
                {
                    double scale = (double)preset.Resolution / Math.Max(sourceWidth, sourceHeight);
                    job.Width = Math.Max(64, (int)Math.Round(sourceWidth * scale / 8) * 8);
                    job.Height = Math.Max(64, (int)Math.Round(sourceHeight * scale / 8) * 8);
                }
                int preparedWidth = job.Width;
                int preparedHeight = job.Height;
                if (reference.Mode == GenerationReferenceMode.Inpaint)
                {
                    preparedWidth = sourceWidth;
                    preparedHeight = sourceHeight;
                }
                SKMatrix sourceTransform = SKMatrix.Identity;
                using SKBitmap prepared = new(preparedWidth, preparedHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
                using (SKCanvas canvas = new(prepared))
                {
                    SKColor background = SKColors.White;
                    if (job.GenerationBackend == GenerationBackend.Codex)
                    {
                        background = SKColors.Transparent;
                    }
                    if (reference.Mode == GenerationReferenceMode.Inpaint)
                    {
                        background = SKColors.Transparent;
                    }
                    canvas.Clear(background);
                    canvas.Scale((float)preparedWidth / sourceWidth, (float)preparedHeight / sourceHeight);
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
                    sourceTransform = canvas.TotalMatrix;
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
                using (FileStream stream = new(destination, FileMode.CreateNew, FileAccess.Write))
                {
                    encoded.SaveTo(stream);
                }
                job.ReferenceMode = reference.Mode;
                job.ReferenceImageName = reference.ImageName;
                job.ReferenceImagePath = destination;
                if (reference.Mode == GenerationReferenceMode.Inpaint)
                {
                    using SKMemoryStream maskInput = new(reference.MaskData);
                    using SKCodec maskCodec = SKCodec.Create(maskInput);
                    if (maskCodec == null)
                    {
                        throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
                    }
                    if (maskCodec.Info.Width < 1)
                    {
                        throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
                    }
                    if (maskCodec.Info.Height < 1)
                    {
                        throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
                    }
                    if ((long)maskCodec.Info.Width * maskCodec.Info.Height > GenerationReferenceInput.MaximumImagePixels)
                    {
                        throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
                    }
                    long aspectDifference = Math.Abs((long)maskCodec.Info.Width * source.Height - (long)maskCodec.Info.Height * source.Width);
                    if (aspectDifference > Math.Max(source.Width, source.Height))
                    {
                        throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
                    }
                    using SKBitmap mask = SKBitmap.Decode(maskCodec);
                    if (mask == null)
                    {
                        throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
                    }
                    bool hasSelection = false;
                    for (int row = 0; row < mask.Height; row++)
                    {
                        token.ThrowIfCancellationRequested();
                        for (int column = 0; column < mask.Width; column++)
                        {
                            if (mask.GetPixel(column, row).Alpha > 0)
                            {
                                hasSelection = true;
                                break;
                            }
                        }
                        if (hasSelection == true)
                        {
                            break;
                        }
                    }
                    if (hasSelection == false)
                    {
                        throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
                    }
                    using SKBitmap preparedMask = new(preparedWidth, preparedHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
                    using (SKCanvas maskCanvas = new(preparedMask))
                    {
                        maskCanvas.Clear(SKColors.Transparent);
                        maskCanvas.SetMatrix(sourceTransform);
                        using SKImage maskImage = SKImage.FromBitmap(mask);
                        maskCanvas.DrawImage(maskImage, new SKRect(0, 0, source.Width, source.Height), new SKSamplingOptions(SKFilterMode.Linear));
                    }
                    string maskPath = _jobs.WorkingImagePath(job, Path.Combine("Inputs", "Mask.png"));
                    using SKImage maskOutput = SKImage.FromBitmap(preparedMask);
                    using SKData maskEncoded = maskOutput.Encode(SKEncodedImageFormat.Png, 100);
                    if (maskEncoded == null)
                    {
                        throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
                    }
                    using (FileStream maskStream = new(maskPath, FileMode.CreateNew, FileAccess.Write))
                    {
                        maskEncoded.SaveTo(maskStream);
                    }
                    job.ReferenceMaskPath = maskPath;
                }
                if (job.GenerationBackend == GenerationBackend.ComfyUI)
                {
                    job.Denoise = reference.ChangeStrength;
                    if (reference.Mode == GenerationReferenceMode.Restyle)
                    {
                        job.OutlineControlStrength = preset.OutlineStrength;
                    }
                }
                token.ThrowIfCancellationRequested();
            }, token).ConfigureAwait(false);
        }

        public Task<string> ReadTrainingCaptionAsync(string imagePath, CancellationToken token)
        {
            return Task.Run(() => _trainer.ReadTrainingCaptionAsync(imagePath, string.Empty, token), token);
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
            JobRecord job = new()
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
            if (job.GenerationBackend == GenerationBackend.ComfyUI)
            {
                if (job.RemoveBackground == true)
                {
                    workingFile = Path.Combine("Processed", Guid.NewGuid().ToString("N") + ".png");
                    await _backgroundRemoval.RemoveAsync(_jobs.WorkingImagePath(job, imageFile),
                        _jobs.WorkingImagePath(job, workingFile), progress, token).ConfigureAwait(false);
                    removeWorkingFile = true;
                }
            }
            if (job.ReferenceMode == GenerationReferenceMode.Inpaint)
            {
                string compositeFile = Path.Combine("Processed", Guid.NewGuid().ToString("N") + ".png");
                await CompositeMaskedImageAsync(job, _jobs.WorkingImagePath(job, workingFile), _jobs.WorkingImagePath(job, compositeFile), token).ConfigureAwait(false);
                if (removeWorkingFile == true)
                {
                    File.Delete(_jobs.WorkingImagePath(job, workingFile));
                }
                workingFile = compositeFile;
                removeWorkingFile = true;
            }
            await _jobs.PublishImageAsync(job, workingFile, removeWorkingFile, _time.GetUtcNow(), token).ConfigureAwait(false);
        }

        private Task CompositeMaskedImageAsync(JobRecord job, string generatedPath, string destinationPath, CancellationToken token)
        {
            return Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                using SKBitmap reference = SKBitmap.Decode(job.ReferenceImagePath);
                using SKBitmap mask = SKBitmap.Decode(job.ReferenceMaskPath);
                using SKBitmap generated = SKBitmap.Decode(generatedPath);
                if (reference == null)
                {
                    throw new StudioException(StudioMessageCode.InvalidReferenceImage);
                }
                if (mask == null)
                {
                    throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
                }
                if (generated == null)
                {
                    throw new StudioException(StudioMessageCode.InvalidReferenceImage);
                }
                if (mask.Width != reference.Width)
                {
                    throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
                }
                if (mask.Height != reference.Height)
                {
                    throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
                }
                if (mask.BytesPerPixel != 4)
                {
                    throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
                }
                using SKBitmap result = new(reference.Width, reference.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
                using (SKCanvas resultCanvas = new(result))
                {
                    resultCanvas.Clear(SKColors.Transparent);
                    using SKImage referenceImage = SKImage.FromBitmap(reference);
                    resultCanvas.DrawImage(referenceImage, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
                }
                using SKBitmap resized = new(reference.Width, reference.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
                using (SKCanvas resizedCanvas = new(resized))
                {
                    resizedCanvas.Clear(SKColors.Transparent);
                    using SKImage generatedImage = SKImage.FromBitmap(generated);
                    resizedCanvas.DrawImage(generatedImage, new SKRect(0, 0, reference.Width, reference.Height), new SKSamplingOptions(SKFilterMode.Linear));
                }
                int rowLength = reference.Width * 4;
                byte[] resultRow = new byte[rowLength];
                byte[] generatedRow = new byte[rowLength];
                byte[] maskRow = new byte[rowLength];
                for (int row = 0; row < reference.Height; row++)
                {
                    token.ThrowIfCancellationRequested();
                    IntPtr resultPointer = IntPtr.Add(result.GetPixels(), row * result.RowBytes);
                    Marshal.Copy(resultPointer, resultRow, 0, rowLength);
                    Marshal.Copy(IntPtr.Add(resized.GetPixels(), row * resized.RowBytes), generatedRow, 0, rowLength);
                    Marshal.Copy(IntPtr.Add(mask.GetPixels(), row * mask.RowBytes), maskRow, 0, rowLength);
                    for (int column = 0; column < reference.Width; column++)
                    {
                        int pixelOffset = column * 4;
                        int opacity = maskRow[pixelOffset + 3];
                        if (opacity == 0)
                        {
                            continue;
                        }
                        for (int channel = 0; channel < 4; channel++)
                        {
                            int offset = pixelOffset + channel;
                            resultRow[offset] = (byte)((resultRow[offset] * (255 - opacity) + generatedRow[offset] * opacity + 127) / 255);
                        }
                    }
                    Marshal.Copy(resultRow, 0, resultPointer, rowLength);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                using SKImage output = SKImage.FromBitmap(result);
                using SKData encoded = output.Encode(SKEncodedImageFormat.Png, 100);
                if (encoded == null)
                {
                    throw new StudioException(StudioMessageCode.InvalidReferenceImage);
                }
                using FileStream destination = new(destinationPath, FileMode.CreateNew, FileAccess.Write);
                encoded.SaveTo(destination);
                job.Width = result.Width;
                job.Height = result.Height;
            }, token);
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
            RuntimeLayout layout = new(settings.InstallDirectory);
            _runtimeInstaller.EnsureInstalled(layout);
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
                    List<string> parts = new();
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
            List<string> description = new();
            description.Add(prompt.Trim());
            if (string.IsNullOrWhiteSpace(positive) == false)
            {
                description.Add(positive.Trim());
            }
            List<string> parts = new();
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

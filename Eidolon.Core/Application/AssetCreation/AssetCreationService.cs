using Eidolon.Core.Domain;
using Eidolon.Core.Infrastructure;
using SkiaSharp;

namespace Eidolon.Core.Application
{
    public class AssetCreationService
    {
        private readonly StudioService _studio;
        private readonly JobStore _jobs;
        private readonly TimeProvider _time;
        private readonly AssetSheetExporter _exporter;
        private readonly AssetFrameImporter _importer;
        private readonly AssetFramePromptBuilder _prompts;

        public AssetCreationService(StudioService studio, JobStore jobs, TimeProvider time, AssetSheetExporter exporter, AssetFrameImporter importer, AssetFramePromptBuilder prompts)
        {
            _studio = studio;
            _jobs = jobs;
            _time = time;
            _exporter = exporter;
            _importer = importer;
            _prompts = prompts;
        }

        public AssetCollection Prepare(StudioSettings settings, ModelAsset model, IReadOnlyList<ModelAsset> loras, AssetCreationInput input, CancellationToken cancellationToken)
        {
            if (input == null)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            input.Validate();
            if (settings == null)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (loras == null)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            settings.Validate();
            ModelAsset generationModel = new ModelAsset();
            List<ModelAsset> generationLoras = new List<ModelAsset>();
            long seed = 0;
            if (settings.GenerationBackend == GenerationBackend.ComfyUI)
            {
                if (model == null)
                {
                    throw new StudioException(StudioMessageCode.ModelRequired);
                }
                if (model.Kind != AssetKind.Checkpoint)
                {
                    throw new StudioException(StudioMessageCode.ModelRequired);
                }
                if (model.Family == ModelFamily.Unknown)
                {
                    throw new StudioException(StudioMessageCode.ModelRequired);
                }
                foreach (ModelAsset lora in loras)
                {
                    if (lora == null)
                    {
                        throw new StudioException(StudioMessageCode.ModelFamilyMismatch);
                    }
                    if (lora.Kind != AssetKind.Lora)
                    {
                        throw new StudioException(StudioMessageCode.ModelFamilyMismatch);
                    }
                    if (lora.Family != model.Family)
                    {
                        throw new StudioException(StudioMessageCode.ModelFamilyMismatch);
                    }
                    if (lora.RuntimeRoot != model.RuntimeRoot)
                    {
                        throw new StudioException(StudioMessageCode.ModelFamilyMismatch);
                    }
                }
                generationModel = model.Copy();
                generationLoras = loras.Select(lora => lora.Copy()).ToList();
                seed = input.Seed;
            }
            using SKMemoryStream memory = new SKMemoryStream(input.Reference.ImageData);
            using SKCodec codec = SKCodec.Create(memory);
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
            cancellationToken.ThrowIfCancellationRequested();
            AssetCollection collection = new AssetCollection
            {
                Kind = input.Kind, Prompt = _prompts.Subject(input.Prompt),
                Settings = settings.Copy(), Model = generationModel, Loras = generationLoras,
                UseServerAssets = settings.UseServerAssets,
                ReferenceName = Path.GetFileName(input.Reference.ImageName), ChangeStrength = input.Reference.ChangeStrength,
                Seed = seed, RemoveBackground = input.RemoveBackground, PixelArt = input.PixelArt,
                FrameWidth = input.FrameWidth, FrameHeight = input.FrameHeight, Columns = input.Columns,
                CreatedAtUtc = _time.GetUtcNow()
            };
            for (int frameIndex = 0; frameIndex < AssetCreationInput.ViewCount; frameIndex++)
            {
                collection.Frames.Add(_prompts.CreateFrame(collection, frameIndex));
            }
            string directory = _jobs.DirectoryFor(collection.Id);
            if (collection.Frames.Any(frame => frame.Prompt.Length > 16000) == true)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "Reference.bin"), input.Reference.ImageData);
            _jobs.SaveAssetCollection(collection);
            return collection;
        }

        public async Task GenerateAsync(string collectionId, IProgress<WorkProgress> progress, CancellationToken cancellationToken)
        {
            AssetCollection collection = _jobs.LoadAssetCollection(collectionId);
            if (collection.Kind == AssetCreationKind.SpriteAnimation)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            collection.State = JobState.Running;
            collection.ErrorCode = default;
            collection.ErrorArguments = Array.Empty<object>();
            _jobs.SaveAssetCollection(collection);
            try
            {
                for (int frameIndex = 0; frameIndex < collection.Frames.Count; frameIndex++)
                {
                    if (collection.Frames[frameIndex].State == JobState.Completed)
                    {
                        if (File.Exists(collection.Frames[frameIndex].ImagePath) == true)
                        {
                            continue;
                        }
                    }
                    await GenerateFrameAsync(collection, frameIndex, progress, cancellationToken).ConfigureAwait(false);
                }
                collection.State = JobState.Completed;
            }
            catch (Exception error)
            {
                SetFailure(collection, error, cancellationToken);
                throw;
            }
            finally
            {
                _jobs.SaveAssetCollection(collection);
            }
        }

        public async Task RegenerateAsync(string collectionId, int frameNumber, string prompt, long seed, IProgress<WorkProgress> progress, CancellationToken cancellationToken)
        {
            AssetCollection collection = _jobs.LoadAssetCollection(collectionId);
            if (collection.Kind == AssetCreationKind.SpriteAnimation)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (frameNumber < 1)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (frameNumber > collection.Frames.Count)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (string.IsNullOrWhiteSpace(prompt) == true)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (prompt.Length > 16000)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (seed < 0)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            collection.Frames[frameNumber - 1].Prompt = prompt.Trim();
            if (collection.Settings.GenerationBackend == GenerationBackend.Codex)
            {
                seed = 0;
            }
            collection.Frames[frameNumber - 1].Seed = seed;
            collection.State = JobState.Running;
            try
            {
                await GenerateFrameAsync(collection, frameNumber - 1, progress, cancellationToken, true).ConfigureAwait(false);
                collection.State = JobState.Completed;
                if (collection.Frames.Any(frame => frame.State != JobState.Completed) == true)
                {
                    collection.State = JobState.Interrupted;
                }
                collection.ErrorCode = default;
                collection.ErrorArguments = Array.Empty<object>();
            }
            catch (Exception error)
            {
                SetFailure(collection, error, cancellationToken);
                throw;
            }
            finally
            {
                _jobs.SaveAssetCollection(collection);
            }
        }

        private async Task GenerateFrameAsync(AssetCollection collection, int frameIndex, IProgress<WorkProgress> progress, CancellationToken cancellationToken, bool singleFrame = false)
        {
            AssetFrame frame = collection.Frames[frameIndex];
            frame.State = JobState.Running;
            frame.ErrorCode = default;
            frame.ErrorArguments = Array.Empty<object>();
            _jobs.SaveAssetCollection(collection);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] referenceImageData = await File.ReadAllBytesAsync(Path.Combine(_jobs.DirectoryFor(collection.Id), "Reference.bin"), cancellationToken).ConfigureAwait(false);
                GenerationReferenceInput reference = new GenerationReferenceInput
                {
                    ImageData = referenceImageData, ImageName = collection.ReferenceName,
                    Mode = GenerationReferenceMode.Reimagine, ChangeStrength = collection.ChangeStrength
                };
                StudioSettings requestSettings = collection.Settings.Copy();
                if (frame.Label == "top")
                {
                    if (string.IsNullOrWhiteSpace(requestSettings.NegativePrompt) == false)
                    {
                        requestSettings.NegativePrompt += ", ";
                    }
                    requestSettings.NegativePrompt += "eye-level view, high-angle portrait, three-quarter view, diagonal camera, frontal elevation, visible front-facing chest, standing full-body portrait, unforeshortened vertical torso or legs, face turned up toward camera";
                }
                JobRecord generatedJob = await _studio.GenerateAsync(requestSettings, frame.Prompt, collection.Model.Copy(), collection.Loras.Select(lora => lora.Copy()).ToList(), collection.RemoveBackground, frame.Seed, reference, new AssetCreationProgress(progress, frameIndex, collection.Frames.Count, singleFrame), cancellationToken).ConfigureAwait(false);
                string generatedImagePath = _jobs.ImagePath(generatedJob, generatedJob.ImageFiles.Last());
                _jobs.SaveAssetFrameMetadata(collection, frame, generatedJob, generatedImagePath, cancellationToken);
                frame.ImagePath = generatedImagePath;
                frame.SourceJobId = generatedJob.Id;
                frame.ImageSeed = generatedJob.Seed;
                if (generatedJob.GenerationBackend == GenerationBackend.Codex)
                {
                    collection.Model = generatedJob.Model.Copy();
                }
                frame.State = JobState.Completed;
            }
            catch (Exception error)
            {
                frame.State = JobState.Failed;
                frame.ErrorCode = StudioMessageCode.AssetCreationFailed;
                if (error is OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested == true)
                    {
                        frame.State = JobState.Cancelled;
                    }
                }
                if (error is StudioException knownException)
                {
                    frame.ErrorCode = knownException.Code;
                    frame.ErrorArguments = knownException.Arguments;
                }
                throw;
            }
            finally
            {
                _jobs.SaveAssetCollection(collection);
            }
        }

        public void Discard(string collectionId)
        {
            AssetCollection collection = _jobs.LoadAssetCollection(collectionId);
            if (collection.Kind == AssetCreationKind.SpriteAnimation)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (collection.State == JobState.Preparing)
            {
                collection.State = JobState.Cancelled;
                _jobs.SaveAssetCollection(collection);
            }
        }

        public string Export(string id, string directory, CancellationToken token)
        {
            return _exporter.Export(_jobs.LoadAssetCollection(id), directory, token);
        }

        public void ReplaceFrame(string collectionId, int frameNumber, byte[] imageBytes, CancellationToken cancellationToken)
        {
            AssetCollection collection = _jobs.LoadAssetCollection(collectionId);
            if (collection.Kind == AssetCreationKind.SpriteAnimation)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (frameNumber < 1)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (frameNumber > collection.Frames.Count)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            AssetFrame frame = collection.Frames[frameNumber - 1];
            string imagePath = _importer.Import(collectionId, imageBytes, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            frame.ImagePath = imagePath;
            frame.SourceJobId = string.Empty;
            frame.ImageSeed = 0;
            frame.State = JobState.Completed;
            frame.ErrorCode = default;
            frame.ErrorArguments = Array.Empty<object>();
            collection.State = JobState.Interrupted;
            if (collection.Frames.All(item => item.State == JobState.Completed) == true)
            {
                collection.State = JobState.Completed;
            }
            collection.ErrorCode = default;
            collection.ErrorArguments = Array.Empty<object>();
            _jobs.SaveAssetCollection(collection);
        }

        private void SetFailure(AssetCollection collection, Exception error, CancellationToken token)
        {
            collection.State = JobState.Failed;
            collection.ErrorCode = StudioMessageCode.AssetCreationFailed;
            if (error is OperationCanceledException && token.IsCancellationRequested == true)
            {
                collection.State = JobState.Cancelled;
            }
            if (error is StudioException known)
            {
                collection.ErrorCode = known.Code;
                collection.ErrorArguments = known.Arguments;
            }
        }
    }
}

using Eidolon.Core.Domain;
using Eidolon.Core.Infrastructure;
using SkiaSharp;
using System.Globalization;

namespace Eidolon.Core.Application
{
    public class AssetCreationService
    {
        private readonly StudioService _studio;
        private readonly JobStore _jobs;
        private readonly TimeProvider _time;
        private readonly AssetSheetExporter _exporter;
        private readonly AssetFrameImporter _importer;

        public AssetCreationService(StudioService studio, JobStore jobs, TimeProvider time, AssetSheetExporter exporter, AssetFrameImporter importer)
        {
            _studio = studio;
            _jobs = jobs;
            _time = time;
            _exporter = exporter;
            _importer = importer;
        }

        public AssetCollection Prepare(StudioSettings settings, ModelAsset model, IReadOnlyList<ModelAsset> loras,
            AssetCreationInput input, CancellationToken token)
        {
            ValidateInput(input);
            if (settings != null && settings.GenerationBackend != GenerationBackend.ComfyUI)
            {
                throw new StudioException(StudioMessageCode.InvalidGenerationBackend);
            }
            if (model == null || model.Kind != AssetKind.Checkpoint || model.Family == ModelFamily.Unknown)
            {
                throw new StudioException(StudioMessageCode.ModelRequired);
            }
            if (settings == null || loras == null)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (loras.Any(lora => lora == null || lora.Kind != AssetKind.Lora || lora.Family != model.Family
                || lora.RuntimeRoot != model.RuntimeRoot) == true)
            {
                throw new StudioException(StudioMessageCode.ModelFamilyMismatch);
            }
            using SKMemoryStream memory = new SKMemoryStream(input.Reference.ImageData);
            using SKCodec codec = SKCodec.Create(memory);
            if (codec == null || codec.Info.Width < 1 || codec.Info.Height < 1
                || (long)codec.Info.Width * codec.Info.Height > GenerationReferenceInput.MaximumImagePixels)
            {
                throw new StudioException(StudioMessageCode.InvalidReferenceImage);
            }
            token.ThrowIfCancellationRequested();
            AssetCollection collection = new AssetCollection
            {
                Kind = input.Kind, Prompt = input.Prompt.Trim(), ActionPrompt = input.ActionPrompt.Trim(),
                Settings = settings.Copy(), Model = model.Copy(), Loras = loras.Select(lora => lora.Copy()).ToList(),
                UseServerAssets = settings.UseServerAssets,
                ReferenceName = Path.GetFileName(input.Reference.ImageName), ChangeStrength = input.Reference.ChangeStrength,
                Seed = input.Seed, RemoveBackground = input.RemoveBackground, PixelArt = input.PixelArt,
                FrameWidth = input.FrameWidth, FrameHeight = input.FrameHeight, Columns = input.Columns,
                FramesPerSecond = input.FramesPerSecond, CreatedAtUtc = _time.GetUtcNow()
            };
            int count = 3;
            if (input.Kind == AssetCreationKind.SpriteAnimation)
            {
                count = input.FrameCount;
            }
            for (int index = 0; index < count; index++)
            {
                string label = (index + 1).ToString(CultureInfo.InvariantCulture);
                string instruction;
                if (input.Kind == AssetCreationKind.SpriteAnimation)
                {
                    instruction = "animation key pose " + (index + 1) + " of " + count + ", " + collection.ActionPrompt
                        + ", phase " + ((double)index / count).ToString("P0", CultureInfo.InvariantCulture) + " of a looping motion";
                    if (input.FrameDescriptions.Count > 0)
                    {
                        instruction = input.FrameDescriptions[index].Trim();
                    }
                }
                else
                {
                    string[] views = { "front", "side", "back" };
                    if (input.Kind == AssetCreationKind.ObjectViews)
                    {
                        views[2] = "top";
                    }
                    label = views[index];
                    instruction = views[index] + " view, orthographic projection, no perspective, neutral pose";
                }
                collection.Frames.Add(new AssetFrame
                {
                    Number = index + 1, Label = label, Seed = collection.Seed,
                    Prompt = collection.Prompt + ", " + instruction
                        + ", single subject, entire subject visible, consistent design and proportions, fixed camera, plain background"
                });
                if (input.PixelArt == true)
                {
                    collection.Frames.Last().Prompt += ", pixel art, crisp pixel edges, no anti-aliasing";
                }
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

        public async Task GenerateAsync(string id, IProgress<WorkProgress> progress, CancellationToken token)
        {
            AssetCollection collection = _jobs.LoadAssetCollection(id);
            collection.State = JobState.Running;
            collection.ErrorCode = default;
            collection.ErrorArguments = Array.Empty<object>();
            _jobs.SaveAssetCollection(collection);
            try
            {
                for (int index = 0; index < collection.Frames.Count; index++)
                {
                    if (collection.Frames[index].State == JobState.Completed && File.Exists(collection.Frames[index].ImagePath) == true)
                    {
                        continue;
                    }
                    await GenerateFrameAsync(collection, index, progress, token).ConfigureAwait(false);
                }
                collection.State = JobState.Completed;
            }
            catch (Exception error)
            {
                SetFailure(collection, error, token);
                throw;
            }
            finally
            {
                _jobs.SaveAssetCollection(collection);
            }
        }

        public async Task RegenerateAsync(string id, int number, string prompt, long seed,
            IProgress<WorkProgress> progress, CancellationToken token)
        {
            AssetCollection collection = _jobs.LoadAssetCollection(id);
            if (number < 1 || number > collection.Frames.Count || string.IsNullOrWhiteSpace(prompt) == true || prompt.Length > 16000 || seed < 0)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            collection.Frames[number - 1].Prompt = prompt.Trim();
            collection.Frames[number - 1].Seed = seed;
            collection.State = JobState.Running;
            try
            {
                await GenerateFrameAsync(collection, number - 1, progress, token, true).ConfigureAwait(false);
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
                SetFailure(collection, error, token);
                throw;
            }
            finally
            {
                _jobs.SaveAssetCollection(collection);
            }
        }

        private async Task GenerateFrameAsync(AssetCollection collection, int index,
            IProgress<WorkProgress> progress, CancellationToken token, bool singleFrame = false)
        {
            AssetFrame frame = collection.Frames[index];
            frame.State = JobState.Running;
            frame.ErrorCode = default;
            frame.ErrorArguments = Array.Empty<object>();
            _jobs.SaveAssetCollection(collection);
            try
            {
                token.ThrowIfCancellationRequested();
                byte[] data = await File.ReadAllBytesAsync(Path.Combine(_jobs.DirectoryFor(collection.Id), "Reference.bin"), token).ConfigureAwait(false);
                GenerationReferenceInput reference = new GenerationReferenceInput
                {
                    ImageData = data, ImageName = collection.ReferenceName,
                    Mode = GenerationReferenceMode.Reimagine, ChangeStrength = collection.ChangeStrength
                };
                JobRecord result = await _studio.GenerateAsync(collection.Settings.Copy(), frame.Prompt,
                    collection.Model.Copy(), collection.Loras.Select(lora => lora.Copy()).ToList(), collection.RemoveBackground,
                    frame.Seed, reference, new AssetCreationProgress(progress, index, collection.Frames.Count, singleFrame), token).ConfigureAwait(false);
                frame.ImagePath = _jobs.ImagePath(result, result.ImageFiles.Last());
                frame.SourceJobId = result.Id;
                frame.ImageSeed = result.Seed;
                frame.State = JobState.Completed;
            }
            catch (Exception error)
            {
                frame.State = JobState.Failed;
                frame.ErrorCode = StudioMessageCode.AssetCreationFailed;
                if (error is OperationCanceledException && token.IsCancellationRequested == true)
                {
                    frame.State = JobState.Cancelled;
                }
                if (error is StudioException known)
                {
                    frame.ErrorCode = known.Code;
                    frame.ErrorArguments = known.Arguments;
                }
                throw;
            }
            finally
            {
                _jobs.SaveAssetCollection(collection);
            }
        }

        public void Discard(string id)
        {
            AssetCollection collection = _jobs.LoadAssetCollection(id);
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

        public void ReplaceFrame(string id, int number, byte[] bytes, CancellationToken token)
        {
            AssetCollection collection = _jobs.LoadAssetCollection(id);
            if (number < 1 || number > collection.Frames.Count)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            AssetFrame frame = collection.Frames[number - 1];
            string path = _importer.Import(id, bytes, token);
            token.ThrowIfCancellationRequested();
            frame.ImagePath = path;
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

        private void ValidateInput(AssetCreationInput input)
        {
            if (input == null || Enum.IsDefined(input.Kind) == false || string.IsNullOrWhiteSpace(input.Prompt) == true
                || input.Prompt.Length > 16000 || input.ActionPrompt == null || input.ActionPrompt.Length > 2000 || input.FrameDescriptions == null)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (input.Reference == null || input.Reference.ImageData == null || input.Reference.ImageData.Length == 0
                || input.Reference.ImageData.Length > GenerationReferenceInput.MaximumImageBytes)
            {
                throw new StudioException(StudioMessageCode.InvalidReferenceImage);
            }
            if (double.IsFinite(input.Reference.ChangeStrength) == false || input.Reference.ChangeStrength < 0.05
                || input.Reference.ChangeStrength > 0.95 || input.Seed < 0)
            {
                throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
            }
            if (input.FrameCount < 2 || input.FrameCount > AssetCreationInput.MaximumFrames
                || input.FrameWidth < 16 || input.FrameWidth > AssetCreationInput.MaximumFrameSize
                || input.FrameHeight < 16 || input.FrameHeight > AssetCreationInput.MaximumFrameSize
                || input.Columns < 1 || input.Columns > AssetCreationInput.MaximumFrames
                || input.FramesPerSecond < 1 || input.FramesPerSecond > 60)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (input.Kind == AssetCreationKind.SpriteAnimation && string.IsNullOrWhiteSpace(input.ActionPrompt) == true)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (input.FrameDescriptions.Count > 0 && (input.FrameDescriptions.Count != input.FrameCount
                || input.FrameDescriptions.Any(text => string.IsNullOrWhiteSpace(text) == true || text.Length > 4000) == true))
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            int count = 3;
            if (input.Kind == AssetCreationKind.SpriteAnimation)
            {
                count = input.FrameCount;
            }
            int columns = Math.Min(input.Columns, count);
            int rows = (count + columns - 1) / columns;
            if ((long)input.FrameWidth * input.FrameHeight * columns * rows > AssetCreationInput.MaximumSheetPixels)
            {
                throw new StudioException(StudioMessageCode.AssetSheetTooLarge);
            }
        }
    }
}

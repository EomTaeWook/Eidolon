using Avalonia.Threading;
using Eidolon.App.Localization;
using Eidolon.App.Services;
using Eidolon.App.ViewModels;
using Eidolon.Core.Application;
using Eidolon.Core.Domain;
using Eidolon.Core.Infrastructure;
using Eidolon.Mcp;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Eidolon.App.Mcp
{
    public class McpService
    {
        private readonly StudioSession _session;
        private readonly AssetsViewModel _assets;
        private readonly GenerationViewModel _generation;
        private readonly TrainingViewModel _training;
        private readonly JobStore _jobs;
        private readonly ISeedProvider _seeds;
        private readonly StringHelper _strings;
        private readonly AssetCreationViewModel _creation;
        private readonly AssetCreationService _assetService;
        private readonly JsonSerializerOptions _assetJson = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };

        public McpService(StudioSession session, AssetsViewModel assets, GenerationViewModel generation,
            JobStore jobs, ISeedProvider seeds, StringHelper strings, AssetCreationViewModel creation, AssetCreationService assetService, TrainingViewModel training)
        {
            _session = session;
            _assets = assets;
            _generation = generation;
            _training = training;
            _jobs = jobs;
            _seeds = seeds;
            _strings = strings;
            _creation = creation;
            _assetService = assetService;
        }

        public IReadOnlyList<McpTool> CreateTools()
        {
            return new[]
            {
                new McpTool(new McpToolDefinition("eidolon_get_status",
                    "Read Eidolon readiness, current work, pending queue, progress and error. Poll after enqueueing an image or LoRA training request.")
                {
                    ReadOnly = true,
                    Idempotent = true,
                    OpenWorld = false
                }, GetStatusAsync),
                new McpTool(new McpToolDefinition("eidolon_list_models",
                    "List available checkpoints and LoRAs with asset IDs, engine filenames, compatible families, trigger words and prompt_guidance. Before ComfyUI generation, editing or asset creation, read the target checkpoint's prompt_guidance, using selected_model_id when model_id is omitted. Guidance is matched by filename, not a verified weight identity; renamed or custom checkpoints require their own model card. A shared SDXL family does not imply shared quality, rating or source tags. Model and LoRA families must match.")
                {
                    ReadOnly = true,
                    Idempotent = true,
                    OpenWorld = false
                }, ListModelsAsync),
                new McpTool(new McpToolDefinition("eidolon_get_instructions",
                    "Read saved positive and negative instructions, generation_backend, selected_model_id and prompt_guidance for the active backend and selected checkpoint. Read this before image generation, editing or asset creation. For a different ComfyUI checkpoint, read its prompt_guidance from eidolon_list_models. Resolve conflicting model-specific tags with request-only instruction overrides; saved settings remain unchanged.")
                {
                    ReadOnly = true,
                    Idempotent = true,
                    OpenWorld = false
                }, GetInstructionsAsync),
                new McpTool(new McpToolDefinition("eidolon_train_lora",
                    "Queue one new or continued LoRA training job from local image files or folders without opening a file picker. Requires name, trigger_word and at least one image in white_images or black_images. Optional resume_lora_id is an existing LoRA ID from eidolon_list_models, with the same family and local runtime as the checkpoint. It loads the existing weights and rank, then trains on the supplied images; previous images are not automatically included. The optimizer and schedule start fresh, and the result is saved as a new LoRA while preserving the original. Omit resume_lora_id to start a new LoRA regardless of UI continuation settings. Both background groups train together into one LoRA; only transparent areas are filled with the group's color. Folder paths include supported images in subfolders. Uses the local ComfyUI training runtime and requires an NVIDIA GPU, even when the saved image-generation backend is Codex. Omitting model_id uses the UI checkpoint; steps defaults to 1000 and means additional steps when continuing. Optional description MUST be in English as detailed comma-separated tags or short tag phrases. Existing sibling .txt captions and Eidolon metadata take precedence over description. Original images and captions are preserved. Returns acceptance, not completion; poll eidolon_get_status, then eidolon_list_models for the trained LoRA. Does not change UI training inputs or saved settings.",
                    TrainingSchema()), TrainLoraAsync),
                new McpTool(new McpToolDefinition("eidolon_generate_image",
                    "Queue text-to-image generation, optionally with a reference image. All supplied prompts MUST be in English and use detailed comma-separated tags or short tag phrases. Translate requests in other languages before calling. Read eidolon_get_instructions first; for ComfyUI, follow the target checkpoint prompt_guidance from eidolon_list_models rather than a universal tag template. Specify the subject, appearance, pose, composition, viewpoint, style, materials, colors, lighting and background in relevant detail; put unwanted elements in negative_prompt. Returns accepted and the fixed seed, not a completed image. Poll eidolon_get_status then eidolon_list_images. Uses saved instructions unless positive_prompt or negative_prompt is provided. Omitting model_id uses the UI model; omitting lora_ids uses compatible selected UI LoRAs. An empty lora_ids array disables LoRAs for this request. Does not change UI inputs or saved settings.",
                    ImageSchema(false)), GenerateImageAsync),
                new McpTool(new McpToolDefinition("eidolon_edit_image",
                    "Queue a style edit using an existing local reference image. Requires reference_path. All supplied prompts MUST be in English and use detailed comma-separated tags or short tag phrases. Translate requests in other languages before calling. Read eidolon_get_instructions first; for ComfyUI, follow the target checkpoint prompt_guidance from eidolon_list_models rather than a universal tag template. Describe the desired style, textures, colors, lighting and details to preserve from the reference; put unwanted elements in negative_prompt. ComfyUI style edits automatically extract Canny edges and apply a compatible ControlNet to constrain the reference shape. The managed local runtime prepares the control weights on first use; external servers must provide the required Canny ControlNet and core nodes. Stored result metadata includes OutlineControlModel and OutlineControlStrength. Lower change_strength preserves more of the original; default 0.35. Returns accepted and the fixed seed. Poll status and list_images for completion or preparation errors. Request instruction overrides do not change saved settings.",
                    ImageSchema(true)), EditImageAsync),
                new McpTool(new McpToolDefinition("eidolon_list_images",
                    "Read generated PNG paths and stored generation metadata from the current output folder, newest first. JSON metadata may be absent. Paths refer to local files; this tool does not transfer image bytes.")
                {
                    InputSchema = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["page"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 1000000 },
                            ["page_size"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 50 }
                        },
                        ["additionalProperties"] = false
                    },
                    ReadOnly = true,
                    Idempotent = true,
                    OpenWorld = false
                }, ListImagesAsync),
                new McpTool(new McpToolDefinition("eidolon_create_sprite_animation",
                    "Queue a sprite-animation draft from one reference image. Every supplied prompt, instruction, action and frame description MUST be in English with detailed comma-separated tags or short tag phrases. Translate requests in other languages before calling. Read eidolon_get_instructions first; for ComfyUI, follow the target checkpoint prompt_guidance from eidolon_list_models rather than a universal tag template. Describe the subject and visual details, and separate motion, facing direction, limb positions and animation phase into specific tags. Only reference_path is required; prompt and action are optional. An omitted action uses a walk-in-place cycle with ordered automatic poses. Uses the saved ComfyUI or Codex backend and shared instructions. Checkpoints, LoRAs, seed and change_strength apply only to ComfyUI. Identity and a smooth loop are not guaranteed. Optional frame_descriptions has one pose description per frame. Returns collection_id immediately; poll eidolon_get_asset_collection. Export after reviewing all frames.",
                    AssetCreationSchema(true)), CreateSpriteAnimationAsync),
                new McpTool(new McpToolDefinition("eidolon_create_views",
                    "Queue front, side, back and directly overhead top images of the same subject. All supplied prompts MUST be in English with detailed comma-separated tags or short tag phrases. Translate requests in other languages before calling. Read eidolon_get_instructions first; for ComfyUI, follow the target checkpoint prompt_guidance from eidolon_list_models rather than a universal tag template. Describe the subject's appearance, proportions, materials, colors and style in relevant detail. Only reference_path is required; prompt is optional. Uses the saved ComfyUI or Codex backend. Checkpoints, LoRAs, seed and change_strength apply only to ComfyUI. These are AI reference drafts, not a 3D model or geometrically verified projections. Returns collection_id; poll eidolon_get_asset_collection.",
                    AssetCreationSchema(false)), CreateViewsAsync),
                new McpTool(new McpToolDefinition("eidolon_get_asset_collection", "Read a collection state and ordered frame paths, prompts, seeds and errors.",
                    CollectionSchema()) { ReadOnly = true, Idempotent = true, OpenWorld = false }, GetAssetCollectionAsync),
                new McpTool(new McpToolDefinition("eidolon_list_asset_collections", "List the latest 20 asset collections with IDs and completion counts.")
                    { ReadOnly = true, Idempotent = true, OpenWorld = false }, ListAssetCollectionsAsync),
                new McpTool(new McpToolDefinition("eidolon_resume_asset_collection", "Queue only unfinished frames using the collection's saved backend and generation conditions. Requires an idle queue.",
                    CollectionSchema()), ResumeAssetCollectionAsync),
                new McpTool(new McpToolDefinition("eidolon_regenerate_asset_frame", "Queue a replacement for one frame or view using its captured reference, backend and generation conditions. The replacement prompt MUST be in English with detailed comma-separated tags or short tag phrases. Translate requests in other languages before calling. Read eidolon_get_instructions first; for ComfyUI, follow the target checkpoint prompt_guidance from eidolon_list_models rather than a universal tag template. Specify the subject, pose, direction, viewpoint and desired visual details for this frame. For regeneration, read eidolon_get_asset_collection and use its captured backend and checkpoint, not the current UI selection. Seed applies only to ComfyUI collections. Requires an idle queue. Keeps the old image if regeneration fails.",
                    RegenerateAssetSchema()), RegenerateAssetFrameAsync),
                new McpTool(new McpToolDefinition("eidolon_export_asset_collection", "Export all reviewed frame images into a new subfolder: normalized Frames PNGs, Sheet.png, Sheet.json with frame rectangles, bottom-center pivots and FPS, and preserved Sources. All frames must have images. Does not overwrite existing files.",
                    ExportAssetSchema()), ExportAssetCollectionAsync)
            };
        }

        private JsonObject ImageSchema(bool editing)
        {
            JsonArray required = new JsonArray("prompt");
            if (editing == true)
            {
                required.Add("reference_path");
            }
            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["prompt"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 16000, ["description"] = "Required English-only image description as detailed comma-separated tags or short tag phrases. Translate the user's request into English. For ComfyUI, use the target checkpoint prompt_guidance from eidolon_list_models; read the backend and saved instruction prefixes with eidolon_get_instructions first. Separate subject and count, appearance, shape, pose and action, composition, camera angle, viewpoint, art style, materials, textures, colors, lighting and background into specific relevant tags. For edits, specify the desired changes and reference details to preserve." },
                    ["model_id"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 128 },
                    ["lora_ids"] = new JsonObject
                    {
                        ["type"] = "array", ["maxItems"] = 32,
                        ["items"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 128 }
                    },
                    ["seed"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = long.MaxValue },
                    ["remove_background"] = new JsonObject { ["type"] = "boolean" },
                    ["positive_prompt"] = new JsonObject { ["type"] = "string", ["maxLength"] = 16000, ["description"] = "Optional request-only instructions, written exclusively as detailed comma-separated English tags or short tag phrases for desired style, composition, lighting, background and quality. Use only model-supported quality, rating and source tags from the target checkpoint prompt_guidance. Omit to use saved instructions; an empty string clears them for this request." },
                    ["negative_prompt"] = new JsonObject { ["type"] = "string", ["maxLength"] = 16000, ["description"] = "Optional request-only exclusions, written exclusively as detailed comma-separated English tags naming unwanted elements and artifacts. Follow checkpoint-specific negative guidance and avoid exclusions that conflict with the requested subject or style. Omit to use saved exclusions; an empty string clears them for this request." },
                    ["reference_path"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 4096 },
                    ["change_strength"] = new JsonObject { ["type"] = "number", ["minimum"] = 0.05, ["maximum"] = 0.95 }
                },
                ["required"] = required,
                ["additionalProperties"] = false
            };
        }

        private JsonObject TrainingSchema()
        {
            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["name"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["description"] = "Name of the new output LoRA, including continued training. The original LoRA is preserved." },
                    ["trigger_word"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["description"] = "Single trigger token without whitespace or commas. It will be prepended to training captions and automatically applied when this LoRA is selected for generation." },
                    ["model_id"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 128, ["description"] = "Available local checkpoint ID from eidolon_list_models. Omit to use the UI checkpoint. Training uses this model regardless of the saved image-generation backend." },
                    ["resume_lora_id"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 128, ["description"] = "Optional existing local LoRA ID from eidolon_list_models. Must match the checkpoint family and local runtime. Loads its weights and rank for additional training on the supplied images. Previous images are not automatically included; optimizer and schedule start fresh. Output is a new LoRA. Omit to start new training; UI continuation selection is ignored." },
                    ["steps"] = new JsonObject { ["type"] = "integer", ["minimum"] = TrainingPreset.MinimumSteps, ["maximum"] = TrainingPreset.MaximumSteps, ["default"] = TrainingPreset.MaxSteps, ["description"] = "Training steps for this request. With resume_lora_id, these are additional steps from the existing weights." },
                    ["description"] = new JsonObject { ["type"] = "string", ["maxLength"] = 16000, ["description"] = "Optional fallback training caption in English using detailed comma-separated tags or short tag phrases. Translate the user's description into English. Existing sibling .txt captions and Eidolon image metadata are used first. Omit to use those captions or the descriptive filename." },
                    ["white_images"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 4096 },
                        ["description"] = "Absolute paths to PNG, JPG, JPEG or BMP files, or folders containing them. Subfolders are included. Transparent areas are filled with white. Duplicates within this group are ignored."
                    },
                    ["black_images"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 4096 },
                        ["description"] = "Absolute paths to PNG, JPG, JPEG or BMP files, or folders containing them. Subfolders are included. Transparent areas are filled with black. Duplicates within this group are ignored. The same file may also appear in white_images to train both backgrounds into one LoRA."
                    }
                },
                ["required"] = new JsonArray("name", "trigger_word"),
                ["additionalProperties"] = false
            };
        }

        private async Task<McpToolResult> TrainLoraAsync(JsonObject arguments, CancellationToken token)
        {
            try
            {
                List<TrainingImageInput> images = await Task.Run(() => ReadTrainingImages(arguments, token), token).ConfigureAwait(false);
                return await OnUiAsync(() =>
                {
                    if (_session.CanQueue == false)
                    {
                        throw new ArgumentException("Eidolon is not ready to accept a LoRA training request.");
                    }
                    string name = arguments["name"].GetValue<string>().Trim();
                    if (string.IsNullOrWhiteSpace(name) == true)
                    {
                        throw new ArgumentException("name must contain a LoRA name.");
                    }
                    string triggerWord = arguments["trigger_word"].GetValue<string>().Trim();
                    if (string.IsNullOrWhiteSpace(triggerWord) == true)
                    {
                        throw new ArgumentException("trigger_word must contain a single trigger token.");
                    }
                    if (triggerWord.Any(char.IsWhiteSpace) == true)
                    {
                        throw new ArgumentException("trigger_word must not contain whitespace.");
                    }
                    if (triggerWord.Contains(',') == true)
                    {
                        throw new ArgumentException("trigger_word must not contain commas.");
                    }
                    AssetItem selectedModel = _assets.SelectedModel;
                    if (arguments["model_id"] != null)
                    {
                        string modelId = arguments["model_id"].GetValue<string>();
                        selectedModel = _assets.Models.FirstOrDefault(item => item.Asset.Id == modelId);
                    }
                    if (selectedModel == null)
                    {
                        throw new ArgumentException("Choose an available checkpoint from eidolon_list_models.");
                    }
                    if (selectedModel.Asset.Family == ModelFamily.Unknown)
                    {
                        throw new ArgumentException("The selected checkpoint must have a known supported model family.");
                    }
                    int steps = TrainingPreset.MaxSteps;
                    if (arguments["steps"] != null)
                    {
                        steps = arguments["steps"].GetValue<int>();
                    }
                    string description = string.Empty;
                    if (arguments["description"] != null)
                    {
                        description = arguments["description"].GetValue<string>().Trim();
                    }
                    DesktopSettings settings = _session.CreateActiveSettings(_session.Settings);
                    if (ComfyServerAddress.UsesServerAssets(settings) == true)
                    {
                        throw new StudioException(StudioMessageCode.LocalTrainingRuntimeRequired);
                    }
                    ModelAsset resumeLora = null;
                    if (arguments["resume_lora_id"] != null)
                    {
                        string resumeLoraId = arguments["resume_lora_id"].GetValue<string>();
                        AssetItem selectedResumeLora = _assets.Library.FirstOrDefault(item => item.Asset.Id == resumeLoraId);
                        if (selectedResumeLora == null)
                        {
                            throw new ArgumentException("resume_lora_id must identify an available local LoRA from eidolon_list_models.");
                        }
                        if (selectedResumeLora.Asset.Kind != AssetKind.Lora)
                        {
                            throw new ArgumentException("resume_lora_id must identify a LoRA, not a checkpoint.");
                        }
                        if (selectedResumeLora.Asset.Family != selectedModel.Asset.Family)
                        {
                            throw new StudioException(StudioMessageCode.ModelFamilyMismatch);
                        }
                        if (string.Equals(selectedResumeLora.Asset.RuntimeRoot, selectedModel.Asset.RuntimeRoot, StringComparison.OrdinalIgnoreCase) == false)
                        {
                            throw new ArgumentException("resume_lora_id must use the same local runtime as the training checkpoint.");
                        }
                        resumeLora = selectedResumeLora.Asset.Copy();
                    }
                    TrainingInput input = new TrainingInput
                    {
                        Model = selectedModel.Asset.Copy(),
                        ResumeLora = resumeLora,
                        Images = images,
                        Name = name,
                        TriggerWord = triggerWord,
                        Description = description,
                        Steps = steps
                    };
                    token.ThrowIfCancellationRequested();
                    _training.EnqueueTraining(settings, input);
                    JsonObject result = new JsonObject
                    {
                        ["accepted"] = true,
                        ["name"] = input.Name,
                        ["trigger_word"] = input.TriggerWord,
                        ["model_id"] = input.Model.Id,
                        ["steps"] = input.Steps,
                        ["image_count"] = input.Images.Count
                    };
                    if (input.ResumeLora != null)
                    {
                        result["resume_lora_id"] = input.ResumeLora.Id;
                    }
                    return result;
                }, token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is StudioException || error is IOException || error is UnauthorizedAccessException)
            {
                string message = await Dispatcher.UIThread.InvokeAsync(() => _strings.GetExceptionMessage(error));
                return new McpToolResult(message, true);
            }
        }

        private List<TrainingImageInput> ReadTrainingImages(JsonObject arguments, CancellationToken token)
        {
            List<TrainingImageInput> images = new List<TrainingImageInput>();
            string[] extensions = { ".png", ".jpg", ".jpeg", ".bmp" };
            foreach (TrainingBackground background in new[] { TrainingBackground.White, TrainingBackground.Black })
            {
                string argumentName = "white_images";
                if (background == TrainingBackground.Black)
                {
                    argumentName = "black_images";
                }
                if (arguments[argumentName] is not JsonArray requestedPaths)
                {
                    continue;
                }
                foreach (JsonNode requestedPath in requestedPaths)
                {
                    token.ThrowIfCancellationRequested();
                    string path = requestedPath.GetValue<string>();
                    if (Path.IsPathFullyQualified(path) == false)
                    {
                        throw new ArgumentException(argumentName + " must contain absolute image or folder paths: " + path);
                    }
                    string fullPath = Path.GetFullPath(path);
                    bool isDirectory = Directory.Exists(fullPath);
                    if (isDirectory == false)
                    {
                        if (File.Exists(fullPath) == false)
                        {
                            throw new ArgumentException("Training image or folder does not exist: " + fullPath);
                        }
                    }
                    IEnumerable<string> imagePaths = new[] { fullPath };
                    if (isDirectory == true)
                    {
                        imagePaths = Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories);
                    }
                    foreach (string imagePath in imagePaths)
                    {
                        token.ThrowIfCancellationRequested();
                        if (extensions.Contains(Path.GetExtension(imagePath).ToLowerInvariant()) == false)
                        {
                            if (isDirectory == true)
                            {
                                continue;
                            }
                            throw new ArgumentException("Training images must be PNG, JPG, JPEG or BMP files: " + imagePath);
                        }
                        if (File.Exists(imagePath) == false)
                        {
                            throw new ArgumentException("Training image does not exist: " + imagePath);
                        }
                        if (images.Where(image => image.Background == background).Any(image => image.FilePath.Equals(imagePath, StringComparison.OrdinalIgnoreCase)) == true)
                        {
                            continue;
                        }
                        images.Add(new TrainingImageInput { FilePath = imagePath, Background = background });
                    }
                }
            }
            if (images.Count == 0)
            {
                throw new ArgumentException("Provide at least one supported image in white_images or black_images.");
            }
            return images;
        }

        private async Task<McpToolResult> GetStatusAsync(JsonObject arguments, CancellationToken token)
        {
            return await OnUiAsync(() => new JsonObject
            {
                ["initialized"] = _session.IsInitialized,
                ["can_queue"] = _session.CanQueue,
                ["engine_connected"] = _session.IsEngineConnected,
                ["busy"] = _session.IsBusy,
                ["idle"] = _session.IsIdle,
                ["status"] = _session.Status,
                ["active_request"] = _session.ActiveRequestTitle,
                ["pending_count"] = _session.PendingRequests.Count,
                ["progress_percent"] = _session.Percent,
                ["indeterminate"] = _session.Indeterminate,
                ["error"] = _session.Error
            }, token);
        }

        private async Task<McpToolResult> ListModelsAsync(JsonObject arguments, CancellationToken token)
        {
            return await OnUiAsync(() =>
            {
                JsonArray models = new JsonArray();
                foreach (AssetItem item in _assets.Library)
                {
                    ModelAsset asset = item.Asset;
                    models.Add(new JsonObject
                    {
                        ["id"] = asset.Id,
                        ["name"] = asset.Name,
                        ["kind"] = asset.Kind.ToString(),
                        ["family"] = asset.Family.ToString(),
                        ["trigger"] = asset.TriggerWord,
                        ["engine_name"] = asset.EngineName,
                        ["prompt_guidance"] = GetModelPromptGuidance(asset)
                    });
                }
                string selected = string.Empty;
                if (_assets.SelectedModel != null)
                {
                    selected = _assets.SelectedModel.Asset.Id;
                }
                return new JsonObject { ["assets"] = models, ["selected_model_id"] = selected, ["generation_backend"] = _session.Settings.GenerationBackend.ToString() };
            }, token);
        }

        private string GetModelPromptGuidance(ModelAsset model)
        {
            if (model == null)
            {
                return "No ComfyUI checkpoint is selected. Read eidolon_list_models and choose a checkpoint before using model-specific tags.";
            }
            if (model.Kind != AssetKind.Checkpoint)
            {
                return "This is an adapter, not a checkpoint. For generation, follow the target checkpoint's prompt_guidance. The service adds registered LoRA trigger words automatically; do not duplicate them.";
            }
            string fileName = Path.GetFileName(model.EngineName);
            if (model.Family == ModelFamily.Sdxl)
            {
                if (fileName.Equals("Illustrious-XL-v0.1.safetensors", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return "Filename-matched guidance for Illustrious XL v0.1: prefer detailed comma-separated Danbooru-style English tags; short natural-language phrases can clarify relationships. Suggested organization, not a guaranteed weighting order: optional supported quality tags, subject/count/identity, one consistent framing/viewpoint and pose, appearance/clothing, background/lighting, requested style. Supported quality examples include best quality and masterpiece; relevant negatives include worst quality, bad quality and lowres. Avoid conflicting composition tags. Do not assume score_9, score_8_up, rating_safe or source_anime are supported defaults. Add character or artist names only when requested. Other Illustrious versions and derivatives need their own guidance. Source: https://huggingface.co/OnomaAIResearch/Illustrious-xl-early-release-v0";
                }
                if (fileName.Equals("RealVisXL_V4.0.safetensors", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return "Filename-matched guidance for RealVisXL 4.0: the model targets photorealism. When photography is requested, use comma-separated descriptive English phrases for subject/action, framing, materials, lighting and setting; preserve the user's requested style. For relevant human portraits, the model card suggests face asymmetry, eyes asymmetry and deformed eyes as possible negative terms. Do not suppress an open mouth when it is requested. No universal Danbooru, score_9, rating_* or source_* prefix is prescribed. These photographic phrasing suggestions are an application recommendation based on the model's stated focus. Source: https://huggingface.co/SG161222/RealVisXL_V4.0";
                }
                if (fileName.Equals("sd_xl_base_1.0.safetensors", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return "Filename-matched guidance for SDXL Base 1.0: the model card uses natural-language descriptions. Use detailed comma-separated English phrases describing the subject and action, spatial relationships, composition, style, materials, lighting and background. State relationships clearly instead of listing disconnected nouns. Do not assume Danbooru quality, score_9, rating_* or source_* tags are required because the family is SDXL. Put only relevant unwanted features in negative_prompt. Source: https://huggingface.co/stabilityai/stable-diffusion-xl-base-1.0";
                }
            }
            if (model.Family == ModelFamily.StableDiffusion15)
            {
                if (fileName.Equals("v1-5-pruned-emaonly.safetensors", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return "Filename-matched guidance for Stable Diffusion 1.5 base: use focused English descriptive phrases, separated by commas, with the main subject/action first and relevant composition, style, lighting and background details. This is a practical organization based on the model card's English-caption and natural-language examples, not a guaranteed tag weighting rule. Do not inherit anime-finetune or Pony-style quality, rating or source tags from the SD1.5 family alone. Put relevant unwanted features in negative_prompt. Source: https://huggingface.co/stable-diffusion-v1-5/stable-diffusion-v1-5";
                }
            }
            return "No checkpoint-specific prompt guidance is registered for this filename and family. Consult the exact checkpoint's model card; do not infer its training vocabulary from an editable display name, a filename substring, SDXL/SD1.5 compatibility or another version. Until confirmed, use detailed comma-separated English descriptive phrases and relevant negatives without inventing score, rating, source or mandatory quality prefixes. Treat ordering as organization advice, not guaranteed priority.";
        }

        private async Task<McpToolResult> GetInstructionsAsync(JsonObject arguments, CancellationToken token)
        {
            return await OnUiAsync(() =>
            {
                string selected = string.Empty;
                string guidance = "The saved backend is Codex. ComfyUI checkpoint families, Danbooru quality prefixes and local LoRA selection do not apply to this backend. Use detailed comma-separated English descriptive phrases expressing the requested content, relationships, style and constraints, and preserve the saved instructions unless a request-only override is needed.";
                if (_session.Settings.GenerationBackend == GenerationBackend.ComfyUI)
                {
                    ModelAsset model = null;
                    if (_assets.SelectedModel != null)
                    {
                        model = _assets.SelectedModel.Asset;
                        selected = model.Id;
                    }
                    guidance = GetModelPromptGuidance(model);
                }
                return new JsonObject
                {
                    ["positive_prompt"] = _session.Settings.PositivePrompt,
                    ["negative_prompt"] = _session.Settings.NegativePrompt,
                    ["generation_backend"] = _session.Settings.GenerationBackend.ToString(),
                    ["selected_model_id"] = selected,
                    ["prompt_guidance"] = guidance
                };
            }, token);
        }

        private Task<McpToolResult> GenerateImageAsync(JsonObject arguments, CancellationToken token)
        {
            return QueueImageAsync(arguments, false, token);
        }

        private Task<McpToolResult> EditImageAsync(JsonObject arguments, CancellationToken token)
        {
            return QueueImageAsync(arguments, true, token);
        }

        private async Task<McpToolResult> QueueImageAsync(JsonObject arguments, bool editing, CancellationToken token)
        {
            try
            {
                GenerationReferenceInput reference = await ReadReferenceAsync(arguments, editing, token).ConfigureAwait(false);
                return await OnUiAsync(() =>
                {
                    string prompt = arguments["prompt"].GetValue<string>().Trim();
                    if (string.IsNullOrWhiteSpace(prompt) == true)
                    {
                        throw new ArgumentException("prompt must contain an image description.");
                    }
                    if (_session.CanQueue == false)
                    {
                        throw new ArgumentException("Eidolon is not ready to accept an image request.");
                    }
                    McpGenerationOptions options = CreateGenerationOptions(arguments);
                    ModelAsset model = options.Model;
                    List<ModelAsset> loras = options.Loras;
                    DesktopSettings settings = options.Settings;
                    long seed = options.Seed;
                    bool removeBackground = options.RemoveBackground;
                    string titleKey = "EidolonText106";
                    if (editing == true)
                    {
                        titleKey = "EidolonText456";
                    }
                    token.ThrowIfCancellationRequested();
                    _generation.EnqueueGeneration(settings, prompt, model, loras, removeBackground, seed, reference, titleKey);
                    JsonObject result = new JsonObject
                    {
                        ["accepted"] = true,
                        ["prompt"] = prompt,
                        ["positive_prompt"] = settings.PositivePrompt,
                        ["negative_prompt"] = settings.NegativePrompt,
                        ["output_directory"] = settings.GenerationDirectory,
                        ["generation_backend"] = settings.GenerationBackend.ToString()
                    };
                    if (settings.GenerationBackend == GenerationBackend.ComfyUI)
                    {
                        result["seed"] = seed;
                        result["model_id"] = model.Id;
                    }
                    else
                    {
                        result["codex_model"] = settings.CodexModel;
                    }
                    return result;
                }, token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is StudioException || error is IOException || error is UnauthorizedAccessException)
            {
                string message = await Dispatcher.UIThread.InvokeAsync(() => _strings.GetExceptionMessage(error));
                return new McpToolResult(message, true);
            }
        }

        private async Task<GenerationReferenceInput> ReadReferenceAsync(JsonObject arguments, bool editing, CancellationToken token)
        {
            if (arguments["reference_path"] == null)
            {
                if (editing == true)
                {
                    throw new ArgumentException("An edit requires reference_path.");
                }
                if (arguments.ContainsKey("change_strength") == true)
                {
                    throw new ArgumentException("change_strength requires reference_path.");
                }
                return null;
            }
            string path = arguments["reference_path"].GetValue<string>();
            if (Path.IsPathFullyQualified(path) == false)
            {
                throw new ArgumentException("reference_path must be an absolute local file path.");
            }
            await using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                8192, FileOptions.Asynchronous);
            if (stream.Length < 1 || stream.Length > GenerationReferenceInput.MaximumImageBytes)
            {
                throw new StudioException(StudioMessageCode.InvalidReferenceImage);
            }
            byte[] data = new byte[(int)stream.Length];
            await stream.ReadExactlyAsync(data, token).ConfigureAwait(false);
            GenerationReferenceInput reference = new GenerationReferenceInput
            {
                ImageData = data,
                ImageName = Path.GetFileName(path)
            };
            if (editing == true)
            {
                reference.Mode = GenerationReferenceMode.Restyle;
                reference.ChangeStrength = 0.35;
            }
            if (arguments["change_strength"] != null)
            {
                reference.ChangeStrength = arguments["change_strength"].GetValue<double>();
            }
            return reference;
        }

        private async Task<McpToolResult> ListImagesAsync(JsonObject arguments, CancellationToken token)
        {
            string directory = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                EnsureAvailable(token);
                return _session.Settings.GenerationDirectory;
            });
            int page = 1;
            int size = 12;
            if (arguments["page"] != null)
            {
                page = (int)arguments["page"].GetValue<double>();
            }
            if (arguments["page_size"] != null)
            {
                size = (int)arguments["page_size"].GetValue<double>();
            }
            GenerationPage result = await Task.Run(() => _jobs.LoadGenerationPage(directory, page, size, token), token).ConfigureAwait(false);
            JsonArray images = new JsonArray();
            foreach (GenerationImage image in result.Images)
            {
                images.Add(new JsonObject
                {
                    ["path"] = image.FilePath,
                    ["created_at_utc"] = image.CreatedAtUtc.ToString("O"),
                    ["metadata"] = JsonSerializer.SerializeToNode(image.Metadata)
                });
            }
            return new McpToolResult(new JsonObject
            {
                ["page"] = result.Number,
                ["page_count"] = result.PageCount,
                ["total_count"] = result.TotalCount,
                ["images"] = images
            });
        }

        private async Task<McpToolResult> OnUiAsync(Func<JsonObject> action, CancellationToken token)
        {
            JsonObject result = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                EnsureAvailable(token);
                return action();
            });
            return new McpToolResult(result);
        }

        private void EnsureAvailable(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (_session.IsClosing == true || _session.IsInitialized == false)
            {
                throw new ArgumentException("Eidolon is closing or has not finished initialization.");
            }
        }

        private McpGenerationOptions CreateGenerationOptions(JsonObject arguments, bool defaultRemoveBackground = false)
        {
            DesktopSettings settings = _session.CreateActiveSettings(_session.Settings);
            if (arguments.ContainsKey("positive_prompt") == true)
            {
                settings.PositivePrompt = arguments["positive_prompt"].GetValue<string>();
            }
            if (arguments.ContainsKey("negative_prompt") == true)
            {
                settings.NegativePrompt = arguments["negative_prompt"].GetValue<string>();
            }
            bool removeBackground = defaultRemoveBackground;
            if (arguments["remove_background"] != null)
            {
                removeBackground = arguments["remove_background"].GetValue<bool>();
            }
            if (settings.GenerationBackend == GenerationBackend.Codex)
            {
                if (arguments.ContainsKey("model_id") == true || arguments.ContainsKey("lora_ids") == true
                    || arguments.ContainsKey("seed") == true || arguments.ContainsKey("change_strength") == true)
                {
                    throw new ArgumentException("Codex generation does not apply checkpoints, LoRAs, seeds or change_strength.");
                }
                return new McpGenerationOptions { Settings = settings, Loras = new List<ModelAsset>(),
                    Seed = 0, RemoveBackground = removeBackground };
            }
            AssetItem selected = _assets.SelectedModel;
            if (arguments["model_id"] != null)
            {
                string id = arguments["model_id"].GetValue<string>();
                selected = _assets.Models.FirstOrDefault(item => item.Asset.Id == id);
            }
            if (selected == null)
            {
                throw new ArgumentException("Choose an available checkpoint from eidolon_list_models.");
            }
            ModelAsset model = selected.Asset.Copy();
            if (model.Family == ModelFamily.Unknown)
            {
                throw new ArgumentException("The selected model must have a known supported model family.");
            }
            List<ModelAsset> loras = _assets.Loras.Where(item => item.IsSelected == true
                && item.Asset.Family == model.Family && item.Asset.RuntimeRoot == model.RuntimeRoot)
                .Select(item => item.Asset.Copy()).ToList();
            if (arguments["lora_ids"] is JsonArray requested)
            {
                loras.Clear();
                HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonNode id in requested)
                {
                    string value = id.GetValue<string>();
                    AssetItem item = _assets.Library.FirstOrDefault(asset => asset.Asset.Id == value && asset.Asset.Kind == AssetKind.Lora
                        && asset.Asset.Family == model.Family && asset.Asset.RuntimeRoot == model.RuntimeRoot);
                    if (item == null || ids.Add(value) == false)
                    {
                        throw new ArgumentException("lora_ids must contain unique compatible available LoRAs.");
                    }
                    loras.Add(item.Asset.Copy());
                }
            }
            return new McpGenerationOptions { Settings = settings, Model = model, Loras = loras,
                Seed = ReadSeed(arguments), RemoveBackground = removeBackground };
        }

        private long ReadSeed(JsonObject arguments)
        {
            if (arguments["seed"] == null)
            {
                return _seeds.Next();
            }
            if (long.TryParse(arguments["seed"].ToJsonString(), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out long seed) == false || seed < 0)
            {
                throw new ArgumentException("seed must be an integer from 0 to Int64.MaxValue.");
            }
            return seed;
        }

        private JsonObject AssetCreationSchema(bool sprite)
        {
            JsonObject schema = ImageSchema(false);
            JsonObject properties = schema["properties"].AsObject();
            schema["required"] = new JsonArray("reference_path");
            properties["prompt"] = new JsonObject { ["type"] = "string", ["maxLength"] = 16000, ["description"] = "Optional English-only subject description as detailed comma-separated tags or short tag phrases for appearance, proportions, materials, textures, colors, style, lighting and background. Translate the user's request into English. For ComfyUI, use the target checkpoint prompt_guidance from eidolon_list_models; read the backend and saved instruction prefixes with eidolon_get_instructions first. Omit to use the reference subject without an additional description." };
            properties["frame_size"] = new JsonObject { ["type"] = "integer", ["minimum"] = 16, ["maximum"] = AssetCreationInput.MaximumFrameSize };
            properties["columns"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = AssetCreationInput.MaximumFrames };
            properties["pixel_art"] = new JsonObject { ["type"] = "boolean" };
            if (sprite == true)
            {
                properties["action"] = new JsonObject { ["type"] = "string", ["maxLength"] = 2000, ["description"] = "Optional English-only motion description as detailed comma-separated tags or short tag phrases for action, movement direction, facing, body and limb motion, timing and cycle. Omit to use the automatic walk-in-place cycle." };
                properties["frame_count"] = new JsonObject { ["type"] = "integer", ["minimum"] = 2, ["maximum"] = AssetCreationInput.MaximumFrames };
                properties["fps"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 60 };
                properties["frame_descriptions"] = new JsonObject { ["type"] = "array", ["maxItems"] = AssetCreationInput.MaximumFrames, ["description"] = "Optional ordered pose descriptions, one per frame. Every entry MUST use detailed English tags or short tag phrases.",
                    ["items"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 4000, ["description"] = "English-only comma-separated tags specifying this frame's animation phase, pose, limb positions, facing direction, viewpoint and continuity with adjacent frames." } };
            }
            return schema;
        }
        private JsonObject CollectionSchema()
        {
            return new JsonObject { ["type"] = "object", ["properties"] = new JsonObject
            {
                ["collection_id"] = new JsonObject { ["type"] = "string", ["minLength"] = 32, ["maxLength"] = 32 }
            }, ["required"] = new JsonArray("collection_id"), ["additionalProperties"] = false };
        }
        private JsonObject RegenerateAssetSchema()
        {
            JsonObject schema = CollectionSchema();
            JsonObject properties = schema["properties"].AsObject();
            properties["frame_number"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = AssetCreationInput.MaximumFrames };
            properties["prompt"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 16000, ["description"] = "Required English-only replacement description as detailed comma-separated tags or short tag phrases for the subject, appearance, pose, limb positions, facing direction, camera angle, viewpoint, style, materials, colors, lighting and background. Translate the user's request into English before calling. Use the captured collection checkpoint's prompt_guidance, not a different selected UI checkpoint's guidance." };
            properties["seed"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = long.MaxValue };
            schema["required"].AsArray().Add("frame_number");
            schema["required"].AsArray().Add("prompt");
            return schema;
        }
        private JsonObject ExportAssetSchema()
        {
            JsonObject schema = CollectionSchema();
            schema["properties"].AsObject()["directory"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 4096 };
            schema["required"].AsArray().Add("directory");
            return schema;
        }
        private int ReadInteger(JsonObject arguments, string name, int fallback)
        {
            if (arguments[name] == null)
            {
                return fallback;
            }
            return (int)arguments[name].GetValue<double>();
        }
        private Task<McpToolResult> CreateSpriteAnimationAsync(JsonObject arguments, CancellationToken token)
        {
            return ExecuteAssetAsync(() => CreateAssetAsync(arguments, AssetCreationKind.SpriteAnimation, token));
        }
        private Task<McpToolResult> CreateViewsAsync(JsonObject arguments, CancellationToken token)
        {
            return ExecuteAssetAsync(() => CreateAssetAsync(arguments, AssetCreationKind.FourViews, token));
        }
        private async Task<McpToolResult> CreateAssetAsync(JsonObject arguments, AssetCreationKind kind, CancellationToken token)
        {
            GenerationReferenceInput reference = await ReadReferenceAsync(arguments, false, token).ConfigureAwait(false);
            if (arguments["change_strength"] == null)
            {
                reference.ChangeStrength = 0.45;
            }
            Task<AssetCollection> queued = await Dispatcher.UIThread.InvokeAsync<Task<AssetCollection>>(() =>
            {
                EnsureAvailable(token);
                McpGenerationOptions options = CreateGenerationOptions(arguments, true);
                int defaultSize = AssetCreationInput.DefaultViewFrameSize;
                if (kind == AssetCreationKind.SpriteAnimation)
                {
                    defaultSize = AssetCreationInput.DefaultSpriteFrameSize;
                }
                AssetCreationInput input = new AssetCreationInput
                {
                    Kind = kind, Reference = reference, Seed = options.Seed,
                    RemoveBackground = options.RemoveBackground, FrameCount = ReadInteger(arguments, "frame_count", 8),
                    FrameWidth = ReadInteger(arguments, "frame_size", defaultSize), FrameHeight = ReadInteger(arguments, "frame_size", defaultSize),
                    Columns = ReadInteger(arguments, "columns", 4), FramesPerSecond = ReadInteger(arguments, "fps", 10)
                };
                if (arguments["prompt"] != null)
                {
                    input.Prompt = arguments["prompt"].GetValue<string>();
                }
                if (arguments["action"] != null)
                {
                    input.ActionPrompt = arguments["action"].GetValue<string>();
                }
                if (arguments["pixel_art"] != null)
                {
                    input.PixelArt = arguments["pixel_art"].GetValue<bool>();
                }
                if (arguments["frame_descriptions"] is JsonArray descriptions)
                {
                    input.FrameDescriptions = descriptions.Select(item => item.GetValue<string>()).ToList();
                }
                return _creation.EnqueueAsync(options.Settings, options.Model, options.Loras, input, token);
            });
            AssetCollection collection = await queued.ConfigureAwait(false);
            JsonObject result = new JsonObject { ["accepted"] = true, ["collection_id"] = collection.Id,
                ["generation_backend"] = collection.Settings.GenerationBackend.ToString(),
                ["frame_count"] = collection.Frames.Count, ["state"] = "Preparing" };
            if (collection.Settings.GenerationBackend == GenerationBackend.ComfyUI)
            {
                result["seed"] = collection.Seed;
            }
            else
            {
                result["codex_model"] = collection.Settings.CodexModel;
            }
            return new McpToolResult(result);
        }
        private Task<McpToolResult> GetAssetCollectionAsync(JsonObject arguments, CancellationToken token)
        {
            return ExecuteAssetAsync(async () =>
            {
                AssetCollection collection = await Task.Run(() => _jobs.LoadAssetCollection(arguments["collection_id"].GetValue<string>()), token).ConfigureAwait(false);
                return new McpToolResult(JsonSerializer.SerializeToNode(collection, _assetJson).AsObject());
            });
        }
        private Task<McpToolResult> ListAssetCollectionsAsync(JsonObject arguments, CancellationToken token)
        {
            return ExecuteAssetAsync(async () =>
            {
                List<AssetCollection> collections = await Task.Run(() => _jobs.LoadAssetCollections(token), token).ConfigureAwait(false);
                JsonArray items = new JsonArray();
                foreach (AssetCollection collection in collections.Take(20))
                {
                    items.Add(new JsonObject { ["collection_id"] = collection.Id, ["prompt"] = collection.Prompt,
                        ["kind"] = collection.Kind.ToString(), ["state"] = collection.State.ToString(),
                        ["generation_backend"] = collection.Settings.GenerationBackend.ToString(),
                        ["frame_count"] = collection.Frames.Count,
                        ["completed_count"] = collection.Frames.Count(frame => frame.State == JobState.Completed) });
                }
                return new McpToolResult(new JsonObject { ["collections"] = items });
            });
        }
        private Task<McpToolResult> ResumeAssetCollectionAsync(JsonObject arguments, CancellationToken token)
        {
            return ExecuteAssetAsync(() => QueueExistingAssetAsync(arguments, false, token));
        }
        private Task<McpToolResult> RegenerateAssetFrameAsync(JsonObject arguments, CancellationToken token)
        {
            return ExecuteAssetAsync(() => QueueExistingAssetAsync(arguments, true, token));
        }
        private async Task<McpToolResult> QueueExistingAssetAsync(JsonObject arguments, bool regenerate, CancellationToken token)
        {
            string id = arguments["collection_id"].GetValue<string>();
            long seed = 0;
            if (regenerate == true)
            {
                AssetCollection collection = await Task.Run(() => _jobs.LoadAssetCollection(id), token).ConfigureAwait(false);
                if (collection.Settings.GenerationBackend == GenerationBackend.Codex)
                {
                    if (arguments.ContainsKey("seed") == true)
                    {
                        throw new ArgumentException("Codex asset regeneration does not apply seeds.");
                    }
                }
                else
                {
                    seed = ReadSeed(arguments);
                }
            }
            Task queued = await Dispatcher.UIThread.InvokeAsync<Task>(() =>
            {
                EnsureAvailable(token);
                if (regenerate == true)
                {
                    return _creation.EnqueueExistingAsync(id, ReadInteger(arguments, "frame_number", 0),
                        arguments["prompt"].GetValue<string>(), seed);
                }
                return _creation.EnqueueExistingAsync(id);
            });
            await queued.ConfigureAwait(false);
            return new McpToolResult(new JsonObject { ["accepted"] = true, ["collection_id"] = id });
        }
        private Task<McpToolResult> ExportAssetCollectionAsync(JsonObject arguments, CancellationToken token)
        {
            return ExecuteAssetAsync(async () =>
            {
                string directory = await Task.Run(() => _assetService.Export(arguments["collection_id"].GetValue<string>(),
                    arguments["directory"].GetValue<string>(), token), token).ConfigureAwait(false);
                return new McpToolResult(new JsonObject { ["directory"] = directory,
                    ["sheet"] = Path.Combine(directory, "Sheet.png"), ["metadata"] = Path.Combine(directory, "Sheet.json") });
            });
        }
        private async Task<McpToolResult> ExecuteAssetAsync(Func<Task<McpToolResult>> action)
        {
            try
            {
                return await action().ConfigureAwait(false);
            }
            catch (Exception error) when (error is StudioException || error is IOException || error is UnauthorizedAccessException)
            {
                string message = await Dispatcher.UIThread.InvokeAsync(() => _strings.GetExceptionMessage(error));
                return new McpToolResult(message, true);
            }
        }
    }
}

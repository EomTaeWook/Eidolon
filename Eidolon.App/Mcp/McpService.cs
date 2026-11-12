using Avalonia.Threading;
using Eidolon.App.Localization;
using Eidolon.App.Presenters;
using Eidolon.App.Services;
using Eidolon.App.ViewModels;
using Eidolon.Core.Application;
using Eidolon.Core.Domain;
using Eidolon.Core.Infrastructure;
using Eidolon.Modeling;
using MCP.Core.Models.Tools;
using MCP.Core.Tools;
using SkiaSharp;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Eidolon.App.Mcp
{
    public class McpService
    {
        private const string ReferenceFeaturesInputName = "reference_features";
        private const int MaximumContinuationBytes = 196608;
        private readonly byte[] _referenceContinuationKey = RandomNumberGenerator.GetBytes(32);
        private readonly StudioSession _session;
        private readonly AssetsViewModel _assets;
        private readonly GenerationViewModel _generation;
        private readonly TrainingViewModel _training;
        private readonly StudioWorkPresenter _work;
        private readonly JobStore _jobs;
        private readonly ISeedProvider _seeds;
        private readonly StringHelper _strings;
        private readonly AssetCreationViewModel _creation;
        private readonly AssetCreationService _assetService;
        private readonly ModelingViewModel _modeling;
        private readonly JsonSerializerOptions _assetJson = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };

        public McpService(StudioSession session, AssetsViewModel assets, GenerationViewModel generation, JobStore jobs, ISeedProvider seeds, StringHelper strings, AssetCreationViewModel creation, AssetCreationService assetService, TrainingViewModel training, StudioWorkPresenter work, ModelingViewModel modeling)
        {
            _session = session;
            _assets = assets;
            _generation = generation;
            _training = training;
            _work = work;
            _jobs = jobs;
            _seeds = seeds;
            _strings = strings;
            _creation = creation;
            _assetService = assetService;
            _modeling = modeling;
        }

        public IReadOnlyList<McpTool> CreateTools()
        {
            return new[]
            {
                new McpTool(new McpToolDefinition("eidolon_model_3d", "Queue one 3D modeling request using the saved native GPT/Codex CLI executable and execution model and installed Blender. Supply reference_path for one image or views with explicit front/left/right/back image paths, or source_blend_path to edit an existing Blender file. New modeling needs at least one image; editing a .blend requires instructions. Keep instructions limited to the desired geometry and materials. Eidolon automatically saves the Blender file, exports GLB and renders previews; do not request those operations in instructions. Only absolute existing local PNG/JPEG/WebP images and .blend inputs are accepted. Uses the UI modeling input and common FIFO without changing UI selection or settings. Executable, scripts, working/output directories and agent overrides are not accepted. Claude and WSL modeling execution are unavailable. Originals are preserved and results use a new managed GUID folder. Returns accepted=true and request_id for acceptance only. Poll eidolon_get_modeling_result; use eidolon_get_status, eidolon_cancel_work and eidolon_clear_pending for the common queue.", ModelingSchema()) { ReadOnly = false, Idempotent = false, OpenWorld = true }, QueueModelingAsync),
                new McpTool(new McpToolDefinition("eidolon_get_modeling_result", "Read one managed 3D modeling request by its GUID request_id. Returns completed .blend/.glb paths and the exported triangle count only after the existing completion marker and output files are present. Reports queued/running/cancelled/failed only when confirmed by the current queue, latest modeling execution or existing failure marker. unknown/not_found are not completion or proof of cancellation. Older cancelled/discarded requests without an existing record cannot be reconstructed. No logs, scripts, arbitrary paths or file contents are returned. Poll eidolon_get_status and use its exact active_request with eidolon_cancel_work; eidolon_clear_pending discards queued requests.", ModelingResultSchema()) { ReadOnly = true, Idempotent = true, OpenWorld = false }, GetModelingResultAsync),
                new McpTool(new McpToolDefinition("eidolon_get_status",
                    "Read Eidolon readiness, current work, pending_count, pending_requests, progress, error, can_cancel_work and can_cancel_training. Poll after enqueueing any work. For current-work cancellation pass this response's exact active_request to eidolon_cancel_work. The training-specific alias is eidolon_cancel_training. Use eidolon_clear_pending to discard waiting requests without stopping the active one. In-flight HTTP tool calls use notifications/cancelled with their JSON-RPC request ID, before acceptance or completion.")
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
                new McpTool(new McpToolDefinition("eidolon_delete_model", "Queue permanent deletion of one registered local checkpoint or LoRA file by asset_id from eidolon_list_models. This deletes the actual file, not only its library entry. Set confirm=true only after the user explicitly approves deleting this exact asset's real file; do not infer permission from a generic cleanup request. Arbitrary paths and external server assets are not accepted. Requires initialization, an idle current work and pending queue, and an installed local runtime using local assets. Busy or unavailable requests are rejected. Returns only accepted=true and asset_id, meaning queue acceptance, not completed deletion. Poll eidolon_get_status for completion or errors, then eidolon_list_models to refresh the available assets. Does not change the UI selection to choose the deletion target.", DeleteModelSchema()) { Destructive = true, ReadOnly = false, OpenWorld = false }, DeleteModelAsync),
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
                new McpTool(new McpToolDefinition("eidolon_cancel_training",
                    "Request cancellation of the currently running LoRA training immediately through the existing UI cancellation path, without waiting in the work queue. First read eidolon_get_status, require can_cancel_training=true, and pass its exact active_request string, including any localized prefix. The current title must still match. Rejects idle, non-training, already-cancelling, closing and final LoRA registration states. Does not remove pending requests, delete files or cancel image generation. accepted=true and cancellation_requested=true mean the cancellation signal was requested, not that training and engine recovery have finished. Poll eidolon_get_status for completion or errors.",
                    CancelWorkSchema()) { ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false }, CancelTrainingAsync),
                new McpTool(new McpToolDefinition("eidolon_cancel_work",
                    "Request cancellation of the currently running shared work, including image generation, reference generation, editing, local prompt conversion, LoRA training, multi-view creation, resume, regeneration and queued model maintenance. Read eidolon_get_status, require can_cancel_work=true, and pass its exact active_request title. Runs immediately outside the work queue and verifies the title again at the queue owner. Uses training's existing cancellation path and rejects final LoRA registration. UI and other MCP clients share this work queue. Does not discard pending requests or roll back completed writes or deletions. accepted=true means cancellation was signalled, not that processes, file operations or engine recovery have finished. Poll status. A title is not a unique job ID. For an unfinished HTTP query or export use notifications/cancelled with that call's JSON-RPC request ID.",
                    CancelWorkSchema()) { ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false }, CancelWorkAsync),
                new McpTool(new McpToolDefinition("eidolon_clear_pending",
                    "Discard all requests currently waiting in the shared work queue, including UI and other MCP clients' pending work. Runs immediately without waiting behind the active work. Does not cancel active work, completed work or scheduled connection-setting changes. Read eidolon_get_status.pending_requests first. To stop both waiting and active work, clear pending requests first, refresh status, then cancel the current work. Completed files are preserved; discarding a prepared multi-view request marks its unfinished collection as cancelled. Poll status after the response.")
                { ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false }, ClearPendingAsync),
                new McpTool(new McpToolDefinition("eidolon_generate_image",
                    "Queue text-to-image generation, optionally with a reference image. Accept prompt as tags, descriptive phrases or natural-language sentences. For ComfyUI, an installed local converter adapts prompt to the captured checkpoint in the work queue; without it, provide English input already suited to that model. Codex receives prompt directly without local conversion. Keep positive_prompt and negative_prompt in English using the target model's format. Read eidolon_get_instructions first; for ComfyUI, follow the target checkpoint prompt_guidance from eidolon_list_models rather than a universal tag template. Specify the subject, appearance, pose, composition, viewpoint, style, materials, colors, lighting and background in relevant detail; put unwanted elements in negative_prompt. Returns accepted and the fixed seed, not a completed image. Poll eidolon_get_status then eidolon_list_images. Uses saved instructions unless positive_prompt or negative_prompt is provided. Omitting model_id uses the UI model; omitting lora_ids uses compatible selected UI LoRAs. An empty lora_ids array disables LoRAs for this request. Does not change UI inputs or saved settings. With reference_path, preservation_prompt is the caller-confirmed final preservation text: provide suitable English for ComfyUI and resolve conflicts with requested changes before submitting. An explicit empty string skips preservation. If omitted, clients declaring sampling can use MCP 2026-07-28 input_required/inputResponses/requestState or the official 2025 initialized Streamable HTTP POST SSE round trip; other clients must provide preservation_prompt. No generation is queued until this round trip is complete. Acceptance echoes the captured preservation text; final application is recorded in result PositivePrompt.",
                    ImageSchema(false)), GenerateImageAsync),
                new McpTool(new McpToolDefinition("eidolon_edit_image",
                    "Queue a style edit using an existing local reference image. Requires reference_path. Accept prompt as tags, descriptive phrases or natural-language sentences. For ComfyUI, an installed local converter adapts prompt to the captured checkpoint in the work queue; without it, provide English input already suited to that model. Codex receives prompt directly without local conversion. Keep positive_prompt and negative_prompt in English using the target model's format. Read eidolon_get_instructions first; for ComfyUI, follow the target checkpoint prompt_guidance from eidolon_list_models rather than a universal tag template. Describe the desired style, textures, colors, lighting and details to preserve from the reference; put unwanted elements in negative_prompt. ComfyUI style edits automatically extract Canny edges and apply a compatible ControlNet to constrain the reference shape. The managed local runtime prepares the control weights on first use; external servers must provide the required Canny ControlNet and core nodes. Stored result metadata includes OutlineControlModel and OutlineControlStrength. Lower change_strength preserves more of the original; default 0.35. Returns accepted and the fixed seed. Poll status and list_images for completion or preparation errors. Request instruction overrides do not change saved settings. With reference_path, preservation_prompt is the caller-confirmed final preservation text: provide suitable English for ComfyUI and resolve conflicts with requested changes before submitting. An explicit empty string skips preservation. If omitted, clients declaring sampling can use MCP 2026-07-28 input_required/inputResponses/requestState or the official 2025 initialized Streamable HTTP POST SSE round trip; other clients must provide preservation_prompt. No generation is queued until this round trip is complete. Acceptance echoes the captured preservation text; final application is recorded in result PositivePrompt.",
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
                new McpTool(new McpToolDefinition("eidolon_create_views", "Queue exactly four views in this order: front, anatomical right side, anatomical left side, back. Only the camera moves; preserve the reference pose, geometry, proportions and equipment on their original anatomical sides. Right and left are separate true side views, never mirrored copies. Side cameras are exactly 90 degrees from the front and back is 180 degrees; no diagonal or mixed views. All supplied prompts MUST be in English with detailed comma-separated tags or short tag phrases. Translate requests in other languages before calling. Read eidolon_get_instructions first; for ComfyUI, follow the target checkpoint prompt_guidance from eidolon_list_models rather than a universal tag template. Describe appearance, materials, colors and style without changing the required viewpoints or pose. Only reference_path is required; prompt is optional. Frame size defaults to 1024. Sheet columns default to 4 and accept integers from 1 to 4. Uses the saved ComfyUI or Codex backend. Checkpoints, LoRAs, seed and change_strength apply only to ComfyUI. These are AI reference images, not geometrically verified projections; review each result. Returns collection_id; poll eidolon_get_asset_collection.", AssetCreationSchema()), CreateViewsAsync),
                new McpTool(new McpToolDefinition("eidolon_get_asset_collection", "Read a collection state and ordered frame paths, prompts, seeds and errors.",
                    CollectionSchema()) { ReadOnly = true, Idempotent = true, OpenWorld = false }, GetAssetCollectionAsync),
                new McpTool(new McpToolDefinition("eidolon_list_asset_collections", "List the latest 20 asset collections with IDs and completion counts.")
                    { ReadOnly = true, Idempotent = true, OpenWorld = false }, ListAssetCollectionsAsync),
                new McpTool(new McpToolDefinition("eidolon_resume_asset_collection", "Queue only unfinished frames using the collection's saved backend and generation conditions. Requires an idle queue.",
                    CollectionSchema()), ResumeAssetCollectionAsync),
                new McpTool(new McpToolDefinition("eidolon_regenerate_asset_frame", "Queue a replacement for one frame or view using its captured reference, backend and generation conditions. The replacement prompt MUST be in English with detailed comma-separated tags or short tag phrases. Translate requests in other languages before calling. Read eidolon_get_instructions first; for ComfyUI, follow the target checkpoint prompt_guidance from eidolon_list_models rather than a universal tag template. Specify the subject, pose, direction, viewpoint and desired visual details for this frame. For regeneration, read eidolon_get_asset_collection and use its captured backend and checkpoint, not the current UI selection. Seed applies only to ComfyUI collections. Requires an idle queue. Keeps the old image if regeneration fails.",
                    RegenerateAssetSchema()), RegenerateAssetFrameAsync),
                new McpTool(new McpToolDefinition("eidolon_export_asset_collection", "Export all reviewed view images into a new subfolder: Frames PNGs on a common canvas, Sheet.png, Sheet.json with frame rectangles, bottom-center pivots and Loop=false, and preserved Sources. All views must have images. Does not overwrite existing files. Sprite-animation collections are not supported.", ExportAssetSchema()), ExportAssetCollectionAsync)
            };
        }

        private JsonObject CancelWorkSchema()
        {
            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["active_request"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["description"] = "The exact active_request title from the most recent eidolon_get_status response. Require can_cancel_work=true for eidolon_cancel_work or can_cancel_training=true for eidolon_cancel_training. Do not construct it from editable UI inputs. The title is not a unique job ID." }
                },
                ["required"] = new JsonArray("active_request"),
                ["additionalProperties"] = false
            };
        }

        private Task<McpToolResult> CancelTrainingAsync(JsonObject arguments, CancellationToken token)
        {
            return ExecuteAssetAsync(async () =>
            {
                string activeRequest = arguments["active_request"].GetValue<string>();
                Task cancellation = await Dispatcher.UIThread.InvokeAsync<Task>(() =>
                {
                    EnsureAvailable(token);
                    if (string.Equals(_session.ActiveRequestTitle, activeRequest, StringComparison.Ordinal) == false)
                    {
                        throw new ArgumentException("active_request no longer matches the current work. Read eidolon_get_status again.");
                    }
                    if (_training.CanCancelTraining == false)
                    {
                        throw new ArgumentException("The current work is not cancellable LoRA training. It may be idle, already cancelling or registering the completed LoRA.");
                    }
                    return _training.CancelTrainingAsync();
                });
                await cancellation.ConfigureAwait(false);
                return new McpToolResult(new JsonObject { ["accepted"] = true, ["cancellation_requested"] = true, ["active_request"] = activeRequest });
            });
        }

        private bool CanCancelWork()
        {
            if (_session.IsClosing == true)
            {
                return false;
            }
            if (_session.IsInitialized == false)
            {
                return false;
            }
            if (_session.IsBusy == false)
            {
                return false;
            }
            if (string.IsNullOrEmpty(_session.ActiveRequestTitle) == true)
            {
                return false;
            }
            if (_training.IsTrainingActive == true)
            {
                return _training.CanCancelTraining;
            }
            return true;
        }

        private Task<McpToolResult> CancelWorkAsync(JsonObject arguments, CancellationToken token)
        {
            return ExecuteAssetAsync(async () =>
            {
                string activeRequest = arguments["active_request"].GetValue<string>();
                Task cancellation = await Dispatcher.UIThread.InvokeAsync<Task>(() =>
                {
                    EnsureAvailable(token);
                    if (string.Equals(_session.ActiveRequestTitle, activeRequest, StringComparison.Ordinal) == false)
                    {
                        throw new ArgumentException("active_request no longer matches the current work. Read eidolon_get_status again.");
                    }
                    if (CanCancelWork() == false)
                    {
                        throw new ArgumentException("The current work cannot be cancelled. It may have finished or be registering a completed LoRA.");
                    }
                    if (_training.IsTrainingActive == true)
                    {
                        return _training.CancelTrainingAsync();
                    }
                    return _work.CancelCurrentAsync(activeRequest);
                });
                await cancellation.ConfigureAwait(false);
                return new McpToolResult(new JsonObject { ["accepted"] = true, ["cancellation_requested"] = true, ["active_request"] = activeRequest });
            });
        }

        private Task<McpToolResult> ClearPendingAsync(JsonObject arguments, CancellationToken token)
        {
            return OnUiAsync(() =>
            {
                _work.ClearPending();
                return new JsonObject { ["accepted"] = true, ["pending_count"] = _work.GetPendingTitles().Count };
            }, token);
        }

        private JsonObject DeleteModelSchema()
        {
            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["asset_id"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 128, ["description"] = "The actual registered local checkpoint or LoRA ID returned by eidolon_list_models. Do not supply a file path or external server asset ID." },
                    ["confirm"] = new JsonObject { ["type"] = "boolean", ["enum"] = new JsonArray(JsonValue.Create(true)), ["description"] = "Must be true. Set true only when the user has explicitly authorized permanent deletion of this exact asset's real file." }
                },
                ["required"] = new JsonArray("asset_id", "confirm"),
                ["additionalProperties"] = false
            };
        }

        private Task<McpToolResult> DeleteModelAsync(JsonObject arguments, CancellationToken cancellationToken)
        {
            return ExecuteAssetAsync(async () =>
            {
                if (arguments["confirm"].GetValue<bool>() == false)
                {
                    throw new ArgumentException("confirm must be true after explicit user approval to delete this asset's real file.");
                }
                string assetId = arguments["asset_id"].GetValue<string>();
                Task acceptance = await Dispatcher.UIThread.InvokeAsync<Task>(() =>
                {
                    EnsureAvailable(cancellationToken);
                    return _assets.EnqueueDeleteAssetAsync(assetId);
                });
                await acceptance.ConfigureAwait(false);
                return new McpToolResult(new JsonObject { ["accepted"] = true, ["asset_id"] = assetId });
            });
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
                    ["prompt"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 16000, ["description"] = "Required image description, supplied as tags, phrases, sentences or a mixture. For ComfyUI, an installed local converter adapts this description to the captured checkpoint guidance; without it, provide suitable English input. Codex receives the original description directly without local conversion. Preserve subject/count, appearance, pose, composition, viewpoint, style and constraints. Read eidolon_get_instructions and the target checkpoint prompt_guidance first. For edits, describe the requested changes and reference details to preserve. Original input is retained in result metadata." },
                    ["model_id"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 128 },
                    ["lora_ids"] = new JsonObject
                    {
                        ["type"] = "array", ["maxItems"] = 32,
                        ["items"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 128 }
                    },
                    ["seed"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = long.MaxValue },
                    ["remove_background"] = new JsonObject { ["type"] = "boolean" },
                    ["positive_prompt"] = new JsonObject { ["type"] = "string", ["maxLength"] = 16000, ["description"] = "Optional request-only English instructions in the target model's preferred format: tags, descriptive phrases or natural-language sentences. Follow prompt_guidance and preserve requested style, composition, lighting, background and quality. The local converter does not rewrite these instructions; use only requested and model-supported special tags. Omit to use saved instructions; an empty string clears them for this request." },
                    ["negative_prompt"] = new JsonObject { ["type"] = "string", ["maxLength"] = 16000, ["description"] = "Optional request-only English exclusions in the target model's preferred format. Follow checkpoint-specific negative guidance and avoid exclusions that conflict with the requested subject or style. The local converter does not rewrite these exclusions. Omit to use saved exclusions; an empty string clears them for this request." },
                    ["reference_path"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 4096 },
                    ["preservation_prompt"] = new JsonObject { ["type"] = "string", ["maxLength"] = 8192, ["description"] = "Caller-confirmed reference features to preserve. Requires reference_path. For ComfyUI use concise English in the target checkpoint format, and remove features conflicting with the requested changes. An explicit empty string skips preservation instructions. If omitted with a reference, clients declaring sampling use MCP 2026-07-28 input_required or initialized 2025 Streamable HTTP POST SSE sampling; other clients must provide this argument explicitly." },
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
            return await OnUiAsync(() =>
            {
                IReadOnlyList<string> pendingRequests = _work.GetPendingTitles();
                return new JsonObject
                {
                    ["initialized"] = _session.IsInitialized,
                    ["can_queue"] = _session.CanQueue,
                    ["engine_connected"] = _session.IsEngineConnected,
                    ["busy"] = _session.IsBusy,
                    ["idle"] = _session.IsIdle,
                    ["status"] = _session.Status,
                    ["active_request"] = _session.ActiveRequestTitle,
                    ["can_cancel_training"] = _training.CanCancelTraining,
                    ["can_cancel_work"] = CanCancelWork(),
                    ["pending_count"] = pendingRequests.Count,
                    ["pending_requests"] = new JsonArray(pendingRequests.Select(title => (JsonNode)JsonValue.Create(title)).ToArray()),
                    ["progress_percent"] = _session.Percent,
                    ["indeterminate"] = _session.Indeterminate,
                    ["error"] = _session.Error
                };
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
                        ["prompt_guidance"] = _assets.GetModelPromptGuidance(asset)
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


        private async Task<McpToolResult> GetInstructionsAsync(JsonObject arguments, CancellationToken token)
        {
            return await OnUiAsync(() =>
            {
                string selected = string.Empty;
                string guidance = "The saved backend is Codex. ComfyUI checkpoint families, Danbooru quality prefixes and local LoRA selection do not apply to this backend. Use clear natural-language English descriptions expressing the requested content, relationships, style and constraints, and preserve the saved instructions unless a request-only override is needed.";
                if (_session.Settings.GenerationBackend == GenerationBackend.ComfyUI)
                {
                    ModelAsset model = null;
                    if (_assets.SelectedModel != null)
                    {
                        model = _assets.SelectedModel.Asset;
                        selected = model.Id;
                    }
                    guidance = _assets.GetModelPromptGuidance(model);
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

        private Task<McpToolResult> GenerateImageAsync(JsonObject arguments, McpToolCallContext context, CancellationToken token)
        {
            return QueueImageAsync(arguments, context, false, token);
        }

        private Task<McpToolResult> EditImageAsync(JsonObject arguments, McpToolCallContext context, CancellationToken token)
        {
            return QueueImageAsync(arguments, context, true, token);
        }

        private async Task<McpToolResult> QueueImageAsync(JsonObject arguments, McpToolCallContext context, bool editing, CancellationToken token)
        {
            try
            {
                string prompt = arguments["prompt"].GetValue<string>().Trim();
                if (string.IsNullOrWhiteSpace(prompt) == true)
                {
                    throw new ArgumentException("prompt must contain an image description.");
                }
                McpReferenceContinuation continuation = null;
                if (string.IsNullOrEmpty(context.RequestState) == false)
                {
                    continuation = ReadReferenceContinuation(context.RequestState, context, editing);
                    if (JsonNode.DeepEquals(continuation.Arguments, arguments) == false)
                    {
                        throw new ArgumentException("The reference continuation arguments changed. Start a new request.");
                    }
                    if (context.IsInputContinuation == true)
                    {
                        if (context.IsModern == true)
                        {
                            throw new ArgumentException("Internal reference continuation requires the original initialized 2025 request.");
                        }
                        if (string.IsNullOrEmpty(context.SessionId) == true)
                        {
                            throw new ArgumentException("Internal reference continuation requires a valid initialized session.");
                        }
                        if (JsonNode.DeepEquals(continuation.OriginalRequestId, context.RequestId) == false)
                        {
                            throw new ArgumentException("Internal reference continuation must preserve the original JSON-RPC request id.");
                        }
                    }
                    else
                    {
                        if (context.IsModern == false)
                        {
                            throw new ArgumentException("A 2025 reference continuation must remain inside its original Streamable HTTP request.");
                        }
                        if (JsonNode.DeepEquals(continuation.OriginalRequestId, context.RequestId) == true)
                        {
                            throw new ArgumentException("A continuation retry requires a new JSON-RPC request id.");
                        }
                    }
                }
                else if (context.IsInputContinuation == true)
                {
                    throw new ArgumentException("Internal reference continuation requires the original protected requestState.");
                }
                else if (context.InputResponses.Count > 0)
                {
                    throw new ArgumentException("inputResponses requires the original requestState.");
                }
                if (arguments.ContainsKey("preservation_prompt") == true)
                {
                    if (arguments["reference_path"] == null)
                    {
                        throw new ArgumentException("preservation_prompt requires reference_path.");
                    }
                }
                GenerationReferenceInput reference = await ReadReferenceAsync(arguments, editing, token).ConfigureAwait(false);
                McpGenerationOptions options = await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    EnsureAvailable(token);
                    if (_session.CanQueue == false)
                    {
                        throw new ArgumentException("Eidolon is not ready to accept an image request.");
                    }
                    if (continuation == null)
                    {
                        return CreateGenerationOptions(arguments);
                    }
                    EnsureReferenceGenerationEnvironment(arguments, continuation);
                    return continuation.Options;
                });
                string preservationSource = "none";
                if (reference != null)
                {
                    if (continuation != null)
                    {
                        string referenceHash = Convert.ToHexStringLower(SHA256.HashData(reference.ImageData));
                        if (referenceHash != continuation.ReferenceSha256)
                        {
                            throw new ArgumentException("The reference image changed during sampling. Start a new request.");
                        }
                        if (context.InputResponses.ContainsKey(ReferenceFeaturesInputName) == false)
                        {
                            RequireReferenceSampling(context);
                            return await RequestReferenceFeaturesAsync(reference, prompt, context.RequestState, token).ConfigureAwait(false);
                        }
                        reference.PreservationPrompt = ReadSampledReferenceFeatures(context.InputResponses[ReferenceFeaturesInputName]);
                        preservationSource = "sampling";
                    }
                    else if (arguments.ContainsKey("preservation_prompt") == true)
                    {
                        reference.PreservationPrompt = arguments["preservation_prompt"].GetValue<string>().Trim();
                        preservationSource = "caller";
                    }
                    else
                    {
                        RequireReferenceSampling(context);
                        string requestState = CreateReferenceContinuation(arguments, options, reference, context, editing);
                        return await RequestReferenceFeaturesAsync(reference, prompt, requestState, token).ConfigureAwait(false);
                    }
                }
                else if (continuation != null)
                {
                    throw new ArgumentException("Reference continuation requires the original reference_path.");
                }
                return await OnUiAsync(() =>
                {
                    if (continuation != null)
                    {
                        EnsureReferenceGenerationEnvironment(arguments, continuation);
                    }
                    if (_session.CanQueue == false)
                    {
                        throw new ArgumentException("Eidolon is not ready to accept an image request.");
                    }
                    string titleKey = "EidolonText106";
                    if (editing == true)
                    {
                        titleKey = "EidolonText456";
                    }
                    token.ThrowIfCancellationRequested();
                    _generation.EnqueueGeneration(options.Settings, prompt, options.Model, options.Loras, options.RemoveBackground, options.Seed, reference, titleKey);
                    JsonObject result = new JsonObject
                    {
                        ["accepted"] = true,
                        ["prompt"] = prompt,
                        ["positive_prompt"] = options.Settings.PositivePrompt,
                        ["negative_prompt"] = options.Settings.NegativePrompt,
                        ["output_directory"] = options.Settings.GenerationDirectory,
                        ["generation_backend"] = options.Settings.GenerationBackend.ToString()
                    };
                    if (reference != null)
                    {
                        result["preservation_prompt"] = reference.PreservationPrompt;
                        result["preservation_source"] = preservationSource;
                    }
                    if (options.Settings.GenerationBackend == GenerationBackend.ComfyUI)
                    {
                        result["seed"] = options.Seed;
                        result["model_id"] = options.Model.Id;
                    }
                    else
                    {
                        result["codex_model"] = options.Settings.CodexModel;
                    }
                    return result;
                }, token).ConfigureAwait(false);
            }
            catch (StudioException error)
            {
                string message = await Dispatcher.UIThread.InvokeAsync(() => _strings.GetExceptionMessage(error));
                return new McpToolResult(message, true);
            }
            catch (IOException error)
            {
                string message = await Dispatcher.UIThread.InvokeAsync(() => _strings.GetExceptionMessage(error));
                return new McpToolResult(message, true);
            }
            catch (UnauthorizedAccessException error)
            {
                string message = await Dispatcher.UIThread.InvokeAsync(() => _strings.GetExceptionMessage(error));
                return new McpToolResult(message, true);
            }
        }

        private void RequireReferenceSampling(McpToolCallContext context)
        {
            if (context.IsModern == false)
            {
                if (string.IsNullOrEmpty(context.SessionId) == true)
                {
                    throw new ArgumentException("Provide preservation_prompt for this reference request, or an empty string to skip preservation. Automatic reference sampling requires a valid initialized 2025 session or MCP 2026-07-28, with client-declared sampling support.");
                }
            }
            if (context.ClientCapabilities["sampling"] is not JsonObject)
            {
                throw new ArgumentException("The client did not declare sampling support. Provide caller-confirmed preservation_prompt, or an empty string to skip preservation.");
            }
        }

        private void EnsureReferenceGenerationEnvironment(JsonObject arguments, McpReferenceContinuation continuation)
        {
            if (continuation.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            {
                throw new ArgumentException("The reference continuation is invalid or expired. Start a new request.");
            }
            JsonObject resumedArguments = (JsonObject)arguments.DeepClone();
            if (continuation.Options.Settings.GenerationBackend == GenerationBackend.ComfyUI)
            {
                if (resumedArguments.ContainsKey("seed") == false)
                {
                    resumedArguments["seed"] = continuation.Options.Seed;
                }
            }
            McpGenerationOptions currentOptions = CreateGenerationOptions(resumedArguments);
            if (JsonNode.DeepEquals(DescribeImageOptions(currentOptions), DescribeImageOptions(continuation.Options)) == false)
            {
                throw new ArgumentException("The generation environment changed during reference sampling. Start a new request.");
            }
        }

        private JsonObject DescribeImageOptions(McpGenerationOptions options)
        {
            DesktopSettings settings = options.Settings;
            JsonObject result = new JsonObject
            {
                ["generation_backend"] = settings.GenerationBackend.ToString(),
                ["generation_directory"] = settings.GenerationDirectory,
                ["positive_prompt"] = settings.PositivePrompt,
                ["negative_prompt"] = settings.NegativePrompt,
                ["remove_background"] = options.RemoveBackground
            };
            if (settings.GenerationBackend == GenerationBackend.ComfyUI)
            {
                result["install_directory"] = settings.InstallDirectory;
                result["server_address"] = settings.ServerAddress;
                result["use_server_assets"] = settings.UseServerAssets;
                result["use_cpu"] = settings.UseCpu;
                result["model"] = JsonSerializer.SerializeToNode(options.Model);
                result["loras"] = JsonSerializer.SerializeToNode(options.Loras);
                result["seed"] = options.Seed;
            }
            else
            {
                result["codex_executable_path"] = settings.CodexExecutablePath;
                result["codex_model"] = settings.CodexModel;
            }
            return result;
        }

        private string CreateReferenceContinuation(JsonObject arguments, McpGenerationOptions options, GenerationReferenceInput reference, McpToolCallContext context, bool editing)
        {
            if (context.RequestId == null)
            {
                throw new ArgumentException("Reference sampling requires a JSON-RPC request id.");
            }
            DesktopSettings settings = options.Settings.Copy();
            settings.PromptPresets.Clear();
            settings.AdditionalSettings.Clear();
            settings.ActivePromptPresetId = string.Empty;
            McpReferenceContinuation continuation = new McpReferenceContinuation
            {
                Arguments = (JsonObject)arguments.DeepClone(),
                OriginalRequestId = context.RequestId.DeepClone(),
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5),
                ReferenceSha256 = Convert.ToHexStringLower(SHA256.HashData(reference.ImageData)),
                UseServerAssets = settings.UseServerAssets,
                Options = new McpGenerationOptions
                {
                    Settings = settings,
                    Model = options.Model,
                    Loras = options.Loras,
                    Seed = options.Seed,
                    RemoveBackground = options.RemoveBackground
                }
            };
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(continuation);
            if (payload.Length > MaximumContinuationBytes)
            {
                throw new ArgumentException("The reference request snapshot is too large. Provide preservation_prompt directly instead.");
            }
            byte[] protectedState = new byte[28 + payload.Length];
            RandomNumberGenerator.Fill(protectedState.AsSpan(0, 12));
            using AesGcm encryption = new AesGcm(_referenceContinuationKey, 16);
            encryption.Encrypt(protectedState.AsSpan(0, 12), payload, protectedState.AsSpan(28), protectedState.AsSpan(12, 16), ReferenceContinuationPurpose(context, editing));
            return Convert.ToBase64String(protectedState);
        }

        private McpReferenceContinuation ReadReferenceContinuation(string requestState, McpToolCallContext context, bool editing)
        {
            if (requestState.Length > ((MaximumContinuationBytes + 30) / 3) * 4)
            {
                throw new ArgumentException("The reference continuation is invalid or expired. Start a new request.");
            }
            McpReferenceContinuation continuation;
            try
            {
                byte[] protectedState = Convert.FromBase64String(requestState);
                if (protectedState.Length <= 28)
                {
                    throw new ArgumentException("The reference continuation is invalid or expired. Start a new request.");
                }
                if (protectedState.Length > MaximumContinuationBytes + 28)
                {
                    throw new ArgumentException("The reference continuation is invalid or expired. Start a new request.");
                }
                byte[] payload = new byte[protectedState.Length - 28];
                using AesGcm encryption = new AesGcm(_referenceContinuationKey, 16);
                encryption.Decrypt(protectedState.AsSpan(0, 12), protectedState.AsSpan(28), protectedState.AsSpan(12, 16), payload, ReferenceContinuationPurpose(context, editing));
                continuation = JsonSerializer.Deserialize<McpReferenceContinuation>(payload);
            }
            catch (FormatException)
            {
                throw new ArgumentException("The reference continuation is invalid or expired. Start a new request.");
            }
            catch (CryptographicException)
            {
                throw new ArgumentException("The reference continuation is invalid or expired. Start a new request.");
            }
            catch (JsonException)
            {
                throw new ArgumentException("The reference continuation is invalid or expired. Start a new request.");
            }
            if (continuation == null)
            {
                throw new ArgumentException("The reference continuation is invalid or expired. Start a new request.");
            }
            if (continuation.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            {
                throw new ArgumentException("The reference continuation is invalid or expired. Start a new request.");
            }
            if (continuation.Arguments == null)
            {
                throw new ArgumentException("The reference continuation is invalid or expired. Start a new request.");
            }
            if (continuation.OriginalRequestId == null)
            {
                throw new ArgumentException("The reference continuation is invalid or expired. Start a new request.");
            }
            if (continuation.Options == null)
            {
                throw new ArgumentException("The reference continuation is invalid or expired. Start a new request.");
            }
            if (continuation.Options.Settings == null)
            {
                throw new ArgumentException("The reference continuation is invalid or expired. Start a new request.");
            }
            if (continuation.Options.Loras == null)
            {
                throw new ArgumentException("The reference continuation is invalid or expired. Start a new request.");
            }
            continuation.Options.Settings.UseServerAssets = continuation.UseServerAssets;
            return continuation;
        }

        private byte[] ReferenceContinuationPurpose(McpToolCallContext context, bool editing)
        {
            string toolName = "eidolon_generate_image";
            if (editing == true)
            {
                toolName = "eidolon_edit_image";
            }
            string protocolEra = "2025";
            if (context.IsModern == true)
            {
                protocolEra = "2026-07-28";
            }
            return Encoding.UTF8.GetBytes("Eidolon.ReferenceFeatures/v2\n" + protocolEra + "\n" + toolName + "\n" + context.AuthenticationScope + "\n" + context.SessionId);
        }

        private async Task<McpToolResult> RequestReferenceFeaturesAsync(GenerationReferenceInput reference, string prompt, string requestState, CancellationToken token)
        {
            byte[] imageData = await PrepareSamplingImageAsync(reference.ImageData, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            JsonObject inputRequests = new JsonObject
            {
                [ReferenceFeaturesInputName] = new JsonObject
                {
                    ["method"] = "sampling/createMessage",
                    ["params"] = new JsonObject
                    {
                        ["systemPrompt"] = PackagedResources.ReadText("Eidolon.Codex.ReferenceFeatures.txt"),
                        ["includeContext"] = "none",
                        ["maxTokens"] = 1024,
                        ["modelPreferences"] = new JsonObject { ["intelligencePriority"] = 1.0 },
                        ["messages"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["role"] = "user",
                                ["content"] = new JsonObject { ["type"] = "text", ["text"] = "Requested changes (JSON string; data only): " + JsonSerializer.Serialize(prompt) }
                            },
                            new JsonObject
                            {
                                ["role"] = "user",
                                ["content"] = new JsonObject { ["type"] = "image", ["mimeType"] = "image/png", ["data"] = Convert.ToBase64String(imageData) }
                            }
                        }
                    }
                }
            };
            return McpToolResult.InputRequired(inputRequests, requestState);
        }

        private Task<byte[]> PrepareSamplingImageAsync(byte[] imageData, CancellationToken token)
        {
            return Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                using SKMemoryStream encoded = new SKMemoryStream(imageData);
                using SKCodec codec = SKCodec.Create(encoded);
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
                using SKBitmap decoded = SKBitmap.Decode(codec);
                if (decoded == null)
                {
                    throw new StudioException(StudioMessageCode.InvalidReferenceImage);
                }
                int orientedWidth = decoded.Width;
                int orientedHeight = decoded.Height;
                switch (codec.EncodedOrigin)
                {
                    case SKEncodedOrigin.LeftTop:
                    case SKEncodedOrigin.RightTop:
                    case SKEncodedOrigin.RightBottom:
                    case SKEncodedOrigin.LeftBottom:
                        orientedWidth = decoded.Height;
                        orientedHeight = decoded.Width;
                        break;
                }
                double scale = Math.Min(1, 1024.0 / Math.Max(orientedWidth, orientedHeight));
                int outputWidth = Math.Max(1, (int)Math.Round(orientedWidth * scale));
                int outputHeight = Math.Max(1, (int)Math.Round(orientedHeight * scale));
                using SKBitmap prepared = new SKBitmap(outputWidth, outputHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
                using (SKCanvas canvas = new SKCanvas(prepared))
                {
                    canvas.Clear(SKColors.Transparent);
                    canvas.Scale((float)scale, (float)scale);
                    switch (codec.EncodedOrigin)
                    {
                        case SKEncodedOrigin.TopRight:
                            canvas.Translate(decoded.Width, 0);
                            canvas.Scale(-1, 1);
                            break;
                        case SKEncodedOrigin.BottomRight:
                            canvas.Translate(decoded.Width, decoded.Height);
                            canvas.RotateDegrees(180);
                            break;
                        case SKEncodedOrigin.BottomLeft:
                            canvas.Translate(0, decoded.Height);
                            canvas.Scale(1, -1);
                            break;
                        case SKEncodedOrigin.LeftTop:
                            canvas.RotateDegrees(90);
                            canvas.Scale(1, -1);
                            break;
                        case SKEncodedOrigin.RightTop:
                            canvas.Translate(decoded.Height, 0);
                            canvas.RotateDegrees(90);
                            break;
                        case SKEncodedOrigin.RightBottom:
                            canvas.Translate(decoded.Height, decoded.Width);
                            canvas.RotateDegrees(90);
                            canvas.Scale(-1, 1);
                            break;
                        case SKEncodedOrigin.LeftBottom:
                            canvas.Translate(0, decoded.Width);
                            canvas.RotateDegrees(270);
                            break;
                    }
                    using SKImage sourceImage = SKImage.FromBitmap(decoded);
                    canvas.DrawImage(sourceImage, 0, 0, new SKSamplingOptions(SKFilterMode.Linear));
                }
                token.ThrowIfCancellationRequested();
                using SKImage preparedImage = SKImage.FromBitmap(prepared);
                using SKData preparedData = preparedImage.Encode(SKEncodedImageFormat.Png, 100);
                if (preparedData == null)
                {
                    throw new StudioException(StudioMessageCode.InvalidReferenceImage);
                }
                if (preparedData.Size > 8 * 1024 * 1024)
                {
                    throw new ArgumentException("The reference image is too large for client sampling. Provide preservation_prompt directly.");
                }
                token.ThrowIfCancellationRequested();
                return preparedData.ToArray();
            }, token);
        }

        private string ReadSampledReferenceFeatures(JsonNode responseNode)
        {
            if (responseNode is not JsonObject response)
            {
                throw new ArgumentException("The sampling response must be a CreateMessageResult object.");
            }
            if (response["role"]?.GetValueKind() != JsonValueKind.String)
            {
                throw new ArgumentException("The sampling response requires assistant role.");
            }
            if (response["role"].GetValue<string>() != "assistant")
            {
                throw new ArgumentException("The sampling response requires assistant role.");
            }
            if (response["model"]?.GetValueKind() != JsonValueKind.String)
            {
                throw new ArgumentException("The sampling response requires the responding model name.");
            }
            if (string.IsNullOrWhiteSpace(response["model"].GetValue<string>()) == true)
            {
                throw new ArgumentException("The sampling response requires the responding model name.");
            }
            if (response.ContainsKey("stopReason") == true)
            {
                if (response["stopReason"]?.GetValueKind() != JsonValueKind.String)
                {
                    throw new ArgumentException("The sampling response has an invalid stopReason.");
                }
                string stopReason = response["stopReason"].GetValue<string>();
                if (stopReason == "maxTokens")
                {
                    throw new ArgumentException("Reference sampling was truncated. Provide complete preservation_prompt or retry sampling.");
                }
                if (stopReason == "refusal")
                {
                    throw new ArgumentException("Reference sampling was declined. Provide preservation_prompt explicitly.");
                }
                if (stopReason == "toolUse")
                {
                    throw new ArgumentException("Reference sampling must return features without invoking tools.");
                }
            }
            StringBuilder responseText = new StringBuilder();
            if (response["content"] is JsonObject content)
            {
                AppendReferenceSamplingText(content, responseText);
            }
            else if (response["content"] is JsonArray contents)
            {
                if (contents.Count != 1)
                {
                    throw new ArgumentException("Reference sampling must return one text content block.");
                }
                foreach (JsonNode contentNode in contents)
                {
                    if (contentNode is not JsonObject textContent)
                    {
                        throw new ArgumentException("Reference sampling must return text content only.");
                    }
                    AppendReferenceSamplingText(textContent, responseText);
                }
            }
            else
            {
                throw new ArgumentException("Reference sampling must return text content only.");
            }
            try
            {
                using JsonDocument document = JsonDocument.Parse(responseText.ToString());
                JsonElement result = document.RootElement;
                if (result.ValueKind != JsonValueKind.Object)
                {
                    throw new ArgumentException("Reference sampling must return a features/error JSON object.");
                }
                if (result.EnumerateObject().Count() != 2)
                {
                    throw new ArgumentException("Reference sampling must return only features and error.");
                }
                if (result.TryGetProperty("features", out JsonElement features) == false)
                {
                    throw new ArgumentException("Reference sampling did not return features.");
                }
                if (result.TryGetProperty("error", out JsonElement error) == false)
                {
                    throw new ArgumentException("Reference sampling did not return error.");
                }
                if (features.ValueKind != JsonValueKind.String)
                {
                    throw new ArgumentException("Reference sampling features must be text.");
                }
                if (error.ValueKind != JsonValueKind.String)
                {
                    throw new ArgumentException("Reference sampling error must be text.");
                }
                if (string.IsNullOrWhiteSpace(error.GetString()) == false)
                {
                    throw new ArgumentException("Reference sampling failed. Provide preservation_prompt explicitly or start a new request.");
                }
                string preservationPrompt = features.GetString().Trim();
                if (preservationPrompt.Length == 0)
                {
                    throw new ArgumentException("Reference sampling returned no features. Provide preservation_prompt explicitly; use an empty string to intentionally skip preservation.");
                }
                if (preservationPrompt.Length > 8192)
                {
                    throw new ArgumentException("Reference sampling features exceed 8192 characters.");
                }
                return preservationPrompt;
            }
            catch (JsonException)
            {
                throw new ArgumentException("Reference sampling must return valid features/error JSON text.");
            }
        }

        private void AppendReferenceSamplingText(JsonObject content, StringBuilder responseText)
        {
            if (content["type"]?.GetValueKind() != JsonValueKind.String)
            {
                throw new ArgumentException("Reference sampling must return text content only.");
            }
            if (content["type"].GetValue<string>() != "text")
            {
                throw new ArgumentException("Reference sampling must return text content only.");
            }
            if (content["text"]?.GetValueKind() != JsonValueKind.String)
            {
                throw new ArgumentException("Reference sampling must return text content only.");
            }
            string text = content["text"].GetValue<string>();
            if (text.Length > 65536 - responseText.Length)
            {
                throw new ArgumentException("Reference sampling text exceeds the result size limit.");
            }
            responseText.Append(text);
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

        private JsonObject AssetCreationSchema()
        {
            JsonObject schema = ImageSchema(false);
            JsonObject properties = schema["properties"].AsObject();
            properties.Remove("preservation_prompt");
            schema["required"] = new JsonArray("reference_path");
            properties["prompt"] = new JsonObject { ["type"] = "string", ["maxLength"] = 16000, ["description"] = "Optional English-only subject description as detailed comma-separated tags or short tag phrases for appearance, proportions, materials, textures, colors, style, lighting and background. Translate the user's request into English. For ComfyUI, use the target checkpoint prompt_guidance from eidolon_list_models; read the backend and saved instruction prefixes with eidolon_get_instructions first. Omit to use the reference subject without an additional description." };
            properties["frame_size"] = new JsonObject { ["type"] = "integer", ["minimum"] = 16, ["maximum"] = AssetCreationInput.MaximumFrameSize };
            properties["columns"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = AssetCreationInput.ViewCount, ["default"] = AssetCreationInput.ViewCount };
            properties["pixel_art"] = new JsonObject { ["type"] = "boolean" };
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

        private JsonObject ModelingSchema()
        {
            JsonObject views = new JsonObject();
            foreach (string direction in new string[] { "front", "left", "right", "back" })
            {
                views[direction] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 4096 };
            }
            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["instructions"] = new JsonObject { ["type"] = "string", ["maxLength"] = 16000, ["description"] = "Describe only the desired geometry and materials. Eidolon automatically saves the Blender file, exports GLB and renders previews; exclude those operations from instructions. Optional for a new model with images; required when editing an existing .blend." },
                    ["reference_path"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 4096 },
                    ["views"] = new JsonObject { ["type"] = "object", ["properties"] = views, ["additionalProperties"] = false },
                    ["source_blend_path"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 4096 },
                    ["maximum_triangle_count"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 1000000, ["default"] = 0 }
                },
                ["additionalProperties"] = false
            };
        }

        private JsonObject ModelingResultSchema()
        {
            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject { ["request_id"] = new JsonObject { ["type"] = "string", ["minLength"] = 32, ["maxLength"] = 36 } },
                ["required"] = new JsonArray("request_id"),
                ["additionalProperties"] = false
            };
        }

        private async Task<McpToolResult> QueueModelingAsync(JsonObject arguments, CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (KeyValuePair<string, JsonNode> argument in arguments)
                {
                    if (new string[] { "instructions", "reference_path", "views", "source_blend_path", "maximum_triangle_count" }.Contains(argument.Key) == false)
                    {
                        throw new ArgumentException("Unsupported modeling argument: " + argument.Key);
                    }
                    if (argument.Value == null)
                    {
                        throw new ArgumentException("Modeling arguments cannot be null.");
                    }
                }
                if (arguments.ContainsKey("reference_path") == true)
                {
                    if (arguments.ContainsKey("views") == true)
                    {
                        throw new ArgumentException("Use reference_path or views, not both.");
                    }
                }
                string instructions = string.Empty;
                if (arguments["instructions"] != null)
                {
                    instructions = arguments["instructions"].GetValue<string>();
                }
                string sourceBlendPath = string.Empty;
                if (arguments["source_blend_path"] != null)
                {
                    sourceBlendPath = ReadModelingPath(arguments["source_blend_path"]);
                }
                List<ModelingReference> references = new List<ModelingReference>();
                if (arguments["reference_path"] != null)
                {
                    references.Add(new ModelingReference("reference", ReadModelingPath(arguments["reference_path"])));
                }
                if (arguments["views"] != null)
                {
                    JsonObject views = arguments["views"].AsObject();
                    if (views.Count == 0)
                    {
                        throw new ArgumentException("views must contain at least one direction.");
                    }
                    string[] directions = new string[] { "front", "left", "right", "back" };
                    foreach (KeyValuePair<string, JsonNode> view in views)
                    {
                        if (directions.Contains(view.Key) == false)
                        {
                            throw new ArgumentException("views only supports front, left, right and back.");
                        }
                        if (view.Value == null)
                        {
                            throw new ArgumentException("A view must contain an image path.");
                        }
                    }
                    foreach (string direction in directions)
                    {
                        if (views[direction] != null)
                        {
                            references.Add(new ModelingReference(direction, ReadModelingPath(views[direction])));
                        }
                    }
                }
                int maximumTriangleCount = 0;
                if (arguments["maximum_triangle_count"] != null)
                {
                    maximumTriangleCount = arguments["maximum_triangle_count"].GetValue<int>();
                }
                Task<string> acceptance = await Dispatcher.UIThread.InvokeAsync<Task<string>>(() =>
                {
                    EnsureAvailable(cancellationToken);
                    return _modeling.EnqueueAsync(instructions, references, sourceBlendPath, maximumTriangleCount, cancellationToken);
                });
                string requestId = await acceptance.ConfigureAwait(false);
                return new McpToolResult(new JsonObject { ["accepted"] = true, ["request_id"] = requestId, ["result_tool"] = "eidolon_get_modeling_result", ["status_tool"] = "eidolon_get_status" });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == true)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return new McpToolResult("Modeling readiness could not be confirmed before acceptance. Use eidolon_get_status and retry after checking native GPT and Blender readiness.", true);
            }
            catch (ArgumentException error)
            {
                return new McpToolResult(error.Message, true);
            }
            catch (InvalidOperationException error)
            {
                return new McpToolResult(error.Message, true);
            }
            catch (Exception)
            {
                return new McpToolResult("The modeling request was not accepted. Check the local input files and native GPT/Blender readiness with eidolon_get_status.", true);
            }
        }

        private string ReadModelingPath(JsonNode value)
        {
            string path = value.GetValue<string>();
            if (string.IsNullOrWhiteSpace(path) == true)
            {
                throw new ArgumentException("Modeling input paths cannot be empty.");
            }
            if (path.Length > 4096)
            {
                throw new ArgumentException("Modeling input paths must not exceed 4096 characters.");
            }
            if (OperatingSystem.IsWindows() == true)
            {
                string windowsPath = path.Replace('/', '\\');
                foreach (string prefix in new string[] { @"\\", @"\??\", @"\Device\" })
                {
                    if (windowsPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        throw new ArgumentException("UNC and device paths are not accepted as local modeling inputs.");
                    }
                }
            }
            if (Path.IsPathFullyQualified(path) == false)
            {
                throw new ArgumentException("Modeling input paths must be absolute local file paths.");
            }
            return Path.GetFullPath(path);
        }

        private async Task<McpToolResult> GetModelingResultAsync(JsonObject arguments, CancellationToken cancellationToken)
        {
            try
            {
                if (arguments.Count != 1)
                {
                    throw new ArgumentException("Only request_id is accepted.");
                }
                if (arguments["request_id"] == null)
                {
                    throw new ArgumentException("request_id is required.");
                }
                if (Guid.TryParse(arguments["request_id"].GetValue<string>(), out Guid requestGuid) == false)
                {
                    throw new ArgumentException("request_id must be a GUID.");
                }
                string requestId = requestGuid.ToString("N");
                (string Directory, string State) context = await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    EnsureAvailable(cancellationToken);
                    return (Path.Combine(_session.DataDirectory, "Modeling"), _modeling.GetRequestState(requestId));
                });
                if (context.State != "unknown")
                {
                    return new McpToolResult(ModelingRequestState(requestId, context.State));
                }
                JsonObject result = await Task.Run(() => ReadModelingResultAsync(context.Directory, requestId, cancellationToken), cancellationToken).ConfigureAwait(false);
                if (result["state"].GetValue<string>() == "not_found")
                {
                    return new McpToolResult(result.ToJsonString(), true);
                }
                return new McpToolResult(result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == true)
            {
                throw;
            }
            catch (ArgumentException error)
            {
                return new McpToolResult(error.Message, true);
            }
            catch (Exception)
            {
                return new McpToolResult("The managed modeling result could not be read. No completion is confirmed; use eidolon_get_status.", true);
            }
        }

        private JsonObject ModelingRequestState(string requestId, string state)
        {
            return new JsonObject { ["request_id"] = requestId, ["state"] = state, ["completed"] = false, ["status_tool"] = "eidolon_get_status", ["cancel_tool"] = "eidolon_cancel_work", ["clear_pending_tool"] = "eidolon_clear_pending" };
        }

        private async Task<JsonObject> ReadModelingResultAsync(string modelingDirectory, string requestId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string requestDirectory = Path.Combine(modelingDirectory, requestId);
            EnsureModelingResultHasNoLinks(modelingDirectory, requestDirectory);
            if (Directory.Exists(requestDirectory) == false)
            {
                return ModelingRequestState(requestId, "not_found");
            }
            if (File.Exists(Path.Combine(requestDirectory, "Failure.txt")) == true)
            {
                return ModelingRequestState(requestId, "failed");
            }
            if (Directory.Exists(Path.Combine(requestDirectory, "Refined")) == true)
            {
                return ModelingRequestState(requestId, "unknown");
            }
            string completionPath = Path.Combine(requestDirectory, "Control", "Draft.json");
            EnsureModelingResultHasNoLinks(modelingDirectory, completionPath);
            if (File.Exists(completionPath) == false)
            {
                return ModelingRequestState(requestId, "unknown");
            }
            await using FileStream completionStream = new FileStream(completionPath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true);
            if (completionStream.Length < 1)
            {
                throw new InvalidDataException("Incomplete modeling completion marker.");
            }
            if (completionStream.Length > 65536)
            {
                throw new InvalidDataException("Invalid modeling completion marker size.");
            }
            using JsonDocument completion = await JsonDocument.ParseAsync(completionStream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (completion.RootElement.TryGetProperty("error", out JsonElement errorElement) == false)
            {
                throw new InvalidDataException("Missing modeling completion state.");
            }
            if (errorElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("Invalid modeling completion state.");
            }
            if (string.IsNullOrEmpty(errorElement.GetString()) == false)
            {
                return ModelingRequestState(requestId, "failed");
            }
            if (completion.RootElement.TryGetProperty("triangleCount", out JsonElement triangleElement) == false)
            {
                return ModelingRequestState(requestId, "unknown");
            }
            if (triangleElement.TryGetInt32(out int triangleCount) == false)
            {
                throw new InvalidDataException("Invalid exported triangle count.");
            }
            ArgumentOutOfRangeException.ThrowIfNegative(triangleCount);
            string blendPath = Path.Combine(requestDirectory, "Draft", "Model.blend");
            string glbPath = Path.Combine(requestDirectory, "Draft", "Model.glb");
            foreach (string outputPath in new string[] { blendPath, glbPath })
            {
                EnsureModelingResultHasNoLinks(modelingDirectory, outputPath);
                FileInfo output = new FileInfo(outputPath);
                if (output.Exists == false)
                {
                    return ModelingRequestState(requestId, "unknown");
                }
                if (output.Length < 1)
                {
                    return ModelingRequestState(requestId, "unknown");
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new JsonObject { ["request_id"] = requestId, ["state"] = "completed", ["completed"] = true, ["blend_path"] = blendPath, ["glb_path"] = glbPath, ["triangle_count"] = triangleCount };
        }

        private void EnsureModelingResultHasNoLinks(string modelingDirectory, string path)
        {
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(modelingDirectory));
            string currentPath = Path.GetFullPath(path);
            while (true)
            {
                try
                {
                    if ((File.GetAttributes(currentPath) & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new IOException("Linked modeling results are not accepted.");
                    }
                }
                catch (FileNotFoundException)
                {
                }
                catch (DirectoryNotFoundException)
                {
                }
                if (currentPath == root)
                {
                    return;
                }
                currentPath = Path.GetDirectoryName(currentPath);
                if (string.IsNullOrEmpty(currentPath) == true)
                {
                    throw new IOException("The modeling result is outside the managed directory.");
                }
            }
        }
        private Task<McpToolResult> CreateViewsAsync(JsonObject arguments, CancellationToken cancellationToken)
        {
            return ExecuteAssetAsync(() => CreateAssetAsync(arguments, cancellationToken));
        }
        private async Task<McpToolResult> CreateAssetAsync(JsonObject arguments, CancellationToken cancellationToken)
        {
            GenerationReferenceInput reference = await ReadReferenceAsync(arguments, false, cancellationToken).ConfigureAwait(false);
            if (arguments["change_strength"] == null)
            {
                reference.ChangeStrength = 0.45;
            }
            Task<AssetCollection> queuedCollectionTask = await Dispatcher.UIThread.InvokeAsync<Task<AssetCollection>>(() =>
            {
                EnsureAvailable(cancellationToken);
                McpGenerationOptions generationOptions = CreateGenerationOptions(arguments, true);
                AssetCreationInput input = new AssetCreationInput
                {
                    Kind = AssetCreationKind.FourViews, Reference = reference, Seed = generationOptions.Seed,
                    RemoveBackground = generationOptions.RemoveBackground,
                    FrameWidth = ReadInteger(arguments, "frame_size", AssetCreationInput.DefaultViewFrameSize), FrameHeight = ReadInteger(arguments, "frame_size", AssetCreationInput.DefaultViewFrameSize),
                    Columns = ReadInteger(arguments, "columns", AssetCreationInput.ViewCount)
                };
                if (arguments["prompt"] != null)
                {
                    input.Prompt = arguments["prompt"].GetValue<string>();
                }
                if (arguments["pixel_art"] != null)
                {
                    input.PixelArt = arguments["pixel_art"].GetValue<bool>();
                }
                return _creation.EnqueueAsync(generationOptions.Settings, generationOptions.Model, generationOptions.Loras, input, cancellationToken);
            });
            AssetCollection collection = await queuedCollectionTask.ConfigureAwait(false);
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
        private async Task<McpToolResult> QueueExistingAssetAsync(JsonObject arguments, bool regenerate, CancellationToken cancellationToken)
        {
            string collectionId = arguments["collection_id"].GetValue<string>();
            AssetCollection collection = await Task.Run(() => _jobs.LoadAssetCollection(collectionId), cancellationToken).ConfigureAwait(false);
            if (collection.Kind == AssetCreationKind.SpriteAnimation)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            long seed = 0;
            if (regenerate == true)
            {
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
            Task queuedTask = await Dispatcher.UIThread.InvokeAsync<Task>(() =>
            {
                EnsureAvailable(cancellationToken);
                if (regenerate == true)
                {
                    return _creation.EnqueueExistingAsync(collectionId, ReadInteger(arguments, "frame_number", 0), arguments["prompt"].GetValue<string>(), seed);
                }
                return _creation.EnqueueExistingAsync(collectionId);
            });
            await queuedTask.ConfigureAwait(false);
            return new McpToolResult(new JsonObject { ["accepted"] = true, ["collection_id"] = collectionId });
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

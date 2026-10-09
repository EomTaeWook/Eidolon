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
        private readonly JobStore _jobs;
        private readonly ISeedProvider _seeds;
        private readonly StringHelper _strings;
        private readonly AssetCreationViewModel _creation;
        private readonly AssetCreationService _assetService;
        private readonly JsonSerializerOptions _assetJson = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };

        public McpService(StudioSession session, AssetsViewModel assets, GenerationViewModel generation,
            JobStore jobs, ISeedProvider seeds, StringHelper strings, AssetCreationViewModel creation, AssetCreationService assetService)
        {
            _session = session;
            _assets = assets;
            _generation = generation;
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
                    "Read Eidolon readiness, current work, pending queue, progress and error. Poll after enqueueing an image request.")
                {
                    ReadOnly = true,
                    Idempotent = true,
                    OpenWorld = false
                }, GetStatusAsync),
                new McpTool(new McpToolDefinition("eidolon_list_models",
                    "List currently available checkpoints and LoRAs with asset IDs, model families and trigger words. Model and LoRA families must match.")
                {
                    ReadOnly = true,
                    Idempotent = true,
                    OpenWorld = false
                }, ListModelsAsync),
                new McpTool(new McpToolDefinition("eidolon_get_instructions",
                    "Read the saved shared positive and negative generation instructions. A generate or edit call can override either instruction for that request only.")
                {
                    ReadOnly = true,
                    Idempotent = true,
                    OpenWorld = false
                }, GetInstructionsAsync),
                new McpTool(new McpToolDefinition("eidolon_generate_image",
                    "Queue text-to-image generation, optionally with a reference image. Returns accepted and the fixed seed, not a completed image. Poll eidolon_get_status then eidolon_list_images. Uses saved instructions unless positive_prompt or negative_prompt is provided. Omitting model_id uses the UI model; omitting lora_ids uses compatible selected UI LoRAs. An empty lora_ids array disables LoRAs for this request. Does not change UI inputs or saved settings.",
                    ImageSchema(false)), GenerateImageAsync),
                new McpTool(new McpToolDefinition("eidolon_edit_image",
                    "Queue a style edit using an existing local reference image. Requires reference_path. Lower change_strength preserves more of the original; default 0.35. Returns accepted and the fixed seed. Poll status and list_images for the result. Request instruction overrides do not change saved settings.",
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
                    "Queue a sprite-animation draft from one reference image using the saved ComfyUI or Codex backend and shared instructions. Checkpoints, LoRAs, seed and change_strength apply only to ComfyUI. Identity and a smooth loop are not guaranteed. Optional frame_descriptions has one pose description per frame. Returns collection_id immediately; poll eidolon_get_asset_collection. Export after reviewing all frames.",
                    AssetCreationSchema(true)), CreateSpriteAnimationAsync),
                new McpTool(new McpToolDefinition("eidolon_create_views",
                    "Queue front, side, back and top images of the same subject using the saved ComfyUI or Codex backend. Checkpoints, LoRAs, seed and change_strength apply only to ComfyUI. These are AI reference drafts, not a 3D model or geometrically verified projections. Returns collection_id; poll eidolon_get_asset_collection.",
                    AssetCreationSchema(false)), CreateViewsAsync),
                new McpTool(new McpToolDefinition("eidolon_get_asset_collection", "Read a collection state and ordered frame paths, prompts, seeds and errors.",
                    CollectionSchema()) { ReadOnly = true, Idempotent = true, OpenWorld = false }, GetAssetCollectionAsync),
                new McpTool(new McpToolDefinition("eidolon_list_asset_collections", "List the latest 20 asset collections with IDs and completion counts.")
                    { ReadOnly = true, Idempotent = true, OpenWorld = false }, ListAssetCollectionsAsync),
                new McpTool(new McpToolDefinition("eidolon_resume_asset_collection", "Queue only unfinished frames using the collection's saved backend and generation conditions. Requires an idle queue.",
                    CollectionSchema()), ResumeAssetCollectionAsync),
                new McpTool(new McpToolDefinition("eidolon_regenerate_asset_frame", "Queue a replacement for one frame or view using its captured reference, backend and generation conditions. Seed applies only to ComfyUI collections. Requires an idle queue. Keeps the old image if regeneration fails.",
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
                    ["prompt"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 16000 },
                    ["model_id"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 128 },
                    ["lora_ids"] = new JsonObject
                    {
                        ["type"] = "array", ["maxItems"] = 32,
                        ["items"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 128 }
                    },
                    ["seed"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = long.MaxValue },
                    ["remove_background"] = new JsonObject { ["type"] = "boolean" },
                    ["positive_prompt"] = new JsonObject { ["type"] = "string", ["maxLength"] = 16000 },
                    ["negative_prompt"] = new JsonObject { ["type"] = "string", ["maxLength"] = 16000 },
                    ["reference_path"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 4096 },
                    ["change_strength"] = new JsonObject { ["type"] = "number", ["minimum"] = 0.05, ["maximum"] = 0.95 }
                },
                ["required"] = required,
                ["additionalProperties"] = false
            };
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
                        ["trigger"] = asset.TriggerWord
                    });
                }
                string selected = string.Empty;
                if (_assets.SelectedModel != null)
                {
                    selected = _assets.SelectedModel.Asset.Id;
                }
                return new JsonObject { ["assets"] = models, ["selected_model_id"] = selected };
            }, token);
        }

        private async Task<McpToolResult> GetInstructionsAsync(JsonObject arguments, CancellationToken token)
        {
            return await OnUiAsync(() => new JsonObject
            {
                ["positive_prompt"] = _session.Settings.PositivePrompt,
                ["negative_prompt"] = _session.Settings.NegativePrompt
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
            schema["required"].AsArray().Add("reference_path");
            properties["frame_size"] = new JsonObject { ["type"] = "integer", ["minimum"] = 16, ["maximum"] = AssetCreationInput.MaximumFrameSize };
            properties["columns"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = AssetCreationInput.MaximumFrames };
            properties["pixel_art"] = new JsonObject { ["type"] = "boolean" };
            if (sprite == true)
            {
                properties["action"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 2000 };
                properties["frame_count"] = new JsonObject { ["type"] = "integer", ["minimum"] = 2, ["maximum"] = AssetCreationInput.MaximumFrames };
                properties["fps"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 60 };
                properties["frame_descriptions"] = new JsonObject { ["type"] = "array", ["maxItems"] = AssetCreationInput.MaximumFrames,
                    ["items"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 4000 } };
                schema["required"].AsArray().Add("action");
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
            properties["prompt"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 16000 };
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
                    Kind = kind, Prompt = arguments["prompt"].GetValue<string>(), Reference = reference, Seed = options.Seed,
                    RemoveBackground = options.RemoveBackground, FrameCount = ReadInteger(arguments, "frame_count", 8),
                    FrameWidth = ReadInteger(arguments, "frame_size", defaultSize), FrameHeight = ReadInteger(arguments, "frame_size", defaultSize),
                    Columns = ReadInteger(arguments, "columns", 4), FramesPerSecond = ReadInteger(arguments, "fps", 10)
                };
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

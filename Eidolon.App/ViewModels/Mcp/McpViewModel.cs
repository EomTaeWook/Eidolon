using Avalonia.Threading;
using Dignus.Log;
using Eidolon.App.Localization;
using Eidolon.App.Mcp;
using Eidolon.App.Presenters;
using Eidolon.App.Services;
using MCP.Core.Models.Server;
using MCP.Core.Transport;

namespace Eidolon.App.ViewModels
{
    public class McpViewModel : StudioPanelViewModel
    {
        private readonly McpHttpServer _server;

        public McpViewModel(StudioSession session, StudioNavigationViewModel navigation, StudioWorkPresenter work,
            StringHelper strings, DesktopDialogs dialogs, McpService tools) : base(session, navigation, work, strings, dialogs)
        {
            _server = new McpHttpServer(new McpServerOptions
            {
                Name = "Eidolon",
                Instructions = "Eidolon queues local image generation, style edits and LoRA training. Image-generation and style-edit prompts may be tags, descriptive phrases or natural-language sentences. For ComfyUI, an installed local converter adapts the original prompt to the captured checkpoint inside the existing work queue; without it, provide English input already suited to that model. Codex receives the original prompt directly without local conversion. The input format does not determine the output format: follow the target model's prompt_guidance instead of requiring comma-separated tags for every model. Keep positive_prompt, negative_prompt, asset descriptions and training captions in English and already adapted to their target model; these inputs are not rewritten by the local converter. Preserve subject/count, relationships, pose, viewpoint, style and constraints without inventing details. Put unwanted elements and artifacts in negative_prompt using the target model's format. Before image generation, editing or asset creation, read eidolon_get_instructions for the active backend, selected checkpoint and saved instructions. For ComfyUI also call eidolon_list_models and follow the target checkpoint's prompt_guidance, using selected_model_id when model_id is omitted. Model families establish compatibility, not a universal tag vocabulary; never assume score_*, rating_* or source_* prefixes apply to all SDXL models. Unknown or renamed checkpoints need their exact model card. For frame regeneration use the collection's captured backend and checkpoint from eidolon_get_asset_collection instead of current UI selection. Resolve conflicting saved tags with request-only instruction overrides. This also applies to frame regeneration. Optional prompt fields may still be omitted and instruction overrides may be empty to clear them. Image and training tools return acceptance, not completion. Poll status and read result paths. Instruction overrides apply only to their request. For LoRA training, call eidolon_train_lora with absolute image or folder paths in white_images and black_images; do not automate a file picker. Both groups train one LoRA. To continue training existing weights, pass resume_lora_id from eidolon_list_models; it must be a local LoRA with the same family and runtime as the training checkpoint. Omit it for new training, independently of UI continuation settings. Only the supplied images are used; include earlier images explicitly if they should be trained again. Continuation loads weights and rank, starts a fresh optimizer and schedule, and preserves the original by saving a new LoRA. Write any supplied description as detailed comma-separated English tags. The training runtime is local ComfyUI with an NVIDIA GPU, regardless of the image-generation backend. Poll eidolon_get_status, then eidolon_list_models for the trained LoRA."
            }, tools.CreateTools(), error => LogHelper.Error(error));
            _server.StateChanged += OnServerStateChanged;
            StartCommand = Command(StartAsync, () => Session.IsInitialized == true && Session.IsClosing == false && IsRunning == false);
            StopCommand = Command(StopAsync, () => Session.IsClosing == false && IsRunning == true);
        }

        public AsyncCommand StartCommand { get; private set; }
        public AsyncCommand StopCommand { get; private set; }

        public string Endpoint
        {
            get
            {
                return _server.Endpoint.AbsoluteUri;
            }
        }

        public bool IsRunning
        {
            get
            {
                return _server.IsRunning;
            }
        }

        public string StatusCaption
        {
            get
            {
                if (IsRunning == true)
                {
                    return _strings.GetString("EidolonText598");
                }
                return _strings.GetString("EidolonText599");
            }
        }

        private async Task StartAsync()
        {
            try
            {
                await _server.StartAsync(Session.Lifetime);
            }
            catch (System.Net.HttpListenerException error) when (error.NativeErrorCode == 5)
            {
                LogHelper.Error(error);
                throw new InvalidOperationException(_strings.GetString("EidolonText602"), error);
            }
            RefreshState();
        }

        private async Task StopAsync()
        {
            await _server.StopAsync();
            RefreshState();
        }

        private void OnServerStateChanged()
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (Session.IsClosing == false)
                {
                    RefreshState();
                }
            });
        }

        private void RefreshState()
        {
            Raise(nameof(IsRunning));
            Raise(nameof(StatusCaption));
            RefreshCommands();
        }

        public override void Localize()
        {
            RefreshState();
            base.Localize();
        }

        internal async Task CloseAsync()
        {
            _server.StateChanged -= OnServerStateChanged;
            await _server.DisposeAsync();
        }
    }
}

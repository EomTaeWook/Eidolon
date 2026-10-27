using System.Text.Json.Nodes;
using Eidolon.Core.Domain;

namespace Eidolon.Core.Infrastructure
{
    public class ComfyWorkflowBuilder
    {
        public ComfyWorkflowBuilder()
        {
        }

        public JsonObject Build(JobRecord job)
        {
            return Build(job, string.Empty);
        }

        public JsonObject Build(JobRecord job, string referenceImage)
        {
            return Build(job, referenceImage, string.Empty);
        }

        public JsonObject Build(JobRecord job, string referenceImage, string referenceMask)
        {
            JsonObject workflow = new();
            Add("1", "CheckpointLoaderSimple", new JsonObject { ["ckpt_name"] = job.Model.EngineName });
            string modelNode = "1";
            string clipNode = "1";
            int clipSlot = 1;
            int index = 20;
            foreach (ModelAsset lora in job.Loras)
            {
                string id = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                Add(id, "LoraLoader", new JsonObject
                {
                    ["model"] = Link(modelNode, 0),
                    ["clip"] = Link(clipNode, clipSlot),
                    ["lora_name"] = lora.EngineName,
                    ["strength_model"] = 1.0,
                    ["strength_clip"] = 1.0
                });
                modelNode = id;
                clipNode = id;
                clipSlot = 1;
                index++;
            }
            Add("2", "CLIPTextEncode", new JsonObject { ["text"] = job.PositivePrompt, ["clip"] = Link(clipNode, clipSlot) });
            Add("3", "CLIPTextEncode", new JsonObject { ["text"] = job.NegativePrompt, ["clip"] = Link(clipNode, clipSlot) });
            if (job.ReferenceMode == GenerationReferenceMode.None)
            {
                Add("4", "EmptyLatentImage", new JsonObject { ["width"] = job.Width, ["height"] = job.Height, ["batch_size"] = 1 });
            }
            else
            {
                if (string.IsNullOrWhiteSpace(referenceImage) == true)
                {
                    throw new StudioException(StudioMessageCode.InvalidReferenceImage);
                }
                Add("8", "LoadImage", new JsonObject { ["image"] = referenceImage });
                if (job.ReferenceMode == GenerationReferenceMode.Inpaint)
                {
                    if (string.IsNullOrWhiteSpace(referenceMask) == true)
                    {
                        throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
                    }
                    Add("12", "ImageScale", new JsonObject { ["image"] = Link("8", 0), ["upscale_method"] = "lanczos", ["width"] = job.Width, ["height"] = job.Height, ["crop"] = "disabled" });
                    Add("13", "LoadImage", new JsonObject { ["image"] = referenceMask });
                    Add("14", "InvertMask", new JsonObject { ["mask"] = Link("13", 1) });
                    Add("4", "VAEEncodeForInpaint", new JsonObject { ["pixels"] = Link("12", 0), ["vae"] = Link("1", 2), ["mask"] = Link("14", 0), ["grow_mask_by"] = 6 });
                }
                else
                {
                    Add("4", "VAEEncode", new JsonObject { ["pixels"] = Link("8", 0), ["vae"] = Link("1", 2) });
                }
            }
            string positiveNode = "2";
            string negativeNode = "3";
            int negativeSlot = 0;
            if (job.ReferenceMode == GenerationReferenceMode.Restyle)
            {
                if (string.IsNullOrWhiteSpace(job.OutlineControlModel) == true)
                {
                    throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
                }
                if (job.OutlineControlStrength <= 0)
                {
                    throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
                }
                Add("9", "Canny", new JsonObject { ["image"] = Link("8", 0), ["low_threshold"] = 0.4, ["high_threshold"] = 0.8 });
                Add("10", "ControlNetLoader", new JsonObject { ["control_net_name"] = job.OutlineControlModel });
                Add("11", "ControlNetApplyAdvanced", new JsonObject
                {
                    ["positive"] = Link("2", 0),
                    ["negative"] = Link("3", 0),
                    ["control_net"] = Link("10", 0),
                    ["image"] = Link("9", 0),
                    ["vae"] = Link("1", 2),
                    ["strength"] = job.OutlineControlStrength,
                    ["start_percent"] = 0.0,
                    ["end_percent"] = 1.0
                });
                positiveNode = "11";
                negativeNode = "11";
                negativeSlot = 1;
            }
            Add("5", "KSampler", new JsonObject
            {
                ["model"] = Link(modelNode, 0),
                ["seed"] = job.Seed,
                ["steps"] = job.Steps,
                ["cfg"] = job.Guidance,
                ["sampler_name"] = job.Sampler,
                ["scheduler"] = job.Scheduler,
                ["positive"] = Link(positiveNode, 0),
                ["negative"] = Link(negativeNode, negativeSlot),
                ["latent_image"] = Link("4", 0),
                ["denoise"] = job.Denoise
            });
            Add("6", "VAEDecode", new JsonObject { ["samples"] = Link("5", 0), ["vae"] = Link("1", 2) });
            Add("7", "SaveImage", new JsonObject { ["images"] = Link("6", 0), ["filename_prefix"] = "Eidolon/" + job.Id });
            return workflow;

            void Add(string id, string type, JsonObject inputs)
            {
                workflow[id] = new JsonObject { ["class_type"] = type, ["inputs"] = inputs };
            }
        }

        private JsonArray Link(string id, int output)
        {
            return new JsonArray(id, output);
        }
    }
}

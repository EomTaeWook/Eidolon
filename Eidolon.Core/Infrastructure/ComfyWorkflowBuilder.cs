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
            JsonObject workflow = new JsonObject();
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
            Add("4", "EmptyLatentImage", new JsonObject { ["width"] = job.Width, ["height"] = job.Height, ["batch_size"] = 1 });
            Add("5", "KSampler", new JsonObject
            {
                ["model"] = Link(modelNode, 0),
                ["seed"] = job.Seed,
                ["steps"] = job.Steps,
                ["cfg"] = job.Guidance,
                ["sampler_name"] = job.Sampler,
                ["scheduler"] = job.Scheduler,
                ["positive"] = Link("2", 0),
                ["negative"] = Link("3", 0),
                ["latent_image"] = Link("4", 0),
                ["denoise"] = 1.0
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

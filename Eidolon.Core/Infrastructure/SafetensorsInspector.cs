using Eidolon.Core.Application;
using System.Buffers.Binary;
using System.Text.Json;
using Eidolon.Core.Domain;

namespace Eidolon.Core.Infrastructure
{
    public class SafetensorsInspector
    {
        public SafetensorsInspector()
        {
        }
        public ModelFamily Inspect(string path, AssetKind kind)
        {
            using FileStream stream = File.OpenRead(path);
            byte[] prefix = new byte[8];
            stream.ReadExactly(prefix);
            long headerLength = BinaryPrimitives.ReadInt64LittleEndian(prefix);
            if (headerLength <= 0 || headerLength > 16 * 1024 * 1024)
            {
                throw new StudioException(StudioMessageCode.InvalidSafetensorsHeader);
            }
            if (headerLength > stream.Length - 8)
            {
                throw new StudioException(StudioMessageCode.IncompleteSafetensors);
            }
            byte[] header = new byte[(int)headerLength];
            stream.ReadExactly(header);
            using JsonDocument document = JsonDocument.Parse(header);
            long payloadLength = stream.Length - headerLength - 8;
            int tensorCount = 0;
            bool isLora = false;
            ModelFamily detected = ModelFamily.Unknown;
            foreach (JsonProperty tensor in document.RootElement.EnumerateObject())
            {
                if (tensor.Name == "__metadata__")
                {
                    continue;
                }
                tensorCount++;
                JsonElement offsets = tensor.Value.GetProperty("data_offsets");
                long start = offsets[0].GetInt64();
                long end = offsets[1].GetInt64();
                if (start < 0 || end < start || end > payloadLength)
                {
                    throw new StudioException(StudioMessageCode.InvalidTensorRange);
                }
                if (tensor.Name.Contains("lora_", StringComparison.OrdinalIgnoreCase) == true)
                {
                    isLora = true;
                }
                if (tensor.Name.StartsWith("double_blocks.", StringComparison.Ordinal) == true ||
                    tensor.Name.Contains("joint_blocks.", StringComparison.Ordinal) == true)
                {
                    throw new StudioException(StudioMessageCode.UnsupportedAssetArchitecture);
                }
                if (tensor.Name.StartsWith("conditioner.embedders.1.", StringComparison.Ordinal) == true ||
                    tensor.Name.StartsWith("lora_te2_", StringComparison.Ordinal) == true)
                {
                    detected = ModelFamily.Sdxl;
                }
                if (detected == ModelFamily.Unknown && tensor.Name.Contains("attn2.to_k.weight", StringComparison.Ordinal) == true)
                {
                    JsonElement shape = tensor.Value.GetProperty("shape");
                    if (shape.GetArrayLength() == 2 && shape[1].GetInt32() == 768)
                    {
                        detected = ModelFamily.StableDiffusion15;
                    }
                    if (shape.GetArrayLength() == 2 && shape[1].GetInt32() == 2048)
                    {
                        detected = ModelFamily.Sdxl;
                    }
                }
                if (kind == AssetKind.Checkpoint && tensor.Name.EndsWith("input_blocks.0.0.weight", StringComparison.Ordinal) == true)
                {
                    JsonElement shape = tensor.Value.GetProperty("shape");
                    if (shape.GetArrayLength() == 4 && shape[1].GetInt32() != 4)
                    {
                        throw new StudioException(StudioMessageCode.SpecialCheckpointUnsupported);
                    }
                }
            }
            if (tensorCount == 0)
            {
                throw new StudioException(StudioMessageCode.ModelTensorsMissing);
            }
            if (kind == AssetKind.Lora && isLora == false)
            {
                throw new StudioException(StudioMessageCode.InvalidLoraWeights);
            }
            if (kind == AssetKind.Checkpoint && isLora == true)
            {
                throw new StudioException(StudioMessageCode.InvalidCheckpointWeights);
            }
            if (document.RootElement.TryGetProperty("__metadata__", out JsonElement metadata) == true)
            {
                foreach (string key in new[] { "ss_base_model_version", "modelspec.architecture" })
                {
                    if (metadata.TryGetProperty(key, out JsonElement value) == true)
                    {
                        string text = value.GetString() ?? string.Empty;
                        if (text.Contains("refiner", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            throw new StudioException(StudioMessageCode.RefinerUnsupported);
                        }
                        if (text.Contains("sdxl", StringComparison.OrdinalIgnoreCase) == true ||
                            text.Contains("stable-diffusion-xl", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            detected = ModelFamily.Sdxl;
                        }
                        else if (text.StartsWith("sd_v1", StringComparison.OrdinalIgnoreCase) == true ||
                            text.Contains("stable-diffusion-v1", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            detected = ModelFamily.StableDiffusion15;
                        }
                        else if (string.IsNullOrEmpty(text) == false)
                        {
                            throw new StudioException(StudioMessageCode.UnsupportedWeightsFamily);
                        }
                    }
                }
            }
            return detected;
        }
    }
}

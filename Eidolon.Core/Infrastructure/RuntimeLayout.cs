using Eidolon.Core.Domain;

namespace Eidolon.Core.Infrastructure
{
    public class RuntimeLayout
    {
        public const string ComfyVersion = "v0.38.0";
        public const string TrainingVersion = "v0.12.0";
        public const string UvVersion = "0.12.22";
        public const string PythonVersion = "3.11.17";
        public const string TorchVersion = "2.10.0";
        public const string VisionVersion = "0.25.0";
        public const string OutlineNodeName = "comfyui_controlnet_aux";
        public const string OutlineNodeRevision = "59b1fc411ede8623b2997855b8018f0b3b6cf49f";
        public const string PromptConverterVersion = "b11889";
        public const string PromptConverterRevision = "d854d98bae598388ecea1ed84396587e5edc326d";
        public const string PromptConverterArchiveUrl = "https://github.com/ggml-org/llama.cpp/releases/download/b11889/llama-b11889-bin-win-cpu-x64.zip";
        public const string PromptConverterArchiveSha256 = "bf563dffb502a230ae06c9dc455b54cbc079e7e0bc270cd4d6191209b28d175d";
        public const string PromptConverterModelRevision = "ae44f08e1392f39c0e474af10c3ff8355c8b6688";
        public const string PromptConverterModelName = "Qwen_Qwen3-4B-Instruct-2507-Q4_K_M.gguf";
        public const string PromptConverterModelUrl = "https://huggingface.co/bartowski/Qwen_Qwen3-4B-Instruct-2507-GGUF/resolve/ae44f08e1392f39c0e474af10c3ff8355c8b6688/Qwen_Qwen3-4B-Instruct-2507-Q4_K_M.gguf";
        public const string PromptConverterModelSha256 = "2fde00ce69dd4899c70d020845e2638353015bba0fdf161b3eb965f2bca4464e";
        public const long PromptConverterModelBytes = 2497280736;
        public const string PromptConverterOriginalModelRevision = "cdbee75f17c01a7cc42f958dc650907174af0554";

        public string Root { get; private set; }
        public string ComfyDirectory { get; private set; }
        public string TrainingDirectory { get; private set; }
        public string OutlineNodesDirectory { get; private set; }
        public string ComfyPython { get; private set; }
        public string TrainingPython { get; private set; }
        public string ModelsDirectory { get; private set; }
        public string LorasDirectory { get; private set; }
        public string ControlNetsDirectory { get; private set; }
        public string TrainingDataDirectory { get; private set; }
        public string LogDirectory { get; private set; }
        public string UvExecutable { get; private set; }
        public string PromptConverterExecutable { get; private set; }
        public string PromptConverterModelPath { get; private set; }

        public RuntimeLayout(string installDirectory)
        {
            if (string.IsNullOrWhiteSpace(installDirectory) == true)
            {
                throw new StudioException(StudioMessageCode.InstallDirectoryRequired);
            }
            if (Path.IsPathFullyQualified(installDirectory) == false)
            {
                throw new StudioException(StudioMessageCode.InstallPathInvalid);
            }
            Root = Path.Combine(Path.GetFullPath(installDirectory), "EidolonRuntime");
            ComfyDirectory = Path.Combine(Root, "Packages", "ComfyUI-" + ComfyVersion);
            TrainingDirectory = Path.Combine(Root, "Packages", "SdScripts-" + TrainingVersion);
            OutlineNodesDirectory = Path.Combine(ComfyDirectory, "custom_nodes", OutlineNodeName);
            ComfyPython = Path.Combine(Root, "Environments", "ComfyUI", "Scripts", "python.exe");
            TrainingPython = Path.Combine(Root, "Environments", "Training", "Scripts", "python.exe");
            ModelsDirectory = Path.Combine(Root, "Models", "checkpoints");
            LorasDirectory = Path.Combine(Root, "Models", "loras");
            ControlNetsDirectory = Path.Combine(ComfyDirectory, "models", "controlnet");
            TrainingDataDirectory = Path.Combine(Root, "TrainingData");
            LogDirectory = Path.Combine(Root, "Logs");
            UvExecutable = Path.Combine(Root, "Tools", "uv.exe");
            PromptConverterExecutable = Path.Combine(Root, "Tools", "PromptConverter-" + PromptConverterVersion, "llama-completion.exe");
            PromptConverterModelPath = Path.Combine(Root, "Models", "PromptConverter-" + PromptConverterModelRevision, PromptConverterModelName);
        }

        public string AssetPath(ModelAsset asset)
        {
            string directory = ModelsDirectory;
            if (asset.Kind == AssetKind.Lora)
            {
                directory = LorasDirectory;
            }
            string path = Path.GetFullPath(Path.Combine(directory, asset.EngineName));
            string prefix = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == false)
            {
                throw new StudioException(StudioMessageCode.AssetPathInvalid);
            }
            return path;
        }

    }
}

using System.Runtime.InteropServices;
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

        public string Root { get; private set; }
        public string ComfyDirectory { get; private set; }
        public string TrainingDirectory { get; private set; }
        public string ComfyPython { get; private set; }
        public string TrainingPython { get; private set; }
        public string ModelsDirectory { get; private set; }
        public string LorasDirectory { get; private set; }
        public string LogDirectory { get; private set; }
        public string UvExecutable { get; private set; }

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
            ComfyPython = Path.Combine(Root, "Environments", "ComfyUI", "Scripts", "python.exe");
            TrainingPython = Path.Combine(Root, "Environments", "Training", "Scripts", "python.exe");
            ModelsDirectory = Path.Combine(Root, "Models", "checkpoints");
            LorasDirectory = Path.Combine(Root, "Models", "loras");
            LogDirectory = Path.Combine(Root, "Logs");
            UvExecutable = Path.Combine(Root, "Tools", "uv.exe");
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

        public void EnsureInstalled(bool needsTraining = false)
        {
            if (OperatingSystem.IsWindows() == false || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            {
                throw new StudioException(StudioMessageCode.UnsupportedRuntimePlatform);
            }
            RuntimeManifest manifest = new AtomicJsonFile().Read<RuntimeManifest>(Path.Combine(Root, "Runtime.json"));
            if (manifest == null)
            {
                throw new StudioException(StudioMessageCode.RuntimeNotInstalled);
            }
            if (manifest.State != "Ready")
            {
                throw new StudioException(StudioMessageCode.RuntimeIncomplete);
            }
            if (manifest.Owner != "Eidolon" || manifest.SchemaVersion != 1)
            {
                throw new StudioException(StudioMessageCode.UnsupportedRuntimeSchema);
            }
            if (manifest.ComfyVersion != ComfyVersion || manifest.TrainingVersion != TrainingVersion)
            {
                throw new StudioException(StudioMessageCode.RuntimeVersionMismatch);
            }
            if (File.Exists(ComfyPython) == false || File.Exists(Path.Combine(ComfyDirectory, "main.py")) == false)
            {
                throw new StudioException(StudioMessageCode.GenerationRuntimeMissing);
            }
            if (needsTraining == true && File.Exists(TrainingPython) == false)
            {
                throw new StudioException(StudioMessageCode.TrainingRuntimeMissing);
            }
        }
    }
}

using System.Diagnostics;
using Eidolon.Core.Domain;

namespace Eidolon.Core.Infrastructure
{
    public class RuntimeModuleReader
    {
        public RuntimeModuleReader()
        {
        }

        public IReadOnlyList<RuntimeModule> Read(RuntimeLayout layout, RuntimeManifest manifest, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string comfyVersion = RuntimeLayout.ComfyVersion;
            string trainingVersion = RuntimeLayout.TrainingVersion;
            if (manifest != null)
            {
                comfyVersion = SourceVersion(manifest.ComfyVersion, comfyVersion);
                trainingVersion = SourceVersion(manifest.TrainingVersion, trainingVersion);
            }
            string comfyDirectory = Path.Combine(layout.Root, "Packages", "ComfyUI-" + comfyVersion);
            string trainingDirectory = Path.Combine(layout.Root, "Packages", "SdScripts-" + trainingVersion);
            string generationEnvironment = Path.Combine(layout.Root, "Environments", "ComfyUI");
            string trainingEnvironment = Path.Combine(layout.Root, "Environments", "Training");
            string sitePackages = Path.Combine(generationEnvironment, "Lib", "site-packages");
            List<RuntimeModule> modules = new List<RuntimeModule>();
            Add(modules, RuntimeModuleKind.ComfyUi, comfyDirectory, comfyVersion,
                File.Exists(Path.Combine(comfyDirectory, "main.py")));
            Add(modules, RuntimeModuleKind.GenerationPython, layout.ComfyPython, PythonVersion(generationEnvironment),
                File.Exists(layout.ComfyPython));
            Add(modules, RuntimeModuleKind.Torch, Path.Combine(sitePackages, "torch"), PackageVersion(sitePackages, "torch", token),
                File.Exists(Path.Combine(sitePackages, "torch", "__init__.py")));
            Add(modules, RuntimeModuleKind.TorchVision, Path.Combine(sitePackages, "torchvision"), PackageVersion(sitePackages, "torchvision", token),
                File.Exists(Path.Combine(sitePackages, "torchvision", "__init__.py")));
            Add(modules, RuntimeModuleKind.SdScripts, trainingDirectory, trainingVersion,
                File.Exists(Path.Combine(trainingDirectory, "sdxl_train_network.py")));
            Add(modules, RuntimeModuleKind.TrainingPython, layout.TrainingPython, PythonVersion(trainingEnvironment),
                File.Exists(layout.TrainingPython));
            string uvVersion = string.Empty;
            if (File.Exists(layout.UvExecutable) == true)
            {
                FileVersionInfo information = FileVersionInfo.GetVersionInfo(layout.UvExecutable);
                if (information.ProductVersion != null)
                {
                    uvVersion = information.ProductVersion;
                }
            }
            Add(modules, RuntimeModuleKind.Uv, layout.UvExecutable, uvVersion, File.Exists(layout.UvExecutable));
            ReadCustomNodes(modules, Path.Combine(comfyDirectory, "custom_nodes"), token);
            if (modules.Any(module => module.Kind == RuntimeModuleKind.CustomNode && module.Name == RuntimeLayout.OutlineNodeName) == false)
            {
                modules.Add(new RuntimeModule
                {
                    Kind = RuntimeModuleKind.CustomNode,
                    State = RuntimeModuleState.NotInstalled,
                    Name = RuntimeLayout.OutlineNodeName,
                    Location = Path.Combine(comfyDirectory, "custom_nodes", RuntimeLayout.OutlineNodeName)
                });
            }
            return modules;
        }

        private string SourceVersion(string version, string fallback)
        {
            if (string.IsNullOrWhiteSpace(version) == true)
            {
                return fallback;
            }
            if (version.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || version == "." || version == "..")
            {
                throw new StudioException(StudioMessageCode.UnsupportedRuntimeSchema);
            }
            return version;
        }

        private void Add(List<RuntimeModule> modules, RuntimeModuleKind kind, string location, string version, bool installed)
        {
            RuntimeModule module = new RuntimeModule { Kind = kind, Location = location };
            if (installed == true)
            {
                module.State = RuntimeModuleState.Installed;
                module.Version = version;
            }
            modules.Add(module);
        }

        private string PythonVersion(string environment)
        {
            string configuration = Path.Combine(environment, "pyvenv.cfg");
            if (File.Exists(configuration) == false)
            {
                return string.Empty;
            }
            foreach (string line in File.ReadLines(configuration))
            {
                int separator = line.IndexOf('=');
                if (separator < 0)
                {
                    continue;
                }
                string name = line.Substring(0, separator).Trim();
                if (name == "version" || name == "version_info")
                {
                    return line.Substring(separator + 1).Trim();
                }
            }
            return string.Empty;
        }

        private string PackageVersion(string sitePackages, string name, CancellationToken token)
        {
            if (Directory.Exists(sitePackages) == false)
            {
                return string.Empty;
            }
            foreach (string directory in Directory.EnumerateDirectories(sitePackages, name + "-*.dist-info")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                string metadata = Path.Combine(directory, "METADATA");
                if (File.Exists(metadata) == false)
                {
                    continue;
                }
                string packageName = string.Empty;
                string version = string.Empty;
                foreach (string line in File.ReadLines(metadata))
                {
                    token.ThrowIfCancellationRequested();
                    if (line.Length == 0)
                    {
                        break;
                    }
                    if (line.StartsWith("Name: ", StringComparison.Ordinal) == true)
                    {
                        packageName = line.Substring(6).Trim();
                    }
                    if (line.StartsWith("Version: ", StringComparison.Ordinal) == true)
                    {
                        version = line.Substring(9).Trim();
                    }
                }
                if (string.Equals(packageName, name, StringComparison.OrdinalIgnoreCase) == true)
                {
                    return version;
                }
            }
            return string.Empty;
        }

        private void ReadCustomNodes(List<RuntimeModule> modules, string directory, CancellationToken token)
        {
            if (Directory.Exists(directory) == false)
            {
                return;
            }
            foreach (string path in Directory.EnumerateFileSystemEntries(directory)
                .OrderBy(entry => entry, StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                string name = Path.GetFileName(path);
                if (name.StartsWith(".", StringComparison.Ordinal) == true || name == "__pycache__" || name == "__init__.py")
                {
                    continue;
                }
                bool disabled = name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                string moduleName = name;
                if (disabled == true)
                {
                    moduleName = name.Substring(0, name.Length - ".disabled".Length);
                }
                string pythonFile = path;
                if (Directory.Exists(path) == true)
                {
                    pythonFile = Path.Combine(path, "__init__.py");
                    if (File.Exists(pythonFile) == false)
                    {
                        continue;
                    }
                }
                else
                {
                    if (moduleName.EndsWith(".py", StringComparison.OrdinalIgnoreCase) == false)
                    {
                        continue;
                    }
                    moduleName = Path.GetFileNameWithoutExtension(moduleName);
                }
                string version = ReadDeclaredVersion(pythonFile, "__version__", string.Empty, token);
                if (string.IsNullOrWhiteSpace(version) == true && Directory.Exists(path) == true)
                {
                    version = ReadDeclaredVersion(Path.Combine(path, "pyproject.toml"), "version", "[project]", token);
                }
                RuntimeModule module = new RuntimeModule
                {
                    Kind = RuntimeModuleKind.CustomNode,
                    State = RuntimeModuleState.Installed,
                    Name = moduleName,
                    Location = path,
                    Version = version
                };
                if (disabled == true)
                {
                    module.State = RuntimeModuleState.Disabled;
                }
                modules.Add(module);
            }
        }

        private string ReadDeclaredVersion(string path, string name, string section, CancellationToken token)
        {
            if (File.Exists(path) == false)
            {
                return string.Empty;
            }
            bool inSection = string.IsNullOrEmpty(section);
            foreach (string line in File.ReadLines(path))
            {
                token.ThrowIfCancellationRequested();
                string text = line.Trim();
                if (string.IsNullOrEmpty(section) == false && text.StartsWith("[", StringComparison.Ordinal) == true)
                {
                    inSection = text == section;
                    continue;
                }
                if (inSection == false)
                {
                    continue;
                }
                int separator = text.IndexOf('=');
                if (separator < 0 || text.Substring(0, separator).Trim() != name)
                {
                    continue;
                }
                string value = text.Substring(separator + 1).Trim();
                if (value.Length < 2 || (value[0] != '\'' && value[0] != '"'))
                {
                    continue;
                }
                int closingQuote = value.IndexOf(value[0], 1);
                if (closingQuote > 1)
                {
                    return value.Substring(1, closingQuote - 1);
                }
            }
            return string.Empty;
        }
    }
}

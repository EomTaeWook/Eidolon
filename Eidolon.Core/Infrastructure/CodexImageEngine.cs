using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Eidolon.Core.Domain;
using SkiaSharp;

namespace Eidolon.Core.Infrastructure
{
    public class CodexImageEngine
    {
        private const string ResultSchema = """
        {
            "type": "object",
            "properties": {
            "image_path": { "type": "string" },
            "error": { "type": "string" }
            },
            "required": ["image_path", "error"],
            "additionalProperties": false
        }
        """;
        private readonly ProcessRunner _processes;
        private readonly CodexExecutableLocator _executables;
        private readonly CodexInstructionTemplates _templates;

        public CodexImageEngine(ProcessRunner processes, CodexExecutableLocator executables, CodexInstructionTemplates templates)
        {
            _processes = processes;
            _executables = executables;
            _templates = templates;
        }

        public async Task GenerateAsync(StudioSettings settings, JobRecord job, JobStore jobs,
            IProgress<WorkProgress> progress, CancellationToken token)
        {
            string executable = _executables.Find(settings.CodexExecutablePath);
            if (string.IsNullOrEmpty(executable) == true)
            {
                throw new StudioException(StudioMessageCode.CodexExecutableNotFound);
            }
            string output = jobs.WorkingImagePath(job, Path.Combine("Originals", Guid.NewGuid().ToString("N") + ".png"));
            string workingDirectory = Path.Combine(Path.GetDirectoryName(output), "Codex");
            Directory.CreateDirectory(workingDirectory);
            string logPath = Path.Combine(workingDirectory, "Codex.log");
            string instructions = CreateInstructions(job);
            string schemaPath = Path.Combine(workingDirectory, "ResultSchema.json");
            string resultPath = Path.Combine(workingDirectory, "Result.json");
            await File.WriteAllTextAsync(schemaPath, ResultSchema, new UTF8Encoding(false), token).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(workingDirectory, "Request.txt"), instructions,
                new UTF8Encoding(false), token).ConfigureAwait(false);
            List<string> arguments = new List<string>
            {
                "exec", "--skip-git-repo-check", "--ephemeral", "--ignore-user-config",
                "--sandbox", "read-only", "--enable", "image_generation", "--json",
                "--color", "never", "--output-schema", schemaPath, "--output-last-message", resultPath
            };
            if (string.IsNullOrWhiteSpace(settings.CodexModel) == false)
            {
                arguments.Add("--model");
                arguments.Add(settings.CodexModel.Trim());
            }
            if (job.ReferenceMode != GenerationReferenceMode.None)
            {
                arguments.Add("--image");
                arguments.Add(job.ReferenceImagePath);
            }
            arguments.Add("-");
            job.State = JobState.Running;
            jobs.Save(job);
            progress.Report(new WorkProgress(StudioMessageCode.CodexGenerating));
            CodexOutputReader response = new CodexOutputReader();
            await _processes.RunAsync(executable, arguments, workingDirectory, logPath, token,
                stopWithParent: true, standardInput: instructions,
                standardOutputReceived: response.ReadLine).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            CodexImageResult result = ReadResult(resultPath, logPath);
            string source = ResolveSourceImage(result.ImagePath, response.ThreadId, logPath);
            await CopyOriginalAsync(source, output, logPath, token).ConfigureAwait(false);
            await Task.Run(() => ReadDimensions(job, output, logPath, token), token).ConfigureAwait(false);
            job.OriginalImageFiles.Add(output);
            jobs.Save(job);
        }

        private string CreateInstructions(JobRecord job)
        {
            string backgroundInstructions = string.Empty;
            if (job.RemoveBackground == true)
            {
                backgroundInstructions = _templates.TransparentBackground;
            }
            string referenceInstructions = string.Empty;
            if (job.ReferenceMode == GenerationReferenceMode.Restyle)
            {
                referenceInstructions = _templates.Restyle;
            }
            else if (job.ReferenceMode == GenerationReferenceMode.Reimagine)
            {
                referenceInstructions = _templates.Reimagine;
            }
            Dictionary<string, string> values = new Dictionary<string, string>
            {
                ["PositivePrompt"] = JsonSerializer.Serialize(job.PositivePrompt),
                ["NegativePrompt"] = JsonSerializer.Serialize(job.NegativePrompt),
                ["BackgroundInstructions"] = backgroundInstructions,
                ["ReferenceInstructions"] = referenceInstructions
            };
            return Regex.Replace(_templates.Request, @"\{\{(\w+)\}\}", match =>
            {
                return values[match.Groups[1].Value];
            });
        }

        private CodexImageResult ReadResult(string path, string logPath)
        {
            if (File.Exists(path) == false)
            {
                throw new StudioException(StudioMessageCode.CodexOutputMissing, logPath);
            }
            if (new FileInfo(path).Length > 65536)
            {
                throw new StudioException(StudioMessageCode.InvalidCodexOutput, logPath);
            }
            try
            {
                using FileStream stream = File.OpenRead(path);
                CodexImageResult result = JsonSerializer.Deserialize<CodexImageResult>(stream);
                if (result == null)
                {
                    throw new StudioException(StudioMessageCode.InvalidCodexOutput, logPath);
                }
                if (string.IsNullOrWhiteSpace(result.ImagePath) == false)
                {
                    if (string.IsNullOrWhiteSpace(result.Error) == false)
                    {
                        throw new StudioException(StudioMessageCode.CodexOutputMissing, logPath);
                    }
                }
                return result;
            }
            catch (JsonException)
            {
                throw new StudioException(StudioMessageCode.InvalidCodexOutput, logPath);
            }
        }

        private string ResolveSourceImage(string path, string threadId, string logPath)
        {
            if (Guid.TryParse(threadId, out Guid thread) == false)
            {
                throw new StudioException(StudioMessageCode.InvalidCodexOutput, logPath);
            }
            string codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (string.IsNullOrWhiteSpace(codexHome) == true)
            {
                codexHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            }
            if (Path.IsPathFullyQualified(codexHome) == false)
            {
                throw new StudioException(StudioMessageCode.InvalidCodexOutput, logPath);
            }
            codexHome = Path.GetFullPath(codexHome);
            string images = Path.Combine(codexHome, "generated_images");
            string session = Path.Combine(images, thread.ToString("D"));
            foreach (string directory in new string[] { codexHome, images, session })
            {
                if (Directory.Exists(directory) == false)
                {
                    throw new StudioException(StudioMessageCode.CodexOutputMissing, logPath);
                }
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new StudioException(StudioMessageCode.InvalidCodexOutput, logPath);
                }
            }
            if (string.IsNullOrWhiteSpace(path) == true)
            {
                // 경로 전달 실패는 현재 세션에 실제로 생성된 PNG로 판별한다.
                string[] candidates = Directory.EnumerateFiles(session, "*.png", SearchOption.TopDirectoryOnly).Take(2).ToArray();
                if (candidates.Length == 0)
                {
                    throw new StudioException(StudioMessageCode.CodexOutputMissing, logPath);
                }
                if (candidates.Length != 1)
                {
                    throw new StudioException(StudioMessageCode.InvalidCodexOutput, logPath);
                }
                path = candidates[0];
            }
            if (Path.IsPathFullyQualified(path) == false)
            {
                throw new StudioException(StudioMessageCode.InvalidCodexOutput, logPath);
            }
            string source = Path.GetFullPath(path);
            if (string.Equals(Path.GetDirectoryName(source), session, StringComparison.OrdinalIgnoreCase) == false)
            {
                throw new StudioException(StudioMessageCode.InvalidCodexOutput, logPath);
            }
            if (string.Equals(Path.GetExtension(source), ".png", StringComparison.OrdinalIgnoreCase) == false)
            {
                throw new StudioException(StudioMessageCode.InvalidCodexOutput, logPath);
            }
            if (File.Exists(source) == false)
            {
                throw new StudioException(StudioMessageCode.CodexOutputMissing, logPath);
            }
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            {
                throw new StudioException(StudioMessageCode.InvalidCodexOutput, logPath);
            }
            return source;
        }

        private async Task CopyOriginalAsync(string source, string output, string logPath, CancellationToken token)
        {
            using FileStream input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (input.Length == 0)
            {
                throw new StudioException(StudioMessageCode.InvalidCodexOutput, logPath);
            }
            if (input.Length > GenerationReferenceInput.MaximumImageBytes)
            {
                throw new StudioException(StudioMessageCode.InvalidCodexOutput, logPath);
            }
            using FileStream destination = new FileStream(output, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 81920, FileOptions.Asynchronous);
            await input.CopyToAsync(destination, token).ConfigureAwait(false);
            await destination.FlushAsync(token).ConfigureAwait(false);
        }

        private void ReadDimensions(JobRecord job, string output, string logPath, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            FileInfo file = new FileInfo(output);
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new StudioException(StudioMessageCode.InvalidCodexOutput, logPath);
            }
            if (file.Length == 0)
            {
                throw new StudioException(StudioMessageCode.InvalidCodexOutput, logPath);
            }
            if (file.Length > GenerationReferenceInput.MaximumImageBytes)
            {
                throw new StudioException(StudioMessageCode.InvalidCodexOutput, logPath);
            }
            using FileStream stream = File.OpenRead(output);
            using SKManagedStream input = new SKManagedStream(stream);
            using SKCodec codec = SKCodec.Create(input);
            if (codec == null)
            {
                throw new StudioException(StudioMessageCode.InvalidCodexOutput, logPath);
            }
            if (codec.EncodedFormat != SKEncodedImageFormat.Png)
            {
                throw new StudioException(StudioMessageCode.InvalidCodexOutput, logPath);
            }
            if (codec.Info.Width < 1)
            {
                throw new StudioException(StudioMessageCode.InvalidCodexOutput, logPath);
            }
            if (codec.Info.Height < 1)
            {
                throw new StudioException(StudioMessageCode.InvalidCodexOutput, logPath);
            }
            if ((long)codec.Info.Width * codec.Info.Height > GenerationReferenceInput.MaximumImagePixels)
            {
                throw new StudioException(StudioMessageCode.InvalidCodexOutput, logPath);
            }
            using SKBitmap image = SKBitmap.Decode(codec);
            if (image == null)
            {
                throw new StudioException(StudioMessageCode.InvalidCodexOutput, logPath);
            }
            if (job.RemoveBackground == true)
            {
                bool hasTransparency = false;
                foreach (SKColor pixel in image.Pixels)
                {
                    if (pixel.Alpha < byte.MaxValue)
                    {
                        hasTransparency = true;
                        break;
                    }
                }
                if (hasTransparency == false)
                {
                    throw new StudioException(StudioMessageCode.InvalidCodexOutput, logPath);
                }
            }
            job.Width = codec.Info.Width;
            job.Height = codec.Info.Height;
            token.ThrowIfCancellationRequested();
        }
    }
}

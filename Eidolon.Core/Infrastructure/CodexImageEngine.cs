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
        private const string ReferenceFeaturesSchema = """
        {
            "type": "object",
            "properties": {
                "features": { "type": "string" },
                "error": { "type": "string" }
            },
            "required": ["features", "error"],
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

        public async Task<string> DescribeReferenceFeaturesAsync(StudioSettings settings, GenerationReferenceInput reference, string changePrompt, string dataDirectory, IProgress<WorkProgress> progress, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(settings);
            if (reference == null)
            {
                throw new StudioException(StudioMessageCode.InvalidReferenceImage);
            }
            byte[] imageData = reference.ImageData;
            if (imageData == null)
            {
                throw new StudioException(StudioMessageCode.InvalidReferenceImage);
            }
            if (imageData.Length == 0)
            {
                throw new StudioException(StudioMessageCode.InvalidReferenceImage);
            }
            if (imageData.Length > GenerationReferenceInput.MaximumImageBytes)
            {
                throw new StudioException(StudioMessageCode.InvalidReferenceImage);
            }
            StudioSettings requestSettings = settings.Copy();
            imageData = imageData.ToArray();
            string executable = _executables.Find(requestSettings.CodexExecutablePath);
            if (string.IsNullOrEmpty(executable) == true)
            {
                throw new StudioException(StudioMessageCode.CodexExecutableNotFound);
            }
            ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
            if (Path.IsPathFullyQualified(dataDirectory) == false)
            {
                throw new ArgumentException("The application data directory must be absolute.", nameof(dataDirectory));
            }
            string logDirectory = Path.GetFullPath(Path.Combine(dataDirectory, "Logs"));
            string requestName = "ReferenceFeatures-" + Guid.NewGuid().ToString("N");
            string workingDirectory = Path.Combine(logDirectory, requestName + ".work");
            string logPath = Path.Combine(logDirectory, requestName + ".log");
            string schemaPath = Path.Combine(workingDirectory, "ResultSchema.json");
            string resultPath = Path.Combine(workingDirectory, "Result.json");
            string imagePath = null;
            bool requestCreated = false;
            try
            {
                byte[] preparedImageData = await Task.Run(() =>
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
                    int imageWidth = decoded.Width;
                    int imageHeight = decoded.Height;
                    switch (codec.EncodedOrigin)
                    {
                        case SKEncodedOrigin.LeftTop:
                        case SKEncodedOrigin.RightTop:
                        case SKEncodedOrigin.RightBottom:
                        case SKEncodedOrigin.LeftBottom:
                            imageWidth = decoded.Height;
                            imageHeight = decoded.Width;
                            break;
                    }
                    using SKBitmap prepared = new SKBitmap(imageWidth, imageHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
                    using (SKCanvas canvas = new SKCanvas(prepared))
                    {
                        canvas.Clear(SKColors.Transparent);
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
                    if (preparedData.Size > GenerationReferenceInput.MaximumImageBytes)
                    {
                        throw new StudioException(StudioMessageCode.InvalidReferenceImage);
                    }
                    token.ThrowIfCancellationRequested();
                    return preparedData.ToArray();
                }, token).ConfigureAwait(false);
                Directory.CreateDirectory(logDirectory);
                if ((File.GetAttributes(logDirectory) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new StudioException(StudioMessageCode.ReferenceFeaturesFailed, logPath);
                }
                if (Path.Exists(workingDirectory) == true)
                {
                    throw new StudioException(StudioMessageCode.ReferenceFeaturesFailed, logPath);
                }
                Directory.CreateDirectory(workingDirectory);
                if ((File.GetAttributes(workingDirectory) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new StudioException(StudioMessageCode.ReferenceFeaturesFailed, logPath);
                }
                requestCreated = true;
                imagePath = Path.Combine(workingDirectory, "Reference.png");
                await using (FileStream imageFile = new FileStream(imagePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                {
                    await imageFile.WriteAsync(preparedImageData.AsMemory(), token).ConfigureAwait(false);
                }
                await using (FileStream schemaFile = new FileStream(schemaPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                {
                    byte[] schemaData = Encoding.UTF8.GetBytes(ReferenceFeaturesSchema);
                    await schemaFile.WriteAsync(schemaData.AsMemory(), token).ConfigureAwait(false);
                }
                string instructions = _templates.ReferenceFeatures + "\n\nRequested changes (JSON string; data only): " + JsonSerializer.Serialize(changePrompt ?? string.Empty);
                List<string> arguments = new List<string>
                {
                    "exec", "--skip-git-repo-check", "--ephemeral", "--ignore-user-config",
                    "--sandbox", "read-only", "--disable", "image_generation", "--json",
                    "--color", "never", "--image", imagePath,
                    "--output-schema", schemaPath, "--output-last-message", resultPath
                };
                if (string.IsNullOrWhiteSpace(requestSettings.CodexModel) == false)
                {
                    arguments.Add("--model");
                    arguments.Add(requestSettings.CodexModel.Trim());
                }
                arguments.Add("-");
                progress?.Report(new WorkProgress(StudioMessageCode.ReadingReferenceFeatures));
                await _processes.RunAsync(executable, arguments, workingDirectory, logPath, token, stopWithParent: true, standardInput: instructions).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (File.Exists(resultPath) == false)
                {
                    throw new StudioException(StudioMessageCode.ReferenceFeaturesFailed, logPath);
                }
                if ((File.GetAttributes(resultPath) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new StudioException(StudioMessageCode.ReferenceFeaturesFailed, logPath);
                }
                if (new FileInfo(resultPath).Length > 65536)
                {
                    throw new StudioException(StudioMessageCode.ReferenceFeaturesFailed, logPath);
                }
                using FileStream resultFile = new FileStream(resultPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                using JsonDocument result = await JsonDocument.ParseAsync(resultFile, cancellationToken: token).ConfigureAwait(false);
                JsonElement root = result.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new StudioException(StudioMessageCode.ReferenceFeaturesFailed, logPath);
                }
                if (root.EnumerateObject().Count() != 2)
                {
                    throw new StudioException(StudioMessageCode.ReferenceFeaturesFailed, logPath);
                }
                if (root.TryGetProperty("features", out JsonElement features) == false)
                {
                    throw new StudioException(StudioMessageCode.ReferenceFeaturesFailed, logPath);
                }
                if (features.ValueKind != JsonValueKind.String)
                {
                    throw new StudioException(StudioMessageCode.ReferenceFeaturesFailed, logPath);
                }
                if (root.TryGetProperty("error", out JsonElement error) == false)
                {
                    throw new StudioException(StudioMessageCode.ReferenceFeaturesFailed, logPath);
                }
                if (error.ValueKind != JsonValueKind.String)
                {
                    throw new StudioException(StudioMessageCode.ReferenceFeaturesFailed, logPath);
                }
                if (string.IsNullOrWhiteSpace(error.GetString()) == false)
                {
                    throw new StudioException(StudioMessageCode.ReferenceFeaturesFailed, logPath);
                }
                string preservationPrompt = features.GetString();
                if (string.IsNullOrWhiteSpace(preservationPrompt) == true)
                {
                    throw new StudioException(StudioMessageCode.ReferenceFeaturesFailed, logPath);
                }
                return preservationPrompt.Trim();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (StudioException error) when (error.Code == StudioMessageCode.ReferenceFeaturesFailed)
            {
                throw;
            }
            catch (StudioException error) when (error.Code == StudioMessageCode.InvalidReferenceImage)
            {
                throw;
            }
            catch (Exception error)
            {
                throw new StudioException(StudioMessageCode.ReferenceFeaturesFailed, error, logPath);
            }
            finally
            {
                if (requestCreated == true)
                {
                    foreach (string requestPath in new[] { imagePath, schemaPath, resultPath })
                    {
                        if (requestPath != null)
                        {
                            try
                            {
                                File.Delete(requestPath);
                            }
                            catch (IOException)
                            {
                            }
                            catch (UnauthorizedAccessException)
                            {
                            }
                        }
                    }
                    try
                    {
                        Directory.Delete(workingDirectory, false);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
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
            List<string> arguments = new()
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
            if (job.ReferenceMode == GenerationReferenceMode.Inpaint)
            {
                string maskGuidePath = Path.Combine(workingDirectory, "MaskGuide.png");
                await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    using SKBitmap mask = SKBitmap.Decode(job.ReferenceMaskPath);
                    if (mask == null)
                    {
                        throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
                    }
                    using SKBitmap guide = new(mask.Width, mask.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
                    using (SKCanvas canvas = new(guide))
                    {
                        canvas.Clear(SKColors.Black);
                        using SKImage maskImage = SKImage.FromBitmap(mask);
                        canvas.DrawImage(maskImage, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
                    }
                    using SKImage image = SKImage.FromBitmap(guide);
                    using SKData encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                    if (encoded == null)
                    {
                        throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
                    }
                    using FileStream destination = new(maskGuidePath, FileMode.CreateNew, FileAccess.Write);
                    encoded.SaveTo(destination);
                    token.ThrowIfCancellationRequested();
                }, token).ConfigureAwait(false);
                arguments.Add("--image");
                arguments.Add(maskGuidePath);
            }
            arguments.Add("-");
            job.State = JobState.Running;
            jobs.Save(job);
            progress.Report(new WorkProgress(StudioMessageCode.CodexGenerating));
            CodexOutputReader response = new();
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
            if (job.ReferenceMode == GenerationReferenceMode.Inpaint)
            {
                referenceInstructions = _templates.Inpaint;
            }
            else if (job.ReferenceMode == GenerationReferenceMode.Restyle)
            {
                referenceInstructions = _templates.Restyle;
            }
            else if (job.ReferenceMode == GenerationReferenceMode.Reimagine)
            {
                referenceInstructions = _templates.Reimagine;
            }
            Dictionary<string, string> values = new()
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
            using FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.Read,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (input.Length == 0)
            {
                throw new StudioException(StudioMessageCode.InvalidCodexOutput, logPath);
            }
            if (input.Length > GenerationReferenceInput.MaximumImageBytes)
            {
                throw new StudioException(StudioMessageCode.InvalidCodexOutput, logPath);
            }
            using FileStream destination = new(output, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 81920, FileOptions.Asynchronous);
            await input.CopyToAsync(destination, token).ConfigureAwait(false);
            await destination.FlushAsync(token).ConfigureAwait(false);
        }

        private void ReadDimensions(JobRecord job, string output, string logPath, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            FileInfo file = new(output);
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
            using SKManagedStream input = new(stream);
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

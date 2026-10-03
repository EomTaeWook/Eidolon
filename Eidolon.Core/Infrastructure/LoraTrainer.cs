using Eidolon.Core.Application;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Eidolon.Core.Domain;

namespace Eidolon.Core.Infrastructure
{
    public class LoraTrainer
    {
        private readonly ProcessRunner _runner;
        private readonly RuntimeInstaller _installer;
        private readonly JobStore _jobs;
        private readonly TimeProvider _time;

        public LoraTrainer(ProcessRunner runner, RuntimeInstaller installer, JobStore jobs, TimeProvider time)
        {
            _runner = runner;
            _installer = installer;
            _jobs = jobs;
            _time = time;
        }

        public async Task<string> TrainAsync(StudioSettings settings, JobRecord job, string imageDirectory,
            IProgress<WorkProgress> progress, CancellationToken cancellationToken)
        {
            RuntimeLayout layout = new RuntimeLayout(settings.InstallDirectory);
            layout.EnsureInstalled(true);
            if (settings.UseCpu == true)
            {
                throw new StudioException(StudioMessageCode.TrainingGpuRequired);
            }
            string modelPath = layout.AssetPath(job.Model);
            if (File.Exists(modelPath) == false)
            {
                throw new StudioException(StudioMessageCode.TrainingModelMissing);
            }
            string jobDirectory = _jobs.DirectoryFor(job.Id);
            string datasetDirectory = Path.Combine(jobDirectory, "Dataset");
            Directory.CreateDirectory(datasetDirectory);
            string[] extensions = { ".png", ".jpg", ".jpeg", ".bmp" };
            List<string> images = Directory.EnumerateFiles(imageDirectory, "*", SearchOption.AllDirectories)
                .Where(path => extensions.Contains(Path.GetExtension(path).ToLowerInvariant())).OrderBy(path => path).ToList();
            if (images.Count == 0)
            {
                throw new StudioException(StudioMessageCode.TrainingImagesMissing);
            }
            int index = 0;
            foreach (string image in images)
            {
                cancellationToken.ThrowIfCancellationRequested();
                index++;
                string stem = index.ToString("D6", CultureInfo.InvariantCulture);
                string target = Path.Combine(datasetDirectory, stem + Path.GetExtension(image).ToLowerInvariant());
                progress.Report(new WorkProgress(StudioMessageCode.PreparingDataset, index * 100.0 / images.Count, false, index, images.Count));
                await using (FileStream input = File.OpenRead(image))
                await using (FileStream output = File.Create(target))
                {
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                }
                string caption = job.DatasetDescription;
                string captionPath = Path.ChangeExtension(image, ".txt");
                if (File.Exists(captionPath) == true)
                {
                    caption = await File.ReadAllTextAsync(captionPath, cancellationToken).ConfigureAwait(false);
                }
                caption = caption.Trim();
                string combined = job.TriggerWord;
                if (string.IsNullOrWhiteSpace(caption) == false)
                {
                    bool hasTrigger = caption.Equals(job.TriggerWord, StringComparison.OrdinalIgnoreCase) ||
                        caption.StartsWith(job.TriggerWord + ",", StringComparison.OrdinalIgnoreCase) ||
                        caption.StartsWith(job.TriggerWord + " ", StringComparison.OrdinalIgnoreCase);
                    if (hasTrigger == false)
                    {
                        combined += ", " + caption;
                    }
                    else
                    {
                        combined = caption;
                    }
                }
                await File.WriteAllTextAsync(Path.Combine(datasetDirectory, stem + ".txt"), combined,
                    new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            }
            job.DatasetImageCount = images.Count;
            _jobs.Save(job);
            GenerationPreset imagePreset = new GenerationPreset(job.Model.Family);
            string datasetConfig = "[general]\ncaption_extension = \".txt\"\nkeep_tokens = 1\n" +
                "[[datasets]]\nresolution = " + imagePreset.Resolution.ToString(CultureInfo.InvariantCulture) +
                "\nbatch_size = 1\nenable_bucket = true\nbucket_no_upscale = true\n" +
                "[[datasets.subsets]]\nimage_dir = " + QuoteToml(datasetDirectory) +
                "\nnum_repeats = " + TrainingPreset.Repeats.ToString(CultureInfo.InvariantCulture) + "\n";
            string configPath = Path.Combine(jobDirectory, "Dataset.toml");
            await File.WriteAllTextAsync(configPath, datasetConfig, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            string logPath = Path.Combine(jobDirectory, "Training.log");
            progress.Report(new WorkProgress(StudioMessageCode.CheckingTrainingEnvironment));
            const string validateCode = "import sys,torch; from pathlib import Path; from PIL import Image; " +
                "assert torch.cuda.is_available(), 'NVIDIA GPU is not available: check the driver and GPU installation'; " +
                "paths=[p for p in Path(sys.argv[1]).iterdir() if p.suffix.lower() in ('.png','.jpg','.jpeg','.bmp')]; " +
                "[Image.open(p).verify() for p in paths]; print('Validated images:',len(paths))";
            await _runner.RunAsync(layout.TrainingPython, new[] { "-c", validateCode, datasetDirectory },
                layout.TrainingDirectory, logPath, cancellationToken, _installer.EnvironmentFor(layout)).ConfigureAwait(false);
            string script = "train_network.py";
            if (job.Model.Family == ModelFamily.Sdxl)
            {
                script = "sdxl_train_network.py";
            }
            string outputName = "Eidolon-" + job.Id.Substring(0, 8);
            string outputDirectory = Path.Combine(jobDirectory, "Output");
            List<string> arguments = new List<string>
            {
                "-u", "-m", "accelerate.commands.launch", "--num_processes", "1", "--num_machines", "1",
                "--mixed_precision", TrainingPreset.Precision, "--dynamo_backend", "no", "--num_cpu_threads_per_process", "2",
                script, "--pretrained_model_name_or_path", modelPath,
                "--dataset_config", configPath, "--output_dir", outputDirectory, "--output_name", outputName,
                "--save_model_as", "safetensors", "--network_module", "networks.lora",
                "--network_dim", TrainingPreset.NetworkDimension.ToString(CultureInfo.InvariantCulture),
                "--network_alpha", TrainingPreset.NetworkAlpha.ToString(CultureInfo.InvariantCulture),
                "--max_train_steps", TrainingPreset.MaxSteps.ToString(CultureInfo.InvariantCulture),
                "--learning_rate", TrainingPreset.LearningRate, "--optimizer_type", TrainingPreset.Optimizer,
                "--mixed_precision", TrainingPreset.Precision, "--save_precision", TrainingPreset.Precision,
                "--seed", job.Seed.ToString(CultureInfo.InvariantCulture), "--cache_latents", "--gradient_checkpointing",
                "--network_train_unet_only", "--max_data_loader_n_workers", "0", "--sdpa"
            };
            if (job.Model.Family == ModelFamily.Sdxl)
            {
                arguments.Add("--no_half_vae");
                arguments.Add("--cache_text_encoder_outputs");
            }
            await File.WriteAllTextAsync(Path.Combine(jobDirectory, "TrainingArguments.json"),
                System.Text.Json.JsonSerializer.Serialize(arguments, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
                cancellationToken).ConfigureAwait(false);
            job.State = JobState.Running;
            _jobs.Save(job);
            if (job.Loras.Count > 0)
            {
                arguments.Add("--network_weights");
                arguments.Add(layout.AssetPath(job.Loras[0]));
                arguments.Add("--dim_from_weights");
            }
            progress.Report(new WorkProgress(StudioMessageCode.TrainingStarting));
            DateTimeOffset lastReportAtUtc = DateTimeOffset.MinValue;
            bool hasStepProgress = false;
            object reportGate = new object();
            await _runner.RunAsync(layout.TrainingPython, arguments, layout.TrainingDirectory,
                logPath, cancellationToken, _installer.EnvironmentFor(layout), line =>
                {
                    lock (reportGate)
                    {
                        DateTimeOffset now = _time.GetUtcNow();
                        if ((now - lastReportAtUtc).TotalMilliseconds < 250)
                        {
                            return;
                        }
                        Match match = Regex.Match(line, @"(?<step>\d+)/(?<total>\d+)\s*\[");
                        if (match.Success == true)
                        {
                            int step = int.Parse(match.Groups["step"].Value, CultureInfo.InvariantCulture);
                            int total = int.Parse(match.Groups["total"].Value, CultureInfo.InvariantCulture);
                            if (total == TrainingPreset.MaxSteps)
                            {
                                hasStepProgress = true;
                                progress.Report(new WorkProgress(StudioMessageCode.TrainingProgress, step * 100.0 / total, false, step, total));
                                lastReportAtUtc = now;
                                return;
                            }
                        }
                        if (hasStepProgress == false)
                        {
                            progress.Report(new WorkProgress(StudioMessageCode.TrainingRunning));
                            lastReportAtUtc = now;
                        }
                    }
                }).ConfigureAwait(false);
            string trainedPath = Path.Combine(outputDirectory, outputName + ".safetensors");
            if (File.Exists(trainedPath) == false)
            {
                throw new StudioException(StudioMessageCode.TrainingOutputMissing);
            }
            return trainedPath;
        }

        private string QuoteToml(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
        }
    }
}

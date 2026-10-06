using Eidolon.Core.Domain;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace Eidolon.Core.Infrastructure
{
    public class BackgroundRemovalService
    {
        private const int InputSize = 1024;
        private const float MinimumForegroundConfidence = 0.5f;
        private const string ModelUrl = "https://github.com/danielgatis/rembg/releases/download/v0.0.0/isnet-general-use.onnx";
        private const string ModelMd5 = "fc16ebd8b0c10d971d3513d564d01e29";
        private readonly FileDownloader _downloader;
        private readonly string _modelDirectory;

        public BackgroundRemovalService(FileDownloader downloader, string modelDirectory)
        {
            _downloader = downloader;
            _modelDirectory = modelDirectory;
        }

        public async Task RemoveAsync(string source, string destination, IProgress<WorkProgress> progress,
            CancellationToken token)
        {
            if (File.Exists(source) == false)
            {
                throw new StudioException(StudioMessageCode.GenerationOutputMissing);
            }
            string modelPath = Path.Combine(_modelDirectory, "isnet-general-use.onnx");
            progress.Report(new WorkProgress(StudioMessageCode.BackgroundModelDownloading));
            await _downloader.DownloadAsync(ModelUrl, modelPath, "md5:" + ModelMd5, progress, token).ConfigureAwait(false);
            await _downloader.DownloadAsync("https://raw.githubusercontent.com/xuebinqin/DIS/master/README.md",
                Path.Combine(_modelDirectory, "NOTICE-ISNet.md"), string.Empty, progress, token).ConfigureAwait(false);
            await Task.Run(() => ProcessImage(source, destination, modelPath, progress, token), token).ConfigureAwait(false);
        }

        private void ProcessImage(string source, string destination, string modelPath,
            IProgress<WorkProgress> progress, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            progress.Report(new WorkProgress(StudioMessageCode.BackgroundRemoving, 10, false));
            using SKBitmap original = SKBitmap.Decode(source);
            if (original == null)
            {
                throw new StudioException(StudioMessageCode.BackgroundRemovalFailed);
            }
            using SKBitmap resized = original.Resize(new SKImageInfo(InputSize, InputSize, SKColorType.Rgba8888, SKAlphaType.Unpremul),
                new SKSamplingOptions(SKFilterMode.Linear));
            if (resized == null)
            {
                throw new StudioException(StudioMessageCode.BackgroundRemovalFailed);
            }
            DenseTensor<float> input = PrepareInput(resized.Pixels);
            token.ThrowIfCancellationRequested();
            using SessionOptions options = new SessionOptions();
            using InferenceSession session = new InferenceSession(modelPath, options);
            using RunOptions runOptions = new RunOptions();
            using CancellationTokenRegistration cancellation = token.Register(() => runOptions.Terminate = true);
            NamedOnnxValue[] inputs = { NamedOnnxValue.CreateFromTensor(session.InputMetadata.Keys.First(), input) };
            float[] prediction;
            try
            {
                using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = session.Run(inputs,
                    new[] { session.OutputMetadata.Keys.First() }, runOptions);
                prediction = outputs.First().AsTensor<float>().ToArray();
            }
            catch (OnnxRuntimeException) when (token.IsCancellationRequested == true)
            {
                throw new OperationCanceledException(token);
            }
            catch (OnnxRuntimeException error)
            {
                throw new StudioException(StudioMessageCode.BackgroundRemovalFailed, error);
            }
            token.ThrowIfCancellationRequested();
            progress.Report(new WorkProgress(StudioMessageCode.BackgroundRemoving, 75, false));
            using SKBitmap mask = CreateMask(prediction);
            using SKBitmap fullMask = mask.Resize(new SKImageInfo(original.Width, original.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul),
                new SKSamplingOptions(SKFilterMode.Linear));
            if (fullMask == null)
            {
                throw new StudioException(StudioMessageCode.BackgroundRemovalFailed);
            }
            SKColor[] colors = original.Pixels;
            SKColor[] alpha = fullMask.Pixels;
            for (int index = 0; index < colors.Length; index++)
            {
                if (index % original.Width == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                SKColor color = colors[index];
                byte opacity = (byte)(color.Alpha * alpha[index].Red / 255);
                colors[index] = new SKColor(color.Red, color.Green, color.Blue, opacity);
            }
            using SKBitmap result = new SKBitmap(original.Width, original.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            result.Pixels = colors;
            using SKData png = result.Encode(SKEncodedImageFormat.Png, 100);
            if (png == null)
            {
                throw new StudioException(StudioMessageCode.BackgroundRemovalFailed);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                progress.Report(new WorkProgress(StudioMessageCode.BackgroundRemoving, 90, false));
                using (FileStream output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                {
                    png.SaveTo(output);
                    output.Flush(true);
                }
                token.ThrowIfCancellationRequested();
                File.Move(temporary, destination);
            }
            finally
            {
                if (File.Exists(temporary) == true)
                {
                    File.Delete(temporary);
                }
            }
        }

        private DenseTensor<float> PrepareInput(SKColor[] pixels)
        {
            float maximum = 1;
            foreach (SKColor color in pixels)
            {
                maximum = Math.Max(maximum, Math.Max(color.Red, Math.Max(color.Green, color.Blue)));
            }
            DenseTensor<float> tensor = new DenseTensor<float>(new[] { 1, 3, InputSize, InputSize });
            for (int y = 0; y < InputSize; y++)
            {
                for (int x = 0; x < InputSize; x++)
                {
                    SKColor color = pixels[y * InputSize + x];
                    tensor[0, 0, y, x] = color.Red / maximum - 0.5f;
                    tensor[0, 1, y, x] = color.Green / maximum - 0.5f;
                    tensor[0, 2, y, x] = color.Blue / maximum - 0.5f;
                }
            }
            return tensor;
        }

        private SKBitmap CreateMask(float[] prediction)
        {
            if (prediction.Length != InputSize * InputSize || prediction.Any(value => float.IsFinite(value) == false) == true)
            {
                throw new StudioException(StudioMessageCode.BackgroundRemovalFailed);
            }
            float minimum = prediction.Min();
            float maximum = prediction.Max();
            float range = maximum - minimum;
            if (maximum < MinimumForegroundConfidence || range <= 0)
            {
                throw new StudioException(StudioMessageCode.BackgroundSubjectNotFound);
            }
            SKColor[] colors = new SKColor[prediction.Length];
            for (int index = 0; index < prediction.Length; index++)
            {
                float value = (prediction[index] - minimum) / range;
                byte opacity = (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
                colors[index] = new SKColor(opacity, opacity, opacity, 255);
            }
            SKBitmap mask = new SKBitmap(InputSize, InputSize, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            mask.Pixels = colors;
            return mask;
        }
    }
}

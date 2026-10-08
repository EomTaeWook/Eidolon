using Eidolon.Core.Domain;
using SkiaSharp;
using System.Globalization;

namespace Eidolon.Core.Infrastructure
{
    public class AssetSheetExporter
    {
        private readonly JobStore _jobs;

        public AssetSheetExporter(JobStore jobs)
        {
            _jobs = jobs;
        }

        public string Export(AssetCollection collection, string parentDirectory, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(parentDirectory) == true || Path.IsPathFullyQualified(parentDirectory) == false)
            {
                throw new StudioException(StudioMessageCode.InvalidGenerationDirectory);
            }
            if (collection.Frames.Any(frame => string.IsNullOrWhiteSpace(frame.ImagePath) == true || File.Exists(frame.ImagePath) == false) == true)
            {
                throw new StudioException(StudioMessageCode.AssetExportIncomplete);
            }
            if (collection.FrameWidth < 16 || collection.FrameWidth > AssetCreationInput.MaximumFrameSize
                || collection.FrameHeight < 16 || collection.FrameHeight > AssetCreationInput.MaximumFrameSize
                || collection.Columns < 1 || collection.Columns > AssetCreationInput.MaximumFrames
                || collection.FramesPerSecond < 1 || collection.FramesPerSecond > 60)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            int columns = Math.Min(collection.Columns, collection.Frames.Count);
            int rows = (collection.Frames.Count + columns - 1) / columns;
            int width = collection.FrameWidth * columns;
            int height = collection.FrameHeight * rows;
            if ((long)width * height > AssetCreationInput.MaximumSheetPixels)
            {
                throw new StudioException(StudioMessageCode.AssetSheetTooLarge);
            }
            List<SKRectI> bounds = new List<SKRectI>();
            List<int> canvasSizes = new List<int>();
            float maximumWidth = 0;
            float maximumHeight = 0;
            foreach (AssetFrame frame in collection.Frames)
            {
                token.ThrowIfCancellationRequested();
                using SKBitmap bitmap = Decode(frame.ImagePath);
                SKRectI subject = SubjectBounds(bitmap, token);
                bounds.Add(subject);
                int canvasSize = Math.Max(bitmap.Width, bitmap.Height);
                canvasSizes.Add(canvasSize);
                maximumWidth = Math.Max(maximumWidth, (float)subject.Width / canvasSize);
                maximumHeight = Math.Max(maximumHeight, (float)subject.Height / canvasSize);
            }
            float scale = Math.Min((float)collection.FrameWidth / maximumWidth, (float)collection.FrameHeight / maximumHeight);
            SKSamplingOptions sampling = new SKSamplingOptions(SKFilterMode.Linear);
            if (collection.PixelArt == true)
            {
                sampling = new SKSamplingOptions(SKFilterMode.Nearest);
            }
            string directory = Path.Combine(Path.GetFullPath(parentDirectory), "Eidolon-Assets-" + collection.Id.Substring(0, 8)
                + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(directory, "Frames"));
            Directory.CreateDirectory(Path.Combine(directory, "Sources"));
            AssetSheetManifest manifest = new AssetSheetManifest
            {
                CollectionId = collection.Id, Kind = collection.Kind, Width = width, Height = height,
                FrameWidth = collection.FrameWidth, FrameHeight = collection.FrameHeight,
                FramesPerSecond = collection.FramesPerSecond,
                Loop = collection.Kind == AssetCreationKind.SpriteAnimation, PixelArt = collection.PixelArt
            };
            using SKBitmap sheet = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using SKCanvas sheetCanvas = new SKCanvas(sheet);
            sheetCanvas.Clear(SKColors.Transparent);
            for (int index = 0; index < collection.Frames.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                AssetFrame frame = collection.Frames[index];
                using SKBitmap original = Decode(frame.ImagePath);
                using SKImage source = SKImage.FromBitmap(original);
                SKRectI subject = bounds[index];
                float frameWidth = (float)subject.Width / canvasSizes[index] * scale;
                float frameHeight = (float)subject.Height / canvasSizes[index] * scale;
                SKRect destination = SKRect.Create((collection.FrameWidth - frameWidth) / 2,
                    collection.FrameHeight - frameHeight, frameWidth, frameHeight);
                using SKBitmap normalized = new SKBitmap(collection.FrameWidth, collection.FrameHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
                using (SKCanvas canvas = new SKCanvas(normalized))
                {
                    canvas.Clear(SKColors.Transparent);
                    canvas.DrawImage(source, new SKRect(subject.Left, subject.Top, subject.Right, subject.Bottom), destination, sampling, null);
                }
                string number = (index + 1).ToString("D3", CultureInfo.InvariantCulture);
                string file = "Frames/" + number + ".png";
                SavePng(normalized, Path.Combine(directory, "Frames", number + ".png"));
                int x = index % columns * collection.FrameWidth;
                int y = index / columns * collection.FrameHeight;
                using SKImage cell = SKImage.FromBitmap(normalized);
                sheetCanvas.DrawImage(cell, x, y);
                File.Copy(frame.ImagePath, Path.Combine(directory, "Sources", number + ".png"), false);
                string metadata = Path.ChangeExtension(frame.ImagePath, ".json");
                if (File.Exists(metadata) == true)
                {
                    File.Copy(metadata, Path.Combine(directory, "Sources", number + ".json"), false);
                }
                manifest.Frames.Add(new AssetSheetFrame
                {
                    Number = index + 1, Label = frame.Label, File = file, X = x, Y = y,
                    Width = collection.FrameWidth, Height = collection.FrameHeight,
                    SourceJobId = frame.SourceJobId, Seed = frame.ImageSeed
                });
            }
            token.ThrowIfCancellationRequested();
            SavePng(sheet, Path.Combine(directory, "Sheet.png"));
            _jobs.WriteAssetSheetManifest(Path.Combine(directory, "Sheet.json"), manifest);
            return directory;
        }

        private SKBitmap Decode(string path)
        {
            using FileStream stream = File.OpenRead(path);
            if (stream.Length < 1 || stream.Length > GenerationReferenceInput.MaximumImageBytes)
            {
                throw new StudioException(StudioMessageCode.InvalidReferenceImage);
            }
            using SKManagedStream managed = new SKManagedStream(stream);
            using SKCodec codec = SKCodec.Create(managed);
            if (codec == null || codec.Info.Width < 1 || codec.Info.Height < 1
                || (long)codec.Info.Width * codec.Info.Height > GenerationReferenceInput.MaximumImagePixels)
            {
                throw new StudioException(StudioMessageCode.InvalidReferenceImage);
            }
            SKBitmap bitmap = SKBitmap.Decode(codec);
            if (bitmap == null)
            {
                throw new StudioException(StudioMessageCode.InvalidReferenceImage);
            }
            return bitmap;
        }

        private SKRectI SubjectBounds(SKBitmap bitmap, CancellationToken token)
        {
            int left = bitmap.Width;
            int top = bitmap.Height;
            int right = 0;
            int bottom = 0;
            for (int y = 0; y < bitmap.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x < bitmap.Width; x++)
                {
                    if (bitmap.GetPixel(x, y).Alpha > 16)
                    {
                        left = Math.Min(left, x);
                        right = Math.Max(right, x + 1);
                        top = Math.Min(top, y);
                        bottom = Math.Max(bottom, y + 1);
                    }
                }
            }
            if (right <= left || bottom <= top)
            {
                throw new StudioException(StudioMessageCode.BackgroundSubjectNotFound);
            }
            return new SKRectI(left, top, right, bottom);
        }

        private void SavePng(SKBitmap bitmap, string path)
        {
            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            if (data == null)
            {
                throw new StudioException(StudioMessageCode.AssetCreationFailed);
            }
            using FileStream output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            data.SaveTo(output);
            output.Flush(true);
        }
    }
}

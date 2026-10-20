using Eidolon.Core.Domain;
using SkiaSharp;
using System.Globalization;

namespace Eidolon.Core.Infrastructure
{
    public class AssetSheetExporter
    {
        private readonly JobStore _jobs;
        private readonly AssetFrameRenderer _renderer;

        public AssetSheetExporter(JobStore jobs, AssetFrameRenderer renderer)
        {
            _jobs = jobs;
            _renderer = renderer;
        }

        public string Export(AssetCollection collection, string parentDirectory, CancellationToken cancellationToken)
        {
            if (collection.Kind == AssetCreationKind.SpriteAnimation)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (string.IsNullOrWhiteSpace(parentDirectory) == true)
            {
                throw new StudioException(StudioMessageCode.InvalidGenerationDirectory);
            }
            if (Path.IsPathFullyQualified(parentDirectory) == false)
            {
                throw new StudioException(StudioMessageCode.InvalidGenerationDirectory);
            }
            foreach (AssetFrame frame in collection.Frames)
            {
                if (string.IsNullOrWhiteSpace(frame.ImagePath) == true)
                {
                    throw new StudioException(StudioMessageCode.AssetExportIncomplete);
                }
                if (File.Exists(frame.ImagePath) == false)
                {
                    throw new StudioException(StudioMessageCode.AssetExportIncomplete);
                }
            }
            if (collection.FrameWidth < 16)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (collection.FrameWidth > AssetCreationInput.MaximumFrameSize)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (collection.FrameHeight < 16)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (collection.FrameHeight > AssetCreationInput.MaximumFrameSize)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (collection.Columns < 1)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (collection.Columns > AssetCreationInput.MaximumFrames)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            int columnCount = Math.Min(collection.Columns, collection.Frames.Count);
            int rowCount = (collection.Frames.Count + columnCount - 1) / columnCount;
            int sheetWidth = collection.FrameWidth * columnCount;
            int sheetHeight = collection.FrameHeight * rowCount;
            if ((long)sheetWidth * sheetHeight > AssetCreationInput.MaximumSheetPixels)
            {
                throw new StudioException(StudioMessageCode.AssetSheetTooLarge);
            }
            List<SKRectI> subjectBounds = new List<SKRectI>();
            List<int> sourceCanvasSizes = new List<int>();
            float maximumWidth = 0;
            float maximumHeight = 0;
            foreach (AssetFrame frame in collection.Frames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using SKBitmap bitmap = Decode(frame.ImagePath);
                SKRectI subject = _renderer.MeasureSubjectBounds(bitmap, cancellationToken);
                subjectBounds.Add(subject);
                int canvasSize = Math.Max(bitmap.Width, bitmap.Height);
                sourceCanvasSizes.Add(canvasSize);
                maximumWidth = Math.Max(maximumWidth, (float)subject.Width / canvasSize);
                maximumHeight = Math.Max(maximumHeight, (float)subject.Height / canvasSize);
            }
            float scale = Math.Min((float)collection.FrameWidth / maximumWidth, (float)collection.FrameHeight / maximumHeight);
            SKSamplingOptions samplingOptions = new SKSamplingOptions(SKFilterMode.Linear);
            if (collection.PixelArt == true)
            {
                samplingOptions = new SKSamplingOptions(SKFilterMode.Nearest);
            }
            string exportDirectory = Path.Combine(Path.GetFullPath(parentDirectory), "Eidolon-Assets-" + collection.Id.Substring(0, 8) + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(exportDirectory, "Frames"));
            Directory.CreateDirectory(Path.Combine(exportDirectory, "Sources"));
            AssetSheetManifest manifest = new AssetSheetManifest
            {
                CollectionId = collection.Id, Kind = collection.Kind, Width = sheetWidth, Height = sheetHeight,
                FrameWidth = collection.FrameWidth, FrameHeight = collection.FrameHeight,
                Loop = false, PixelArt = collection.PixelArt
            };
            using SKBitmap sheet = new SKBitmap(sheetWidth, sheetHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            using SKCanvas sheetCanvas = new SKCanvas(sheet);
            sheetCanvas.Clear(SKColors.Transparent);
            for (int frameIndex = 0; frameIndex < collection.Frames.Count; frameIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AssetFrame frame = collection.Frames[frameIndex];
                using SKBitmap original = Decode(frame.ImagePath);
                SKRectI subject = subjectBounds[frameIndex];
                using SKBitmap frameCanvas = new SKBitmap(collection.FrameWidth, collection.FrameHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
                float frameWidth = (float)subject.Width / sourceCanvasSizes[frameIndex] * scale;
                float frameHeight = (float)subject.Height / sourceCanvasSizes[frameIndex] * scale;
                SKRect destination = SKRect.Create((collection.FrameWidth - frameWidth) / 2, collection.FrameHeight - frameHeight, frameWidth, frameHeight);
                using (SKImage source = SKImage.FromBitmap(original))
                using (SKCanvas canvas = new SKCanvas(frameCanvas))
                {
                    canvas.Clear(SKColors.Transparent);
                    canvas.DrawImage(source, new SKRect(subject.Left, subject.Top, subject.Right, subject.Bottom), destination, samplingOptions, null);
                }
                string frameNumberText = (frameIndex + 1).ToString("D3", CultureInfo.InvariantCulture);
                string frameFile = "Frames/" + frameNumberText + ".png";
                SavePng(frameCanvas, Path.Combine(exportDirectory, "Frames", frameNumberText + ".png"));
                int frameX = frameIndex % columnCount * collection.FrameWidth;
                int frameY = frameIndex / columnCount * collection.FrameHeight;
                using SKImage cell = SKImage.FromBitmap(frameCanvas);
                sheetCanvas.DrawImage(cell, frameX, frameY);
                File.Copy(frame.ImagePath, Path.Combine(exportDirectory, "Sources", frameNumberText + ".png"), false);
                string sourceMetadataPath = Path.ChangeExtension(frame.ImagePath, ".json");
                if (File.Exists(sourceMetadataPath) == true)
                {
                    File.Copy(sourceMetadataPath, Path.Combine(exportDirectory, "Sources", frameNumberText + ".json"), false);
                }
                manifest.Frames.Add(new AssetSheetFrame
                {
                    Number = frameIndex + 1, Label = frame.Label, File = frameFile, X = frameX, Y = frameY,
                    Width = collection.FrameWidth, Height = collection.FrameHeight,
                    SourceJobId = frame.SourceJobId, Seed = frame.ImageSeed
                });
            }
            cancellationToken.ThrowIfCancellationRequested();
            SavePng(sheet, Path.Combine(exportDirectory, "Sheet.png"));
            _jobs.WriteAssetSheetManifest(Path.Combine(exportDirectory, "Sheet.json"), manifest);
            return exportDirectory;
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

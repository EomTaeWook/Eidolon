using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Eidolon.Core.Domain;
using SkiaSharp;

namespace Eidolon.App.Views
{
    public class ReferenceMaskEditor : Control
    {
        private const int MaximumPreviewSize = 1024;
        public static readonly StyledProperty<byte[]> ImageDataProperty = AvaloniaProperty.Register<ReferenceMaskEditor, byte[]>(nameof(ImageData), Array.Empty<byte>());
        public static readonly StyledProperty<byte[]> MaskDataProperty = AvaloniaProperty.Register<ReferenceMaskEditor, byte[]>(nameof(MaskData), Array.Empty<byte>());
        public static readonly StyledProperty<double> BrushSizeProperty = AvaloniaProperty.Register<ReferenceMaskEditor, double>(nameof(BrushSize), 32);
        public static readonly StyledProperty<bool> IsErasingProperty = AvaloniaProperty.Register<ReferenceMaskEditor, bool>(nameof(IsErasing));
        public static readonly StyledProperty<IBrush> SelectionBrushProperty = AvaloniaProperty.Register<ReferenceMaskEditor, IBrush>(nameof(SelectionBrush), Brushes.Transparent);
        private Bitmap _referencePreview;
        private Bitmap _maskPreview;
        private SKBitmap _mask;
        private int _originalWidth;
        private int _originalHeight;
        private SKEncodedOrigin _origin;
        private bool _drawing;
        private bool _hasPreviousPoint;
        private bool _publishingMask;
        private SKPoint _previousPoint;
        private IPointer _capturedPointer;

        public ReferenceMaskEditor()
        {
            ClipToBounds = true;
            Cursor = new Cursor(StandardCursorType.Cross);
        }

        public byte[] ImageData
        {
            get
            {
                return GetValue(ImageDataProperty);
            }
            set
            {
                SetValue(ImageDataProperty, value);
            }
        }

        public byte[] MaskData
        {
            get
            {
                return GetValue(MaskDataProperty);
            }
            set
            {
                SetValue(MaskDataProperty, value);
            }
        }

        public double BrushSize
        {
            get
            {
                return GetValue(BrushSizeProperty);
            }
            set
            {
                SetValue(BrushSizeProperty, value);
            }
        }

        public bool IsErasing
        {
            get
            {
                return GetValue(IsErasingProperty);
            }
            set
            {
                SetValue(IsErasingProperty, value);
            }
        }

        public IBrush SelectionBrush
        {
            get
            {
                return GetValue(SelectionBrushProperty);
            }
            set
            {
                SetValue(SelectionBrushProperty, value);
            }
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == ImageDataProperty)
            {
                LoadImage();
            }
            if (change.Property == MaskDataProperty)
            {
                if (_publishingMask == false)
                {
                    LoadMask();
                }
            }
            InvalidateVisual();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs eventArguments)
        {
            base.OnAttachedToVisualTree(eventArguments);
            LoadImage();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs eventArguments)
        {
            if (_drawing == true)
            {
                _drawing = false;
                PublishMask();
            }
            DisposeImages();
            base.OnDetachedFromVisualTree(eventArguments);
        }

        private void DisposeImages()
        {
            _drawing = false;
            IPointer capturedPointer = _capturedPointer;
            _capturedPointer = null;
            capturedPointer?.Capture(null);
            _referencePreview?.Dispose();
            _referencePreview = null;
            _maskPreview?.Dispose();
            _maskPreview = null;
            _mask?.Dispose();
            _mask = null;
            _drawing = false;
            _hasPreviousPoint = false;
        }

        private void LoadImage()
        {
            DisposeImages();
            if (ImageData.Length == 0)
            {
                return;
            }
            using SKMemoryStream input = new(ImageData);
            using SKCodec codec = SKCodec.Create(input);
            if (codec == null)
            {
                return;
            }
            if (codec.Info.Width < 1)
            {
                return;
            }
            if (codec.Info.Height < 1)
            {
                return;
            }
            if ((long)codec.Info.Width * codec.Info.Height > GenerationReferenceInput.MaximumImagePixels)
            {
                return;
            }
            _originalWidth = codec.Info.Width;
            _originalHeight = codec.Info.Height;
            _origin = codec.EncodedOrigin;
            using SKBitmap decoded = SKBitmap.Decode(codec);
            if (decoded == null)
            {
                return;
            }
            double scale = Math.Min(1, (double)MaximumPreviewSize / Math.Max(decoded.Width, decoded.Height));
            int width = Math.Max(1, (int)Math.Round(decoded.Width * scale));
            int height = Math.Max(1, (int)Math.Round(decoded.Height * scale));
            using SKBitmap preview = decoded.Resize(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul), new SKSamplingOptions(SKFilterMode.Linear));
            if (preview == null)
            {
                return;
            }
            _referencePreview = CreatePreview(preview);
            _mask = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            LoadMask();
        }

        private void LoadMask()
        {
            if (_mask == null)
            {
                return;
            }
            using (SKCanvas canvas = new(_mask))
            {
                canvas.Clear(SKColors.Transparent);
                if (MaskData.Length > 0)
                {
                    using SKMemoryStream input = new(MaskData);
                    using SKCodec codec = SKCodec.Create(input);
                    if (codec != null)
                    {
                        if ((long)codec.Info.Width * codec.Info.Height <= GenerationReferenceInput.MaximumImagePixels)
                        {
                            using SKBitmap mask = SKBitmap.Decode(codec);
                            if (mask != null)
                            {
                                using SKImage image = SKImage.FromBitmap(mask);
                                canvas.DrawImage(image, new SKRect(0, 0, _mask.Width, _mask.Height), new SKSamplingOptions(SKFilterMode.Linear));
                            }
                        }
                    }
                }
            }
            UpdateMaskPreview();
        }

        private Bitmap CreatePreview(SKBitmap bitmap)
        {
            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            using Stream stream = encoded.AsStream();
            return new Bitmap(stream);
        }

        private void UpdateMaskPreview()
        {
            Bitmap previous = _maskPreview;
            _maskPreview = CreatePreview(_mask);
            previous?.Dispose();
            InvalidateVisual();
        }

        private Matrix ImageTransform(out double scale)
        {
            Matrix orientation = Matrix.Identity;
            int orientedWidth = _originalWidth;
            int orientedHeight = _originalHeight;
            switch (_origin)
            {
                case SKEncodedOrigin.TopRight:
                    orientation = new Matrix(-1, 0, 0, 1, _originalWidth, 0);
                    break;
                case SKEncodedOrigin.BottomRight:
                    orientation = new Matrix(-1, 0, 0, -1, _originalWidth, _originalHeight);
                    break;
                case SKEncodedOrigin.BottomLeft:
                    orientation = new Matrix(1, 0, 0, -1, 0, _originalHeight);
                    break;
                case SKEncodedOrigin.LeftTop:
                    orientation = new Matrix(0, 1, 1, 0, 0, 0);
                    orientedWidth = _originalHeight;
                    orientedHeight = _originalWidth;
                    break;
                case SKEncodedOrigin.RightTop:
                    orientation = new Matrix(0, 1, -1, 0, _originalHeight, 0);
                    orientedWidth = _originalHeight;
                    orientedHeight = _originalWidth;
                    break;
                case SKEncodedOrigin.RightBottom:
                    orientation = new Matrix(0, -1, -1, 0, _originalHeight, _originalWidth);
                    orientedWidth = _originalHeight;
                    orientedHeight = _originalWidth;
                    break;
                case SKEncodedOrigin.LeftBottom:
                    orientation = new Matrix(0, -1, 1, 0, 0, _originalWidth);
                    orientedWidth = _originalHeight;
                    orientedHeight = _originalWidth;
                    break;
            }
            scale = Math.Min(Bounds.Width / orientedWidth, Bounds.Height / orientedHeight);
            double horizontalOffset = (Bounds.Width - orientedWidth * scale) / 2;
            double verticalOffset = (Bounds.Height - orientedHeight * scale) / 2;
            return orientation * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(horizontalOffset, verticalOffset);
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            if (_referencePreview == null)
            {
                return;
            }
            using (context.PushTransform(ImageTransform(out _)))
            {
                Rect source = new(_referencePreview.Size);
                Rect destination = new(0, 0, _originalWidth, _originalHeight);
                context.DrawImage(_referencePreview, source, destination);
                if (_maskPreview != null)
                {
                    using (context.PushOpacity(0.5))
                    using (context.PushOpacityMask(new ImageBrush(_maskPreview) { Stretch = Stretch.Fill }, destination))
                    {
                        context.FillRectangle(SelectionBrush, destination);
                    }
                }
            }
        }

        private bool TryGetMaskPoint(Point position, out SKPoint point, out double scale)
        {
            point = default;
            scale = 0;
            if (_referencePreview == null)
            {
                return false;
            }
            if (_mask == null)
            {
                return false;
            }
            Matrix transform = ImageTransform(out scale);
            if (scale <= 0)
            {
                return false;
            }
            if (transform.TryInvert(out Matrix inverse) == false)
            {
                return false;
            }
            Point original = inverse.Transform(position);
            if (original.X < 0)
            {
                return false;
            }
            if (original.Y < 0)
            {
                return false;
            }
            if (original.X >= _originalWidth)
            {
                return false;
            }
            if (original.Y >= _originalHeight)
            {
                return false;
            }
            point = new SKPoint((float)(original.X * _mask.Width / _originalWidth), (float)(original.Y * _mask.Height / _originalHeight));
            return true;
        }

        private void Paint(Point position)
        {
            if (TryGetMaskPoint(position, out SKPoint point, out double scale) == false)
            {
                _hasPreviousPoint = false;
                return;
            }
            using SKPaint paint = new()
            {
                Color = SKColors.White,
                Style = SKPaintStyle.Stroke,
                StrokeCap = SKStrokeCap.Round,
                StrokeWidth = (float)(BrushSize / scale * _mask.Width / _originalWidth),
                IsAntialias = true
            };
            if (IsErasing == true)
            {
                paint.BlendMode = SKBlendMode.Clear;
            }
            using (SKCanvas canvas = new(_mask))
            {
                if (_hasPreviousPoint == false)
                {
                    paint.Style = SKPaintStyle.Fill;
                    canvas.DrawCircle(point, paint.StrokeWidth / 2, paint);
                }
                else
                {
                    canvas.DrawLine(_previousPoint, point, paint);
                }
            }
            _previousPoint = point;
            _hasPreviousPoint = true;
            UpdateMaskPreview();
        }

        private void PublishMask()
        {
            if (_mask == null)
            {
                return;
            }
            bool hasSelection = false;
            foreach (SKColor pixel in _mask.Pixels)
            {
                if (pixel.Alpha > 0)
                {
                    hasSelection = true;
                    break;
                }
            }
            byte[] data = Array.Empty<byte>();
            if (hasSelection == true)
            {
                using SKImage image = SKImage.FromBitmap(_mask);
                using SKData encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                data = encoded.ToArray();
            }
            _publishingMask = true;
            try
            {
                SetCurrentValue(MaskDataProperty, data);
            }
            finally
            {
                _publishingMask = false;
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs eventArguments)
        {
            base.OnPointerPressed(eventArguments);
            if (_drawing == true)
            {
                return;
            }
            if (eventArguments.GetCurrentPoint(this).Properties.IsLeftButtonPressed == false)
            {
                return;
            }
            if (TryGetMaskPoint(eventArguments.GetPosition(this), out _, out _) == false)
            {
                return;
            }
            _drawing = true;
            _hasPreviousPoint = false;
            _capturedPointer = eventArguments.Pointer;
            _capturedPointer.Capture(this);
            Paint(eventArguments.GetPosition(this));
            eventArguments.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs eventArguments)
        {
            base.OnPointerMoved(eventArguments);
            if (_drawing == false)
            {
                return;
            }
            if (ReferenceEquals(eventArguments.Pointer, _capturedPointer) == false)
            {
                return;
            }
            Paint(eventArguments.GetPosition(this));
            eventArguments.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs eventArguments)
        {
            base.OnPointerReleased(eventArguments);
            if (_drawing == false)
            {
                return;
            }
            if (ReferenceEquals(eventArguments.Pointer, _capturedPointer) == false)
            {
                return;
            }
            _drawing = false;
            eventArguments.Pointer.Capture(null);
            _capturedPointer = null;
            PublishMask();
            eventArguments.Handled = true;
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs eventArguments)
        {
            base.OnPointerCaptureLost(eventArguments);
            _capturedPointer = null;
            if (_drawing == true)
            {
                _drawing = false;
                PublishMask();
            }
        }
    }
}

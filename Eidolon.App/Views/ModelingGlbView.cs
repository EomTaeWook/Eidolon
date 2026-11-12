using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.VisualTree;
using Eidolon.App.Services.Modeling;

namespace Eidolon.App.Views
{
    public class ModelingGlbView : OpenGlControlBase
    {
        public static readonly StyledProperty<string> ModelPathProperty = AvaloniaProperty.Register<ModelingGlbView, string>(nameof(ModelPath), string.Empty);
        public static readonly DirectProperty<ModelingGlbView, bool> IsModelLoadingProperty = AvaloniaProperty.RegisterDirect<ModelingGlbView, bool>(nameof(IsModelLoading), view => view.IsModelLoading);
        public static readonly DirectProperty<ModelingGlbView, bool> HasModelProperty = AvaloniaProperty.RegisterDirect<ModelingGlbView, bool>(nameof(HasModel), view => view.HasModel);
        public static readonly DirectProperty<ModelingGlbView, bool> HasModelErrorProperty = AvaloniaProperty.RegisterDirect<ModelingGlbView, bool>(nameof(HasModelError), view => view.HasModelError);

        private static readonly TimeSpan GraphicsInitializationTimeout = TimeSpan.FromSeconds(5);
        private const float DefaultYaw = 0.6f;
        private const float DefaultPitch = 0.3f;
        private const float OrbitRadiansPerDip = 0.01f;
        private const float PitchLimit = MathF.PI / 2 - 0.03f;
        private Visual[] _visibilityAncestors = Array.Empty<Visual>();
        private CancellationTokenSource _modelLoadCancellation;
        private Task _modelLoadTask = Task.CompletedTask;
        private TaskCompletionSource _graphicsReady;
        private ModelingGlbRenderer _renderer;
        private float[] _pendingVertices;
        private string _activeModelPath = string.Empty;
        private bool _isAttached;
        private bool _discardRenderer;
        private bool _isModelLoading;
        private bool _hasModel;
        private bool _hasModelError;
        private IPointer _dragPointer;
        private Point _lastPointerPosition;
        private float _yaw = DefaultYaw;
        private float _pitch = DefaultPitch;
        private float _zoom = 1;

        public ModelingGlbView()
        {
            ClipToBounds = true;
        }

        public string ModelPath
        {
            get
            {
                return GetValue(ModelPathProperty);
            }
            set
            {
                SetValue(ModelPathProperty, value);
            }
        }

        public bool IsModelLoading
        {
            get
            {
                return _isModelLoading;
            }
        }

        public bool HasModel
        {
            get
            {
                return _hasModel;
            }
        }

        public bool HasModelError
        {
            get
            {
                return _hasModelError;
            }
        }

        public void ResetView()
        {
            ResetCamera();
            if (HasModel == true)
            {
                RequestNextFrameRendering();
            }
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs arguments)
        {
            _isAttached = true;
            _graphicsReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            base.OnAttachedToVisualTree(arguments);
            _visibilityAncestors = this.GetVisualAncestors().ToArray();
            foreach (Visual ancestor in _visibilityAncestors)
            {
                ancestor.PropertyChanged += OnAncestorPropertyChanged;
            }
            RefreshActiveModel();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs arguments)
        {
            _isAttached = false;
            foreach (Visual ancestor in _visibilityAncestors)
            {
                ancestor.PropertyChanged -= OnAncestorPropertyChanged;
            }
            _visibilityAncestors = Array.Empty<Visual>();
            CancelModelLoad();
            EndDrag();
            _activeModelPath = string.Empty;
            _pendingVertices = null;
            _graphicsReady = null;
            SetModelState(false, false, false);
            base.OnDetachedFromVisualTree(arguments);
            _renderer = null;
            _discardRenderer = false;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == ModelPathProperty)
            {
                RefreshActiveModel();
                return;
            }
            if (change.Property == IsVisibleProperty)
            {
                RefreshActiveModel();
            }
        }

        private void OnAncestorPropertyChanged(object sender, AvaloniaPropertyChangedEventArgs change)
        {
            if (change.Property == IsVisibleProperty)
            {
                RefreshActiveModel();
            }
        }

        private void RefreshActiveModel(bool restart = false)
        {
            if (_isAttached == false)
            {
                return;
            }
            string modelPath = GetVisibleModelPath();
            if (restart == false)
            {
                if (string.Equals(modelPath, _activeModelPath, StringComparison.Ordinal) == true)
                {
                    return;
                }
            }
            CancelModelLoad();
            EndDrag();
            _pendingVertices = null;
            _discardRenderer = true;
            _activeModelPath = modelPath;
            if (modelPath.Length == 0)
            {
                SetModelState(false, false, false);
                RequestNextFrameRendering();
                return;
            }
            ResetCamera();
            CancellationTokenSource request = new CancellationTokenSource();
            Task graphicsReady = _graphicsReady.Task;
            Task previousLoad = _modelLoadTask;
            _modelLoadCancellation = request;
            SetModelState(true, false, false);
            if (ReferenceEquals(_modelLoadCancellation, request) == false)
            {
                request.Dispose();
                return;
            }
            _modelLoadTask = LoadModelAsync(modelPath, graphicsReady, previousLoad, request);
            RequestNextFrameRendering();
        }

        private string GetVisibleModelPath()
        {
            if (IsVisible == false)
            {
                return string.Empty;
            }
            foreach (Visual ancestor in _visibilityAncestors)
            {
                if (ancestor.IsVisible == false)
                {
                    return string.Empty;
                }
            }
            string modelPath = ModelPath;
            if (string.IsNullOrWhiteSpace(modelPath) == true)
            {
                return string.Empty;
            }
            return modelPath;
        }

        private void CancelModelLoad()
        {
            CancellationTokenSource previous = _modelLoadCancellation;
            _modelLoadCancellation = null;
            if (previous != null)
            {
                previous.Cancel();
            }
        }

        private async Task LoadModelAsync(string modelPath, Task graphicsReady, Task previousLoad, CancellationTokenSource request)
        {
            CancellationToken cancellationToken = request.Token;
            try
            {
                await previousLoad;
                cancellationToken.ThrowIfCancellationRequested();
                // Avalonia may decline GL initialization without calling OnOpenGlInit.
                await graphicsReady.WaitAsync(GraphicsInitializationTimeout, cancellationToken);
                float[] vertices = await Task.Run(() => ModelingGlbReader.Read(modelPath, cancellationToken), cancellationToken);
                if (ReferenceEquals(_modelLoadCancellation, request) == false)
                {
                    return;
                }
                _pendingVertices = vertices;
                RequestNextFrameRendering();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == true)
            {
            }
            catch (Exception error)
            {
                if (ReferenceEquals(_modelLoadCancellation, request) == true)
                {
                    Debug.WriteLine(error.Message);
                    SetModelState(false, false, true);
                }
            }
            finally
            {
                if (ReferenceEquals(_modelLoadCancellation, request) == true)
                {
                    _modelLoadCancellation = null;
                }
                request.Dispose();
            }
        }

        protected override void OnOpenGlInit(GlInterface graphics)
        {
            if (_graphicsReady == null)
            {
                return;
            }
            _graphicsReady.TrySetResult();
            if (HasModelError == true)
            {
                RefreshActiveModel(true);
            }
        }

        protected override void OnOpenGlDeinit(GlInterface graphics)
        {
            ReleaseRenderer();
        }

        protected override void OnOpenGlLost()
        {
            // The context no longer owns usable handles; deletion must not run against it.
            _renderer = null;
            if (_isAttached == true)
            {
                _graphicsReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                RefreshActiveModel(true);
            }
        }

        protected override void OnOpenGlRender(GlInterface graphics, int framebuffer)
        {
            double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            int width = Math.Max(1, (int)(Bounds.Width * scaling));
            int height = Math.Max(1, (int)(Bounds.Height * scaling));
            try
            {
                ModelingGlbRenderer.ClearFrame(graphics, framebuffer, width, height);
                if (_discardRenderer == true)
                {
                    ReleaseRenderer();
                    _discardRenderer = false;
                }
                if (_pendingVertices != null)
                {
                    _renderer = new ModelingGlbRenderer(graphics, GlVersion);
                    _renderer.Upload(_pendingVertices);
                    _pendingVertices = null;
                    _renderer.Draw(width, height, _yaw, _pitch, _zoom);
                    SetModelState(false, true, false);
                    return;
                }
                if (_renderer != null)
                {
                    _renderer.Draw(width, height, _yaw, _pitch, _zoom);
                }
            }
            catch (Exception error)
            {
                Debug.WriteLine(error.Message);
                _pendingVertices = null;
                ReleaseRenderer();
                SetModelState(false, false, _activeModelPath.Length != 0);
            }
        }

        private void ReleaseRenderer()
        {
            ModelingGlbRenderer renderer = _renderer;
            _renderer = null;
            if (renderer != null)
            {
                try
                {
                    renderer.Release();
                }
                catch (Exception error)
                {
                    Debug.WriteLine(error.Message);
                }
            }
        }

        private void SetModelState(bool isLoading, bool hasModel, bool hasError)
        {
            SetAndRaise(HasModelProperty, ref _hasModel, hasModel);
            SetAndRaise(HasModelErrorProperty, ref _hasModelError, hasError);
            SetAndRaise(IsModelLoadingProperty, ref _isModelLoading, isLoading);
        }

        private void ResetCamera()
        {
            _yaw = DefaultYaw;
            _pitch = DefaultPitch;
            _zoom = 1;
        }

        private void EndDrag()
        {
            IPointer pointer = _dragPointer;
            _dragPointer = null;
            if (pointer != null)
            {
                pointer.Capture(null);
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs arguments)
        {
            base.OnPointerPressed(arguments);
            if (HasModel == false)
            {
                return;
            }
            if (_dragPointer != null)
            {
                return;
            }
            if (arguments.GetCurrentPoint(this).Properties.IsLeftButtonPressed == false)
            {
                return;
            }
            _lastPointerPosition = arguments.GetPosition(this);
            _dragPointer = arguments.Pointer;
            _dragPointer.Capture(this);
            arguments.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs arguments)
        {
            base.OnPointerMoved(arguments);
            if (ReferenceEquals(arguments.Pointer, _dragPointer) == false)
            {
                return;
            }
            Point position = arguments.GetPosition(this);
            if (position.Equals(_lastPointerPosition) == false)
            {
                float horizontalMovement = (float)(position.X - _lastPointerPosition.X);
                float verticalMovement = (float)(position.Y - _lastPointerPosition.Y);
                _lastPointerPosition = position;
                _yaw = MathF.IEEERemainder(_yaw - horizontalMovement * OrbitRadiansPerDip, 2 * MathF.PI);
                _pitch = Math.Clamp(_pitch + verticalMovement * OrbitRadiansPerDip, -PitchLimit, PitchLimit);
                RequestNextFrameRendering();
            }
            arguments.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs arguments)
        {
            base.OnPointerReleased(arguments);
            if (ReferenceEquals(arguments.Pointer, _dragPointer) == false)
            {
                return;
            }
            if (arguments.GetCurrentPoint(this).Properties.IsLeftButtonPressed == true)
            {
                return;
            }
            EndDrag();
            arguments.Handled = true;
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs arguments)
        {
            base.OnPointerCaptureLost(arguments);
            _dragPointer = null;
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs arguments)
        {
            base.OnPointerWheelChanged(arguments);
            if (HasModel == false)
            {
                return;
            }
            if (arguments.Delta.Y == 0)
            {
                return;
            }
            float zoom = Math.Clamp(_zoom * MathF.Exp(-0.16f * (float)arguments.Delta.Y), 0.35f, 8);
            if (zoom != _zoom)
            {
                _zoom = zoom;
                RequestNextFrameRendering();
            }
            arguments.Handled = true;
        }
    }
}

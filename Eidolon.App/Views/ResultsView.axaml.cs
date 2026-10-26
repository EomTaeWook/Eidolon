using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Eidolon.App.ViewModels;

namespace Eidolon.App.Views
{
    public partial class ResultsView : UserControl
    {
        private const int MinimumCardWidth = 220;
        private readonly ListBox _gallery;
        private int _galleryColumns = 2;
        private double _galleryRowHeight;
        private TopLevel _topLevel;

        public ResultsView()
        {
            AvaloniaXamlLoader.Load(this);
            _gallery = this.FindControl<ListBox>("Gallery");
            _gallery.AddHandler(PointerPressedEvent, OnGalleryPointerPressed, RoutingStrategies.Tunnel);
            _gallery.AddHandler(GotFocusEvent, OnGalleryGotFocus, RoutingStrategies.Bubble);
            _gallery.LayoutUpdated += OnGalleryLayoutUpdated;
            AttachedToVisualTree += OnResultsAttachedToVisualTree;
            DetachedFromVisualTree += OnResultsDetachedFromVisualTree;
        }

        private void OnResultsAttachedToVisualTree(object sender, VisualTreeAttachmentEventArgs args)
        {
            _topLevel = TopLevel.GetTopLevel(this);
            _topLevel?.AddHandler(KeyDownEvent, OnTopLevelKeyDown, RoutingStrategies.Tunnel);
        }

        private void OnResultsDetachedFromVisualTree(object sender, VisualTreeAttachmentEventArgs args)
        {
            _topLevel?.RemoveHandler(KeyDownEvent, OnTopLevelKeyDown);
            _topLevel = null;
        }

        private void OnGalleryPointerPressed(object sender, PointerPressedEventArgs args)
        {
            if (sender is not ListBox gallery)
            {
                return;
            }
            if (args.GetCurrentPoint(gallery).Properties.IsLeftButtonPressed == false)
            {
                return;
            }
            SelectGeneration(args.Source);
        }

        private void OnGalleryGotFocus(object sender, FocusChangedEventArgs args)
        {
            if (args.NavigationMethod != NavigationMethod.Pointer)
            {
                SelectGeneration(args.Source);
            }
        }

        private void SelectGeneration(object source)
        {
            if (DataContext is not GalleryViewModel viewModel)
            {
                return;
            }
            if (source is not Control control)
            {
                return;
            }
            if (control.DataContext is not GenerationItem item)
            {
                return;
            }
            viewModel.SelectGenerationCommand.Execute(item);
        }

        private void OnGalleryKeyDown(object sender, KeyEventArgs args)
        {
            if (DataContext is not GalleryViewModel viewModel)
            {
                return;
            }
            if (args.Key == Key.Delete)
            {
                viewModel.DeleteGenerationCommand.Execute(null);
                args.Handled = true;
            }
            else if (args.Key == Key.Escape)
            {
                viewModel.ClearGallerySelectionCommand.Execute(null);
                args.Handled = true;
            }
        }

        private void OnTopLevelKeyDown(object sender, KeyEventArgs args)
        {
            if (args.Handled == true)
            {
                return;
            }
            if (args.Key != Key.A)
            {
                return;
            }
            if (args.KeyModifiers != KeyModifiers.Control)
            {
                return;
            }
            if (DataContext is not GalleryViewModel viewModel)
            {
                return;
            }
            if (viewModel.Navigation.IsResultsView == false)
            {
                return;
            }
            if (viewModel.Session.IsClosing == true)
            {
                return;
            }
            if (_gallery.IsEnabled == false)
            {
                return;
            }
            if (IsEffectivelyVisible == false)
            {
                return;
            }
            if (args.Source is Visual source)
            {
                foreach (Visual visual in source.GetVisualAncestors().Prepend(source))
                {
                    if (visual is TextBox)
                    {
                        return;
                    }
                    if (visual is SelectableTextBlock)
                    {
                        return;
                    }
                }
            }
            _gallery.SelectAll();
            args.Handled = true;
        }

        private void OnGalleryLayoutUpdated(object sender, EventArgs args)
        {
            if (DataContext is not GalleryViewModel viewModel)
            {
                return;
            }
            if (viewModel.Navigation.IsResultsView == false)
            {
                return;
            }
            if (IsEffectivelyVisible == false)
            {
                return;
            }
            ScrollViewer scrollViewer = _gallery.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (scrollViewer == null)
            {
                return;
            }
            if (scrollViewer.Viewport.Width <= 0)
            {
                return;
            }
            if (scrollViewer.Viewport.Height <= 0)
            {
                return;
            }
            int columns = Math.Max(2, (int)(scrollViewer.Viewport.Width / MinimumCardWidth));
            if (columns != _galleryColumns)
            {
                _galleryColumns = columns;
                Resources["ResultsGalleryColumns"] = columns;
                return;
            }
            if (_gallery.ItemsPanelRoot == null)
            {
                return;
            }
            foreach (Control item in _gallery.ItemsPanelRoot.Children)
            {
                if (item.IsMeasureValid == true)
                {
                    _galleryRowHeight = Math.Max(_galleryRowHeight, item.DesiredSize.Height);
                }
            }
            if (_galleryRowHeight <= 0)
            {
                return;
            }
            int rows = Math.Max(1, (int)(scrollViewer.Viewport.Height / _galleryRowHeight));
            viewModel.SetGalleryPageSize(columns * rows);
        }
    }
}

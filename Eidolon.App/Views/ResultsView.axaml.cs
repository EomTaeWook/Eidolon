using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Eidolon.App.ViewModels;

namespace Eidolon.App.Views
{
    public partial class ResultsView : UserControl
    {
        private const int MinimumCardWidth = 220;
        private int _galleryColumns = 2;

        public ResultsView()
        {
            AvaloniaXamlLoader.Load(this);
            ListBox gallery = this.FindControl<ListBox>("Gallery");
            gallery.AddHandler(PointerPressedEvent, OnGalleryPointerPressed, RoutingStrategies.Tunnel);
            gallery.AddHandler(GotFocusEvent, OnGalleryGotFocus, RoutingStrategies.Bubble);
            gallery.PropertyChanged += OnGalleryPropertyChanged;
        }

        private void OnGalleryPointerPressed(object sender, PointerPressedEventArgs args)
        {
            if (sender is ListBox gallery && args.GetCurrentPoint(gallery).Properties.IsLeftButtonPressed == true)
            {
                SelectGeneration(args.Source);
            }
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
            if (DataContext is StudioViewModel viewModel && source is Control control
                && control.DataContext is GenerationItem item)
            {
                viewModel.SelectGenerationCommand.Execute(item);
            }
        }

        private void OnGalleryKeyDown(object sender, KeyEventArgs args)
        {
            if (DataContext is StudioViewModel viewModel && sender is ListBox gallery)
            {
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
                else if (args.Key == Key.A && (args.KeyModifiers & KeyModifiers.Control) != 0)
                {
                    gallery.SelectAll();
                    args.Handled = true;
                }
            }
        }

        private void OnGalleryPropertyChanged(object sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == BoundsProperty && sender is ListBox gallery && gallery.Bounds.Width > 0)
            {
                int columns = Math.Max(2, (int)(gallery.Bounds.Width / MinimumCardWidth));
                if (columns != _galleryColumns)
                {
                    _galleryColumns = columns;
                    Resources["ResultsGalleryColumns"] = columns;
                }
            }
        }
    }
}

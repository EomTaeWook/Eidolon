using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;
using Eidolon.App.ViewModels;

namespace Eidolon.App.Views
{
    public partial class GenerationInputsView : UserControl
    {
        public GenerationInputsView()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnLoraClicked(object sender, RoutedEventArgs args)
        {
            if (DataContext is StudioViewModel viewModel && sender is Button button && button.DataContext is AssetItem item)
            {
                item.IsSelected = item.IsSelected == false;
            }
        }
    }
}

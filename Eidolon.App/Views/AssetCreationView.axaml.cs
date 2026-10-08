using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Eidolon.App.ViewModels;

namespace Eidolon.App.Views
{
    public partial class AssetCreationView : UserControl
    {
        public AssetCreationView()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnLoraClicked(object sender, RoutedEventArgs args)
        {
            if (DataContext is AssetCreationViewModel viewModel && viewModel.Session.CanEditGenerationInputs == true
                && sender is Button button && button.DataContext is AssetItem item)
            {
                item.IsSelected = item.IsSelected == false;
            }
        }
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Eidolon.App.Views
{
    public partial class ModelingView : UserControl
    {
        public ModelingView()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnResetViewClick(object sender, RoutedEventArgs args)
        {
            ModelingGlbView modelViewer = this.FindControl<ModelingGlbView>("ModelViewer");
            if (modelViewer == null)
            {
                return;
            }
            if (modelViewer.HasModel == false)
            {
                return;
            }
            modelViewer.ResetView();
        }
    }
}

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Eidolon.App.ViewModels;

namespace Eidolon.App.Views
{
    public partial class SettingsView : UserControl
    {
        private StudioNavigationViewModel _navigation;

        public SettingsView()
        {
            AvaloniaXamlLoader.Load(this);
            AttachedToVisualTree += OnSettingsAttachedToVisualTree;
            DetachedFromVisualTree += OnSettingsDetachedFromVisualTree;
        }

        private void OnSettingsAttachedToVisualTree(object sender, VisualTreeAttachmentEventArgs arguments)
        {
            if (DataContext is StudioViewModel studio)
            {
                _navigation = studio.Navigation;
                _navigation.PropertyChanged += OnNavigationPropertyChanged;
                Dispatcher.UIThread.Post(ScrollToSelectedSection, DispatcherPriority.Loaded);
            }
        }

        private void OnSettingsDetachedFromVisualTree(object sender, VisualTreeAttachmentEventArgs arguments)
        {
            if (_navigation == null)
            {
                return;
            }
            _navigation.PropertyChanged -= OnNavigationPropertyChanged;
            _navigation = null;
        }

        private void OnNavigationPropertyChanged(object sender, PropertyChangedEventArgs arguments)
        {
            if (arguments.PropertyName != nameof(StudioNavigationViewModel.SelectedSettingsTab))
            {
                if (arguments.PropertyName != nameof(StudioNavigationViewModel.SelectedTab))
                {
                    return;
                }
            }
            Dispatcher.UIThread.Post(ScrollToSelectedSection, DispatcherPriority.Loaded);
        }

        private void ScrollToSelectedSection()
        {
            if (_navigation == null)
            {
                return;
            }
            if (_navigation.IsSettingsView == false)
            {
                return;
            }
            Control selectedSection = UpdateSection;
            if (_navigation.SelectedSettingsTab == 1)
            {
                selectedSection = EngineSection;
            }
            if (_navigation.SelectedSettingsTab == 2)
            {
                selectedSection = McpSection;
            }
            SettingsScroll.Offset = new Vector(0, selectedSection.Bounds.Y);
        }
    }
}
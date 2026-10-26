using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Eidolon.App.Views
{
    public partial class PendingWorkView : UserControl
    {
        public PendingWorkView()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnQueueFlyoutOpening(object sender, EventArgs eventArguments)
        {
            Flyout queueFlyout = sender as Flyout;
            if (queueFlyout == null)
            {
                return;
            }
            Control queueContent = queueFlyout.Content as Control;
            if (queueContent == null)
            {
                return;
            }
            TopLevel topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null)
            {
                return;
            }
            Control placementTarget = queueFlyout.Target;
            if (placementTarget == null)
            {
                return;
            }
            var targetPosition = placementTarget.TranslatePoint(new Point(0, 0), topLevel);
            if (targetPosition.HasValue == false)
            {
                return;
            }
            Point targetOrigin = targetPosition.Value;
            const double windowMargin = 12;
            const double popupGap = 8;
            const double presenterInsets = 34;
            double availableAbove = Math.Max(0, targetOrigin.Y - windowMargin - popupGap);
            double availableBelow = Math.Max(0, topLevel.ClientSize.Height - targetOrigin.Y - placementTarget.Bounds.Height - windowMargin - popupGap);
            queueContent.Width = Math.Min(640, Math.Max(0, topLevel.ClientSize.Width - windowMargin * 2 - presenterInsets));
            queueContent.MaxHeight = Math.Min(420, Math.Max(0, Math.Max(availableAbove, availableBelow) - presenterInsets));
            queueFlyout.Placement = PlacementMode.TopEdgeAlignedLeft;
            queueFlyout.VerticalOffset = -popupGap;
            if (availableBelow > availableAbove)
            {
                queueFlyout.Placement = PlacementMode.BottomEdgeAlignedLeft;
                queueFlyout.VerticalOffset = popupGap;
            }
            double maximumLeft = Math.Max(windowMargin, topLevel.ClientSize.Width - queueContent.Width - presenterInsets - windowMargin);
            double popupLeft = Math.Clamp(targetOrigin.X, windowMargin, maximumLeft);
            queueFlyout.HorizontalOffset = popupLeft - targetOrigin.X;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property != IsVisibleProperty)
            {
                return;
            }
            if (IsVisible == true)
            {
                return;
            }
            DropDownButton queueButton = this.FindControl<DropDownButton>("QueueButton");
            if (queueButton == null)
            {
                return;
            }
            if (queueButton.Flyout == null)
            {
                return;
            }
            queueButton.Flyout.Hide();
        }
    }
}

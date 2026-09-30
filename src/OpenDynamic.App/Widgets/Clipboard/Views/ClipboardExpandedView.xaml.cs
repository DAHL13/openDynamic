using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OpenDynamic.Core.Clipboard;

namespace OpenDynamic.App.Widgets.Clipboard.Views;

/// <summary>
/// Interaction logic for ClipboardExpandedView.xaml
/// </summary>
public partial class ClipboardExpandedView : UserControl
{
    public ClipboardExpandedView()
    {
        InitializeComponent();
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ItemsScrollViewer != null)
        {
            ItemsScrollViewer.ScrollToVerticalOffset(ItemsScrollViewer.VerticalOffset - (e.Delta / 3.0));
            e.Handled = true;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        // Prevent background clicks inside the expanded view from bubbling to mainCapsule toggle-collapse
        e.Handled = true;
    }

    private void OnItemClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ClipboardItem item)
        {
            if (DataContext is ClipboardWidget widget)
            {
                widget.ReCopyItem(item);
                e.Handled = true;
            }
        }
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is ClipboardWidget widget)
        {
            widget.ClearAll();
            e.Handled = true;
        }
    }
}

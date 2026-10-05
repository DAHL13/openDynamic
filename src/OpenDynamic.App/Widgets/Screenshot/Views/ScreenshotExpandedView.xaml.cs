using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OpenDynamic.Core.Screenshots;

namespace OpenDynamic.App.Widgets.Screenshot.Views;

/// <summary>
/// Interaction logic for ScreenshotExpandedView.xaml
/// </summary>
public partial class ScreenshotExpandedView : UserControl
{
    private Point? _dragStartPoint;
    private bool _isDragging;

    public ScreenshotExpandedView()
    {
        InitializeComponent();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragStartPoint = null;
        // Prevent background clicks inside the expanded view from bubbling to mainCapsule toggle-collapse
        e.Handled = true;
    }

    private void OnThumbnailMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);
        e.Handled = true;
    }

    private void OnThumbnailMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging || e.LeftButton != MouseButtonState.Pressed || !_dragStartPoint.HasValue)
        {
            return;
        }

        Point currentPos = e.GetPosition(this);
        Vector diff = _dragStartPoint.Value - currentPos;

        if (Math.Abs(diff.X) >= SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) >= SystemParameters.MinimumVerticalDragDistance)
        {
            if (DataContext is ScreenshotWidget widget && sender is DependencyObject dragSource)
            {
                try
                {
                    _isDragging = true;
                    _dragStartPoint = null;
                    widget.TryStartFileDrag(dragSource);
                }
                finally
                {
                    _isDragging = false;
                }
            }
        }
    }

    private async void OnCopyImageClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is ScreenshotWidget widget)
        {
            await widget.CopyCurrentImageAsync();
        }
    }

    private void OnOpenImageClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is ScreenshotWidget widget)
        {
            widget.OpenCurrentImage();
        }
    }

    private void OnRevealInFolderClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is ScreenshotWidget widget)
        {
            widget.RevealCurrentInFolder();
        }
    }

    private void OnTrashStep1Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is ScreenshotWidget widget)
        {
            widget.RequestTrashStep1();
        }
    }

    private void OnTrashStep2Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is ScreenshotWidget widget)
        {
            widget.AdvanceTrashStep2();
        }
    }

    private void OnCancelTrashClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is ScreenshotWidget widget)
        {
            widget.CancelTrashConfirmation();
        }
    }

    private void OnConfirmRecycleClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is ScreenshotWidget widget)
        {
            widget.ConfirmSendToRecycleBin();
        }
    }

    private void OnToggleHistoryClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is ScreenshotWidget widget)
        {
            widget.ToggleHistoryView();
        }
    }

    private void OnHistoryPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (HistoryScrollViewer != null)
        {
            HistoryScrollViewer.ScrollToVerticalOffset(HistoryScrollViewer.VerticalOffset - (e.Delta / 3.0));
            e.Handled = true;
        }
    }

    private void OnHistoryItemClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ScreenshotEntry entry && DataContext is ScreenshotWidget widget)
        {
            widget.SelectHistoryItem(entry);
            e.Handled = true;
        }
    }

    private void OnRemoveHistoryItemClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement fe && fe.DataContext is ScreenshotEntry entry && DataContext is ScreenshotWidget widget)
        {
            widget.RemoveHistoryItem(entry);
        }
    }
}

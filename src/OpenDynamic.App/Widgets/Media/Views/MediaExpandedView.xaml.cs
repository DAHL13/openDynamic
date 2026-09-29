using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OpenDynamic.App.Widgets.Media.Views;

public partial class MediaExpandedView : UserControl
{
    private readonly MediaWidget _widget;

    public MediaExpandedView(MediaWidget widget)
    {
        _widget = widget ?? throw new ArgumentNullException(nameof(widget));
        DataContext = _widget;
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _widget.PropertyChanged += OnWidgetPropertyChanged;
        UpdateProgressFill();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _widget.PropertyChanged -= OnWidgetPropertyChanged;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateProgressFill();
    }

    private void OnWidgetPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MediaWidget.ProgressRatio) or nameof(MediaWidget.CurrentPosition))
        {
            Dispatcher.InvokeAsync(UpdateProgressFill);
        }
    }

    private void UpdateProgressFill()
    {
        var totalWidth = ProgressBarContainer.ActualWidth;
        if (totalWidth <= 0) return;

        var ratio = Math.Clamp(_widget.ProgressRatio, 0.0, 1.0);
        ActiveProgressFill.Width = totalWidth * ratio;
    }

    private void OnProgressTrackClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        SeekFromMouse(e.GetPosition(ProgressBarContainer));
    }

    private void OnProgressTrackMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            e.Handled = true;
            SeekFromMouse(e.GetPosition(ProgressBarContainer));
        }
    }

    private void SeekFromMouse(Point pt)
    {
        var totalWidth = ProgressBarContainer.ActualWidth;
        if (totalWidth <= 0) return;

        var ratio = Math.Clamp(pt.X / totalWidth, 0.0, 1.0);
        ActiveProgressFill.Width = totalWidth * ratio;
        _ = _widget.SeekToRatioAsync(ratio);
    }
}

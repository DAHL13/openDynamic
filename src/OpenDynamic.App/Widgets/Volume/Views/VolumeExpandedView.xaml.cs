using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OpenDynamic.App.Widgets.Volume.Views;

public partial class VolumeExpandedView : UserControl
{
    public VolumeExpandedView()
    {
        InitializeComponent();
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DataContext is VolumeWidget widget)
        {
            widget.AdjustVolume(e.Delta);
            e.Handled = true;
        }
    }

    private void OnSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DataContext is VolumeWidget widget)
        {
            // Only adjust if difference is significant and user interacted
            float target = (float)(e.NewValue / 100.0);
            if (MathF.Abs(widget.VolumeLevel - target) > 0.015f)
            {
                widget.SetVolume(target);
            }
        }
    }
}

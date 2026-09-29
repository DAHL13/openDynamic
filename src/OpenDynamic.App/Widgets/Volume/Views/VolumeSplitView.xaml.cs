using System.Windows.Controls;
using System.Windows.Input;

namespace OpenDynamic.App.Widgets.Volume.Views;

public partial class VolumeSplitView : UserControl
{
    public VolumeSplitView()
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
}

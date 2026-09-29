using System.Windows.Controls;
using System.Windows.Input;

namespace OpenDynamic.App.Widgets.Volume.Views;

public partial class VolumeCompactView : UserControl
{
    public VolumeCompactView()
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

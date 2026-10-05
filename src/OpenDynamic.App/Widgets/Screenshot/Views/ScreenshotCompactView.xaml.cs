using System.Windows.Controls;
using System.Windows.Input;

namespace OpenDynamic.App.Widgets.Screenshot.Views;

/// <summary>
/// Interaction logic for ScreenshotCompactView.xaml
/// </summary>
public partial class ScreenshotCompactView : UserControl
{
    public ScreenshotCompactView()
    {
        InitializeComponent();
    }

    private void OnRootMouseEnter(object sender, MouseEventArgs e)
    {
        if (DataContext is ScreenshotWidget widget)
        {
            widget.OnViewMouseEnter();
        }
    }

    private void OnRootMouseLeave(object sender, MouseEventArgs e)
    {
        if (DataContext is ScreenshotWidget widget)
        {
            widget.OnViewMouseLeave();
        }
    }
}

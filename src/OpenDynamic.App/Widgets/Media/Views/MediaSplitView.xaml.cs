using System.Windows.Controls;

namespace OpenDynamic.App.Widgets.Media.Views;

public partial class MediaSplitView : UserControl
{
    private readonly MediaWidget _widget;

    public MediaSplitView(MediaWidget widget)
    {
        _widget = widget ?? throw new ArgumentNullException(nameof(widget));
        DataContext = _widget;
        InitializeComponent();
    }
}

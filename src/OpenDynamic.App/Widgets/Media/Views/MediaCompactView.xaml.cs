using System.Windows.Controls;
using System.Windows.Input;

namespace OpenDynamic.App.Widgets.Media.Views;

public partial class MediaCompactView : UserControl
{
    private readonly MediaWidget _widget;

    public MediaCompactView(MediaWidget widget)
    {
        _widget = widget ?? throw new ArgumentNullException(nameof(widget));
        DataContext = _widget;
        InitializeComponent();
    }
}

using OpenDynamic.Core.State;

namespace OpenDynamic.Tests.State;

public class IslandLayoutTests
{
    [Fact]
    public void GetDimensions_ReturnsExpectedDefaults()
    {
        var layout = new IslandLayout();

        var compact = layout.GetDimensions(IslandState.Compact);
        Assert.Equal(200.0, compact.Width);
        Assert.Equal(36.0, compact.Height);
        Assert.Equal(14.0, compact.CornerRadius);
        Assert.Equal(1.0, compact.Opacity);

        var expanded = layout.GetDimensions(IslandState.Expanded);
        Assert.Equal(400.0, expanded.Width);
        Assert.Equal(185.0, expanded.Height);
        Assert.Equal(16.0, expanded.CornerRadius);
        Assert.Equal(1.0, expanded.Opacity);

        var split = layout.GetDimensions(IslandState.Split);
        Assert.Equal(280.0, split.Width);
        Assert.Equal(36.0, split.Height);
        Assert.Equal(14.0, split.CornerRadius);
        Assert.Equal(1.0, split.Opacity);

        var hidden = layout.GetDimensions(IslandState.Hidden);
        Assert.Equal(80.0, hidden.Width);
        Assert.Equal(4.0, hidden.Height);
        Assert.Equal(2.0, hidden.CornerRadius);
        Assert.Equal(0.01, hidden.Opacity);
    }

    [Fact]
    public void CustomDimensions_AreRespected()
    {
        var layout = new IslandLayout
        {
            Expanded = new CapsuleDimensions(Width: 420.0, Height: 180.0, CornerRadius: 28.0, Opacity: 1.0)
        };

        var customExpanded = layout.GetDimensions(IslandState.Expanded);
        Assert.Equal(420.0, customExpanded.Width);
        Assert.Equal(180.0, customExpanded.Height);
        Assert.Equal(28.0, customExpanded.CornerRadius);
    }
}

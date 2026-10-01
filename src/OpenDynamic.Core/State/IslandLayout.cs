namespace OpenDynamic.Core.State;

/// <summary>
/// Geometric target dimensions for the Dynamic Island capsule in DIPs.
/// </summary>
public readonly record struct CapsuleDimensions(
    double Width,
    double Height,
    double CornerRadius,
    double Opacity);

/// <summary>
/// Resolves and configures target capsule dimensions for each <see cref="IslandState"/>.
/// </summary>
public sealed class IslandLayout
{
    /// <summary>
    /// Default dimensions for Hidden state: an ultra-thin notch / sensor (80x4 DIP) at 1% opacity
    /// preserving hit-testing for mouse wheel and hover wake-up while remaining visually invisible.
    /// </summary>
    public CapsuleDimensions Hidden { get; set; } = new(Width: 80.0, Height: 4.0, CornerRadius: 2.0, Opacity: 0.01);
    public CapsuleDimensions Compact { get; set; } = new(Width: 200.0, Height: 36.0, CornerRadius: 14.0, Opacity: 1.0);
    public CapsuleDimensions Split { get; set; } = new(Width: 280.0, Height: 36.0, CornerRadius: 14.0, Opacity: 1.0);
    public CapsuleDimensions Expanded { get; set; } = new(Width: 400.0, Height: 185.0, CornerRadius: 16.0, Opacity: 1.0);

    public CapsuleDimensions GetDimensions(IslandState state) => state switch
    {
        IslandState.Hidden => Hidden,
        IslandState.Compact => Compact,
        IslandState.Split => Split,
        IslandState.Expanded => Expanded,
        _ => Compact
    };
}

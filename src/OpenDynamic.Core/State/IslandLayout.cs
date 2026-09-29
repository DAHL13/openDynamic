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
    public CapsuleDimensions Hidden { get; set; } = new(Width: 0.0, Height: 0.0, CornerRadius: 0.0, Opacity: 0.0);
    public CapsuleDimensions Compact { get; set; } = new(Width: 160.0, Height: 36.0, CornerRadius: 18.0, Opacity: 1.0);
    public CapsuleDimensions Split { get; set; } = new(Width: 260.0, Height: 36.0, CornerRadius: 18.0, Opacity: 1.0);
    public CapsuleDimensions Expanded { get; set; } = new(Width: 400.0, Height: 160.0, CornerRadius: 24.0, Opacity: 1.0);

    public CapsuleDimensions GetDimensions(IslandState state) => state switch
    {
        IslandState.Hidden => Hidden,
        IslandState.Compact => Compact,
        IslandState.Split => Split,
        IslandState.Expanded => Expanded,
        _ => Compact
    };
}

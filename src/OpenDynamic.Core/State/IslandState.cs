namespace OpenDynamic.Core.State;

/// <summary>
/// Defines the operational display states of the Dynamic Island capsule.
/// </summary>
public enum IslandState
{
    /// <summary>
    /// Capsule is invisible or collapsed off-screen.
    /// </summary>
    Hidden,

    /// <summary>
    /// Default compact pill (160x36 DIP) at top-center.
    /// </summary>
    Compact,

    /// <summary>
    /// Split pill with satellite bubble for concurrent activity presentation.
    /// </summary>
    Split,

    /// <summary>
    /// Expanded modal / card view (~400x185 DIP) showing rich interactive widget details.
    /// </summary>
    Expanded
}

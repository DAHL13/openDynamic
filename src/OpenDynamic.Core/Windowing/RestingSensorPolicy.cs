namespace OpenDynamic.Core.Windowing;

/// <summary>
/// Pure decision logic for the resting "sensor" strip used to detect the cursor at the top edge of the
/// screen while the island is <c>Hidden</c> (AUD-001, AUD-005).
/// <para>
/// The strip is intentionally minimal (<see cref="WidthDip"/> x <see cref="HeightDip"/> DIP, flush with the top bezel)
/// so it never swallows clicks on maximized-window title bars or browser tabs, and it only captures input when it
/// has a purpose: the ambient clock is enabled, no fullscreen application is being protected, and the system is awake.
/// </para>
/// Adheres to Golden Rule 5 (Core stays free of WPF / Win32).
/// </summary>
public static class RestingSensorPolicy
{
    /// <summary>Width of the resting sensor strip in device-independent pixels.</summary>
    public const double WidthDip = 120.0;

    /// <summary>Height of the resting sensor strip in device-independent pixels.</summary>
    public const double HeightDip = 4.0;

    /// <summary>
    /// Vertical tolerance above the window's top edge, so a cursor pinned at screen Y = 0 still registers
    /// when the window is positioned with a small negative/rounded offset.
    /// </summary>
    public const double TopToleranceDip = 5.0;

    /// <summary>
    /// Whether the resting sensor may capture input (<c>HTCLIENT</c>) in the <c>Hidden</c> state.
    /// When this returns <see langword="false"/> the window must be fully click-through (<c>HTTRANSPARENT</c>).
    /// </summary>
    public static bool IsInteractive(bool enableAmbientClock, bool isFullscreenSuppressed, bool isPowerSuspended)
    {
        return enableAmbientClock && !isFullscreenSuppressed && !isPowerSuspended;
    }

    /// <summary>
    /// Evaluates whether a point (in window client DIPs, origin at the window's top-left) falls inside the
    /// sensor strip, which is horizontally centered in a window of <paramref name="windowWidthDip"/>.
    /// </summary>
    public static bool ContainsPoint(double clientX, double clientY, double windowWidthDip)
    {
        if (double.IsNaN(clientX) || double.IsNaN(clientY) || double.IsNaN(windowWidthDip) || windowWidthDip <= 0.0)
        {
            return false;
        }

        double center = windowWidthDip / 2.0;
        double half = WidthDip / 2.0;

        return clientX >= center - half &&
               clientX <= center + half &&
               clientY >= -TopToleranceDip &&
               clientY <= HeightDip;
    }

    /// <summary>
    /// Combines <see cref="IsInteractive"/> and <see cref="ContainsPoint"/> into the single hit-test decision.
    /// </summary>
    public static bool ShouldCapture(
        bool enableAmbientClock,
        bool isFullscreenSuppressed,
        bool isPowerSuspended,
        double clientX,
        double clientY,
        double windowWidthDip)
    {
        return IsInteractive(enableAmbientClock, isFullscreenSuppressed, isPowerSuspended) &&
               ContainsPoint(clientX, clientY, windowWidthDip);
    }
}

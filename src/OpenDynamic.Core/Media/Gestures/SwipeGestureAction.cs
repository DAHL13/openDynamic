namespace OpenDynamic.Core.Media.Gestures;

/// <summary>
/// Result of a horizontal swipe or scroll gesture.
/// </summary>
public enum SwipeGestureAction
{
    /// <summary>
    /// Gesture has not crossed threshold or is in cooldown.
    /// </summary>
    None = 0,

    /// <summary>
    /// Gesture requested skipping to the previous track.
    /// </summary>
    Previous = 1,

    /// <summary>
    /// Gesture requested skipping to the next track.
    /// </summary>
    Next = 2
}

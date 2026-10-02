namespace OpenDynamic.Core.Widgets;

/// <summary>
/// Specifies how an activity source is triggered and presented.
/// </summary>
public enum ActivityActivationMode
{
    /// <summary>
    /// Event-driven activity triggered by system events, background services, timers, or explicit actions (Default).
    /// Holds persistent visibility on the island while active.
    /// </summary>
    Event = 0,

    /// <summary>
    /// Ambient activity triggered exclusively when the cursor hovers over the idle island.
    /// Does not count as persistent activity: does not keep the island visible or prevent transitioning to Hidden.
    /// </summary>
    OnHover = 1
}

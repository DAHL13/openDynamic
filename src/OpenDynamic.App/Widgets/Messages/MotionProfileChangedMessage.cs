using OpenDynamic.Core.Animation;

namespace OpenDynamic.App.Widgets.Messages;

/// <summary>
/// Decoupled message broadcast when the active motion profile or decorative animation policies change.
/// </summary>
public sealed record MotionProfileChangedMessage(MotionProfile Profile);

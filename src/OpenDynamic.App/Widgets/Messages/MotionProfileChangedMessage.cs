using CommunityToolkit.Mvvm.Messaging.Messages;
using OpenDynamic.Core.Animation;

namespace OpenDynamic.App.Widgets.Messages;

/// <summary>
/// Decoupled message broadcast when the active motion profile or decorative animation policies change.
/// </summary>
public sealed class MotionProfileChangedMessage : ValueChangedMessage<MotionProfile>
{
    public MotionProfileChangedMessage(MotionProfile profile) : base(profile)
    {
    }
}

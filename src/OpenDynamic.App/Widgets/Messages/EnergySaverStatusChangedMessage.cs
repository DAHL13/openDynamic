using OpenDynamic.Core.EnergySaver;

namespace OpenDynamic.App.Widgets.Messages;

/// <summary>
/// Decoupled message published when Windows energy saver status changes reactively
/// via WM_POWERBROADCAST (PBT_POWERSETTINGCHANGE / GUID_POWER_SAVING_STATUS).
/// </summary>
public sealed record EnergySaverStatusChangedMessage(EnergySaverState State);

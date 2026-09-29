using OpenDynamic.App.Widgets;
using OpenDynamic.Core.State;
using OpenDynamic.Core.Widgets;

namespace OpenDynamic.App.Widgets.Messages;

/// <summary>
/// Broadcast via <see cref="CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger"/>
/// when an activity source changes its active status, priority, or content.
/// </summary>
public sealed record ActivityChangedMessage(IActivitySource Source);

/// <summary>
/// Broadcast when a widget has been registered with the orchestrator.
/// </summary>
public sealed record WidgetRegisteredMessage(IIslandWidget Widget);

/// <summary>
/// Broadcast when a widget has been unregistered from the orchestrator.
/// </summary>
public sealed record WidgetUnregisteredMessage(IIslandWidget Widget);

/// <summary>
/// Broadcast when an interactive view expansion is requested.
/// </summary>
public sealed record ExpandRequestedMessage(string? WidgetId = null);

/// <summary>
/// Broadcast when an interactive collapse is requested.
/// </summary>
public sealed record CollapseRequestedMessage;

/// <summary>
/// Broadcast when the island transitions to a new state.
/// </summary>
public sealed record IslandStateChangedMessage(IslandState PreviousState, IslandState NewState);

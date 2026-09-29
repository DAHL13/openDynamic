using System.Windows.Controls;
using OpenDynamic.Core.Widgets;

namespace OpenDynamic.App.Widgets;

/// <summary>
/// Defines the lifecycle and view rendering contract for Dynamic Island widgets.
/// Integrates domain activity declaration (<see cref="IActivitySource"/>) with WPF presentation.
/// </summary>
public interface IIslandWidget : IActivitySource, IDisposable
{
    /// <summary>
    /// Current display layout mode assigned to this widget by the orchestrator.
    /// </summary>
    WidgetDisplayMode DisplayMode { get; }

    /// <summary>
    /// Creates or returns the compact pill view for standard display.
    /// </summary>
    UserControl? CreateCompactView();

    /// <summary>
    /// Creates or returns the expanded canvas view for interactive presentation.
    /// </summary>
    UserControl? CreateExpandedView();

    /// <summary>
    /// Creates or returns the compact satellite/split view for multitasking split display.
    /// If null, the orchestrator may fall back to <see cref="CreateCompactView"/>.
    /// </summary>
    UserControl? CreateSplitView();

    /// <summary>
    /// Called once when the widget is registered into the orchestrator.
    /// Must perform any necessary service subscriptions or background listeners.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Notifies the widget of its current visibility on screen and active display mode.
    /// Used by widgets to pause sampling timers when hidden (Golden Rule 1).
    /// </summary>
    void SetDisplayState(WidgetDisplayMode mode, bool isVisible);

    /// <summary>
    /// Notifies the widget that its expanded view is now actively displayed.
    /// </summary>
    void OnExpand();

    /// <summary>
    /// Notifies the widget that the island has collapsed back from expanded mode.
    /// </summary>
    void OnCollapse();
}


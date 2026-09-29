using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using OpenDynamic.App.Animation;
using OpenDynamic.App.Views;
using OpenDynamic.App.Widgets;
using OpenDynamic.App.Widgets.Messages;
using OpenDynamic.Core.State;
using OpenDynamic.Core.Widgets;
using Serilog;

namespace OpenDynamic.App.Orchestration;

/// <summary>
/// Central orchestrator possessing EXCLUSIVE authority over <see cref="IslandStateMachine"/> transitions.
/// Resolves active widget priorities, manages transient lifespans, coordinates view rendering,
/// and guarantees fault isolation (Golden Rule 4) so malfunctioning widgets never crash the application.
/// </summary>
public sealed class IslandOrchestrator : IDisposable
{
    private readonly IslandStateMachine _stateMachine;
    private readonly IslandAnimator _animator;
    private readonly PriorityResolver _priorityResolver;
    private readonly List<IIslandWidget> _widgets = new();
    private readonly HashSet<string> _quarantinedWidgetIds = new();

    private IslandView? _islandView;
    private DispatcherTimer? _transientTimer;
    private bool _userExpanded;
    private bool _disposed;

    private IIslandWidget? _activePrimaryWidget;
    private IIslandWidget? _activeSecondaryWidget;
    private bool _isSplitSwapped;
    private string? _lastSplitPrimaryId;
    private string? _lastSplitSecondaryId;

    // View reuse cache to prevent layered window flickering on property changes
    private UserControl? _currentPrimaryView;
    private string? _currentPrimaryWidgetId;
    private WidgetDisplayMode? _currentPrimaryMode;

    private UserControl? _currentSecondaryView;
    private string? _currentSecondaryWidgetId;
    private WidgetDisplayMode? _currentSecondaryMode;

    public IslandStateMachine StateMachine => _stateMachine;
    public IslandAnimator Animator => _animator;
    public PriorityResolver PriorityResolver => _priorityResolver;

    public IReadOnlyList<IIslandWidget> RegisteredWidgets
    {
        get
        {
            lock (_widgets)
            {
                return _widgets.ToList();
            }
        }
    }

    public IIslandWidget? ActivePrimaryWidget => _activePrimaryWidget;
    public IIslandWidget? ActiveSecondaryWidget => _activeSecondaryWidget;
    public bool IsUserExpanded => _userExpanded;
    public bool IsSplitSwapped => _isSplitSwapped;
    public IReadOnlySet<string> QuarantinedWidgetIds => _quarantinedWidgetIds;


    /// <summary>
    /// Default idle state when no activities are active (defaults to <see cref="IslandState.Hidden"/>).
    /// </summary>
    public IslandState IdleState { get; set; } = IslandState.Hidden;

    public IslandOrchestrator(
        IslandStateMachine stateMachine,
        IslandAnimator animator,
        PriorityResolver? priorityResolver = null)
    {
        _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        _animator = animator ?? throw new ArgumentNullException(nameof(animator));
        _priorityResolver = priorityResolver ?? new PriorityResolver();

        // Subscribe to decoupled WeakReferenceMessenger events
        WeakReferenceMessenger.Default.Register<ActivityChangedMessage>(this, (_, msg) =>
        {
            OnActivityChanged(msg.Source);
        });

        WeakReferenceMessenger.Default.Register<ExpandRequestedMessage>(this, (_, _) =>
        {
            RequestExpand();
        });

        WeakReferenceMessenger.Default.Register<CollapseRequestedMessage>(this, (_, _) =>
        {
            RequestCollapse();
        });
    }

    /// <summary>
    /// Attaches the <see cref="IslandView"/> instance for delivering views.
    /// </summary>
    public void AttachView(IslandView islandView)
    {
        _islandView = islandView ?? throw new ArgumentNullException(nameof(islandView));
        _currentPrimaryView = null;
        _currentPrimaryWidgetId = null;
        _currentPrimaryMode = null;
        _currentSecondaryView = null;
        _currentSecondaryWidgetId = null;
        _currentSecondaryMode = null;
        UpdateOrchestration();
    }

    /// <summary>
    /// Registers a widget, initializes it inside a protected sandbox, and hooks its change notifications.
    /// </summary>
    public void RegisterWidget(IIslandWidget widget)
    {
        ArgumentNullException.ThrowIfNull(widget);

        lock (_widgets)
        {
            if (_widgets.Any(w => w.Id == widget.Id))
            {
                Log.Warning("Widget with ID '{WidgetId}' is already registered.", widget.Id);
                return;
            }

            // Fault isolation: safely initialize widget
            try
            {
                widget.Initialize();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to initialize widget '{WidgetId}'. Quarantining widget to maintain system stability.", widget.Id);
                _quarantinedWidgetIds.Add(widget.Id);
                return;
            }

            widget.Changed += OnWidgetChanged;
            _widgets.Add(widget);
            Log.Information("Widget '{WidgetId}' registered successfully (Priority: {Priority}).", widget.Id, widget.Priority);
        }

        WeakReferenceMessenger.Default.Send(new WidgetRegisteredMessage(widget));
        DispatchToUIThread(UpdateOrchestration);
    }

    /// <summary>
    /// Unregisters a widget, unsubscribes events, and disposes it safely.
    /// </summary>
    public void UnregisterWidget(IIslandWidget widget)
    {
        ArgumentNullException.ThrowIfNull(widget);

        lock (_widgets)
        {
            if (!_widgets.Remove(widget))
            {
                return;
            }

            widget.Changed -= OnWidgetChanged;
        }

        if (_currentPrimaryWidgetId == widget.Id)
        {
            _currentPrimaryView = null;
            _currentPrimaryWidgetId = null;
            _currentPrimaryMode = null;
        }
        if (_currentSecondaryWidgetId == widget.Id)
        {
            _currentSecondaryView = null;
            _currentSecondaryWidgetId = null;
            _currentSecondaryMode = null;
        }

        try
        {
            widget.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error disposing widget '{WidgetId}'.", widget.Id);
        }

        Log.Information("Widget '{WidgetId}' unregistered.", widget.Id);
        WeakReferenceMessenger.Default.Send(new WidgetUnregisteredMessage(widget));
        DispatchToUIThread(UpdateOrchestration);
    }

    private void OnWidgetChanged(object? sender, EventArgs e)
    {
        DispatchToUIThread(UpdateOrchestration);
    }

    private void OnActivityChanged(IActivitySource source)
    {
        DispatchToUIThread(UpdateOrchestration);
    }

    public bool IsFullscreenSuppressed => _isFullscreenSuppressed;
    private bool _isFullscreenSuppressed;

    public bool IsPowerSuspended => _isPowerSuspended;
    private bool _isPowerSuspended;

    /// <summary>
    /// Indicates whether a transient expiration timer is currently running.
    /// </summary>
    public bool HasActiveTransientTimer => _transientTimer != null && _transientTimer.IsEnabled;

    /// <summary>
    /// Immediately hides the island, suspends all transient timers and halts animations
    /// when an application enters fullscreen mode.
    /// </summary>
    public void SuspendForFullscreen()
    {
        _isFullscreenSuppressed = true;
        _userExpanded = false;

        // Cancel transient expiration timer
        _transientTimer?.Stop();
        _transientTimer = null;

        // Halt any animation and snap immediately to Hidden (Golden Rule 1: 0% CPU)
        _animator.SnapTo(IslandState.Hidden);

        lock (_widgets)
        {
            foreach (var widget in _widgets)
            {
                try
                {
                    widget.SetDisplayState(WidgetDisplayMode.Compact, isVisible: false);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error updating display state on widget '{WidgetId}' during fullscreen suspend.", widget.Id);
                }
            }
        }

        Log.Information("IslandOrchestrator: Suspended for fullscreen. Animations halted and island hidden.");
    }

    /// <summary>
    /// Resumes normal orchestration and restores previous active presentation upon exiting fullscreen mode.
    /// </summary>
    public void ResumeFromFullscreen()
    {
        if (!_isFullscreenSuppressed) return;

        _isFullscreenSuppressed = false;
        Log.Information("IslandOrchestrator: Resumed from fullscreen mode. Re-evaluating active widgets.");
        DispatchToUIThread(UpdateOrchestration);
    }

    /// <summary>
    /// Suspends all timers, halts animations, and pauses widget activities when the system enters sleep/suspension (WM_POWERBROADCAST).
    /// </summary>
    public void SuspendForPower()
    {
        _isPowerSuspended = true;

        // Cancel transient expiration timer
        _transientTimer?.Stop();
        _transientTimer = null;

        // Halt any in-flight spring animation
        _animator.SnapTo(_animator.TargetDimensions);

        lock (_widgets)
        {
            foreach (var widget in _widgets)
            {
                try
                {
                    widget.SetDisplayState(WidgetDisplayMode.Compact, isVisible: false);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error updating display state on widget '{WidgetId}' during power suspend.", widget.Id);
                }
            }
        }

        Log.Information("IslandOrchestrator: Suspended for power event (PBT_APMSUSPEND). All timers and animations halted.");
    }

    /// <summary>
    /// Resumes orchestration cleanly when the system wakes up from sleep/suspension.
    /// </summary>
    public void ResumeFromPower()
    {
        if (!_isPowerSuspended) return;

        _isPowerSuspended = false;
        Log.Information("IslandOrchestrator: Resumed from power event. Restoring active widgets and layout.");
        DispatchToUIThread(UpdateOrchestration);
    }

    /// <summary>
    /// Evaluates active widget priorities, commands appropriate state transitions,
    /// schedules expiration for transient activities, and delivers views to <see cref="IslandView"/>.
    /// Guaranteed to run on the WPF UI thread.
    /// </summary>
    public void UpdateOrchestration()
    {
        if (_disposed || _isFullscreenSuppressed || _isPowerSuspended) return;

        // Cancel previous expiration timer
        _transientTimer?.Stop();
        _transientTimer = null;

        List<IIslandWidget> activeCandidates;
        lock (_widgets)
        {
            activeCandidates = _widgets
                .Where(w => !_quarantinedWidgetIds.Contains(w.Id) && w.IsActive)
                .ToList();
        }

        var result = _priorityResolver.Resolve(activeCandidates, DateTimeOffset.UtcNow);

        var resolvedPrimary = result.Primary as IIslandWidget;
        var resolvedSecondary = result.Secondary as IIslandWidget;

        // Support interactive Split multitasking swap (Golden Rule M4)
        if (resolvedPrimary != null && resolvedSecondary != null)
        {
            if (_lastSplitPrimaryId != resolvedPrimary.Id || _lastSplitSecondaryId != resolvedSecondary.Id)
            {
                _lastSplitPrimaryId = resolvedPrimary.Id;
                _lastSplitSecondaryId = resolvedSecondary.Id;
            }

            if (_isSplitSwapped)
            {
                _activePrimaryWidget = resolvedSecondary;
                _activeSecondaryWidget = resolvedPrimary;
            }
            else
            {
                _activePrimaryWidget = resolvedPrimary;
                _activeSecondaryWidget = resolvedSecondary;
            }
        }
        else
        {
            _isSplitSwapped = false;
            _lastSplitPrimaryId = null;
            _lastSplitSecondaryId = null;

            _activePrimaryWidget = resolvedPrimary;
            _activeSecondaryWidget = null;
        }


        // Schedule timer if an active transient alert has an expiration scheduled
        if (result.NextExpirationUtc.HasValue)
        {
            var delay = result.NextExpirationUtc.Value - DateTimeOffset.UtcNow;
            if (delay <= TimeSpan.Zero)
            {
                // Already expired: trigger next cycle asynchronously
                DispatchToUIThread(UpdateOrchestration);
                return;
            }

            _transientTimer = new DispatcherTimer
            {
                Interval = delay
            };
            _transientTimer.Tick += (s, e) =>
            {
                _transientTimer.Stop();
                _transientTimer = null;
                Log.Debug("Transient activity lifespan expired. Re-evaluating priorities.");
                UpdateOrchestration();
            };
            _transientTimer.Start();
        }

        // Determine destination state
        IslandState targetState;
        if (_activePrimaryWidget == null)
        {
            // No active activities: return to idle state
            _userExpanded = false;
            targetState = IdleState;
        }
        else
        {
            if (_userExpanded)
            {
                targetState = IslandState.Expanded;
            }
            else if (_activeSecondaryWidget != null)
            {
                targetState = IslandState.Split;
            }
            else
            {
                targetState = IslandState.Compact;
            }
        }

        // Render views with fault isolation and view reuse to eliminate flickering
        UserControl? primaryView = null;
        UserControl? secondaryView = null;

        var primaryMode = targetState == IslandState.Expanded
            ? WidgetDisplayMode.Expanded
            : WidgetDisplayMode.Compact;

        if (_activePrimaryWidget != null)
        {
            if (_currentPrimaryView != null &&
                _currentPrimaryWidgetId == _activePrimaryWidget.Id &&
                _currentPrimaryMode == primaryMode)
            {
                // Reuse existing view instance to prevent flicker on property updates
                primaryView = _currentPrimaryView;
            }
            else
            {
                primaryView = SafeCreateView(_activePrimaryWidget, primaryMode);
                _currentPrimaryView = primaryView;
                _currentPrimaryWidgetId = _activePrimaryWidget.Id;
                _currentPrimaryMode = primaryMode;
            }
        }
        else
        {
            _currentPrimaryView = null;
            _currentPrimaryWidgetId = null;
            _currentPrimaryMode = null;
        }

        if (_activeSecondaryWidget != null && targetState == IslandState.Split)
        {
            const WidgetDisplayMode secondaryMode = WidgetDisplayMode.Split;
            if (_currentSecondaryView != null &&
                _currentSecondaryWidgetId == _activeSecondaryWidget.Id &&
                _currentSecondaryMode == secondaryMode)
            {
                secondaryView = _currentSecondaryView;
            }
            else
            {
                secondaryView = SafeCreateView(_activeSecondaryWidget, secondaryMode);
                _currentSecondaryView = secondaryView;
                _currentSecondaryWidgetId = _activeSecondaryWidget.Id;
                _currentSecondaryMode = secondaryMode;
            }
        }
        else
        {
            _currentSecondaryView = null;
            _currentSecondaryWidgetId = null;
            _currentSecondaryMode = null;
        }

        // Deliver views to IslandView
        _islandView?.PresentViews(primaryView, secondaryView, targetState);

        // Command state transition with exclusive authority
        TransitionTo(targetState);

        // Synchronize display state and visibility on all registered widgets (Golden Rule 1)
        lock (_widgets)

        {
            foreach (var widget in _widgets)
            {
                try
                {
                    if (_activePrimaryWidget != null &&
                        (ReferenceEquals(widget, _activePrimaryWidget) || widget.Id == _activePrimaryWidget.Id))
                    {
                        widget.SetDisplayState(primaryMode, isVisible: targetState != IslandState.Hidden);
                    }
                    else if (_activeSecondaryWidget != null &&
                             targetState == IslandState.Split &&
                             (ReferenceEquals(widget, _activeSecondaryWidget) || widget.Id == _activeSecondaryWidget.Id))
                    {
                        widget.SetDisplayState(WidgetDisplayMode.Split, isVisible: true);
                    }
                    else
                    {
                        widget.SetDisplayState(WidgetDisplayMode.Compact, isVisible: false);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error updating display state on widget '{WidgetId}'.", widget.Id);
                }
            }
        }
    }



    /// <summary>
    /// Safely creates a view from a widget, catching and quarantining on exception.
    /// </summary>
    private UserControl? SafeCreateView(IIslandWidget widget, WidgetDisplayMode mode)
    {
        try
        {
            return mode switch
            {
                WidgetDisplayMode.Expanded => widget.CreateExpandedView(),
                WidgetDisplayMode.Split => widget.CreateSplitView() ?? widget.CreateCompactView(),
                _ => widget.CreateCompactView()
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception caught during {Mode} view generation for widget '{WidgetId}'. Quarantining widget.", mode, widget.Id);
            QuarantineWidget(widget);
            return null;
        }
    }

    /// <summary>
    /// Safely isolates a malfunctioning widget from future orchestration cycles.
    /// </summary>
    private void QuarantineWidget(IIslandWidget widget)
    {
        lock (_widgets)
        {
            _quarantinedWidgetIds.Add(widget.Id);
        }

        if (_currentPrimaryWidgetId == widget.Id)
        {
            _currentPrimaryView = null;
            _currentPrimaryWidgetId = null;
            _currentPrimaryMode = null;
        }
        if (_currentSecondaryWidgetId == widget.Id)
        {
            _currentSecondaryView = null;
            _currentSecondaryWidgetId = null;
            _currentSecondaryMode = null;
        }

        try
        {
            widget.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception while disposing quarantined widget '{WidgetId}'.", widget.Id);
        }

        Log.Warning("Widget '{WidgetId}' has been quarantined due to an unhandled exception.", widget.Id);

        // Re-evaluate orchestration without the broken widget
        DispatchToUIThread(UpdateOrchestration);
    }

    /// <summary>
    /// Executes state machine transition and triggers animator.
    /// Manages intermediate Compact transition when moving from Hidden to Split or Expanded.
    /// </summary>
    public bool TransitionTo(IslandState targetState)
    {
        if ((_isFullscreenSuppressed || _isPowerSuspended) && targetState != IslandState.Hidden)
        {
            Log.Debug("Transition to {TargetState} suppressed because fullscreen or power suspension is active.", targetState);
            return false;
        }

        if (_stateMachine.CurrentState == targetState)
        {
            return true;
        }

        var previousState = _stateMachine.CurrentState;

        // FSM Rule: Direct transition from Hidden to Split or Expanded is strictly forbidden; pass through Compact.
        if (previousState == IslandState.Hidden && targetState is IslandState.Split or IslandState.Expanded)
        {
            Log.Debug("Stepping through Compact to transition from Hidden to {TargetState}.", targetState);
            _animator.AnimateTo(IslandState.Compact);
        }

        bool transitioned = _animator.AnimateTo(targetState);
        if (transitioned)
        {
            Log.Information("Orchestrator transitioned IslandState: {Previous} -> {Current}", previousState, targetState);
            WeakReferenceMessenger.Default.Send(new IslandStateChangedMessage(previousState, targetState));
        }

        return transitioned;
    }

    /// <summary>
    /// User requested expansion.
    /// </summary>
    public void RequestExpand()
    {
        if (_activePrimaryWidget == null && _stateMachine.CurrentState == IslandState.Hidden)
        {
            // If hidden and no widgets, temporarily reveal compact notch or do nothing
            return;
        }

        _userExpanded = true;

        if (_activePrimaryWidget != null)
        {
            try
            {
                _activePrimaryWidget.OnExpand();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error calling OnExpand on widget '{WidgetId}'.", _activePrimaryWidget.Id);
            }
        }

        UpdateOrchestration();
    }

    /// <summary>
    /// User requested collapse.
    /// </summary>
    public void RequestCollapse()
    {
        _userExpanded = false;

        if (_activePrimaryWidget != null)
        {
            try
            {
                _activePrimaryWidget.OnCollapse();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error calling OnCollapse on widget '{WidgetId}'.", _activePrimaryWidget.Id);
            }
        }

        UpdateOrchestration();
    }

    /// <summary>
    /// Toggles between expanded and collapsed/split presentation.
    /// </summary>
    public void RequestToggleExpand()
    {
        if (_stateMachine.CurrentState == IslandState.Expanded)
        {
            RequestCollapse();
        }
        else
        {
            RequestExpand();
        }
    }

    /// <summary>
    /// Manually commands the island to hide.
    /// </summary>
    public void RequestHide()
    {
        _userExpanded = false;
        TransitionTo(IslandState.Hidden);

        lock (_widgets)
        {
            foreach (var widget in _widgets)
            {
                try
                {
                    widget.SetDisplayState(WidgetDisplayMode.Compact, isVisible: false);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error updating display state on widget '{WidgetId}' during hide.", widget.Id);
                }
            }
        }
    }

    /// <summary>
    /// Restores the island from hidden state.
    /// </summary>
    public void RequestRestore()
    {
        UpdateOrchestration();
    }


    /// <summary>
    /// Interactively swaps primary and secondary activities in Split mode (Hito M4).
    /// Clicking the satellite bubble moves the secondary activity to the main capsule
    /// and the primary activity to the satellite bubble.
    /// </summary>
    public void SwapSplitActivities()
    {
        if (_stateMachine.CurrentState != IslandState.Split &&
            !(_activePrimaryWidget != null && _activeSecondaryWidget != null))
        {
            Log.Debug("SwapSplitActivities ignored: Island is not currently presenting Split multitasking.");
            return;
        }

        _isSplitSwapped = !_isSplitSwapped;
        Log.Information("SwapSplitActivities: Swapped Split presentations. IsSplitSwapped is now {IsSwapped}.", _isSplitSwapped);
        UpdateOrchestration();
    }


    private static void DispatchToUIThread(Action action)
    {
        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher)
        {
            if (dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                dispatcher.InvokeAsync(action);
            }
        }
        else
        {
            action();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _transientTimer?.Stop();
        _transientTimer = null;

        WeakReferenceMessenger.Default.UnregisterAll(this);

        _currentPrimaryView = null;
        _currentPrimaryWidgetId = null;
        _currentPrimaryMode = null;
        _currentSecondaryView = null;
        _currentSecondaryWidgetId = null;
        _currentSecondaryMode = null;

        lock (_widgets)
        {
            foreach (var widget in _widgets)
            {
                widget.Changed -= OnWidgetChanged;
                try
                {
                    widget.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error disposing widget '{WidgetId}' during orchestrator shutdown.", widget.Id);
                }
            }
            _widgets.Clear();
        }
    }
}

using System.Windows.Threading;
using OpenDynamic.Core.Stopwatch;
using OpenDynamic.Core.Timer;
using Serilog;

namespace OpenDynamic.App.Services;

/// <summary>
/// Coordinates a single shared <see cref="DispatcherTimer"/> for all active timing widgets (Timer, Stopwatch).
/// Adheres strictly to Golden Rule 1 (0% CPU in idle, no active timers when paused/stopped)
/// and Golden Rule 11 (centralized lifecycle management).
/// </summary>
public sealed class TimingUiCoordinator : IDisposable
{
    private readonly DispatcherTimer _sharedTimer;
    private readonly ITimerCollection _timerCollection;
    private readonly IStopwatchController _stopwatchController;
    private readonly object _lock = new();

    private Func<bool>? _isTimerWidgetActive;
    private Action? _refreshTimerWidget;

    private Func<bool>? _isStopwatchWidgetActive;
    private Action? _refreshStopwatchWidget;

    private bool _disposed;

    /// <summary>
    /// Indicates whether the shared UI timer is currently running.
    /// </summary>
    public bool IsTimerRunning => _sharedTimer.IsEnabled;

    /// <summary>
    /// Current interval of the shared UI timer.
    /// </summary>
    public TimeSpan CurrentInterval => _sharedTimer.Interval;

    public TimingUiCoordinator(
        ITimerCollection timerCollection,
        IStopwatchController stopwatchController)
    {
        _timerCollection = timerCollection ?? throw new ArgumentNullException(nameof(timerCollection));
        _stopwatchController = stopwatchController ?? throw new ArgumentNullException(nameof(stopwatchController));

        _sharedTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _sharedTimer.Tick += OnSharedTimerTick;
    }

    /// <summary>
    /// Connects the TimerWidget lifecycle callbacks.
    /// </summary>
    public void RegisterTimerWidget(Func<bool> isActivePredicate, Action refreshAction)
    {
        lock (_lock)
        {
            _isTimerWidgetActive = isActivePredicate;
            _refreshTimerWidget = refreshAction;
        }
        EvaluateTimerState();
    }

    /// <summary>
    /// Connects the StopwatchWidget lifecycle callbacks.
    /// </summary>
    public void RegisterStopwatchWidget(Func<bool> isActivePredicate, Action refreshAction)
    {
        lock (_lock)
        {
            _isStopwatchWidgetActive = isActivePredicate;
            _refreshStopwatchWidget = refreshAction;
        }
        EvaluateTimerState();
    }

    /// <summary>
    /// Evaluates active widget states and starts, adjusts interval, or stops the shared UI timer.
    /// </summary>
    public void EvaluateTimerState()
    {
        lock (_lock)
        {
            if (_disposed) return;

            bool timerNeedsTick = (_isTimerWidgetActive?.Invoke() ?? false) && _timerCollection.AnyRunning;
            bool stopwatchNeedsTick = (_isStopwatchWidgetActive?.Invoke() ?? false) && _stopwatchController.State == StopwatchState.Running;

            bool shouldRun = timerNeedsTick || stopwatchNeedsTick;

            if (shouldRun)
            {
                // When stopwatch is active, tick at 100ms for centisecond precision.
                // Otherwise for countdown timers, tick once per second.
                var targetInterval = stopwatchNeedsTick ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromSeconds(1);

                if (_sharedTimer.Interval != targetInterval)
                {
                    _sharedTimer.Interval = targetInterval;
                }

                if (!_sharedTimer.IsEnabled)
                {
                    _sharedTimer.Start();
                    Log.Information("[TimingUiCoordinator] Shared UI DispatcherTimer started (Interval: {Interval}ms).", targetInterval.TotalMilliseconds);
                }
            }
            else
            {
                if (_sharedTimer.IsEnabled)
                {
                    _sharedTimer.Stop();
                    Log.Information("[TimingUiCoordinator] Shared UI DispatcherTimer stopped (0% CPU at idle).");
                }
            }
        }
    }

    private void OnSharedTimerTick(object? sender, EventArgs e)
    {
        bool timerNeedsTick;
        bool stopwatchNeedsTick;
        Action? refreshTimer;
        Action? refreshStopwatch;

        lock (_lock)
        {
            if (_disposed) return;
            timerNeedsTick = (_isTimerWidgetActive?.Invoke() ?? false) && _timerCollection.AnyRunning;
            stopwatchNeedsTick = (_isStopwatchWidgetActive?.Invoke() ?? false) && _stopwatchController.State == StopwatchState.Running;
            refreshTimer = _refreshTimerWidget;
            refreshStopwatch = _refreshStopwatchWidget;
        }

        if (timerNeedsTick)
        {
            _timerCollection.UpdateTick();
            refreshTimer?.Invoke();
        }

        if (stopwatchNeedsTick)
        {
            _stopwatchController.UpdateTick();
            refreshStopwatch?.Invoke();
        }

        if (!timerNeedsTick && !stopwatchNeedsTick)
        {
            EvaluateTimerState();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            _sharedTimer.Stop();
            _sharedTimer.Tick -= OnSharedTimerTick;

            _isTimerWidgetActive = null;
            _refreshTimerWidget = null;
            _isStopwatchWidgetActive = null;
            _refreshStopwatchWidget = null;
        }
    }
}

using System.Windows.Controls;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using OpenDynamic.App.Widgets.Clock.Views;
using OpenDynamic.App.Widgets.Messages;
using OpenDynamic.Core.Clock;
using OpenDynamic.Core.Settings;
using OpenDynamic.Core.Widgets;
using Serilog;

namespace OpenDynamic.App.Widgets.Clock;

/// <summary>
/// Ambient clock widget presenting discrete time, date, and optional ISO week number on hover.
/// Adheres strictly to Golden Rule 1 (0% CPU at idle):
/// - ActivationMode is OnHover, priority 5 (lowest in the island hierarchy).
/// - Never keeps the island visible or prevents transitioning to Hidden.
/// - The DispatcherTimer exists and is active ONLY while the widget is visible on screen;
///   stopped and destroyed immediately when the notch hides.
/// - Tick alignment to minute boundary (:00) prevents unnecessary per-second wakeups.
/// - Reactive clock synchronization via WM_TIMECHANGE with zero polling loops.
/// </summary>
public sealed class AmbientClockWidget : IslandWidgetBase
{
    private readonly AppSettings _settings;
    private readonly ClockTickScheduler _tickScheduler;
    private readonly TimeProvider _timeProvider;
    private readonly Dispatcher _dispatcher;

    private DispatcherTimer? _tickTimer;
    private string _timeText = string.Empty;
    private string _dateShortText = string.Empty;
    private string _dateLongText = string.Empty;
    private string _weekNumberText = string.Empty;
    private bool _showWeekNumber;
    private bool _showDate = true;
    private string _accessibleDescription = string.Empty;

    public override string Id => "clock";

    public string TimeText
    {
        get => _timeText;
        private set => SetProperty(ref _timeText, value);
    }

    public string DateShortText
    {
        get => _dateShortText;
        private set => SetProperty(ref _dateShortText, value);
    }

    public string DateLongText
    {
        get => _dateLongText;
        private set => SetProperty(ref _dateLongText, value);
    }

    public string WeekNumberText
    {
        get => _weekNumberText;
        private set => SetProperty(ref _weekNumberText, value);
    }

    public bool ShowWeekNumber
    {
        get => _showWeekNumber;
        private set => SetProperty(ref _showWeekNumber, value);
    }

    public bool ShowDate
    {
        get => _showDate;
        private set => SetProperty(ref _showDate, value);
    }

    public string AccessibleDescription
    {
        get => _accessibleDescription;
        private set => SetProperty(ref _accessibleDescription, value);
    }

    /// <summary>
    /// Indicates whether the internal update timer is actively allocated and running.
    /// Used for performance verification and Golden Rule 1 compliance.
    /// </summary>
    public bool HasActiveTimer => _tickTimer != null && _tickTimer.IsEnabled;

    public AmbientClockWidget(
        AppSettings settings,
        ClockTickScheduler? tickScheduler = null,
        TimeProvider? timeProvider = null,
        Dispatcher? dispatcher = null)
        : base(settings?.DefaultAmbientClockPriority ?? ActivityPriority.AmbientClock)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _tickScheduler = tickScheduler ?? new ClockTickScheduler(_timeProvider);
        _dispatcher = dispatcher ?? System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        ActivationMode = ActivityActivationMode.OnHover;

        WeakReferenceMessenger.Default.Register<SystemTimeChangedMessage>(this, (_, _) =>
        {
            SynchronizeTime();
        });
    }

    public override void Initialize()
    {
        base.Initialize();

        if (_settings.EnableAmbientClock)
        {
            Priority = _settings.DefaultAmbientClockPriority;
            CurrentActivity = new IslandActivity(
                Id: Id,
                Title: "Reloj",
                Subtitle: "Hora y fecha",
                Priority: Priority,
                IsTransient: false,
                Duration: null);
            IsActive = true;
            Log.Information("AmbientClockWidget: Initialized and enabled (Priority {Priority}, ActivationMode=OnHover).", Priority);
        }
        else
        {
            IsActive = false;
            CurrentActivity = null;
            Log.Information("AmbientClockWidget: Disabled by settings.");
        }
    }

    public override void SetDisplayState(WidgetDisplayMode mode, bool isVisible)
    {
        base.SetDisplayState(mode, isVisible);

        if (isVisible)
        {
            // Immediate update on display: never wait for the first tick
            UpdateClockValues();
            StartOrRescheduleTimer();
        }
        else
        {
            // Stop and release timer immediately when the notch hides (0% CPU at idle)
            StopTimer();
        }
    }

    /// <summary>
    /// Reactively synchronizes time and resets timer schedule without polling (WM_TIMECHANGE).
    /// </summary>
    public void SynchronizeTime()
    {
        void Action()
        {
            if (IsVisibleOnIsland)
            {
                UpdateClockValues();
                StartOrRescheduleTimer();
                Log.Information("AmbientClockWidget: Synchronized reactively with WM_TIMECHANGE.");
            }
        }

        if (_dispatcher.CheckAccess())
        {
            Action();
        }
        else
        {
            _dispatcher.InvokeAsync(Action);
        }
    }

    /// <summary>
    /// Evaluates current timestamp and formats all observable text values according to culture and settings.
    /// </summary>
    public void UpdateClockValues()
    {
        var now = _timeProvider.GetLocalNow();
        TimeText = ClockFormatter.FormatTime(now, _settings.ClockTimeFormat, _settings.ClockShowSeconds);
        DateShortText = ClockFormatter.FormatDateShort(now);
        DateLongText = ClockFormatter.FormatDateLong(now);
        WeekNumberText = ClockFormatter.FormatWeekNumber(now);
        ShowWeekNumber = _settings.ClockShowWeekNumber;
        ShowDate = _settings.ClockShowDate;
        AccessibleDescription = ClockFormatter.FormatAccessibleDescription(now, _settings.ClockTimeFormat, _settings.ClockShowSeconds);
    }

    private void StartOrRescheduleTimer()
    {
        StopTimer();

        if (!IsVisibleOnIsland)
        {
            return;
        }

        var delay = _tickScheduler.GetDelayUntilNextTick(_settings.ClockShowSeconds);

        _tickTimer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher)
        {
            Interval = delay
        };

        _tickTimer.Tick += OnTickTimerFired;
        _tickTimer.Start();

        Log.Debug("AmbientClockWidget: Tick timer scheduled in {DelayMs:F0} ms (SecondsEnabled={ShowSeconds}).",
            delay.TotalMilliseconds, _settings.ClockShowSeconds);
    }

    private void OnTickTimerFired(object? sender, EventArgs e)
    {
        if (!IsVisibleOnIsland)
        {
            StopTimer();
            return;
        }

        UpdateClockValues();
        StartOrRescheduleTimer();
    }

    private void StopTimer()
    {
        if (_tickTimer != null)
        {
            _tickTimer.Stop();
            _tickTimer.Tick -= OnTickTimerFired;
            _tickTimer = null;
            Log.Debug("AmbientClockWidget: Tick timer stopped and destroyed (0% CPU).");
        }
    }

    public override UserControl? CreateCompactView()
    {
        return new ClockCompactView { DataContext = this };
    }

    public override UserControl? CreateExpandedView()
    {
        return new ClockExpandedView { DataContext = this };
    }

    public override UserControl? CreateSplitView()
    {
        return CreateCompactView();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopTimer();
            WeakReferenceMessenger.Default.UnregisterAll(this);
        }

        base.Dispose(disposing);
    }
}

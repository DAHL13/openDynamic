using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using OpenDynamic.App.Widgets.Timer.Views;
using OpenDynamic.Core.Settings;
using OpenDynamic.Core.Timer;
using OpenDynamic.Core.Widgets;
using Serilog;

namespace OpenDynamic.App.Widgets.Timer;

/// <summary>
/// Countdown and Pomodoro widget operating via timestamp-based target times (Golden Rule 5).
/// Emits Priority 50 when running and Priority 100 transient alert for 5 seconds upon expiration.
/// Continues ticking accurately even if the island is Hidden because state lives in the controller.
/// </summary>
public sealed class TimerWidget : IslandWidgetBase
{
    private readonly ITimerController _controller;
    private readonly AppSettings _settings;
    private DispatcherTimer? _tickTimer;
    private bool _isCompletedAlert;

    public override string Id => "timer";

    public string FormattedTime => _controller.CurrentSnapshot.FormattedTime;

    public double ProgressPercent => _controller.CurrentSnapshot.ProgressRatio * 100.0;

    public string ModeDescription => _controller.Mode switch
    {
        TimerMode.PomodoroWork => "Pomodoro (Enfoque)",
        TimerMode.PomodoroBreak => "Pomodoro (Descanso)",
        _ => "Temporizador estándar"
    };

    public string ModeBadgeText => _controller.Mode switch
    {
        TimerMode.PomodoroWork => "Enfoque",
        TimerMode.PomodoroBreak => "Descanso",
        _ => string.Empty
    };

    public bool HasModeBadge => _controller.Mode is TimerMode.PomodoroWork or TimerMode.PomodoroBreak;

    public Visibility AlertBadgeVisibility => _isCompletedAlert ? Visibility.Visible : Visibility.Collapsed;

    public string SatelliteTimeText
    {
        get
        {
            var rem = _controller.RemainingTime;
            if (rem <= TimeSpan.Zero) return "00:00";
            if (rem.TotalHours >= 1) return $"{((int)rem.TotalHours)}h {rem.Minutes}m";
            return $"{rem.Minutes:D2}:{rem.Seconds:D2}";
        }
    }

    public SolidColorBrush StatusColorBrush
    {
        get
        {
            if (_isCompletedAlert) return new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)); // Red
            if (_controller.Mode == TimerMode.PomodoroBreak) return new SolidColorBrush(Color.FromRgb(0x34, 0xC7, 0x59)); // Green
            return new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)); // Orange
        }
    }

    public string PlayPauseIconData => _controller.State == TimerState.Running
        ? "M 4,2 H 7 V 14 H 4 Z M 9,2 H 12 V 14 H 9 Z" // Pause bars
        : "M 4,2 L 14,8 L 4,14 Z"; // Play triangle

    public ICommand TogglePlayPauseCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand AddOneMinuteCommand { get; }
    public ICommand AddFiveMinutesCommand { get; }
    public ICommand SwitchToStandardCommand { get; }
    public ICommand SwitchToPomodoroWorkCommand { get; }
    public ICommand SwitchToPomodoroBreakCommand { get; }

    public TimerWidget(ITimerController controller, AppSettings settings)
        : base(settings?.DefaultTimerPriority ?? ActivityPriority.Timer)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        TogglePlayPauseCommand = new RelayCommand(TogglePlayPause);
        ResetCommand = new RelayCommand(Reset);
        AddOneMinuteCommand = new RelayCommand(() => AddTime(TimeSpan.FromMinutes(1)));
        AddFiveMinutesCommand = new RelayCommand(() => AddTime(TimeSpan.FromMinutes(5)));
        SwitchToStandardCommand = new RelayCommand(() => SwitchMode(TimerMode.Standard));
        SwitchToPomodoroWorkCommand = new RelayCommand(() => SwitchMode(TimerMode.PomodoroWork));
        SwitchToPomodoroBreakCommand = new RelayCommand(() => SwitchMode(TimerMode.PomodoroBreak));

        _controller.Tick += OnControllerTick;
        _controller.Completed += OnControllerCompleted;

        _tickTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _tickTimer.Tick += OnTickTimerTick;

        CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Register<Messages.MotionProfileChangedMessage>(this, (_, msg) =>
        {
            IsDecorativeAllowed = msg.Value.AllowDecorative;
        });
    }

    private bool _isDecorativeAllowed = true;

    /// <summary>
    /// Indicates whether decorative animations are permitted.
    /// In reduced motion mode, alert animations are kept strictly static (Task 4).
    /// </summary>
    public bool IsDecorativeAllowed
    {
        get => _isDecorativeAllowed;
        set => SetProperty(ref _isDecorativeAllowed, value);
    }

    public override void Initialize()
    {
        base.Initialize();
        UpdatePresentation();
    }

    public void Start(TimeSpan? duration = null, TimerMode? mode = null)
    {
        _isCompletedAlert = false;
        _controller.Start(duration, mode);

        Priority = _settings.DefaultTimerPriority;
        IsTransient = false;
        TransientDuration = null;
        IsActive = true;

        UpdatePresentation();
        _tickTimer?.Start();

        Log.Information("TimerWidget started: Duration {Total}, Target {Target}",
            _controller.TotalDuration, _controller.TargetEndTimeUtc);
    }

    public void Pause()
    {
        _controller.Pause();
        _tickTimer?.Stop();
        UpdatePresentation();
        Log.Information("TimerWidget paused at {Remaining}", _controller.RemainingTime);
    }

    public void Resume()
    {
        _controller.Resume();
        _tickTimer?.Start();
        UpdatePresentation();
        Log.Information("TimerWidget resumed toward {Target}", _controller.TargetEndTimeUtc);
    }

    public void TogglePlayPause()
    {
        if (_controller.State == TimerState.Running)
        {
            Pause();
        }
        else if (_controller.State == TimerState.Paused)
        {
            Resume();
        }
        else
        {
            Start();
        }
    }

    public void Reset()
    {
        _tickTimer?.Stop();
        _isCompletedAlert = false;
        _controller.Reset();

        IsActive = false;
        IsTransient = false;
        TransientDuration = null;

        UpdatePresentation();
        Log.Information("TimerWidget reset.");
    }

    public void AddTime(TimeSpan additionalTime)
    {
        _controller.AddTime(additionalTime);
        UpdatePresentation();
    }

    public void SwitchMode(TimerMode mode)
    {
        TimeSpan duration = mode switch
        {
            TimerMode.PomodoroWork => TimeSpan.FromMinutes(_settings.PomodoroWorkDurationMinutes),
            TimerMode.PomodoroBreak => TimeSpan.FromMinutes(_settings.PomodoroBreakDurationMinutes),
            _ => TimeSpan.FromMinutes(10)
        };

        _controller.SetMode(mode, duration);
        UpdatePresentation();
    }

    private void OnTickTimerTick(object? sender, EventArgs e)
    {
        if (_controller.State == TimerState.Running)
        {
            _controller.UpdateTick();
        }
        else
        {
            _tickTimer?.Stop();
        }
    }

    private void OnControllerTick(object? sender, TimerSnapshot snapshot)
    {
        UpdatePresentation();
    }

    private void OnControllerCompleted(object? sender, TimerSnapshot snapshot)
    {
        _tickTimer?.Stop();
        _isCompletedAlert = true;

        var alertDuration = TimeSpan.FromSeconds(_settings.TimerAlertTransientDurationSeconds > 0
            ? _settings.TimerAlertTransientDurationSeconds
            : 5.0);

        Log.Information("TimerWidget countdown completed. Triggering transient alert (Priority {Priority}, Duration {Duration}s).",
            _settings.DefaultTimerAlertPriority, alertDuration.TotalSeconds);

        // Raise transient activity with priority 100 for 5 seconds
        Activate(transientDuration: alertDuration, priorityOverride: _settings.DefaultTimerAlertPriority);

        CurrentActivity = new IslandActivity(
            Id: Id,
            Title: "¡Tiempo cumplido!",
            Subtitle: _controller.Mode == TimerMode.PomodoroWork ? "Fin de enfoque" : "Temporizador",
            Priority: Priority,
            IsTransient: true,
            Duration: alertDuration);

        UpdatePresentation();

        try
        {
            System.Media.SystemSounds.Asterisk.Play();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not play notification audio on timer completion.");
        }
    }

    private void UpdatePresentation()
    {
        OnPropertyChanged(nameof(FormattedTime));
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(ModeDescription));
        OnPropertyChanged(nameof(ModeBadgeText));
        OnPropertyChanged(nameof(HasModeBadge));
        OnPropertyChanged(nameof(AlertBadgeVisibility));
        OnPropertyChanged(nameof(SatelliteTimeText));
        OnPropertyChanged(nameof(StatusColorBrush));
        OnPropertyChanged(nameof(PlayPauseIconData));

        if (IsActive && !_isCompletedAlert)
        {
            CurrentActivity = new IslandActivity(
                Id: Id,
                Title: "Temporizador",
                Subtitle: FormattedTime,
                Priority: Priority,
                IsTransient: IsTransient,
                Duration: TransientDuration);
        }
    }

    public override UserControl CreateCompactView()
    {
        return new TimerCompactView { DataContext = this };
    }

    public override UserControl CreateExpandedView()
    {
        return new TimerExpandedView { DataContext = this };
    }

    public override UserControl CreateSplitView()
    {
        return new TimerSplitView { DataContext = this };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_tickTimer != null)
            {
                _tickTimer.Stop();
                _tickTimer.Tick -= OnTickTimerTick;
                _tickTimer = null;
            }

            _controller.Tick -= OnControllerTick;
            _controller.Completed -= OnControllerCompleted;
        }

        base.Dispose(disposing);
    }
}

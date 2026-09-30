using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OpenDynamic.App.Services;
using OpenDynamic.App.Widgets.Messages;
using OpenDynamic.App.Widgets.Timer.Views;
using OpenDynamic.Core.Settings;
using OpenDynamic.Core.Timer;
using OpenDynamic.Core.Widgets;
using Serilog;

namespace OpenDynamic.App.Widgets.Timer;

/// <summary>
/// Display item wrapper representing an individual timer in the expanded view list.
/// </summary>
public sealed class TimerDisplayItem
{
    private readonly TimerController _controller;
    private readonly Action _onChanged;
    private readonly Action<string> _onDelete;

    public string Id => _controller.Id;
    public string Label => _controller.Label;
    public string FormattedTime => _controller.CurrentSnapshot.FormattedTime;
    public bool IsRunning => _controller.State == TimerState.Running;
    public bool IsPaused => _controller.State == TimerState.Paused;
    public bool IsCompleted => _controller.State == TimerState.Completed;

    public string StatusText => _controller.State switch
    {
        TimerState.Running => "En curso",
        TimerState.Paused => "Pausado",
        TimerState.Completed => "Cumplido",
        _ => "Listo"
    };

    public SolidColorBrush StatusBrush => _controller.State switch
    {
        TimerState.Running => new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)),
        TimerState.Completed => new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)),
        TimerState.Paused => new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x93)),
        _ => new SolidColorBrush(Color.FromRgb(0x63, 0x63, 0x66))
    };

    public string PlayPauseIconData => _controller.State == TimerState.Running
        ? "M 4,2 H 7 V 14 H 4 Z M 9,2 H 12 V 14 H 9 Z"
        : "M 4,2 L 14,8 L 4,14 Z";

    public ICommand TogglePlayPauseCommand { get; }
    public ICommand DeleteCommand { get; }

    public TimerDisplayItem(TimerController controller, Action onChanged, Action<string> onDelete)
    {
        _controller = controller;
        _onChanged = onChanged;
        _onDelete = onDelete;

        TogglePlayPauseCommand = new RelayCommand(() =>
        {
            if (_controller.State == TimerState.Running)
            {
                _controller.Pause();
            }
            else if (_controller.State == TimerState.Paused)
            {
                _controller.Resume();
            }
            else
            {
                _controller.Start();
            }
            _onChanged();
        });

        DeleteCommand = new RelayCommand(() => _onDelete(_controller.Id));
    }
}

/// <summary>
/// Countdown and Pomodoro widget operating via timestamp-based target times (Golden Rule 5).
/// Coordinates up to 5 concurrent timers, shows the earliest finishing timer in the notch capsule,
/// provides quick preset buttons (1, 5, 10, 15 min), and sequences transient completion alerts (5s each).
/// Uses shared <see cref="TimingUiCoordinator"/> so zero timers run when idle (Golden Rule 1 &amp; 11).
/// </summary>
public sealed class TimerWidget : IslandWidgetBase
{
    private readonly ITimerCollection _timerCollection;
    private readonly AppSettings _settings;
    private readonly TimingUiCoordinator _timingCoordinator;
    private readonly ITimerPersistenceService? _persistenceService;

    private readonly DispatcherTimer _alertTimer;
    private bool _isCompletedAlert;
    private string _currentAlertTitle = "¡Tiempo cumplido!";
    private string _currentAlertSubtitle = "Temporizador";
    private bool _isDecorativeAllowed = true;

    public override string Id => "timer";

    public TimerController PrimaryController => _timerCollection.PrimaryTimer;

    public string CurrentTimerLabel => PrimaryController.Label;

    public string FormattedTime => PrimaryController.CurrentSnapshot.FormattedTime;

    public double ProgressPercent => PrimaryController.CurrentSnapshot.ProgressRatio * 100.0;

    public string ModeDescription => PrimaryController.Mode switch
    {
        TimerMode.PomodoroWork => "Pomodoro (Enfoque)",
        TimerMode.PomodoroBreak => "Pomodoro (Descanso)",
        _ => PrimaryController.Label
    };

    public string ModeBadgeText => PrimaryController.Mode switch
    {
        TimerMode.PomodoroWork => "Enfoque",
        TimerMode.PomodoroBreak => "Descanso",
        _ => PrimaryController.Label
    };

    public bool HasModeBadge => PrimaryController.Mode is TimerMode.PomodoroWork or TimerMode.PomodoroBreak;

    public Visibility AlertBadgeVisibility => _isCompletedAlert ? Visibility.Visible : Visibility.Collapsed;

    public string AlertTitle => _currentAlertTitle;
    public string AlertSubtitle => _currentAlertSubtitle;

    public string SatelliteTimeText
    {
        get
        {
            var rem = PrimaryController.RemainingTime;
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
            if (PrimaryController.Mode == TimerMode.PomodoroBreak) return new SolidColorBrush(Color.FromRgb(0x34, 0xC7, 0x59)); // Green
            return new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)); // Orange
        }
    }

    public string PlayPauseIconData => PrimaryController.State == TimerState.Running
        ? "M 4,2 H 7 V 14 H 4 Z M 9,2 H 12 V 14 H 9 Z" // Pause bars
        : "M 4,2 L 14,8 L 4,14 Z"; // Play triangle

    public bool IsDecorativeAllowed
    {
        get => _isDecorativeAllowed;
        set => SetProperty(ref _isDecorativeAllowed, value);
    }

    public ObservableCollection<TimerDisplayItem> DisplayTimers { get; } = new();

    public ICommand TogglePlayPauseCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand AddOneMinuteCommand { get; }
    public ICommand AddFiveMinutesCommand { get; }
    public ICommand SwitchToStandardCommand { get; }
    public ICommand SwitchToPomodoroWorkCommand { get; }
    public ICommand SwitchToPomodoroBreakCommand { get; }
    public ICommand PresetOneMinuteCommand { get; }
    public ICommand PresetFiveMinutesCommand { get; }
    public ICommand PresetTenMinutesCommand { get; }
    public ICommand PresetFifteenMinutesCommand { get; }

    public TimerWidget(
        ITimerCollection timerCollection,
        AppSettings settings,
        TimingUiCoordinator timingCoordinator,
        ITimerPersistenceService? persistenceService = null)
        : base(settings?.DefaultTimerPriority ?? ActivityPriority.Timer)
    {
        _timerCollection = timerCollection ?? throw new ArgumentNullException(nameof(timerCollection));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _timingCoordinator = timingCoordinator ?? throw new ArgumentNullException(nameof(timingCoordinator));
        _persistenceService = persistenceService;

        TogglePlayPauseCommand = new RelayCommand(TogglePlayPause);
        ResetCommand = new RelayCommand(Reset);
        AddOneMinuteCommand = new RelayCommand(() => AddTime(TimeSpan.FromMinutes(1)));
        AddFiveMinutesCommand = new RelayCommand(() => AddTime(TimeSpan.FromMinutes(5)));
        SwitchToStandardCommand = new RelayCommand(() => SwitchMode(TimerMode.Standard));
        SwitchToPomodoroWorkCommand = new RelayCommand(() => SwitchMode(TimerMode.PomodoroWork));
        SwitchToPomodoroBreakCommand = new RelayCommand(() => SwitchMode(TimerMode.PomodoroBreak));

        PresetOneMinuteCommand = new RelayCommand(() => ApplyQuickPreset(1));
        PresetFiveMinutesCommand = new RelayCommand(() => ApplyQuickPreset(5));
        PresetTenMinutesCommand = new RelayCommand(() => ApplyQuickPreset(10));
        PresetFifteenMinutesCommand = new RelayCommand(() => ApplyQuickPreset(15));

        _timerCollection.Tick += OnCollectionTick;
        _timerCollection.AlertTriggered += OnCollectionAlertTriggered;
        _timerCollection.TimersChanged += OnCollectionTimersChanged;

        _alertTimer = new DispatcherTimer();
        _alertTimer.Tick += OnAlertTimerTick;

        _timingCoordinator.RegisterTimerWidget(() => IsActive, RefreshFromTick);

        WeakReferenceMessenger.Default.Register<MotionProfileChangedMessage>(this, (_, msg) =>
        {
            IsDecorativeAllowed = msg.Profile.AllowDecorative;
        });

        // Restore persisted timers if any were saved
        RestorePersistedTimers();
        RebuildDisplayTimers();
    }

    public override void Initialize()
    {
        base.Initialize();
        RebuildDisplayTimers();
        UpdatePresentation();
    }

    public void Start(TimeSpan? duration = null, TimerMode? mode = null)
    {
        _isCompletedAlert = false;
        PrimaryController.Start(duration, mode);

        Priority = _settings.DefaultTimerPriority;
        IsTransient = false;
        TransientDuration = null;
        IsActive = true;

        _timingCoordinator.EvaluateTimerState();
        _persistenceService?.Save(_timerCollection.Timers);

        UpdatePresentation();
        Log.Information("TimerWidget started primary: Duration {Total}, Target {Target}",
            PrimaryController.TotalDuration, PrimaryController.TargetEndTimeUtc);
    }

    public void Pause()
    {
        PrimaryController.Pause();
        _timingCoordinator.EvaluateTimerState();
        _persistenceService?.Save(_timerCollection.Timers);
        UpdatePresentation();
        Log.Information("TimerWidget paused at {Remaining}", PrimaryController.RemainingTime);
    }

    public void Resume()
    {
        PrimaryController.Resume();
        _timingCoordinator.EvaluateTimerState();
        _persistenceService?.Save(_timerCollection.Timers);
        UpdatePresentation();
        Log.Information("TimerWidget resumed toward {Target}", PrimaryController.TargetEndTimeUtc);
    }

    public void TogglePlayPause()
    {
        if (PrimaryController.State == TimerState.Running)
        {
            Pause();
        }
        else if (PrimaryController.State == TimerState.Paused)
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
        _isCompletedAlert = false;
        _alertTimer.Stop();
        PrimaryController.Reset();

        if (!_timerCollection.AnyRunning)
        {
            IsActive = false;
            IsTransient = false;
            TransientDuration = null;
        }

        _timingCoordinator.EvaluateTimerState();
        _persistenceService?.Save(_timerCollection.Timers);
        UpdatePresentation();
        Log.Information("TimerWidget primary reset.");
    }

    public void AddTime(TimeSpan additionalTime)
    {
        PrimaryController.AddTime(additionalTime);
        _timingCoordinator.EvaluateTimerState();
        _persistenceService?.Save(_timerCollection.Timers);
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

        PrimaryController.SetMode(mode, duration);
        _timingCoordinator.EvaluateTimerState();
        _persistenceService?.Save(_timerCollection.Timers);
        UpdatePresentation();
    }

    public void ApplyQuickPreset(int minutes)
    {
        var duration = TimeSpan.FromMinutes(minutes);

        if (_timerCollection.Timers.Count < _timerCollection.MaxTimers)
        {
            // Add a new preset timer and start it
            var newTimer = _timerCollection.AddTimer($"Temp {minutes}m", duration);
            newTimer.Start();
        }
        else
        {
            // Update and restart primary timer with preset duration
            PrimaryController.Start(duration, TimerMode.Standard);
        }

        Priority = _settings.DefaultTimerPriority;
        IsTransient = false;
        TransientDuration = null;
        IsActive = true;

        _timingCoordinator.EvaluateTimerState();
        _persistenceService?.Save(_timerCollection.Timers);
        RebuildDisplayTimers();
        UpdatePresentation();

        Log.Information("[TimerWidget] Applied preset {Minutes}m.", minutes);
    }

    public void DeleteTimer(string id)
    {
        _timerCollection.RemoveTimer(id);
        _timingCoordinator.EvaluateTimerState();
        _persistenceService?.Save(_timerCollection.Timers);
        RebuildDisplayTimers();
        UpdatePresentation();
    }

    public void RefreshFromTick()
    {
        UpdatePresentation();
    }

    private void OnCollectionTick(object? sender, TimerSnapshot snapshot)
    {
        UpdatePresentation();
    }

    private void OnCollectionAlertTriggered(object? sender, TimerAlert alert)
    {
        _isCompletedAlert = true;
        _currentAlertTitle = "¡Tiempo cumplido!";
        _currentAlertSubtitle = alert.Label;

        var alertDuration = TimeSpan.FromSeconds(_settings.TimerAlertTransientDurationSeconds > 0
            ? _settings.TimerAlertTransientDurationSeconds
            : 5.0);

        Log.Information("TimerWidget transient alert triggered for '{Label}' (Priority {Priority}, Duration {Duration}s).",
            alert.Label, _settings.DefaultTimerAlertPriority, alertDuration.TotalSeconds);

        Activate(transientDuration: alertDuration, priorityOverride: _settings.DefaultTimerAlertPriority);

        CurrentActivity = new IslandActivity(
            Id: Id,
            Title: _currentAlertTitle,
            Subtitle: _currentAlertSubtitle,
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

        // Schedule 5s alert expiration to cycle through the queue
        _alertTimer.Stop();
        _alertTimer.Interval = alertDuration;
        _alertTimer.Start();
    }

    private void OnAlertTimerTick(object? sender, EventArgs e)
    {
        _alertTimer.Stop();

        // Check if there are more pending alerts in sequence
        var nextAlert = _timerCollection.DismissActiveAlertAndGetNext();
        if (nextAlert != null)
        {
            // Another alert was pending and was promoted
            return;
        }

        _isCompletedAlert = false;

        if (_timerCollection.AnyRunning)
        {
            Priority = _settings.DefaultTimerPriority;
            IsTransient = false;
            TransientDuration = null;
        }
        else
        {
            IsActive = false;
            IsTransient = false;
            TransientDuration = null;
        }

        _timingCoordinator.EvaluateTimerState();
        _persistenceService?.Save(_timerCollection.Timers);
        UpdatePresentation();
    }

    private void OnCollectionTimersChanged(object? sender, EventArgs e)
    {
        RebuildDisplayTimers();
        _timingCoordinator.EvaluateTimerState();
        _persistenceService?.Save(_timerCollection.Timers);
        UpdatePresentation();
    }

    private void RebuildDisplayTimers()
    {
        DisplayTimers.Clear();
        foreach (var timer in _timerCollection.Timers)
        {
            DisplayTimers.Add(new TimerDisplayItem(timer, () =>
            {
                _timingCoordinator.EvaluateTimerState();
                _persistenceService?.Save(_timerCollection.Timers);
                UpdatePresentation();
            }, DeleteTimer));
        }
    }

    private void RestorePersistedTimers()
    {
        if (_persistenceService == null) return;

        try
        {
            var result = _persistenceService.Restore();

            // Restore active/paused timers
            foreach (var rec in result.RestoredTimers)
            {
                if (rec.Id == "primary")
                {
                    // Primary already exists
                    var primary = _timerCollection.PrimaryTimer;
                    primary.Label = rec.Label;
                    primary.SetMode(rec.Mode, TimeSpan.FromSeconds(rec.TotalDurationSeconds));
                    if (rec.State == TimerState.Running && rec.TargetEndTimeUtc.HasValue)
                    {
                        var remaining = rec.TargetEndTimeUtc.Value - DateTimeOffset.UtcNow;
                        if (remaining > TimeSpan.Zero)
                        {
                            primary.Start(remaining, rec.Mode);
                        }
                    }
                }
                else if (_timerCollection.Timers.Count < _timerCollection.MaxTimers)
                {
                    var timer = _timerCollection.AddTimer(rec.Label, TimeSpan.FromSeconds(rec.TotalDurationSeconds), rec.Mode);
                    if (rec.State == TimerState.Running && rec.TargetEndTimeUtc.HasValue)
                    {
                        var remaining = rec.TargetEndTimeUtc.Value - DateTimeOffset.UtcNow;
                        if (remaining > TimeSpan.Zero)
                        {
                            timer.Start(remaining, rec.Mode);
                        }
                    }
                }
            }

            // If any expired while closed, notify user once
            foreach (var exp in result.ExpiredWhileClosed)
            {
                Log.Information("[TimerWidget] Timer '{Label}' expired while application was closed.", exp.Label);
            }

            if (_timerCollection.AnyRunning)
            {
                IsActive = true;
                Priority = _settings.DefaultTimerPriority;
                _timingCoordinator.EvaluateTimerState();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[TimerWidget] Could not restore persisted timers.");
        }
    }

    private void UpdatePresentation()
    {
        OnPropertyChanged(nameof(PrimaryController));
        OnPropertyChanged(nameof(CurrentTimerLabel));
        OnPropertyChanged(nameof(FormattedTime));
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(ModeDescription));
        OnPropertyChanged(nameof(ModeBadgeText));
        OnPropertyChanged(nameof(HasModeBadge));
        OnPropertyChanged(nameof(AlertBadgeVisibility));
        OnPropertyChanged(nameof(AlertTitle));
        OnPropertyChanged(nameof(AlertSubtitle));
        OnPropertyChanged(nameof(SatelliteTimeText));
        OnPropertyChanged(nameof(StatusColorBrush));
        OnPropertyChanged(nameof(PlayPauseIconData));

        // Refresh individual items
        foreach (var item in DisplayTimers)
        {
            // Triggers property changed for display items
        }

        if (IsActive && !_isCompletedAlert)
        {
            CurrentActivity = new IslandActivity(
                Id: Id,
                Title: "Temporizador",
                Subtitle: $"{CurrentTimerLabel}: {FormattedTime}",
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
            _alertTimer.Stop();
            _alertTimer.Tick -= OnAlertTimerTick;

            _timerCollection.Tick -= OnCollectionTick;
            _timerCollection.AlertTriggered -= OnCollectionAlertTriggered;
            _timerCollection.TimersChanged -= OnCollectionTimersChanged;
        }

        base.Dispose(disposing);
    }
}

using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using OpenDynamic.App.Services;
using OpenDynamic.App.Widgets.Stopwatch.Views;
using OpenDynamic.Core.Settings;
using OpenDynamic.Core.Stopwatch;
using OpenDynamic.Core.Widgets;
using Serilog;

namespace OpenDynamic.App.Widgets.Stopwatch;

/// <summary>
/// Continuous stopwatch widget operating via timestamp differences with zero drift (Golden Rule 5).
/// Operates at ActivityPriority.Stopwatch (45). Uses shared <see cref="TimingUiCoordinator"/> for UI updates.
/// </summary>
public sealed class StopwatchWidget : IslandWidgetBase
{
    private readonly IStopwatchController _controller;
    private readonly AppSettings _settings;
    private readonly TimingUiCoordinator _timingCoordinator;

    public override string Id => "stopwatch";

    public string FormattedTime => _controller.CurrentSnapshot.FormattedElapsed;

    public string FormattedPrecise => _controller.CurrentSnapshot.FormattedPrecise;

    public string FormattedCurrentLap => _controller.CurrentSnapshot.FormattedCurrentLap;

    public int LapCount => _controller.Laps.Count;

    public bool HasLaps => _controller.Laps.Count > 0;

    public bool IsRunning => _controller.State == StopwatchState.Running;

    public bool IsPaused => _controller.State == StopwatchState.Paused;

    public ObservableCollection<StopwatchLap> DisplayLaps { get; } = new();

    public string SatelliteTimeText
    {
        get
        {
            var elapsed = _controller.ElapsedTime;
            if (elapsed.TotalHours >= 1) return $"{((int)elapsed.TotalHours)}h {elapsed.Minutes}m";
            return $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
        }
    }

    public SolidColorBrush StatusColorBrush => new(Color.FromRgb(0x0A, 0x84, 0xFF)); // iOS/macOS Dynamic Island System Blue

    public string PlayPauseIconData => _controller.State == StopwatchState.Running
        ? "M 4,2 H 7 V 14 H 4 Z M 9,2 H 12 V 14 H 9 Z" // Pause bars
        : "M 4,2 L 14,8 L 4,14 Z"; // Play triangle

    public string PlayPauseButtonText => _controller.State switch
    {
        StopwatchState.Running => "Pausar",
        StopwatchState.Paused => "Continuar",
        _ => "Iniciar"
    };

    public ICommand TogglePlayPauseCommand { get; }
    public ICommand LapCommand { get; }
    public ICommand ResetCommand { get; }

    public StopwatchWidget(
        IStopwatchController controller,
        AppSettings settings,
        TimingUiCoordinator timingCoordinator)
        : base(settings?.DefaultStopwatchPriority ?? ActivityPriority.Stopwatch)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _timingCoordinator = timingCoordinator ?? throw new ArgumentNullException(nameof(timingCoordinator));

        TogglePlayPauseCommand = new RelayCommand(TogglePlayPause);
        LapCommand = new RelayCommand(Lap);
        ResetCommand = new RelayCommand(Reset);

        _controller.Tick += OnControllerTick;
        _controller.LapRecorded += OnControllerLapRecorded;

        // Register with shared UI timer coordinator
        _timingCoordinator.RegisterStopwatchWidget(() => IsActive, RefreshFromTick);
    }

    public override void Initialize()
    {
        base.Initialize();
        UpdatePresentation();
    }

    public void Start()
    {
        if (!_settings.EnableStopwatchWidget)
        {
            return;
        }

        _controller.Start();
        Priority = _settings.DefaultStopwatchPriority;
        IsTransient = false;
        TransientDuration = null;
        IsActive = true;

        _timingCoordinator.EvaluateTimerState();
        UpdatePresentation();

        Log.Information("[StopwatchWidget] Started at {Time}", _controller.ElapsedTime);
    }

    public void Pause()
    {
        _controller.Pause();
        _timingCoordinator.EvaluateTimerState();
        UpdatePresentation();

        Log.Information("[StopwatchWidget] Paused at {Time}", _controller.ElapsedTime);
    }

    public void TogglePlayPause()
    {
        if (_controller.State == StopwatchState.Running)
        {
            Pause();
        }
        else
        {
            Start();
        }
    }

    public void Lap()
    {
        if (_controller.State == StopwatchState.Running)
        {
            var lap = _controller.Lap();
            UpdatePresentation();
            Log.Information("[StopwatchWidget] Lap {Number} recorded: {LapTime} (Split: {Split})",
                lap.LapNumber, lap.FormattedLap, lap.FormattedSplit);
        }
    }

    public void Reset()
    {
        _controller.Reset();
        IsActive = false;
        IsTransient = false;
        TransientDuration = null;

        _timingCoordinator.EvaluateTimerState();
        DisplayLaps.Clear();
        UpdatePresentation();

        Log.Information("[StopwatchWidget] Reset to 0.");
    }

    public void RefreshFromTick()
    {
        UpdatePresentation();
    }

    private void OnControllerTick(object? sender, StopwatchSnapshot snapshot)
    {
        UpdatePresentation();
    }

    private void OnControllerLapRecorded(object? sender, StopwatchLap lap)
    {
        // Insert latest lap at top for clear list viewing
        DisplayLaps.Insert(0, lap);
        UpdatePresentation();
    }

    private void UpdatePresentation()
    {
        OnPropertyChanged(nameof(FormattedTime));
        OnPropertyChanged(nameof(FormattedPrecise));
        OnPropertyChanged(nameof(FormattedCurrentLap));
        OnPropertyChanged(nameof(LapCount));
        OnPropertyChanged(nameof(HasLaps));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(SatelliteTimeText));
        OnPropertyChanged(nameof(PlayPauseIconData));
        OnPropertyChanged(nameof(PlayPauseButtonText));
        OnPropertyChanged(nameof(StatusColorBrush));

        if (IsActive)
        {
            CurrentActivity = new IslandActivity(
                Id: Id,
                Title: "Cronómetro",
                Subtitle: FormattedTime,
                Priority: Priority,
                IsTransient: false,
                Duration: null);
        }
    }

    public override UserControl CreateCompactView()
    {
        return new StopwatchCompactView { DataContext = this };
    }

    public override UserControl CreateExpandedView()
    {
        return new StopwatchExpandedView { DataContext = this };
    }

    public override UserControl CreateSplitView()
    {
        return new StopwatchSplitView { DataContext = this };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _controller.Tick -= OnControllerTick;
            _controller.LapRecorded -= OnControllerLapRecorded;
        }

        base.Dispose(disposing);
    }
}

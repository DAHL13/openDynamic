using System.Windows.Controls;
using System.Windows.Threading;
using OpenDynamic.App.Services;
using OpenDynamic.App.Widgets.Hardware.Views;
using OpenDynamic.Core.EnergySaver;
using OpenDynamic.Core.Hardware;
using OpenDynamic.Core.Settings;
using OpenDynamic.Core.Widgets;
using Serilog;

namespace OpenDynamic.App.Widgets.Hardware;

/// <summary>
/// Hardware monitoring widget visualizing CPU, physical memory, and optional GPU metrics.
/// Strict adherence to Golden Rule 1: The sampling timer runs ONLY when the widget is actively
/// visible on screen (DisplayMode == Compact || Expanded || Split and IsVisibleOnIsland == true).
/// Halts completely when hidden or inactive to ensure 0% CPU consumption at idle.
/// </summary>
public sealed class HardwareWidget : IslandWidgetBase
{
    private readonly HardwareService _hardwareService;
    private readonly AppSettings _settings;
    private readonly IResourceProfileProvider? _resourceProfileProvider;
    private DispatcherTimer? _sampleTimer;

    private double _cpuUsagePercent;
    private double _ramUsagePercent;
    private double _usedRamGb;
    private double _totalRamGb;
    private double? _gpuUsagePercent;

    public override string Id => "hardware";

    public double CpuUsagePercent
    {
        get => _cpuUsagePercent;
        private set
        {
            if (SetProperty(ref _cpuUsagePercent, value))
            {
                OnPropertyChanged(nameof(CpuSummaryText));
            }
        }
    }

    public double RamUsagePercent
    {
        get => _ramUsagePercent;
        private set
        {
            if (SetProperty(ref _ramUsagePercent, value))
            {
                OnPropertyChanged(nameof(RamSummaryText));
                OnPropertyChanged(nameof(MemorySummaryText));
                OnPropertyChanged(nameof(RamPercentage));
                OnPropertyChanged(nameof(MemoryUsage));
                OnPropertyChanged(nameof(MemoryPercentage));
            }
        }
    }

    /// <summary>
    /// Semantic alias for <see cref="RamUsagePercent"/> (0.0 to 100.0).
    /// </summary>
    public double RamPercentage => RamUsagePercent;

    /// <summary>
    /// Semantic alias for <see cref="RamUsagePercent"/> (0.0 to 100.0).
    /// </summary>
    public double MemoryUsage => RamUsagePercent;

    /// <summary>
    /// Semantic alias for <see cref="RamUsagePercent"/> (0.0 to 100.0).
    /// </summary>
    public double MemoryPercentage => RamUsagePercent;

    public double UsedRamGb
    {
        get => _usedRamGb;
        private set
        {
            if (SetProperty(ref _usedRamGb, value))
            {
                OnPropertyChanged(nameof(RamDetailText));
                OnPropertyChanged(nameof(RamUsageText));
                OnPropertyChanged(nameof(MemoryUsageText));
            }
        }
    }

    public double TotalRamGb
    {
        get => _totalRamGb;
        private set
        {
            if (SetProperty(ref _totalRamGb, value))
            {
                OnPropertyChanged(nameof(RamDetailText));
                OnPropertyChanged(nameof(RamUsageText));
                OnPropertyChanged(nameof(MemoryUsageText));
            }
        }
    }

    public double? GpuUsagePercent
    {
        get => _gpuUsagePercent;
        private set
        {
            if (SetProperty(ref _gpuUsagePercent, value))
            {
                OnPropertyChanged(nameof(GpuSummaryText));
            }
        }
    }

    public string CpuSummaryText => $"{CpuUsagePercent:0}%";
    public string RamSummaryText => $"{RamUsagePercent:0}%";
    public string MemorySummaryText => RamSummaryText;
    public string MemoryUsageText => $"{UsedRamGb:0.0} / {TotalRamGb:0.0} GB";
    public string RamUsageText => MemoryUsageText;
    public string RamDetailText => MemoryUsageText;
    public string GpuSummaryText => GpuUsagePercent.HasValue ? $"{GpuUsagePercent.Value:0}%" : "N/A";

    public HardwareWidget(
        HardwareService hardwareService,
        AppSettings settings,
        IResourceProfileProvider? resourceProfileProvider = null)
        : base(settings?.DefaultHardwarePriority ?? ActivityPriority.Hardware)
    {
        _hardwareService = hardwareService ?? throw new ArgumentNullException(nameof(hardwareService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _resourceProfileProvider = resourceProfileProvider;
    }

    public override void Initialize()
    {
        base.Initialize();

        if (_resourceProfileProvider != null)
        {
            _resourceProfileProvider.ResourceProfileChanged += OnResourceProfileChanged;
        }

        if (_settings.EnableHardwareMonitoring)
        {
            EnableMonitoring();
        }
        else
        {
            IsActive = false;
            CurrentActivity = null;
        }
    }

    /// <summary>
    /// Explicitly enables hardware telemetry monitoring.
    /// Sets IsActive to true, registers the continuous activity, and starts sampling if visible.
    /// </summary>
    public void EnableMonitoring()
    {
        _settings.EnableHardwareMonitoring = true;
        Priority = _settings.DefaultHardwarePriority;
        CurrentActivity = new IslandActivity(
            Id: Id,
            Title: "Monitor de Rendimiento",
            Subtitle: "CPU & RAM",
            Priority: Priority,
            IsTransient: false,
            Duration: null);
        IsActive = true;

        SampleNow();
        UpdateSamplingTimerState();
        Log.Information("HardwareWidget: Telemetry monitoring enabled (Priority: {Priority}).", Priority);
    }

    /// <summary>
    /// Explicitly disables hardware telemetry monitoring.
    /// Sets IsActive to false, clears CurrentActivity, and halts the sampling timer.
    /// </summary>
    public void DisableMonitoring()
    {
        _settings.EnableHardwareMonitoring = false;
        IsActive = false;
        CurrentActivity = null;

        UpdateSamplingTimerState();
        Log.Information("HardwareWidget: Telemetry monitoring disabled.");
    }

    /// <summary>
    /// Toggles hardware monitoring state between active and inactive.
    /// </summary>
    public void ToggleMonitoring()
    {
        if (IsActive)
        {
            DisableMonitoring();
        }
        else
        {
            EnableMonitoring();
        }
    }

    public override void SetDisplayState(WidgetDisplayMode mode, bool isVisible)
    {
        base.SetDisplayState(mode, isVisible);
        UpdateSamplingTimerState();
    }

    /// <summary>
    /// Strict Visibility Condition (Golden Rule 1):
    /// Activates sampling timer ONLY when actively visible on screen and monitoring is active.
    /// Halts completely when hidden or inactive to ensure 0% CPU consumption.
    /// </summary>
    private void UpdateSamplingTimerState()
    {
        bool shouldSample = IsActive &&
                            IsVisibleOnIsland &&
                            (DisplayMode == WidgetDisplayMode.Compact ||
                             DisplayMode == WidgetDisplayMode.Expanded ||
                             DisplayMode == WidgetDisplayMode.Split);

        if (shouldSample)
        {
            TimeSpan currentInterval = _resourceProfileProvider != null
                ? _resourceProfileProvider.CurrentProfile.HardwareSamplingInterval
                : TimeSpan.FromSeconds(_settings.HardwareSamplingIntervalSeconds > 0 ? _settings.HardwareSamplingIntervalSeconds : 2.0);

            if (_sampleTimer == null)
            {
                _sampleTimer = new DispatcherTimer
                {
                    Interval = currentInterval
                };
                _sampleTimer.Tick += OnSampleTimerTick;
            }
            else if (_sampleTimer.Interval != currentInterval)
            {
                _sampleTimer.Interval = currentInterval;
            }

            if (!_sampleTimer.IsEnabled)
            {
                _hardwareService.ResetCpuBaseline();
                SampleNow();
                _sampleTimer.Start();
                Log.Debug("HardwareWidget: Sampling timer started (interval: {Interval}s).", _sampleTimer.Interval.TotalSeconds);
            }
        }
        else
        {
            if (_sampleTimer != null && _sampleTimer.IsEnabled)
            {
                _sampleTimer.Stop();
                Log.Debug("HardwareWidget: Sampling timer halted for zero CPU consumption.");
            }
        }
    }

    private void OnResourceProfileChanged(object? sender, ResourceProfile profile)
    {
        if (_sampleTimer != null && profile.HardwareSamplingInterval > TimeSpan.Zero)
        {
            _sampleTimer.Interval = profile.HardwareSamplingInterval;
            Log.Information("HardwareWidget: Telemetry sampling interval dynamically updated to {Interval}s (ResourceProfile IsEfficient={IsEfficient}).",
                profile.HardwareSamplingInterval.TotalSeconds, profile.IsEfficientModeActive);
        }
    }

    private void OnSampleTimerTick(object? sender, EventArgs e)
    {
        SampleNow();
    }

    private void SampleNow()
    {
        try
        {
            var snap = _hardwareService.Sample();
            CpuUsagePercent = snap.CpuUsagePercent;
            RamUsagePercent = snap.RamUsagePercent;
            UsedRamGb = snap.UsedRamGb;
            TotalRamGb = snap.TotalRamGb;
            GpuUsagePercent = snap.GpuUsagePercent;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error sampling hardware metrics in HardwareWidget.");
        }
    }

    public override UserControl CreateCompactView()
    {
        return new HardwareCompactView { DataContext = this };
    }

    public override UserControl CreateExpandedView()
    {
        SampleNow();
        return new HardwareExpandedView { DataContext = this };
    }

    public override UserControl CreateSplitView()
    {
        return new HardwareSplitView { DataContext = this };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_resourceProfileProvider != null)
            {
                _resourceProfileProvider.ResourceProfileChanged -= OnResourceProfileChanged;
            }

            if (_sampleTimer != null)
            {
                _sampleTimer.Stop();
                _sampleTimer.Tick -= OnSampleTimerTick;
                _sampleTimer = null;
            }
        }

        base.Dispose(disposing);
    }
}

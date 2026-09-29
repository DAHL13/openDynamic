using System.Windows.Controls;
using System.Windows.Threading;
using OpenDynamic.App.Services;
using OpenDynamic.App.Widgets.Hardware.Views;
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
            }
        }
    }

    public double UsedRamGb
    {
        get => _usedRamGb;
        private set
        {
            if (SetProperty(ref _usedRamGb, value))
            {
                OnPropertyChanged(nameof(RamDetailText));
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
    public string RamDetailText => $"{UsedRamGb:0.1} / {TotalRamGb:0.1} GB";
    public string GpuSummaryText => GpuUsagePercent.HasValue ? $"{GpuUsagePercent.Value:0}%" : "N/A";

    public HardwareWidget(HardwareService hardwareService, AppSettings settings)
        : base(settings?.DefaultHardwarePriority ?? ActivityPriority.Hardware)
    {
        _hardwareService = hardwareService ?? throw new ArgumentNullException(nameof(hardwareService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public override void Initialize()
    {
        base.Initialize();

        if (_settings.EnableHardwareMonitoring)
        {
            Priority = _settings.DefaultHardwarePriority;
            IsActive = true;
            CurrentActivity = new IslandActivity(
                Id: Id,
                Title: "Monitor de Rendimiento",
                Subtitle: "CPU & RAM",
                Priority: Priority,
                IsTransient: false,
                Duration: null);


            Log.Information("HardwareWidget initialized with priority {Priority}.", Priority);
        }
    }

    public override void SetDisplayState(WidgetDisplayMode mode, bool isVisible)
    {
        base.SetDisplayState(mode, isVisible);
        UpdateSamplingTimerState();
    }

    /// <summary>
    /// Strict Visibility Condition (Golden Rule 1):
    /// Activates sampling timer ONLY when actively visible on screen.
    /// Halts completely when hidden to ensure 0% CPU consumption.
    /// </summary>
    private void UpdateSamplingTimerState()
    {
        bool shouldSample = IsVisibleOnIsland &&
                            (DisplayMode == WidgetDisplayMode.Compact ||
                             DisplayMode == WidgetDisplayMode.Expanded ||
                             DisplayMode == WidgetDisplayMode.Split);

        if (shouldSample)
        {
            if (_sampleTimer == null)
            {
                double intervalSecs = _settings.HardwareSamplingIntervalSeconds > 0
                    ? _settings.HardwareSamplingIntervalSeconds
                    : 2.0;

                _sampleTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(intervalSecs)
                };
                _sampleTimer.Tick += OnSampleTimerTick;
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

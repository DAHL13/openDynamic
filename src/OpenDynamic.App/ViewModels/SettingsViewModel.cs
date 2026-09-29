using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenDynamic.App.Orchestration;
using OpenDynamic.App.Services;
using OpenDynamic.App.Widgets.Hardware;
using OpenDynamic.App.Windowing;
using OpenDynamic.Core.Autostart;
using OpenDynamic.Core.Settings;
using Serilog;

namespace OpenDynamic.App.ViewModels;

/// <summary>
/// ViewModel for the application settings window.
/// Manages configuration state, debounced persistence, and real-time live application to the active island.
/// All user text and numeric input is strictly isolated to this ViewModel per Golden Rule 3 and project requirements.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IslandOrchestrator _orchestrator;
    private readonly WindowPositioner _windowPositioner;
    private readonly IHotkeyService _hotkeyService;
    private readonly IAutostartService _autostartService;
    private readonly Func<IslandWindow>? _getIslandWindow;

    private readonly AppSettings _settings;

    [ObservableProperty]
    private ObservableCollection<string> _availableMonitors = new();

    [ObservableProperty]
    private int _targetMonitorIndex;

    [ObservableProperty]
    private double _offsetX;

    [ObservableProperty]
    private double _offsetY;

    [ObservableProperty]
    private double _capsuleWidth;

    [ObservableProperty]
    private double _capsuleHeight;

    [ObservableProperty]
    private double _capsuleCornerRadius;

    [ObservableProperty]
    private double _scaleFactor;

    // Multimedia
    [ObservableProperty]
    private bool _enableMediaWidget;

    [ObservableProperty]
    private int _defaultMediaPriority;

    [ObservableProperty]
    private int _mediaPauseGracePeriodSeconds;

    // Volume
    [ObservableProperty]
    private bool _enableVolumeWidget;

    [ObservableProperty]
    private int _defaultVolumePriority;

    [ObservableProperty]
    private double _volumeTransientDurationSeconds;

    // Battery
    [ObservableProperty]
    private bool _enableBatteryWidget;

    [ObservableProperty]
    private int _defaultBatteryPriority;

    [ObservableProperty]
    private int _batteryLowThresholdPercent;

    [ObservableProperty]
    private int _batteryCriticalThresholdPercent;

    // Hardware
    [ObservableProperty]
    private bool _enableHardwareMonitoring;

    [ObservableProperty]
    private int _defaultHardwarePriority;

    [ObservableProperty]
    private double _hardwareSamplingIntervalSeconds;

    [ObservableProperty]
    private bool _enableGpuMonitoring;

    // Timer
    [ObservableProperty]
    private bool _enableTimerWidget;

    [ObservableProperty]
    private int _defaultTimerPriority;

    [ObservableProperty]
    private int _pomodoroWorkDurationMinutes;

    [ObservableProperty]
    private int _pomodoroBreakDurationMinutes;

    // Hotkey & Autostart
    [ObservableProperty]
    private bool _hideOnFullscreen;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private string _toggleIslandHotkey;

    [ObservableProperty]
    private bool _hasHotkeyConflict;

    [ObservableProperty]
    private string? _hotkeyConflictMessage;

    public SettingsViewModel(
        ISettingsService settingsService,
        IslandOrchestrator orchestrator,
        WindowPositioner windowPositioner,
        IHotkeyService hotkeyService,
        IAutostartService autostartService,
        Func<IslandWindow>? getIslandWindow = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _windowPositioner = windowPositioner ?? throw new ArgumentNullException(nameof(windowPositioner));
        _hotkeyService = hotkeyService ?? throw new ArgumentNullException(nameof(hotkeyService));
        _autostartService = autostartService ?? throw new ArgumentNullException(nameof(autostartService));
        _getIslandWindow = getIslandWindow;

        _settings = _settingsService.CurrentSettings;

        // Populate monitors list
        var monitorNames = WindowPositioner.GetAvailableMonitorNames();
        AvailableMonitors = new ObservableCollection<string>(monitorNames);

        // Load current settings into observable properties
        _targetMonitorIndex = Math.Clamp(_settings.TargetMonitorIndex, 0, Math.Max(0, monitorNames.Count - 1));
        _offsetX = _settings.OffsetX;
        _offsetY = _settings.OffsetY;
        _capsuleWidth = _settings.CapsuleWidth;
        _capsuleHeight = _settings.CapsuleHeight;
        _capsuleCornerRadius = _settings.CapsuleCornerRadius;
        _scaleFactor = _settings.ScaleFactor;

        _enableMediaWidget = _settings.EnableMediaWidget;
        _defaultMediaPriority = _settings.DefaultMediaPriority;
        _mediaPauseGracePeriodSeconds = _settings.MediaPauseGracePeriodSeconds;

        _enableVolumeWidget = _settings.EnableVolumeWidget;
        _defaultVolumePriority = _settings.DefaultVolumePriority;
        _volumeTransientDurationSeconds = _settings.VolumeTransientDurationSeconds;

        _enableBatteryWidget = _settings.EnableBatteryWidget;
        _defaultBatteryPriority = _settings.DefaultBatteryPriority;
        _batteryLowThresholdPercent = _settings.BatteryLowThresholdPercent;
        _batteryCriticalThresholdPercent = _settings.BatteryCriticalThresholdPercent;

        _enableHardwareMonitoring = _settings.EnableHardwareMonitoring;
        _defaultHardwarePriority = _settings.DefaultHardwarePriority;
        _hardwareSamplingIntervalSeconds = _settings.HardwareSamplingIntervalSeconds;
        _enableGpuMonitoring = _settings.EnableGpuMonitoring;

        _enableTimerWidget = _settings.EnableTimerWidget;
        _defaultTimerPriority = _settings.DefaultTimerPriority;
        _pomodoroWorkDurationMinutes = _settings.PomodoroWorkDurationMinutes;
        _pomodoroBreakDurationMinutes = _settings.PomodoroBreakDurationMinutes;

        _hideOnFullscreen = _settings.HideOnFullscreen;
        _startWithWindows = _autostartService.IsEnabled();
        _toggleIslandHotkey = _settings.ToggleIslandHotkey;

        _hasHotkeyConflict = _hotkeyService.HasConflict;
        _hotkeyConflictMessage = _hotkeyService.ConflictMessage;

        _hotkeyService.HotkeyConflictOccurred += OnHotkeyConflictOccurred;
    }

    private void OnHotkeyConflictOccurred(object? sender, string message)
    {
        HasHotkeyConflict = true;
        HotkeyConflictMessage = message;
    }

    partial void OnTargetMonitorIndexChanged(int value)
    {
        _settings.TargetMonitorIndex = value;
        _settingsService.SaveDebounced();
        ApplyPositionLive();
    }

    partial void OnOffsetXChanged(double value)
    {
        _settings.OffsetX = value;
        _settingsService.SaveDebounced();
        ApplyPositionLive();
    }

    partial void OnOffsetYChanged(double value)
    {
        _settings.OffsetY = value;
        _settingsService.SaveDebounced();
        ApplyPositionLive();
    }

    partial void OnCapsuleWidthChanged(double value)
    {
        _settings.CapsuleWidth = value;
        _settingsService.SaveDebounced();
        ApplyPositionLive();
    }

    partial void OnCapsuleHeightChanged(double value)
    {
        _settings.CapsuleHeight = value;
        _settingsService.SaveDebounced();
        ApplyPositionLive();
    }

    partial void OnCapsuleCornerRadiusChanged(double value)
    {
        _settings.CapsuleCornerRadius = value;
        _settingsService.SaveDebounced();
        ApplyPositionLive();
    }

    partial void OnScaleFactorChanged(double value)
    {
        _settings.ScaleFactor = value;
        _settingsService.SaveDebounced();
        ApplyPositionLive();
    }

    partial void OnEnableMediaWidgetChanged(bool value)
    {
        _settings.EnableMediaWidget = value;
        _settingsService.SaveDebounced();
    }

    partial void OnDefaultMediaPriorityChanged(int value)
    {
        _settings.DefaultMediaPriority = value;
        _settingsService.SaveDebounced();
    }

    partial void OnMediaPauseGracePeriodSecondsChanged(int value)
    {
        _settings.MediaPauseGracePeriodSeconds = value;
        _settingsService.SaveDebounced();
    }

    partial void OnEnableVolumeWidgetChanged(bool value)
    {
        _settings.EnableVolumeWidget = value;
        _settingsService.SaveDebounced();
    }

    partial void OnDefaultVolumePriorityChanged(int value)
    {
        _settings.DefaultVolumePriority = value;
        _settingsService.SaveDebounced();
    }

    partial void OnVolumeTransientDurationSecondsChanged(double value)
    {
        _settings.VolumeTransientDurationSeconds = value;
        _settingsService.SaveDebounced();
    }

    partial void OnEnableBatteryWidgetChanged(bool value)
    {
        _settings.EnableBatteryWidget = value;
        _settingsService.SaveDebounced();
    }

    partial void OnDefaultBatteryPriorityChanged(int value)
    {
        _settings.DefaultBatteryPriority = value;
        _settingsService.SaveDebounced();
    }

    partial void OnBatteryLowThresholdPercentChanged(int value)
    {
        _settings.BatteryLowThresholdPercent = value;
        _settingsService.SaveDebounced();
    }

    partial void OnBatteryCriticalThresholdPercentChanged(int value)
    {
        _settings.BatteryCriticalThresholdPercent = value;
        _settingsService.SaveDebounced();
    }

    partial void OnEnableHardwareMonitoringChanged(bool value)
    {
        _settings.EnableHardwareMonitoring = value;
        _settingsService.SaveDebounced();

        var hwWidget = _orchestrator.RegisteredWidgets.OfType<HardwareWidget>().FirstOrDefault();
        if (hwWidget != null)
        {
            if (value && !hwWidget.IsActive)
            {
                hwWidget.EnableMonitoring();
            }
            else if (!value && hwWidget.IsActive)
            {
                hwWidget.DisableMonitoring();
            }
        }
    }

    partial void OnDefaultHardwarePriorityChanged(int value)
    {
        _settings.DefaultHardwarePriority = value;
        _settingsService.SaveDebounced();
    }

    partial void OnHardwareSamplingIntervalSecondsChanged(double value)
    {
        _settings.HardwareSamplingIntervalSeconds = value;
        _settingsService.SaveDebounced();
    }

    partial void OnEnableGpuMonitoringChanged(bool value)
    {
        _settings.EnableGpuMonitoring = value;
        _settingsService.SaveDebounced();
    }

    partial void OnEnableTimerWidgetChanged(bool value)
    {
        _settings.EnableTimerWidget = value;
        _settingsService.SaveDebounced();
    }

    partial void OnDefaultTimerPriorityChanged(int value)
    {
        _settings.DefaultTimerPriority = value;
        _settingsService.SaveDebounced();
    }

    partial void OnPomodoroWorkDurationMinutesChanged(int value)
    {
        _settings.PomodoroWorkDurationMinutes = value;
        _settingsService.SaveDebounced();
    }

    partial void OnPomodoroBreakDurationMinutesChanged(int value)
    {
        _settings.PomodoroBreakDurationMinutes = value;
        _settingsService.SaveDebounced();
    }

    partial void OnHideOnFullscreenChanged(bool value)
    {
        _settings.HideOnFullscreen = value;
        _settingsService.SaveDebounced();
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        _settings.StartWithWindows = value;
        _settingsService.SaveDebounced();
        _autostartService.SetEnabled(value);
    }

    partial void OnToggleIslandHotkeyChanged(string value)
    {
        _settings.ToggleIslandHotkey = value;
        _settingsService.SaveDebounced();
        ApplyHotkey();
    }

    [RelayCommand]
    public void ApplyHotkey()
    {
        if (string.IsNullOrWhiteSpace(ToggleIslandHotkey))
        {
            _hotkeyService.Unregister();
            HasHotkeyConflict = false;
            HotkeyConflictMessage = null;
            return;
        }

        bool success = _hotkeyService.UpdateHotkey(ToggleIslandHotkey);
        HasHotkeyConflict = !success;
        HotkeyConflictMessage = _hotkeyService.ConflictMessage;
    }

    /// <summary>
    /// Refreshes the list of available display monitors and adjusts the selected index if needed.
    /// </summary>
    [RelayCommand]
    public void RefreshMonitors()
    {
        var monitorNames = WindowPositioner.GetAvailableMonitorNames();
        AvailableMonitors.Clear();
        foreach (var name in monitorNames)
        {
            AvailableMonitors.Add(name);
        }

        int clampedIndex = Math.Clamp(_settings.TargetMonitorIndex, 0, Math.Max(0, monitorNames.Count - 1));
        if (TargetMonitorIndex != clampedIndex)
        {
            TargetMonitorIndex = clampedIndex;
        }
    }

    [RelayCommand]
    public void ResetPosition()
    {
        TargetMonitorIndex = 0;
        OffsetX = 0.0;
        OffsetY = 0.0;
        CapsuleWidth = 200.0;
        CapsuleHeight = 36.0;
        CapsuleCornerRadius = 14.0;
        ScaleFactor = 1.0;
        ApplyPositionLive();
    }

    [RelayCommand]
    public void ResetToDefaults() => ResetAllDefaults();

    [RelayCommand]
    public void ResetAllDefaults()
    {
        var defaults = new AppSettings();
        _settings.CopyFrom(defaults);
        _settingsService.SaveImmediate();

        // Refresh all VM properties
        TargetMonitorIndex = _settings.TargetMonitorIndex;
        OffsetX = _settings.OffsetX;
        OffsetY = _settings.OffsetY;
        CapsuleWidth = _settings.CapsuleWidth;
        CapsuleHeight = _settings.CapsuleHeight;
        CapsuleCornerRadius = _settings.CapsuleCornerRadius;
        ScaleFactor = _settings.ScaleFactor;

        EnableMediaWidget = _settings.EnableMediaWidget;
        DefaultMediaPriority = _settings.DefaultMediaPriority;
        MediaPauseGracePeriodSeconds = _settings.MediaPauseGracePeriodSeconds;

        EnableVolumeWidget = _settings.EnableVolumeWidget;
        DefaultVolumePriority = _settings.DefaultVolumePriority;
        VolumeTransientDurationSeconds = _settings.VolumeTransientDurationSeconds;

        EnableBatteryWidget = _settings.EnableBatteryWidget;
        DefaultBatteryPriority = _settings.DefaultBatteryPriority;
        BatteryLowThresholdPercent = _settings.BatteryLowThresholdPercent;
        BatteryCriticalThresholdPercent = _settings.BatteryCriticalThresholdPercent;

        EnableHardwareMonitoring = _settings.EnableHardwareMonitoring;
        DefaultHardwarePriority = _settings.DefaultHardwarePriority;
        HardwareSamplingIntervalSeconds = _settings.HardwareSamplingIntervalSeconds;
        EnableGpuMonitoring = _settings.EnableGpuMonitoring;

        EnableTimerWidget = _settings.EnableTimerWidget;
        DefaultTimerPriority = _settings.DefaultTimerPriority;
        PomodoroWorkDurationMinutes = _settings.PomodoroWorkDurationMinutes;
        PomodoroBreakDurationMinutes = _settings.PomodoroBreakDurationMinutes;

        HideOnFullscreen = _settings.HideOnFullscreen;
        StartWithWindows = _settings.StartWithWindows;
        ToggleIslandHotkey = _settings.ToggleIslandHotkey;

        ApplyPositionLive();
        ApplyHotkey();
    }

    private void ApplyPositionLive()
    {
        try
        {
            var window = _getIslandWindow?.Invoke();
            window?.ApplySettingsAndReposition();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to apply live position update from SettingsViewModel.");
        }
    }
}

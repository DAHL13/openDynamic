using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenDynamic.App.Orchestration;
using OpenDynamic.App.Services;
using OpenDynamic.App.Widgets.Hardware;
using OpenDynamic.App.Windowing;
using OpenDynamic.Core.Animation;
using OpenDynamic.Core.Autostart;
using OpenDynamic.Core.Settings;
using OpenDynamic.Core.Stopwatch;
using OpenDynamic.Core.Timer;
using Serilog;

namespace OpenDynamic.App.ViewModels;

/// <summary>
/// Display item wrapper allowing user to edit timer label in SettingsWindow.
/// </summary>
public sealed class EditableTimerItem : ObservableObject
{
    private readonly TimerController _controller;
    private readonly Action _onChanged;

    public string Id => _controller.Id;

    public string Label
    {
        get => _controller.Label;
        set
        {
            if (_controller.Label != value)
            {
                _controller.Label = value ?? string.Empty;
                OnPropertyChanged();
                _onChanged();
            }
        }
    }

    public string DurationDisplay => $"{_controller.TotalDuration.TotalMinutes:F0} min";

    public EditableTimerItem(TimerController controller, Action onChanged)
    {
        _controller = controller;
        _onChanged = onChanged;
    }
}

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
    private readonly NetworkService? _networkService;
    private readonly DeviceService? _deviceService;
    private readonly ITimerCollection? _timerCollection;

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

    [ObservableProperty]
    private bool _enableDynamicMediaColor;

    [ObservableProperty]
    private bool _enableMediaGestures;

    [ObservableProperty]
    private double _mediaGestureSensitivity;

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

    // Multi-timer management (Phase 12)
    [ObservableProperty]
    private ObservableCollection<EditableTimerItem> _editableTimers = new();

    [ObservableProperty]
    private string _newTimerLabel = string.Empty;

    [ObservableProperty]
    private int _newTimerDurationMinutes = 10;

    [ObservableProperty]
    private bool _canAddNewTimer = true;

    // Stopwatch (Phase 12)
    [ObservableProperty]
    private bool _enableStopwatchWidget;

    [ObservableProperty]
    private int _defaultStopwatchPriority;

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

    // Network Alerts (Phase 11)
    [ObservableProperty]
    private bool _enableNetworkAlerts;

    [ObservableProperty]
    private int _defaultNetworkPriority;

    [ObservableProperty]
    private double _networkTransientDurationSeconds;

    // Device Alerts (Phase 11)
    [ObservableProperty]
    private bool _enableDeviceAlerts;

    [ObservableProperty]
    private int _defaultDevicePriority;

    [ObservableProperty]
    private double _deviceTransientDurationSeconds;

    [ObservableProperty]
    private ObservableCollection<string> _ignoredDeviceNames = new();

    [ObservableProperty]
    private string _newIgnoredDeviceName = string.Empty;

    [ObservableProperty]
    private string? _selectedIgnoredDevice;

    // Motion and Animations (Phase 10)
    [ObservableProperty]
    private MotionMode _motionMode;

    [ObservableProperty]
    private string _systemAnimationStatusText = string.Empty;

    public record MotionModeOption(MotionMode Value, string DisplayName);

    public IReadOnlyList<MotionModeOption> MotionModeOptions { get; } = new List<MotionModeOption>
    {
        new(MotionMode.Auto, "Automático (Sigue a Windows)"),
        new(MotionMode.Reduced, "Reducidas (Sin rebote ni efectos)"),
        new(MotionMode.Full, "Completas (Física elástica de resortes)")
    };

    public MotionModeOption SelectedMotionModeOption
    {
        get => MotionModeOptions.FirstOrDefault(o => o.Value == MotionMode) ?? MotionModeOptions[0];
        set
        {
            if (value != null && MotionMode != value.Value)
            {
                MotionMode = value.Value;
                OnPropertyChanged(nameof(SelectedMotionModeOption));
            }
        }
    }

    public SettingsViewModel(
        ISettingsService settingsService,
        IslandOrchestrator orchestrator,
        WindowPositioner windowPositioner,
        IHotkeyService hotkeyService,
        IAutostartService autostartService,
        Func<IslandWindow>? getIslandWindow = null,
        NetworkService? networkService = null,
        DeviceService? deviceService = null,
        ITimerCollection? timerCollection = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _windowPositioner = windowPositioner ?? throw new ArgumentNullException(nameof(windowPositioner));
        _hotkeyService = hotkeyService ?? throw new ArgumentNullException(nameof(hotkeyService));
        _autostartService = autostartService ?? throw new ArgumentNullException(nameof(autostartService));
        _getIslandWindow = getIslandWindow;
        _networkService = networkService;
        _deviceService = deviceService;
        _timerCollection = timerCollection;

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
        _enableDynamicMediaColor = _settings.EnableDynamicMediaColor;
        _enableMediaGestures = _settings.EnableMediaGestures;
        _mediaGestureSensitivity = _settings.MediaGestureSensitivity;

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

        _enableStopwatchWidget = _settings.EnableStopwatchWidget;
        _defaultStopwatchPriority = _settings.DefaultStopwatchPriority;

        if (_timerCollection != null)
        {
            _timerCollection.TimersChanged += (_, _) => ReloadEditableTimers();
            ReloadEditableTimers();
        }

        _enableNetworkAlerts = _settings.EnableNetworkAlerts;
        _defaultNetworkPriority = _settings.DefaultNetworkPriority;
        _networkTransientDurationSeconds = _settings.NetworkTransientDurationSeconds;

        _enableDeviceAlerts = _settings.EnableDeviceAlerts;
        _defaultDevicePriority = _settings.DefaultDevicePriority;
        _deviceTransientDurationSeconds = _settings.DeviceTransientDurationSeconds;

        _ignoredDeviceNames = new ObservableCollection<string>(_settings.IgnoredDeviceNames ?? Enumerable.Empty<string>());

        _hideOnFullscreen = _settings.HideOnFullscreen;
        _startWithWindows = _autostartService.IsEnabled();
        _toggleIslandHotkey = _settings.ToggleIslandHotkey;

        _motionMode = _settings.MotionMode;
        UpdateSystemAnimationStatus();
        System.Windows.SystemParameters.StaticPropertyChanged += OnSystemParametersStaticPropertyChanged;

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

    partial void OnEnableDynamicMediaColorChanged(bool value)
    {
        _settings.EnableDynamicMediaColor = value;
        _settingsService.SaveDebounced();
    }

    partial void OnEnableMediaGesturesChanged(bool value)
    {
        _settings.EnableMediaGestures = value;
        _settingsService.SaveDebounced();
    }

    partial void OnMediaGestureSensitivityChanged(double value)
    {
        _settings.MediaGestureSensitivity = value;
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

    partial void OnEnableStopwatchWidgetChanged(bool value)
    {
        _settings.EnableStopwatchWidget = value;
        _settingsService.SaveDebounced();

        var sw = _orchestrator.RegisteredWidgets.OfType<Widgets.Stopwatch.StopwatchWidget>().FirstOrDefault();
        if (sw != null && !value && sw.IsActive)
        {
            sw.Reset();
        }
    }

    partial void OnDefaultStopwatchPriorityChanged(int value)
    {
        _settings.DefaultStopwatchPriority = value;
        _settingsService.SaveDebounced();

        var sw = _orchestrator.RegisteredWidgets.OfType<Widgets.Stopwatch.StopwatchWidget>().FirstOrDefault();
        if (sw != null)
        {
            sw.Priority = value;
        }
    }

    [RelayCommand]
    public void AddCustomTimer()
    {
        if (_timerCollection == null) return;
        if (_timerCollection.Timers.Count >= _timerCollection.MaxTimers) return;

        string label = string.IsNullOrWhiteSpace(NewTimerLabel)
            ? $"Temporizador {_timerCollection.Timers.Count + 1}"
            : NewTimerLabel.Trim();

        int mins = Math.Clamp(NewTimerDurationMinutes, 1, 180);
        _timerCollection.AddTimer(label, TimeSpan.FromMinutes(mins));

        NewTimerLabel = string.Empty;
        ReloadEditableTimers();
        _settingsService.SaveDebounced();
    }

    [RelayCommand]
    public void DeleteCustomTimer(string id)
    {
        if (_timerCollection == null) return;
        _timerCollection.RemoveTimer(id);
        ReloadEditableTimers();
        _settingsService.SaveDebounced();
    }

    private void ReloadEditableTimers()
    {
        if (_timerCollection == null) return;
        EditableTimers.Clear();
        foreach (var timer in _timerCollection.Timers)
        {
            EditableTimers.Add(new EditableTimerItem(timer, () =>
            {
                _settingsService.SaveDebounced();
            }));
        }
        CanAddNewTimer = _timerCollection.Timers.Count < _timerCollection.MaxTimers;
    }

    partial void OnEnableNetworkAlertsChanged(bool value)
    {
        _settings.EnableNetworkAlerts = value;
        _settingsService.SaveDebounced();

        if (value)
        {
            _networkService?.Start();
        }
        else
        {
            _networkService?.Stop();
        }
    }

    partial void OnDefaultNetworkPriorityChanged(int value)
    {
        _settings.DefaultNetworkPriority = value;
        _settingsService.SaveDebounced();
    }

    partial void OnNetworkTransientDurationSecondsChanged(double value)
    {
        _settings.NetworkTransientDurationSeconds = value;
        _settingsService.SaveDebounced();
    }

    partial void OnEnableDeviceAlertsChanged(bool value)
    {
        _settings.EnableDeviceAlerts = value;
        _settingsService.SaveDebounced();

        if (value)
        {
            var hwnd = _getIslandWindow?.Invoke()?.Hwnd ?? IntPtr.Zero;
            _deviceService?.Start(hwnd);
        }
        else
        {
            _deviceService?.Stop();
        }
    }

    partial void OnDefaultDevicePriorityChanged(int value)
    {
        _settings.DefaultDevicePriority = value;
        _settingsService.SaveDebounced();
    }

    partial void OnDeviceTransientDurationSecondsChanged(double value)
    {
        _settings.DeviceTransientDurationSeconds = value;
        _settingsService.SaveDebounced();
    }

    [RelayCommand]
    public void AddIgnoredDevice()
    {
        if (string.IsNullOrWhiteSpace(NewIgnoredDeviceName)) return;

        string trimmed = NewIgnoredDeviceName.Trim();
        if (!IgnoredDeviceNames.Any(d => string.Equals(d, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            IgnoredDeviceNames.Add(trimmed);
            _settings.IgnoredDeviceNames = IgnoredDeviceNames.ToList();
            _deviceService?.UpdateIgnoredDevices();
            _settingsService.SaveDebounced();
        }

        NewIgnoredDeviceName = string.Empty;
    }

    [RelayCommand]
    public void RemoveIgnoredDevice(string? deviceName)
    {
        string? target = deviceName ?? SelectedIgnoredDevice;
        if (string.IsNullOrWhiteSpace(target)) return;

        var existing = IgnoredDeviceNames.FirstOrDefault(d => string.Equals(d, target, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            IgnoredDeviceNames.Remove(existing);
            _settings.IgnoredDeviceNames = IgnoredDeviceNames.ToList();
            _deviceService?.UpdateIgnoredDevices();
            _settingsService.SaveDebounced();
        }
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

    partial void OnMotionModeChanged(MotionMode value)
    {
        _settings.MotionMode = value;
        _settingsService.SaveDebounced();
        UpdateSystemAnimationStatus();
        ApplyPositionLive();
    }

    private void OnSystemParametersStaticPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(System.Windows.SystemParameters.ClientAreaAnimation))
        {
            UpdateSystemAnimationStatus();
        }
    }

    public void UpdateSystemAnimationStatus()
    {
        bool sysAnim = System.Windows.SystemParameters.ClientAreaAnimation;
        var resolved = MotionProfileResolver.Resolve(MotionMode, sysAnim);
        string modeDesc = resolved.AllowDecorative
            ? "Animaciones activas (Física de resortes completa)"
            : "Animaciones reducidas (Movimiento directo/amortiguado, sin efectos decorativos)";

        SystemAnimationStatusText = $"Windows: {(sysAnim ? "Efectos activados" : "Efectos desactivados")} | Efectivo: {modeDesc}";
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

        EnableStopwatchWidget = _settings.EnableStopwatchWidget;
        DefaultStopwatchPriority = _settings.DefaultStopwatchPriority;

        EnableNetworkAlerts = _settings.EnableNetworkAlerts;
        DefaultNetworkPriority = _settings.DefaultNetworkPriority;
        NetworkTransientDurationSeconds = _settings.NetworkTransientDurationSeconds;

        EnableDeviceAlerts = _settings.EnableDeviceAlerts;
        DefaultDevicePriority = _settings.DefaultDevicePriority;
        DeviceTransientDurationSeconds = _settings.DeviceTransientDurationSeconds;

        IgnoredDeviceNames.Clear();
        foreach (var d in _settings.IgnoredDeviceNames)
        {
            IgnoredDeviceNames.Add(d);
        }
        _deviceService?.UpdateIgnoredDevices();

        HideOnFullscreen = _settings.HideOnFullscreen;
        StartWithWindows = _settings.StartWithWindows;
        ToggleIslandHotkey = _settings.ToggleIslandHotkey;

        MotionMode = _settings.MotionMode;
        OnPropertyChanged(nameof(SelectedMotionModeOption));
        UpdateSystemAnimationStatus();

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

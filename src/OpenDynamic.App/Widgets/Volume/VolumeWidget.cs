using System.Windows.Controls;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using OpenDynamic.App.Services;
using OpenDynamic.App.Widgets.Volume.Views;
using OpenDynamic.Core.Audio;
using OpenDynamic.Core.Settings;
using Serilog;

namespace OpenDynamic.App.Widgets.Volume;

/// <summary>
/// Transient widget displaying system volume level and mute state.
/// Pre-empts lower priority continuous widgets (Priority 80) and auto-expires after 2 seconds.
/// Supports mouse wheel adjustments directly on the capsule, immediately resetting the expiration timer.
/// </summary>
public sealed class VolumeWidget : IslandWidgetBase
{
    public const string WidgetId = "volume";

    private readonly IVolumeController _volumeController;
    private readonly AppSettings _settings;
    private readonly Dispatcher _dispatcher;

    private DispatcherTimer? _transientTimer;

    // Observable UI properties
    private float _volumeLevel;
    private int _percentage;
    private bool _isMuted;
    private VolumeIconType _iconType = VolumeIconType.Medium;
    private string _statusText = "0%";
    private string _deviceName = "Altavoces";

    public override string Id => WidgetId;

    public float VolumeLevel
    {
        get => _volumeLevel;
        private set => SetProperty(ref _volumeLevel, value);
    }

    public int Percentage
    {
        get => _percentage;
        private set => SetProperty(ref _percentage, value);
    }

    public bool IsMuted
    {
        get => _isMuted;
        private set
        {
            if (SetProperty(ref _isMuted, value))
            {
                OnPropertyChanged(nameof(MuteButtonText));
            }
        }
    }

    /// <summary>
    /// Semantic text for the mute toggle action button ("Silenciar" / "Silenciado").
    /// </summary>
    public string MuteButtonText => IsMuted ? "Silenciado" : "Silenciar";

    public VolumeIconType IconType
    {
        get => _iconType;
        private set => SetProperty(ref _iconType, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string DeviceName
    {
        get => _deviceName;
        private set => SetProperty(ref _deviceName, value);
    }

    public IRelayCommand ToggleMuteCommand { get; }

    public VolumeWidget(
        IVolumeController volumeController,
        AppSettings settings,
        Dispatcher? dispatcher = null)
        : base(settings?.DefaultVolumePriority ?? 80)
    {
        _volumeController = volumeController ?? throw new ArgumentNullException(nameof(volumeController));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _dispatcher = dispatcher ?? (System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher);

        ToggleMuteCommand = new RelayCommand(ToggleMute);
    }

    public override void Initialize()
    {
        base.Initialize();

        try
        {
            VolumeLevel = _volumeController.Volume;
            IsMuted = _volumeController.IsMuted;
            Percentage = VolumeCalculator.ToPercentage(VolumeLevel);
            IconType = VolumeCalculator.GetVolumeIconType(VolumeLevel, IsMuted);
            StatusText = IsMuted ? "Silenciado" : $"{Percentage}%";

            if (_volumeController is VolumeService vs && !string.IsNullOrEmpty(vs.CurrentDeviceFriendlyName))
            {
                DeviceName = vs.CurrentDeviceFriendlyName;
            }

            _volumeController.VolumeChanged += OnVolumeChanged;
            Log.Debug("VolumeWidget initialized with level {Level:P0} (Muted: {Muted})", VolumeLevel, IsMuted);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize VolumeWidget state.");
        }
    }

    private void OnVolumeChanged(object? sender, VolumeChangedEventArgs e)
    {
        _dispatcher.InvokeAsync(() =>
        {
            VolumeLevel = e.Volume;
            Percentage = VolumeCalculator.ToPercentage(e.Volume);
            IsMuted = e.IsMuted;
            IconType = VolumeCalculator.GetVolumeIconType(e.Volume, e.IsMuted);
            StatusText = e.IsMuted ? "Silenciado" : $"{Percentage}%";

            if (_volumeController is VolumeService vs && !string.IsNullOrEmpty(vs.CurrentDeviceFriendlyName))
            {
                DeviceName = vs.CurrentDeviceFriendlyName;
            }

            if (!_settings.EnableVolumeWidget)
            {
                return;
            }

            // Trigger transient activation (2 seconds, Priority 80)
            var duration = TimeSpan.FromSeconds(_settings.VolumeTransientDurationSeconds);
            Activate(transientDuration: duration, priorityOverride: _settings.DefaultVolumePriority);
            ResetTransientTimer(duration);

            Log.Information("VolumeWidget transient activated on island: Volume={Volume:P0}, Muted={Muted}, Device='{Device}', Priority={Priority}",
                VolumeLevel, IsMuted, DeviceName, _settings.DefaultVolumePriority);
        });
    }

    /// <summary>
    /// Applies live changes to EnableVolumeWidget.
    /// </summary>
    public void ApplyEnabledState(bool enabled)
    {
        if (!enabled)
        {
            _transientTimer?.Stop();
            _transientTimer = null;
            Deactivate();
        }
    }

    /// <summary>
    /// Resets the transient auto-expiration timer.
    /// </summary>
    private void ResetTransientTimer(TimeSpan duration)
    {
        _transientTimer?.Stop();
        _transientTimer = new DispatcherTimer
        {
            Interval = duration
        };
        _transientTimer.Tick += (s, e) =>
        {
            _transientTimer.Stop();
            _transientTimer = null;
            Log.Debug("VolumeWidget transient lifespan expired. Deactivating.");
            Deactivate();
        };
        _transientTimer.Start();
    }

    /// <summary>
    /// Adjusts system volume in response to mouse wheel interaction over the capsule,
    /// immediately resetting the transient grace timer.
    /// </summary>
    /// <param name="scrollDelta">WPF MouseWheel delta value.</param>
    public void AdjustVolume(int scrollDelta)
    {
        try
        {
            float step = scrollDelta > 0 ? 0.02f : -0.02f;
            _volumeController.ChangeVolume(step);

            // Re-activate immediately to reset transient grace timer in Orchestrator
            var duration = TimeSpan.FromSeconds(_settings.VolumeTransientDurationSeconds);
            Activate(transientDuration: duration, priorityOverride: _settings.DefaultVolumePriority);
            ResetTransientTimer(duration);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error adjusting volume via mouse wheel in VolumeWidget.");
        }
    }

    /// <summary>
    /// Sets system volume directly (e.g. from expanded slider interaction),
    /// immediately resetting the transient grace timer.
    /// </summary>
    /// <param name="newVolume">Normalized level in the range [0.0f, 1.0f].</param>
    public void SetVolume(float newVolume)
    {
        try
        {
            _volumeController.SetVolume(Math.Clamp(newVolume, 0f, 1f));

            var duration = TimeSpan.FromSeconds(_settings.VolumeTransientDurationSeconds);
            Activate(transientDuration: duration, priorityOverride: _settings.DefaultVolumePriority);
            ResetTransientTimer(duration);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error setting volume in VolumeWidget.");
        }
    }

    public void ToggleMute()
    {
        try
        {
            _volumeController.ToggleMute();
            var duration = TimeSpan.FromSeconds(_settings.VolumeTransientDurationSeconds);
            Activate(transientDuration: duration, priorityOverride: _settings.DefaultVolumePriority);
            ResetTransientTimer(duration);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error toggling mute in VolumeWidget.");
        }
    }

    public override UserControl? CreateCompactView() => new VolumeCompactView { DataContext = this };

    public override UserControl? CreateExpandedView() => new VolumeExpandedView { DataContext = this };

    public override UserControl? CreateSplitView() => new VolumeSplitView { DataContext = this };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _transientTimer?.Stop();
            _transientTimer = null;
            _volumeController.VolumeChanged -= OnVolumeChanged;
        }

        base.Dispose(disposing);
    }
}

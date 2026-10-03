using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Win32;
using OpenDynamic.App.Animation;
using OpenDynamic.App.Native;
using OpenDynamic.App.Orchestration;
using OpenDynamic.App.Widgets.Hardware;
using OpenDynamic.Core.State;
using Serilog;

namespace OpenDynamic.App.Windowing;

/// <summary>
/// Interaction logic for IslandWindow.xaml.
/// Hosts <see cref="Views.IslandView"/> within a transparent, click-through overlay window,
/// delegating all state transitions and widget rendering to <see cref="IslandOrchestrator"/>.
/// </summary>
public partial class IslandWindow : Window
{
    private readonly WindowPositioner _windowPositioner;
    private readonly ForegroundWatcher _foregroundWatcher;
    private readonly IslandOrchestrator _orchestrator;
    private readonly IslandAnimator _animator;
    private readonly Services.PowerService? _powerService;
    private readonly FullscreenWatcher? _fullscreenWatcher;
    private readonly Services.NetworkService? _networkService;
    private readonly Services.DeviceService? _deviceService;
    private readonly Services.ClipboardService? _clipboardService;
    private readonly Services.PrivacyAccessMonitor? _privacyMonitor;
    private readonly Core.Settings.AppSettings _settings;

    private readonly DispatcherTimer _hoverEnterTimer;
    private readonly DispatcherTimer _hoverLeaveTimer;
    private readonly DispatcherTimer _restingHoverWatcherTimer;

    private IntPtr _hwnd = IntPtr.Zero;
    private HwndSource? _hwndSource;

#if DEBUG
    private IslandDebugWindow? _debugWindow;
#endif

    public IslandWindow(
        WindowPositioner windowPositioner,
        ForegroundWatcher foregroundWatcher,
        IslandOrchestrator orchestrator,
        Services.PowerService? powerService = null,
        FullscreenWatcher? fullscreenWatcher = null,
        Core.Settings.AppSettings? settings = null,
        Services.NetworkService? networkService = null,
        Services.DeviceService? deviceService = null,
        Services.ClipboardService? clipboardService = null,
        Services.PrivacyAccessMonitor? privacyMonitor = null)
    {
        _windowPositioner = windowPositioner ?? throw new ArgumentNullException(nameof(windowPositioner));
        _foregroundWatcher = foregroundWatcher ?? throw new ArgumentNullException(nameof(foregroundWatcher));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _animator = orchestrator.Animator;
        _powerService = powerService;
        _fullscreenWatcher = fullscreenWatcher;
        _networkService = networkService;
        _deviceService = deviceService;
        _clipboardService = clipboardService;
        _privacyMonitor = privacyMonitor;
        _settings = settings ?? new Core.Settings.AppSettings();

        InitializeComponent();

        _hoverEnterTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _hoverEnterTimer.Tick += OnHoverEnterTimerTick;

        _hoverLeaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        _hoverLeaveTimer.Tick += OnHoverLeaveTimerTick;

        _restingHoverWatcherTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _restingHoverWatcherTimer.Tick += OnRestingHoverWatcherTick;
        _restingHoverWatcherTimer.Start();

        _animator.FrameUpdated += OnAnimatorFrameUpdated;
        _animator.Settled += OnAnimatorSettled;
        IslandHostView.SatelliteFadeOutCompleted += OnSatelliteFadeOutCompleted;

        WeakReferenceMessenger.Default.Register<Widgets.Messages.IslandStateChangedMessage>(this, (_, msg) =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                Log.Debug("IslandWindow: StateChanged: {OldState} -> {NewState}", msg.PreviousState, msg.NewState);

                if (msg.NewState != IslandState.Hidden)
                {
                    _restingHoverWatcherTimer.Stop();

                    if (IslandHostView.Visibility != Visibility.Visible)
                    {
                        IslandHostView.Visibility = Visibility.Visible;
                    }
                    if (this.Visibility != Visibility.Visible)
                    {
                        this.Visibility = Visibility.Visible;
                    }

                    // Keep resting sensor hit-testable to eliminate transition dead zones
                    if (RestingSensorNotch != null)
                    {
                        RestingSensorNotch.Visibility = Visibility.Visible;
                        RestingSensorNotch.IsHitTestVisible = true;
                    }
                }
                else
                {
                    // Re-enable resting sensor notch for hover detection in Hidden state
                    if (RestingSensorNotch != null)
                    {
                        RestingSensorNotch.Visibility = Visibility.Visible;
                        RestingSensorNotch.IsHitTestVisible = true;
                    }

                    if (!IsPhysicalCursorOverInteractiveZone())
                    {
                        _hoverEnterTimer.Stop();
                        _hoverLeaveTimer.Stop();
                        _orchestrator.SetHovering(false);
                    }
                    else
                    {
                        _hoverLeaveTimer.Stop();
                        if (!_hoverEnterTimer.IsEnabled && !_orchestrator.IsHovering)
                        {
                            Log.Debug("[Hover] Enter Timer Started (Retained on State Change)");
                            _hoverEnterTimer.Start();
                        }
                    }

                    if (!_restingHoverWatcherTimer.IsEnabled && !_orchestrator.IsHovering)
                    {
                        _restingHoverWatcherTimer.Start();
                    }

                    CheckAndApplyHiddenVisibility();
                }
            });
        });

        // Attach IslandView to Orchestrator for view delivery
        _orchestrator.AttachView(IslandHostView);

        SetupContextMenu();
        SetupMouseInteractions();

        // Apply initial layout dimensions
        IslandHostView.ApplyDimensions(_animator.CurrentDimensions, _animator.StateMachine.CurrentState);

        if (_privacyMonitor != null)
        {
            _privacyMonitor.StateChanged += OnPrivacyStateChanged;
            UpdatePrivacyDots(_privacyMonitor.CurrentState);
        }
    }

    public IslandWindow() : this(
        new WindowPositioner(),
        new ForegroundWatcher(),
        new IslandOrchestrator(new IslandStateMachine(), new IslandAnimator()))
    {
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwnd = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(WndProc);

        ApplyWin32Styles();
        ApplySettingsAndReposition();

        _foregroundWatcher.ForegroundWindowChanged += OnForegroundWindowChanged;
        _foregroundWatcher.Start();

        _powerService?.RegisterWindowNotifications(_hwnd);
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.TimeChanged += OnSystemEventsTimeChanged;
        SystemParameters.StaticPropertyChanged += OnSystemParametersStaticPropertyChanged;
        UpdateMotionProfileLive();

        if (_fullscreenWatcher != null)
        {
            _fullscreenWatcher.FullscreenChanged += OnFullscreenChanged;
            _fullscreenWatcher.Start();
        }

        _networkService?.Start();
        _deviceService?.Start(_hwnd);
        _clipboardService?.Start(_hwnd);

        Log.Information("IslandWindow initialized successfully with HWND: {Hwnd}. NetworkService={HasNetwork}, DeviceService={HasDevice}, ClipboardService={HasClipboard}",
            _hwnd, _networkService != null, _deviceService != null, _clipboardService != null);
    }

    /// <summary>
    /// Gets the native window handle (HWND) for this window.
    /// </summary>
    public IntPtr Hwnd => _hwnd;

    /// <summary>
    /// Applies updated settings for monitor, offsets, and capsule dimensions in real-time.
    /// </summary>
    public void ApplySettingsAndReposition()
    {
        UpdateMotionProfileLive();

        _windowPositioner.TargetMonitorIndex = _settings.TargetMonitorIndex;
        _windowPositioner.OffsetXDip = _settings.OffsetX;
        _windowPositioner.TopMarginDip = _settings.OffsetY;
        _animator.Layout.Compact = new(_settings.CapsuleWidth, _settings.CapsuleHeight, _settings.CapsuleCornerRadius, 1.0);
        _animator.AnimateTo(_animator.Layout.GetDimensions(_animator.StateMachine.CurrentState));

        if (RestingSensorNotch != null)
        {
            RestingSensorNotch.Width = Math.Max(_settings.CapsuleWidth > 0 ? _settings.CapsuleWidth : 200.0, 240.0);
            RestingSensorNotch.Height = Math.Max(_settings.CapsuleHeight > 0 ? _settings.CapsuleHeight : 28.0, 44.0);
        }

        if (_hwnd != IntPtr.Zero)
        {
            _windowPositioner.PositionWindow(_hwnd);
            _windowPositioner.ReassertTopmost(_hwnd);
        }

        UpdatePrivacyDots(_privacyMonitor?.CurrentState ?? Core.Privacy.PrivacyAccessState.Empty);
    }

    private void OnPrivacyStateChanged(object? sender, Core.Privacy.PrivacyAccessState state)
    {
        Dispatcher.InvokeAsync(() =>
        {
            UpdatePrivacyDots(state);
            _orchestrator.UpdateOrchestration();
            CheckAndApplyHiddenVisibility();
        });
    }

    private void UpdatePrivacyDots(Core.Privacy.PrivacyAccessState state)
    {
        bool showMic = _settings.EnableMicrophoneIndicator && state.IsMicrophoneActive;
        bool showCam = _settings.EnableCameraIndicator && state.IsCameraActive;
        IslandHostView.UpdatePrivacyIndicators(showMic, showCam);

        if ((showMic || showCam) && !_orchestrator.IsFullscreenSuppressed)
        {
            if (IslandHostView.Visibility != Visibility.Visible)
            {
                IslandHostView.Visibility = Visibility.Visible;
            }
            if (this.Visibility != Visibility.Visible)
            {
                this.Visibility = Visibility.Visible;
            }
        }
        else
        {
            CheckAndApplyHiddenVisibility();
        }
    }

    private void OnAnimatorSettled(object? sender, EventArgs e)
    {
        Dispatcher.InvokeAsync(CheckAndApplyHiddenVisibility);
    }

    private void OnSatelliteFadeOutCompleted(object? sender, EventArgs e)
    {
        Dispatcher.InvokeAsync(CheckAndApplyHiddenVisibility);
    }

    /// <summary>
    /// Evaluates if the island has settled into Hidden state without active privacy sensors,
    /// guaranteeing that window hiding waits until in-flight exit animations (main notch spring + satellite fade-out) complete.
    /// </summary>
    private void CheckAndApplyHiddenVisibility()
    {
        if (_orchestrator.IsFullscreenSuppressed)
        {
            return;
        }

        var state = _orchestrator.StateMachine.CurrentState;
        bool hasActivePrivacy = (_privacyMonitor?.CurrentState.IsMicrophoneActive == true && _settings.EnableMicrophoneIndicator) ||
                                (_privacyMonitor?.CurrentState.IsCameraActive == true && _settings.EnableCameraIndicator);

        if (state != IslandState.Hidden || hasActivePrivacy)
        {
            if (IslandHostView.Visibility != Visibility.Visible)
            {
                IslandHostView.Visibility = Visibility.Visible;
            }
            if (this.Visibility != Visibility.Visible)
            {
                this.Visibility = Visibility.Visible;
            }
            return;
        }

        // State IS Hidden and NO active privacy sensors:
        // Ensure all exit animations (main notch spring + satellite fade-out) have settled before hiding
        if (IslandHostView.IsSatelliteFadingOut || !_animator.IsSettled)
        {
            return;
        }

        if (IslandHostView.Visibility != Visibility.Collapsed)
        {
            IslandHostView.Visibility = Visibility.Collapsed;
            Log.Debug("IslandWindow: Exit animations completed. View collapsed.");
        }

        // Reconfirm StateMachine is strictly Hidden when settled
        if (_orchestrator.StateMachine.CurrentState != IslandState.Hidden)
        {
            _orchestrator.StateMachine.TryTransitionTo(IslandState.Hidden);
        }

        // Clean reset of timers and hover state upon settling in Hidden state ONLY if cursor is outside interactive zone
        if (!IsPhysicalCursorOverInteractiveZone())
        {
            _hoverEnterTimer.Stop();
            _hoverLeaveTimer.Stop();
            _orchestrator.SetHovering(false);
        }
        else
        {
            _hoverLeaveTimer.Stop();
            if (!_hoverEnterTimer.IsEnabled && !_orchestrator.IsHovering)
            {
                Log.Debug("[Hover] Enter Timer Started (Retained on Settle)");
                _hoverEnterTimer.Start();
            }
        }

        if (RestingSensorNotch != null)
        {
            RestingSensorNotch.Visibility = Visibility.Visible;
            RestingSensorNotch.IsHitTestVisible = true;
        }

        if (!_restingHoverWatcherTimer.IsEnabled && !_orchestrator.IsHovering)
        {
            _restingHoverWatcherTimer.Start();
        }
    }

    /// <summary>
    /// Applies Win32 extended window styles: WS_EX_TOOLWINDOW, WS_EX_NOACTIVATE, and WS_EX_TOPMOST.
    /// Strictly excludes WS_EX_TRANSPARENT to allow native WPF transparent pixel click-through.
    /// </summary>
    private void ApplyWin32Styles()
    {
        IntPtr currentExStylePtr = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE);
        long exStyle = currentExStylePtr.ToInt64();

        exStyle |= NativeMethods.WS_EX_TOOLWINDOW;
        exStyle |= NativeMethods.WS_EX_NOACTIVATE;
        exStyle |= NativeMethods.WS_EX_TOPMOST;

        const long wsExTransparent = 0x00000020L;
        exStyle &= ~wsExTransparent;

        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle));

        Log.Debug("Applied Win32 extended styles to HWND {Hwnd}: 0x{ExStyle:X8}", _hwnd, exStyle);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            // Intercept mouse activation to prevent stealing focus when clicking the capsule
            case NativeMethods.WM_MOUSEACTIVATE:
                handled = true;
                return new IntPtr(NativeMethods.MA_NOACTIVATE);

            // Non-client hit test: pure geometry evaluation without side effects (Task 2)
            case NativeMethods.WM_NCHITTEST:
            {
                if (IsPhysicalCursorOverInteractiveZone())
                {
                    handled = true;
                    return new IntPtr(NativeMethods.HTCLIENT); // (IntPtr)1
                }
                else
                {
                    handled = true;
                    return new IntPtr(NativeMethods.HTTRANSPARENT); // (IntPtr)(-1)
                }
            }

            // React to cursor movement and hover presence deterministically
            case NativeMethods.WM_MOUSEMOVE:
            case NativeMethods.WM_SETCURSOR:
            {
                OnPhysicalCursorPresence();
                break;
            }

            // React to display, resolution, and monitor connection/disconnection changes
            case NativeMethods.WM_DISPLAYCHANGE:
                int width = (int)(lParam.ToInt64() & 0xFFFF);
                int height = (int)((lParam.ToInt64() >> 16) & 0xFFFF);
                int depth = wParam.ToInt32();
                Log.Information("WM_DISPLAYCHANGE received ({Width}x{Height} @ {Depth}bpp). Re-evaluating display monitors and repositioning IslandWindow...", width, height, depth);
                _windowPositioner.PositionWindow(_hwnd);
                _windowPositioner.ReassertTopmost(_hwnd);
                break;

            // React to PerMonitor DPI changes
            case NativeMethods.WM_DPICHANGED:
                Log.Information("WM_DPICHANGED received. Repositioning IslandWindow...");
                _windowPositioner.PositionWindow(_hwnd);
                _windowPositioner.ReassertTopmost(_hwnd);
                break;

            // React to system power and battery broadcasts (0% CPU polling)
            case NativeMethods.WM_POWERBROADCAST:
                HandlePowerBroadcast(wParam, lParam);
                break;

            // React to system setting changes (Animation effects, High Contrast, etc.) - 100% reactive (Golden Rules 1 & 11)
            case NativeMethods.WM_SETTINGCHANGE:
                HandleSettingChange(wParam, lParam);
                break;

            // React to USB device arrival and removal
            case NativeMethods.WM_DEVICECHANGE:
                Log.Information("IslandWindow WndProc received WM_DEVICECHANGE: wParam=0x{WParam:X4}, lParam=0x{LParam:X16}",
                    wParam.ToInt32(), lParam.ToInt64());
                _deviceService?.HandleDeviceChange(wParam, lParam);
                break;

            // React to clipboard updates via native format listener
            case NativeMethods.WM_CLIPBOARDUPDATE:
                _clipboardService?.HandleClipboardUpdate();
                break;

            // React to system time, timezone, or daylight saving changes
            case NativeMethods.WM_TIMECHANGE:
                Log.Information("IslandWindow WndProc received WM_TIMECHANGE: broadcasting SystemTimeChangedMessage...");
                CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new Widgets.Messages.SystemTimeChangedMessage());
                break;

            // React to horizontal mouse wheel or precision touchpad tilt/swipe
            case NativeMethods.WM_MOUSEHWHEEL:
                if (_settings.EnableMediaGestures && IsPhysicalCursorOverInteractiveZone())
                {
                    var mediaWidget = _orchestrator.RegisteredWidgets.OfType<Widgets.Media.MediaWidget>().FirstOrDefault();
                    if (mediaWidget != null && mediaWidget.IsActive)
                    {
                        short wheelDelta = NativeMethods.GetWheelDelta(wParam);
                        if (mediaWidget.HandleWheelDelta(wheelDelta))
                        {
                            handled = true;
                            return IntPtr.Zero;
                        }
                    }
                }
                break;
        }

        return IntPtr.Zero;
    }

    private void HandleSettingChange(IntPtr wParam, IntPtr lParam)
    {
        int action = wParam.ToInt32();
        Log.Information("WM_SETTINGCHANGE received (wParam SPI: 0x{Action:X4})", action);

        // React reactively on UI thread
        Dispatcher.InvokeAsync(() =>
        {
            UpdateMotionProfileLive();
            UpdateHighContrastThemeLive();
        });
    }

    /// <summary>
    /// Evaluates the system animation preference and resolves the active motion profile in real time.
    /// Preserves in-flight spring velocities (Task 3).
    /// </summary>
    public void UpdateMotionProfileLive()
    {
        bool systemAnimations = SystemParameters.ClientAreaAnimation;
        var resolvedProfile = Core.Animation.MotionProfileResolver.Resolve(_settings.MotionMode, systemAnimations);
        _animator.ApplyProfile(resolvedProfile);
        IslandHostView.UpdateMotionProfile(resolvedProfile);

        CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(
            new Widgets.Messages.MotionProfileChangedMessage(resolvedProfile));

        Log.Information("MotionProfile updated live. Mode: {MotionMode}, Windows Animations: {SystemAnimations}, Stiffness: {Stiffness}, AllowDecorative: {AllowDecorative}",
            _settings.MotionMode, systemAnimations, resolvedProfile.Stiffness, resolvedProfile.AllowDecorative);
    }

    /// <summary>
    /// Reacts to system high contrast mode transitions.
    /// </summary>
    public void UpdateHighContrastThemeLive()
    {
        bool isHighContrast = SystemParameters.HighContrast;
        Infrastructure.AccessibilityThemeManager.ApplyTheme(isHighContrast);
        Log.Information("System HighContrast status evaluated and theme applied: {IsHighContrast}", isHighContrast);
    }

    private void OnSystemParametersStaticPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
        {
            Log.Information("SystemParameters.ClientAreaAnimation static property changed reactively: {Value}", SystemParameters.ClientAreaAnimation);
            UpdateMotionProfileLive();
        }
        else if (e.PropertyName == nameof(SystemParameters.HighContrast))
        {
            Log.Information("SystemParameters.HighContrast static property changed reactively: {Value}", SystemParameters.HighContrast);
            UpdateHighContrastThemeLive();
        }
    }

    private void HandlePowerBroadcast(IntPtr wParam, IntPtr lParam)
    {
        int eventCode = wParam.ToInt32();
        Log.Information("WM_POWERBROADCAST received (wParam: 0x{EventCode:X4})", eventCode);

        switch (eventCode)
        {
            case NativeMethods.PBT_APMSUSPEND:
                Log.Information("System suspending (PBT_APMSUSPEND). Halting timers and rendering loops.");
                _hoverEnterTimer.Stop();
                _hoverLeaveTimer.Stop();
                _orchestrator.SuspendForPower();
                _networkService?.NotifySuspended();
                _deviceService?.NotifySuspended();
                _clipboardService?.NotifySuspended();
                break;

            case NativeMethods.PBT_APMRESUMEAUTOMATIC:
            case NativeMethods.PBT_APMRESUMESUSPEND:
                Log.Information("System resuming from sleep (0x{EventCode:X4}). Repositioning and restoring state...", eventCode);
                _orchestrator.ResumeFromPower();
                if (_hwnd != IntPtr.Zero)
                {
                    _windowPositioner.PositionWindow(_hwnd);
                    _windowPositioner.ReassertTopmost(_hwnd);
                }
                _powerService?.RefreshPowerStatus(isInitial: false);
                _networkService?.NotifyResumed();
                _deviceService?.NotifyResumed();
                _clipboardService?.NotifyResumed();
                break;

            default:
                _powerService?.HandlePowerBroadcast(wParam, lParam);
                break;
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        Log.Information("SystemEvents.PowerModeChanged received: {Mode}", e.Mode);
        switch (e.Mode)
        {
            case PowerModes.Suspend:
                Dispatcher.InvokeAsync(() =>
                {
                    _hoverEnterTimer.Stop();
                    _hoverLeaveTimer.Stop();
                    _orchestrator.SuspendForPower();
                });
                break;

            case PowerModes.Resume:
                Dispatcher.InvokeAsync(() =>
                {
                    _orchestrator.ResumeFromPower();
                    if (_hwnd != IntPtr.Zero)
                    {
                        _windowPositioner.PositionWindow(_hwnd);
                        _windowPositioner.ReassertTopmost(_hwnd);
                    }
                    _powerService?.RefreshPowerStatus(isInitial: false);
                });
                break;
        }
    }

    private void OnSystemEventsTimeChanged(object? sender, EventArgs e)
    {
        Log.Information("SystemEvents.TimeChanged received: broadcasting SystemTimeChangedMessage...");
        CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new Widgets.Messages.SystemTimeChangedMessage());
    }

    private void OnForegroundWindowChanged(object? sender, IntPtr foregroundHwnd)
    {
        if (foregroundHwnd == _hwnd || _hwnd == IntPtr.Zero)
        {
            return;
        }

        Dispatcher.InvokeAsync(() =>
        {
            // Click-away: if another application window receives foreground focus while expanded, collapse cleanly
            if (_orchestrator.StateMachine.CurrentState == IslandState.Expanded)
            {
                Log.Debug("Foreground window changed while Island is Expanded. Collapsing capsule due to click-away.");
                _hoverLeaveTimer.Stop();
                _orchestrator.RequestCollapse();
            }

            if (_hwnd != IntPtr.Zero && IsVisible && !_orchestrator.IsFullscreenSuppressed)
            {
                _windowPositioner.ReassertTopmost(_hwnd);
            }
        });
    }

    private void OnFullscreenChanged(object? sender, bool isFullscreen)
    {
        if (!_settings.HideOnFullscreen) return;

        Dispatcher.InvokeAsync(() =>
        {
            if (isFullscreen)
            {
                Log.Information("Fullscreen detected. Suppressing island and collapsing view.");
                _orchestrator.SuspendForFullscreen();
                IslandHostView.Visibility = Visibility.Collapsed;
            }
            else
            {
                Log.Information("Fullscreen exited. Restoring island.");
                IslandHostView.Visibility = Visibility.Visible;
                _orchestrator.ResumeFromFullscreen();
                if (_hwnd != IntPtr.Zero && IsVisible)
                {
                    _windowPositioner.ReassertTopmost(_hwnd);
                }
            }
        });
    }

    private void SetupContextMenu()
    {
        MouseButtonEventHandler openMenu = (s, e) =>
        {
            var menu = new ContextMenu();

            var settingsItem = new MenuItem
            {
                Header = "⚙ Abrir Ajustes...",
                FontWeight = FontWeights.SemiBold
            };
            settingsItem.Click += (_, _) =>
            {
                var settingsWindow = ((App)Application.Current).Services.GetService(typeof(Views.SettingsWindow)) as Views.SettingsWindow;
                settingsWindow?.ShowSettings();
            };
            menu.Items.Add(settingsItem);
            menu.Items.Add(new Separator());

            var hwWidget = _orchestrator.RegisteredWidgets.OfType<HardwareWidget>().FirstOrDefault();
            if (hwWidget != null)
            {
                var hwItem = new MenuItem
                {
                    Header = "Monitor de Rendimiento (CPU/RAM)",
                    IsCheckable = true,
                    IsChecked = hwWidget.IsActive
                };
                hwItem.Click += (_, _) =>
                {
                    hwWidget.ToggleMonitoring();
                };
                menu.Items.Add(hwItem);
                menu.Items.Add(new Separator());
            }

#if DEBUG
            var debugItem = new MenuItem { Header = "🛠 Panel de Depuración y Widgets (DEBUG)" };
            debugItem.Click += (_, _) => ShowDebugWindow();
            menu.Items.Add(debugItem);
            menu.Items.Add(new Separator());
#endif

            var closeItem = new MenuItem { Header = "Cerrar openDynamic" };
            closeItem.Click += (_, _) =>
            {
                Log.Information("Exit requested via capsule context menu.");
                System.Windows.Application.Current.Shutdown();
            };
            menu.Items.Add(closeItem);

            menu.PlacementTarget = s as UIElement ?? IslandHostView.CapsuleBorder;
            menu.IsOpen = true;
            e.Handled = true;
        };

        IslandHostView.CapsuleBorder.MouseRightButtonUp += openMenu;
        IslandHostView.SatelliteBubble.MouseRightButtonUp += openMenu;
    }

    /// <summary>
    /// Evaluates if a physical screen coordinate falls within the resting notch sensor strip
    /// at the top edge of the screen when the island is in Hidden state.
    /// Converts physical screen coordinates to WPF device-independent pixels (DIPs)
    /// using <see cref="Visual.PointFromScreen"/> taking display DPI scaling into account.
    /// </summary>
    public bool IsScreenPointInHiddenSensorZone(int screenX, int screenY)
    {
        if (!this.IsLoaded || PresentationSource.FromVisual(this) == null)
        {
            return false;
        }

        try
        {
            Point screenPoint = new Point(screenX, screenY);
            Point clientPoint = this.PointFromScreen(screenPoint);

            double windowWidthDip = this.ActualWidth > 0 ? this.ActualWidth : this.Width;
            if (windowWidthDip <= 0) windowWidthDip = 640.0;

            double notchWidthDip = Math.Max(_settings.CapsuleWidth > 0 ? _settings.CapsuleWidth : 200.0, 240.0);
            double sensorHeightDip = Math.Max(_settings.CapsuleHeight > 0 ? _settings.CapsuleHeight : 28.0, 44.0);

            bool hit = (clientPoint.X >= ((windowWidthDip / 2.0) - (notchWidthDip / 2.0) - 2.0) &&
                        clientPoint.X <= ((windowWidthDip / 2.0) + (notchWidthDip / 2.0) + 2.0) &&
                        clientPoint.Y >= -5.0 && clientPoint.Y <= (sensorHeightDip + 2.0));

            if (clientPoint.Y < sensorHeightDip + 10.0)
            {
                double centerDip = windowWidthDip / 2.0;
                double minX = centerDip - (notchWidthDip / 2.0);
                double maxX = centerDip + (notchWidthDip / 2.0);
                Log.Debug("IsScreenPointInHiddenSensorZone: Screen=({ScreenX},{ScreenY}) -> Client=({ClientX:F1},{ClientY:F1}), Bounds=[{MinX:F1}..{MaxX:F1}, -5.0..{SensorHeight:F1}], Hit={Hit}",
                    screenX, screenY, clientPoint.X, clientPoint.Y, minX, maxX, sensorHeightDip, hit);
            }

            return hit;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Evaluates whether the physical mouse cursor (queried directly via Win32 GetCursorPos)
    /// falls within the interactive zone of the island, without relying on WPF IsMouseOver.
    /// </summary>
    public bool IsPhysicalCursorOverInteractiveZone()
    {
        if (!NativeMethods.GetCursorPos(out var cursorPos))
        {
            return false;
        }

        try
        {
            Point clientPoint = this.PointFromScreen(new Point(cursorPos.X, cursorPos.Y));
            double windowWidth = this.ActualWidth > 0 ? this.ActualWidth : 640.0;
            double center = windowWidth / 2.0;

            // When Island is Hidden: the interactive zone is the resting sensor notch at top edge
            if (_orchestrator.StateMachine.CurrentState == IslandState.Hidden)
            {
                double notchWidth = Math.Max(_settings.CapsuleWidth > 0 ? _settings.CapsuleWidth : 200.0, 240.0);
                double sensorHeight = Math.Max(_settings.CapsuleHeight > 0 ? _settings.CapsuleHeight : 28.0, 44.0);
                return (clientPoint.X >= (center - notchWidth / 2.0) &&
                        clientPoint.X <= (center + notchWidth / 2.0) &&
                        clientPoint.Y >= -5.0 && clientPoint.Y <= sensorHeight);
            }
            // When Island is active (Compact, Expanded, Split): the interactive zone is the physical capsule
            else
            {
                double capsuleWidth = IslandHostView.CapsuleBorder.ActualWidth > 0 ? IslandHostView.CapsuleBorder.ActualWidth : 200.0;
                double capsuleHeight = IslandHostView.CapsuleBorder.ActualHeight > 0 ? IslandHostView.CapsuleBorder.ActualHeight : 32.0;

                // Ensure initial spring expansion does not drop below resting notch dimensions
                double notchWidth = Math.Max(_settings.CapsuleWidth > 0 ? _settings.CapsuleWidth : 200.0, 240.0);
                double effectiveWidth = Math.Max(capsuleWidth, notchWidth);
                double effectiveHeight = Math.Max(capsuleHeight, 44.0);

                if (_orchestrator.StateMachine.CurrentState == IslandState.Split &&
                    IslandHostView.SatelliteBubble.Visibility == Visibility.Visible &&
                    IslandHostView.SatelliteBubble.ActualWidth > 0)
                {
                    effectiveWidth += IslandHostView.SatelliteBubble.ActualWidth + 10.0;
                }

                return (clientPoint.X >= (center - effectiveWidth / 2.0) &&
                        clientPoint.X <= (center + effectiveWidth / 2.0) &&
                        clientPoint.Y >= -5.0 && clientPoint.Y <= effectiveHeight + 4.0);
            }
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void OnPhysicalCursorPresence()
    {
        if (_orchestrator.IsPowerSuspended)
        {
            return;
        }

        if (IsPhysicalCursorOverInteractiveZone())
        {
            if (_hoverLeaveTimer.IsEnabled)
            {
                _hoverLeaveTimer.Stop();
            }

            if (!_orchestrator.IsHovering && !_hoverEnterTimer.IsEnabled)
            {
                Log.Debug("[Hover] Enter Timer Started");
                _hoverEnterTimer.Start();
            }
        }
        else
        {
            if (_hoverEnterTimer.IsEnabled)
            {
                _hoverEnterTimer.Stop();
            }

            if ((_orchestrator.IsHovering || _orchestrator.StateMachine.CurrentState == IslandState.Expanded) && !_hoverLeaveTimer.IsEnabled)
            {
                Log.Debug("[Hover] Leave Timer Started");
                _hoverLeaveTimer.Start();
            }
        }
    }

    private void OnPointerEnter()
    {
        OnPhysicalCursorPresence();
    }

    private void OnPointerLeave()
    {
        if (IsPhysicalCursorOverInteractiveZone())
        {
            return;
        }

        if (_hoverEnterTimer.IsEnabled)
        {
            _hoverEnterTimer.Stop();
        }

        var state = _orchestrator.StateMachine.CurrentState;
        if ((state == IslandState.Expanded || _orchestrator.IsHovering) && !_hoverLeaveTimer.IsEnabled)
        {
            Log.Debug("[Hover] Leave Timer Started");
            _hoverLeaveTimer.Start();
        }
    }

    private void SetupMouseInteractions()
    {
        var mainCapsule = IslandHostView.CapsuleBorder;
        var satellite = IslandHostView.SatelliteBubble;

        RestingSensorNotch.MouseEnter += (s, e) => OnPointerEnter();
        RestingSensorNotch.MouseLeave += (s, e) => OnPointerLeave();
        RestingSensorNotch.PreviewMouseMove += (s, e) => OnPhysicalCursorPresence();
        RestingSensorNotch.MouseLeftButtonUp += (s, e) =>
        {
            _hoverEnterTimer.Stop();
            _hoverLeaveTimer.Stop();

            if (_orchestrator.StateMachine.CurrentState == IslandState.Hidden)
            {
                if (_settings.EnableAmbientClock)
                {
                    _orchestrator.SetHovering(true);
                }
                else
                {
                    _orchestrator.RequestRestore();
                }
            }
            else if (_orchestrator.StateMachine.CurrentState is IslandState.Compact or IslandState.Split)
            {
                _orchestrator.RequestToggleExpand();
            }
        };

        this.PreviewMouseDown += (s, e) =>
        {
            if (_orchestrator.IsHovering && !IsPhysicalCursorOverInteractiveZone())
            {
                Log.Debug("Click outside notch detected while hovering. Hiding ambient clock.");
                _hoverLeaveTimer.Stop();
                _orchestrator.SetHovering(false);
            }

            if (_orchestrator.StateMachine.CurrentState == IslandState.Expanded && !IsPhysicalCursorOverInteractiveZone())
            {
                Log.Debug("Click outside notch detected within IslandWindow bounds. Collapsing capsule due to click-away.");
                _hoverLeaveTimer.Stop();
                _orchestrator.RequestCollapse();
            }
        };

        IslandHostView.MouseEnter += (s, e) => OnPointerEnter();
        IslandHostView.MouseLeave += (s, e) => OnPointerLeave();
        IslandHostView.PreviewMouseMove += (s, e) => OnPhysicalCursorPresence();
        IslandHostView.PreviewMouseWheel += (s, e) => OnPhysicalCursorPresence();
        IslandHostView.PreviewMouseDown += (s, e) => OnPhysicalCursorPresence();

        mainCapsule.MouseEnter += (s, e) => OnPointerEnter();
        mainCapsule.MouseLeave += (s, e) => OnPointerLeave();
        satellite.MouseEnter += (s, e) => OnPointerEnter();
        satellite.MouseLeave += (s, e) => OnPointerLeave();

        mainCapsule.MouseLeftButtonUp += (s, e) =>
        {
            _hoverEnterTimer.Stop();
            _hoverLeaveTimer.Stop();

            Log.Information("Capsule clicked. Delegating toggle expand to Orchestrator.");
            _orchestrator.RequestToggleExpand();
        };

        mainCapsule.PreviewMouseMove += (s, e) => OnPhysicalCursorPresence();

        mainCapsule.MouseWheel += (s, e) =>
        {
            _hoverEnterTimer.Stop();
            _hoverLeaveTimer.Stop();

            // In Expanded mode, mouse wheel is reserved for scrolling inside child views.
            // Never adjust volume or collapse the island while expanded.
            if (_orchestrator.StateMachine.CurrentState == IslandState.Expanded)
            {
                return;
            }

            // When VolumeWidget is available, mouse wheel directly adjusts volume and activates volume notice
            var volumeWidget = _orchestrator.RegisteredWidgets.OfType<Widgets.Volume.VolumeWidget>().FirstOrDefault();
            if (volumeWidget != null && _settings.EnableVolumeWidget)
            {
                volumeWidget.AdjustVolume(e.Delta);
                e.Handled = true;
                return;
            }

            if (e.Delta > 0)
            {
                // Scroll Up: Collapse / Hide
                _orchestrator.SetHovering(false);
                if (_orchestrator.StateMachine.CurrentState == IslandState.Expanded)
                {
                    Log.Information("MouseWheel Up detected on Expanded capsule. Collapsing.");
                    _orchestrator.RequestCollapse();
                }
                else if (_orchestrator.StateMachine.CurrentState is IslandState.Compact or IslandState.Split)
                {
                    Log.Information("MouseWheel Up detected. Hiding island.");
                    _orchestrator.RequestHide();
                }
            }
            else if (e.Delta < 0)
            {
                // Scroll Down: Expand / Reveal
                if (_orchestrator.StateMachine.CurrentState is IslandState.Compact or IslandState.Split)
                {
                    Log.Information("MouseWheel Down detected. Expanding capsule.");
                    _orchestrator.RequestExpand();
                }
                else if (_orchestrator.StateMachine.CurrentState == IslandState.Hidden)
                {
                    Log.Information("MouseWheel Down detected on Hidden capsule. Restoring.");
                    _orchestrator.RequestRestore();
                }
            }

            e.Handled = true;
        };

        // Satellite bubble click in Split mode: interactive multitasking swap (Hito M4)
        satellite.MouseLeftButtonUp += (s, e) =>
        {
            if (_orchestrator.StateMachine.CurrentState == IslandState.Split)
            {
                Log.Information("Satellite bubble clicked in Split mode. Swapping primary and secondary activities.");
                _orchestrator.SwapSplitActivities();
                e.Handled = true;
            }
        };
    }

    private void OnHoverEnterTimerTick(object? sender, EventArgs e)
    {
        _hoverEnterTimer.Stop();
        var state = _orchestrator.StateMachine.CurrentState;

        if (IsPhysicalCursorOverInteractiveZone() && !_orchestrator.IsPowerSuspended)
        {
            Log.Debug("[Hover] Enter Timer Fired -> Deploying");

            if ((state == IslandState.Hidden || _orchestrator.ActivePrimaryWidget == null) && _settings.EnableAmbientClock)
            {
                Log.Debug("Hover enter delay elapsed on sensor notch. Activating ambient clock.");
                _orchestrator.SetHovering(true);
            }
            else if (state is IslandState.Compact or IslandState.Split)
            {
                if (_orchestrator.ActivePrimaryWidget is not Widgets.Clock.AmbientClockWidget)
                {
                    Log.Debug("Hover enter delay elapsed. Expanding capsule.");
                    _orchestrator.RequestExpand();
                }
            }
        }
    }

    private void OnRestingHoverWatcherTick(object? sender, EventArgs e)
    {
        if (_orchestrator.StateMachine.CurrentState != IslandState.Hidden || _orchestrator.IsHovering)
        {
            _restingHoverWatcherTimer.Stop();
            return;
        }

        if (IsPhysicalCursorOverInteractiveZone())
        {
            OnPhysicalCursorPresence();
        }
    }

    private void OnHoverLeaveTimerTick(object? sender, EventArgs e)
    {
        _hoverLeaveTimer.Stop();
        var state = _orchestrator.StateMachine.CurrentState;

        if (!IsPhysicalCursorOverInteractiveZone())
        {
            Log.Debug("[Hover] Leave Timer Fired -> Collapsing");

            if (_orchestrator.IsHovering)
            {
                Log.Debug("Hover leave delay elapsed (350ms). Deactivating ambient clock.");
                _orchestrator.SetHovering(false);
            }

            if (state == IslandState.Expanded)
            {
                Log.Debug("Hover leave delay elapsed (350ms). Collapsing capsule.");
                _orchestrator.RequestCollapse();
            }
        }
        else
        {
            // Cursor re-entered the interactive zone before leave timer expired: preserve or re-trigger hover
            if (!_orchestrator.IsHovering && !_hoverEnterTimer.IsEnabled)
            {
                Log.Debug("[Hover] Enter Timer Started (Re-entered during leave grace period)");
                _hoverEnterTimer.Start();
            }
        }
    }

    private void OnAnimatorFrameUpdated(object? sender, EventArgs e)
    {
        IslandHostView.ApplyDimensions(_animator.CurrentDimensions, _animator.StateMachine.CurrentState);
    }

#if DEBUG
    public void ShowDebugWindow()
    {
        if (_debugWindow == null || !_debugWindow.IsLoaded)
        {
            _debugWindow = new IslandDebugWindow(_orchestrator);
            _debugWindow.Closed += (_, _) => _debugWindow = null;
            _debugWindow.Show();
        }
        else
        {
            _debugWindow.Activate();
        }
    }
#endif

    protected override void OnClosed(EventArgs e)
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.TimeChanged -= OnSystemEventsTimeChanged;
        SystemParameters.StaticPropertyChanged -= OnSystemParametersStaticPropertyChanged;

        _hoverEnterTimer.Stop();
        _hoverEnterTimer.Tick -= OnHoverEnterTimerTick;

        _hoverLeaveTimer.Stop();
        _hoverLeaveTimer.Tick -= OnHoverLeaveTimerTick;

        _restingHoverWatcherTimer.Stop();
        _restingHoverWatcherTimer.Tick -= OnRestingHoverWatcherTick;
        _animator.FrameUpdated -= OnAnimatorFrameUpdated;
        _animator.Settled -= OnAnimatorSettled;
        IslandHostView.SatelliteFadeOutCompleted -= OnSatelliteFadeOutCompleted;
        CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Unregister<Widgets.Messages.IslandStateChangedMessage>(this);

#if DEBUG
        _debugWindow?.Close();
#endif

        _foregroundWatcher.ForegroundWindowChanged -= OnForegroundWindowChanged;
        _foregroundWatcher.Dispose();

        if (_fullscreenWatcher != null)
        {
            _fullscreenWatcher.FullscreenChanged -= OnFullscreenChanged;
            _fullscreenWatcher.Dispose();
        }

        _clipboardService?.Stop();

        if (_privacyMonitor != null)
        {
            _privacyMonitor.StateChanged -= OnPrivacyStateChanged;
        }

        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }

        Log.Information("IslandWindow closed and native resources released.");
        base.OnClosed(e);
    }
}

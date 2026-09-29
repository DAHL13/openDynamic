using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
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
    private readonly Core.Settings.AppSettings _settings;

    private readonly DispatcherTimer _hoverEnterTimer;
    private readonly DispatcherTimer _hoverLeaveTimer;

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
        Core.Settings.AppSettings? settings = null)
    {
        _windowPositioner = windowPositioner ?? throw new ArgumentNullException(nameof(windowPositioner));
        _foregroundWatcher = foregroundWatcher ?? throw new ArgumentNullException(nameof(foregroundWatcher));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _animator = orchestrator.Animator;
        _powerService = powerService;
        _fullscreenWatcher = fullscreenWatcher;
        _settings = settings ?? new Core.Settings.AppSettings();

        InitializeComponent();

        _hoverEnterTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _hoverEnterTimer.Tick += OnHoverEnterTimerTick;

        _hoverLeaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        _hoverLeaveTimer.Tick += OnHoverLeaveTimerTick;

        _animator.FrameUpdated += OnAnimatorFrameUpdated;

        // Attach IslandView to Orchestrator for view delivery
        _orchestrator.AttachView(IslandHostView);

        SetupContextMenu();
        SetupMouseInteractions();

        // Apply initial layout dimensions
        IslandHostView.ApplyDimensions(_animator.CurrentDimensions, _animator.StateMachine.CurrentState);
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
        SystemParameters.StaticPropertyChanged += OnSystemParametersStaticPropertyChanged;
        UpdateMotionProfileLive();

        if (_fullscreenWatcher != null)
        {
            _fullscreenWatcher.FullscreenChanged += OnFullscreenChanged;
            _fullscreenWatcher.Start();
        }

        Log.Information("IslandWindow initialized successfully with HWND: {Hwnd}", _hwnd);
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

        if (_hwnd != IntPtr.Zero)
        {
            _windowPositioner.PositionWindow(_hwnd);
            _windowPositioner.ReassertTopmost(_hwnd);
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

        Log.Information("MotionProfile updated live. Mode: {MotionMode}, Windows Animations: {SystemAnimations}, Stiffness: {Stiffness}, AllowDecorative: {AllowDecorative}",
            _settings.MotionMode, systemAnimations, resolvedProfile.Stiffness, resolvedProfile.AllowDecorative);
    }

    /// <summary>
    /// Reacts to system high contrast mode transitions.
    /// </summary>
    public void UpdateHighContrastThemeLive()
    {
        bool isHighContrast = SystemParameters.HighContrast;
        Log.Information("System HighContrast status evaluated: {IsHighContrast}", isHighContrast);
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

    private void OnForegroundWindowChanged(object? sender, IntPtr foregroundHwnd)
    {
        if (foregroundHwnd == _hwnd || _hwnd == IntPtr.Zero)
        {
            return;
        }

        Dispatcher.InvokeAsync(() =>
        {
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

    private void SetupMouseInteractions()
    {
        var mainCapsule = IslandHostView.CapsuleBorder;
        var satellite = IslandHostView.SatelliteBubble;

        bool IsPointerOverNotch()
        {
            return IslandHostView.IsMouseOver ||
                   mainCapsule.IsMouseOver ||
                   satellite.IsMouseOver;
        }

        void OnPointerEnter()
        {
            _hoverLeaveTimer.Stop();

            var state = _orchestrator.StateMachine.CurrentState;
            if (state is IslandState.Compact or IslandState.Split)
            {
                if (!_hoverEnterTimer.IsEnabled)
                {
                    _hoverEnterTimer.Start();
                }
            }
            else if (state == IslandState.Hidden)
            {
                Log.Information("MouseEnter detected on Hidden sensor notch. Restoring capsule.");
                _orchestrator.RequestRestore();
            }
        }

        void OnPointerLeave()
        {
            // CRITICAL: If cursor is still physically within the notch boundary,
            // ignore internal boundary transitions across borders/child controls.
            if (IsPointerOverNotch())
            {
                return;
            }

            _hoverEnterTimer.Stop();

            var state = _orchestrator.StateMachine.CurrentState;
            if (state == IslandState.Expanded)
            {
                _hoverLeaveTimer.Stop();
                _hoverLeaveTimer.Start();
            }
        }

        IslandHostView.MouseEnter += (s, e) => OnPointerEnter();
        IslandHostView.MouseLeave += (s, e) => OnPointerLeave();
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

        mainCapsule.MouseWheel += (s, e) =>
        {
            _hoverEnterTimer.Stop();
            _hoverLeaveTimer.Stop();

            // When in volume mode, mouse wheel directly adjusts volume and resets grace timer
            if (_orchestrator.ActivePrimaryWidget is Widgets.Volume.VolumeWidget volumeWidget)
            {
                volumeWidget.AdjustVolume(e.Delta);
                e.Handled = true;
                return;
            }

            if (e.Delta > 0)
            {
                // Scroll Up: Collapse / Hide
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
            Log.Information("Satellite bubble clicked in Split mode. Swapping primary and secondary activities.");
            _orchestrator.SwapSplitActivities();
            e.Handled = true;
        };
    }

    private void OnHoverEnterTimerTick(object? sender, EventArgs e)
    {
        _hoverEnterTimer.Stop();
        bool isMouseOver = IslandHostView.IsMouseOver ||
                           IslandHostView.CapsuleBorder.IsMouseOver ||
                           IslandHostView.SatelliteBubble.IsMouseOver;
        var state = _orchestrator.StateMachine.CurrentState;

        if (isMouseOver && state is IslandState.Compact or IslandState.Split)
        {
            Log.Debug("Hover enter delay elapsed (250ms). Expanding capsule.");
            _orchestrator.RequestExpand();
        }
    }

    private void OnHoverLeaveTimerTick(object? sender, EventArgs e)
    {
        _hoverLeaveTimer.Stop();
        bool isMouseOver = IslandHostView.IsMouseOver ||
                           IslandHostView.CapsuleBorder.IsMouseOver ||
                           IslandHostView.SatelliteBubble.IsMouseOver;
        var state = _orchestrator.StateMachine.CurrentState;

        if (!isMouseOver && state == IslandState.Expanded)
        {
            Log.Debug("Hover leave delay elapsed (350ms). Collapsing capsule.");
            _orchestrator.RequestCollapse();
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
        SystemParameters.StaticPropertyChanged -= OnSystemParametersStaticPropertyChanged;

        _hoverEnterTimer.Stop();
        _hoverEnterTimer.Tick -= OnHoverEnterTimerTick;

        _hoverLeaveTimer.Stop();
        _hoverLeaveTimer.Tick -= OnHoverLeaveTimerTick;

        _animator.FrameUpdated -= OnAnimatorFrameUpdated;

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

        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }

        Log.Information("IslandWindow closed and native resources released.");
        base.OnClosed(e);
    }
}

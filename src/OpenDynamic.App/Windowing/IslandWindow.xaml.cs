using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using OpenDynamic.App.Animation;
using OpenDynamic.App.Native;
using OpenDynamic.Core.State;
using Serilog;

namespace OpenDynamic.App.Windowing;

/// <summary>
/// Interaction logic for IslandWindow.xaml.
/// Implements a borderless, layered transparent overlay with Win32 styles, reactive topmost z-order,
/// spring physics animation, and state machine mouse interactions.
/// </summary>
public partial class IslandWindow : Window
{
    private readonly WindowPositioner _windowPositioner;
    private readonly ForegroundWatcher _foregroundWatcher;
    private readonly IslandAnimator _animator;

    private readonly DispatcherTimer _hoverEnterTimer;
    private readonly DispatcherTimer _hoverLeaveTimer;

    private IntPtr _hwnd = IntPtr.Zero;
    private HwndSource? _hwndSource;

#if DEBUG
    private IslandDebugWindow? _debugWindow;
#endif

    public IslandWindow(WindowPositioner windowPositioner, ForegroundWatcher foregroundWatcher, IslandAnimator animator)
    {
        _windowPositioner = windowPositioner ?? throw new ArgumentNullException(nameof(windowPositioner));
        _foregroundWatcher = foregroundWatcher ?? throw new ArgumentNullException(nameof(foregroundWatcher));
        _animator = animator ?? throw new ArgumentNullException(nameof(animator));

        InitializeComponent();

        _hoverEnterTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _hoverEnterTimer.Tick += OnHoverEnterTimerTick;

        _hoverLeaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };
        _hoverLeaveTimer.Tick += OnHoverLeaveTimerTick;

        _animator.FrameUpdated += OnAnimatorFrameUpdated;
        _animator.Settled += OnAnimatorSettled;

        SetupContextMenu();
        SetupMouseInteractions();

        // Apply initial layout dimensions
        ApplyDimensions(_animator.CurrentDimensions);
    }

    public IslandWindow() : this(new WindowPositioner(), new ForegroundWatcher(), new IslandAnimator())
    {
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwnd = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(WndProc);

        ApplyWin32Styles();
        _windowPositioner.PositionWindow(_hwnd);

        _foregroundWatcher.ForegroundWindowChanged += OnForegroundWindowChanged;
        _foregroundWatcher.Start();

        Log.Information("IslandWindow initialized successfully with HWND: {Hwnd}", _hwnd);
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

            // React to display and resolution changes
            case NativeMethods.WM_DISPLAYCHANGE:
                Log.Information("WM_DISPLAYCHANGE received. Repositioning IslandWindow...");
                _windowPositioner.PositionWindow(_hwnd);
                break;

            // React to PerMonitor DPI changes
            case NativeMethods.WM_DPICHANGED:
                Log.Information("WM_DPICHANGED received. Repositioning IslandWindow...");
                _windowPositioner.PositionWindow(_hwnd);
                break;
        }

        return IntPtr.Zero;
    }

    private void OnForegroundWindowChanged(object? sender, IntPtr foregroundHwnd)
    {
        if (foregroundHwnd == _hwnd || _hwnd == IntPtr.Zero)
        {
            return;
        }

        Dispatcher.InvokeAsync(() =>
        {
            if (_hwnd != IntPtr.Zero && IsVisible)
            {
                _windowPositioner.ReassertTopmost(_hwnd);
            }
        });
    }

    private void SetupContextMenu()
    {
        CapsuleBorder.MouseRightButtonUp += (s, e) =>
        {
            var menu = new ContextMenu();

#if DEBUG
            var debugItem = new MenuItem { Header = "🛠 Panel de Depuración (DEBUG)" };
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

            menu.PlacementTarget = CapsuleBorder;
            menu.IsOpen = true;
            e.Handled = true;
        };
    }

    private void SetupMouseInteractions()
    {
        CapsuleBorder.MouseEnter += (s, e) =>
        {
            _hoverLeaveTimer.Stop();

            if (_animator.StateMachine.CurrentState == IslandState.Compact)
            {
                _hoverEnterTimer.Stop();
                _hoverEnterTimer.Start();
            }
            else if (_animator.StateMachine.CurrentState == IslandState.Hidden)
            {
                Log.Information("MouseEnter detected on Hidden sensor notch. Restoring Compact.");
                _animator.AnimateTo(IslandState.Compact);
            }
        };

        CapsuleBorder.MouseLeave += (s, e) =>
        {
            _hoverEnterTimer.Stop();

            if (_animator.StateMachine.CurrentState == IslandState.Expanded)
            {
                _hoverLeaveTimer.Stop();
                _hoverLeaveTimer.Start();
            }
        };

        CapsuleBorder.MouseLeftButtonUp += (s, e) =>
        {
            _hoverEnterTimer.Stop();
            _hoverLeaveTimer.Stop();

            switch (_animator.StateMachine.CurrentState)
            {
                case IslandState.Compact:
                    Log.Information("Capsule clicked. Expanding to Expanded state.");
                    _animator.AnimateTo(IslandState.Expanded);
                    break;

                case IslandState.Expanded:
                    Log.Information("Capsule clicked. Collapsing to Compact state.");
                    _animator.AnimateTo(IslandState.Compact);
                    break;

                case IslandState.Split:
                    Log.Information("Capsule clicked while Split. Returning to Compact.");
                    _animator.AnimateTo(IslandState.Compact);
                    break;

                case IslandState.Hidden:
                    _animator.AnimateTo(IslandState.Compact);
                    break;
            }
        };

        CapsuleBorder.MouseWheel += (s, e) =>
        {
            _hoverEnterTimer.Stop();
            _hoverLeaveTimer.Stop();

            if (e.Delta > 0)
            {
                // Scroll Up: Collapse / Hide
                if (_animator.StateMachine.CurrentState == IslandState.Expanded)
                {
                    Log.Information("MouseWheel Up detected on Expanded capsule. Collapsing to Compact.");
                    _animator.AnimateTo(IslandState.Compact);
                }
                else if (_animator.StateMachine.CurrentState == IslandState.Compact)
                {
                    Log.Information("MouseWheel Up detected on Compact capsule. Hiding island.");
                    _animator.AnimateTo(IslandState.Hidden);
                }
                else if (_animator.StateMachine.CurrentState == IslandState.Split)
                {
                    Log.Information("MouseWheel Up detected on Split capsule. Returning to Compact.");
                    _animator.AnimateTo(IslandState.Compact);
                }
            }
            else if (e.Delta < 0)
            {
                // Scroll Down: Expand / Reveal
                if (_animator.StateMachine.CurrentState == IslandState.Compact)
                {
                    Log.Information("MouseWheel Down detected on Compact capsule. Expanding to Expanded.");
                    _animator.AnimateTo(IslandState.Expanded);
                }
                else if (_animator.StateMachine.CurrentState == IslandState.Hidden)
                {
                    Log.Information("MouseWheel Down detected on Hidden capsule. Restoring Compact.");
                    _animator.AnimateTo(IslandState.Compact);
                }
            }

            e.Handled = true;
        };
    }

    private void OnHoverEnterTimerTick(object? sender, EventArgs e)
    {
        _hoverEnterTimer.Stop();
        if (CapsuleBorder.IsMouseOver && _animator.StateMachine.CurrentState == IslandState.Compact)
        {
            Log.Debug("Hover enter delay elapsed (150ms). Expanding capsule.");
            _animator.AnimateTo(IslandState.Expanded);
        }
    }

    private void OnHoverLeaveTimerTick(object? sender, EventArgs e)
    {
        _hoverLeaveTimer.Stop();
        if (!CapsuleBorder.IsMouseOver && _animator.StateMachine.CurrentState == IslandState.Expanded)
        {
            Log.Debug("Hover leave delay elapsed (400ms). Collapsing capsule to Compact.");
            _animator.AnimateTo(IslandState.Compact);
        }
    }

    private void OnAnimatorFrameUpdated(object? sender, EventArgs e)
    {
        ApplyDimensions(_animator.CurrentDimensions);
    }

    private void OnAnimatorSettled(object? sender, EventArgs e)
    {
        // Capsule remains Visibility.Visible at all times to maintain hit-testing on the Hidden sensor notch
    }

    private void ApplyDimensions(CapsuleDimensions dimensions)
    {
        double width = Math.Max(0.0, dimensions.Width);
        double height = Math.Max(0.0, dimensions.Height);
        double cornerRadius = Math.Max(0.0, dimensions.CornerRadius);
        double opacity = Math.Clamp(dimensions.Opacity, 0.0, 1.0);

        CapsuleBorder.Width = width;
        CapsuleBorder.Height = height;
        CapsuleBorder.CornerRadius = new CornerRadius(cornerRadius);
        CapsuleBorder.Opacity = opacity;

        if (width > 0.0 && height > 0.0)
        {
            CapsuleBorder.Clip = new RectangleGeometry(
                new Rect(0, 0, width, height),
                cornerRadius,
                cornerRadius);
        }
        else
        {
            CapsuleBorder.Clip = null;
        }
    }

#if DEBUG
    public void ShowDebugWindow()
    {
        if (_debugWindow == null || !_debugWindow.IsLoaded)
        {
            _debugWindow = new IslandDebugWindow(_animator);
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
        _hoverEnterTimer.Stop();
        _hoverLeaveTimer.Stop();

        _animator.FrameUpdated -= OnAnimatorFrameUpdated;
        _animator.Settled -= OnAnimatorSettled;
        _animator.Dispose();

#if DEBUG
        _debugWindow?.Close();
#endif

        _foregroundWatcher.ForegroundWindowChanged -= OnForegroundWindowChanged;
        _foregroundWatcher.Dispose();

        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }

        Log.Information("IslandWindow closed and native resources released.");
        base.OnClosed(e);
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using OpenDynamic.App.Native;
using Serilog;

namespace OpenDynamic.App.Windowing;

/// <summary>
/// Interaction logic for IslandWindow.xaml.
/// Implements a borderless, layered transparent overlay with Win32 styles and reactive topmost z-order.
/// </summary>
public partial class IslandWindow : Window
{
    private readonly WindowPositioner _windowPositioner;
    private readonly ForegroundWatcher _foregroundWatcher;
    private IntPtr _hwnd = IntPtr.Zero;
    private HwndSource? _hwndSource;

    public IslandWindow(WindowPositioner windowPositioner, ForegroundWatcher foregroundWatcher)
    {
        _windowPositioner = windowPositioner ?? throw new ArgumentNullException(nameof(windowPositioner));
        _foregroundWatcher = foregroundWatcher ?? throw new ArgumentNullException(nameof(foregroundWatcher));

        InitializeComponent();
        SetupDebugContextMenu();
    }

    public IslandWindow() : this(new WindowPositioner(), new ForegroundWatcher())
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
    /// Applies Win32 extended window styles: WS_EX_TOOLWINDOW and WS_EX_NOACTIVATE.
    /// Explicitly excludes WS_EX_TRANSPARENT to allow native WPF transparent pixel click-through.
    /// </summary>
    private void ApplyWin32Styles()
    {
        IntPtr currentExStylePtr = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE);
        long exStyle = currentExStylePtr.ToInt64();

        // Add WS_EX_TOOLWINDOW (hide from Alt+Tab and taskbar)
        // Add WS_EX_NOACTIVATE (prevent window activation on interaction)
        // Add WS_EX_TOPMOST
        exStyle |= NativeMethods.WS_EX_TOOLWINDOW;
        exStyle |= NativeMethods.WS_EX_NOACTIVATE;
        exStyle |= NativeMethods.WS_EX_TOPMOST;

        // Strictly ensure WS_EX_TRANSPARENT (0x00000020) is NOT set
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
        // Ignore if the foreground window is our own window
        if (foregroundHwnd == _hwnd || _hwnd == IntPtr.Zero)
        {
            return;
        }

        // Reassert HWND_TOPMOST reactively on the UI thread
        Dispatcher.InvokeAsync(() =>
        {
            if (_hwnd != IntPtr.Zero && IsVisible)
            {
                _windowPositioner.ReassertTopmost(_hwnd);
            }
        });
    }

    private void SetupDebugContextMenu()
    {
        // Right-clicking the capsule provides a clean exit command until Phase 7 tray icon is built
        CapsuleBorder.MouseRightButtonUp += (s, e) =>
        {
            var menu = new ContextMenu();
            var closeItem = new MenuItem { Header = "Cerrar openDynamic" };
            closeItem.Click += (_, _) =>
            {
                Log.Information("Exit requested via capsule context menu.");
                System.Windows.Application.Current.Shutdown();
            };
            menu.Items.Add(closeItem);
            menu.PlacementTarget = CapsuleBorder;
            menu.IsOpen = true;
        };
    }

    protected override void OnClosed(EventArgs e)
    {
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

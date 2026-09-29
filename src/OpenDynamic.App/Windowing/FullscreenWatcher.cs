using System.Runtime.InteropServices;
using OpenDynamic.App.Native;
using OpenDynamic.Core.Settings;
using Serilog;

namespace OpenDynamic.App.Windowing;

/// <summary>
/// Reactively detects exclusive fullscreen and borderless maximized fullscreen applications
/// (e.g. DirectX games, YouTube in browser, VLC media player) using SHQueryUserNotificationState
/// and WinEvent hooks (EVENT_SYSTEM_FOREGROUND and EVENT_OBJECT_LOCATIONCHANGE).
/// Adheres strictly to Golden Rule 1 (CPU ~0% at rest, zero continuous timers).
/// </summary>
public sealed class FullscreenWatcher : IDisposable
{
    private readonly AppSettings _settings;
    private readonly NativeMethods.WinEventProc _winEventProc;
    private IntPtr _foregroundHookHandle = IntPtr.Zero;
    private IntPtr _locationChangeHookHandle = IntPtr.Zero;
    private bool _isFullscreenActive;
    private bool _isDisposed;

    /// <summary>
    /// Indicates whether a fullscreen exclusive or borderless application is currently in the foreground.
    /// </summary>
    public bool IsFullscreenActive => _isFullscreenActive;

    /// <summary>
    /// Occurs when a fullscreen transition (enter or exit) is detected.
    /// </summary>
    public event EventHandler<bool>? FullscreenChanged;

    public FullscreenWatcher(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _winEventProc = OnWinEvent;
    }

    /// <summary>
    /// Installs the WinEvent hooks for EVENT_SYSTEM_FOREGROUND and EVENT_OBJECT_LOCATIONCHANGE.
    /// </summary>
    public void Start()
    {
        if (_foregroundHookHandle != IntPtr.Zero || _isDisposed)
        {
            return;
        }

        try
        {
            _foregroundHookHandle = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                _winEventProc,
                0,
                0,
                NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

            if (_foregroundHookHandle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                Log.Warning("FullscreenWatcher: Failed to install SetWinEventHook for EVENT_SYSTEM_FOREGROUND. Error: {ErrorCode}", error);
            }
            else
            {
                Log.Debug("FullscreenWatcher successfully installed SetWinEventHook for EVENT_SYSTEM_FOREGROUND (HookHandle: {HookHandle})", _foregroundHookHandle);
            }

            _locationChangeHookHandle = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
                NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
                IntPtr.Zero,
                _winEventProc,
                0,
                0,
                NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

            if (_locationChangeHookHandle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                Log.Warning("FullscreenWatcher: Failed to install SetWinEventHook for EVENT_OBJECT_LOCATIONCHANGE. Error: {ErrorCode}", error);
            }
            else
            {
                Log.Debug("FullscreenWatcher successfully installed SetWinEventHook for EVENT_OBJECT_LOCATIONCHANGE (HookHandle: {HookHandle})", _locationChangeHookHandle);
            }

            // Evaluate current state upon start
            IntPtr initialForeground = NativeMethods.GetForegroundWindow();
            if (initialForeground != IntPtr.Zero)
            {
                EvaluateFullscreenState(initialForeground);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected exception while starting FullscreenWatcher.");
        }
    }

    /// <summary>
    /// Unhooks the WinEvent listeners.
    /// </summary>
    public void Stop()
    {
        if (_foregroundHookHandle != IntPtr.Zero)
        {
            try
            {
                NativeMethods.UnhookWinEvent(_foregroundHookHandle);
                Log.Debug("FullscreenWatcher foreground hook unhooked successfully.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error unhooking foreground WinEvent in FullscreenWatcher.");
            }
            finally
            {
                _foregroundHookHandle = IntPtr.Zero;
            }
        }

        if (_locationChangeHookHandle != IntPtr.Zero)
        {
            try
            {
                NativeMethods.UnhookWinEvent(_locationChangeHookHandle);
                Log.Debug("FullscreenWatcher location change hook unhooked successfully.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error unhooking location change WinEvent in FullscreenWatcher.");
            }
            finally
            {
                _locationChangeHookHandle = IntPtr.Zero;
            }
        }
    }

    private void OnWinEvent(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (eventType == NativeMethods.EVENT_SYSTEM_FOREGROUND && hwnd != IntPtr.Zero)
        {
            EvaluateFullscreenState(hwnd);
        }
        else if (eventType == NativeMethods.EVENT_OBJECT_LOCATIONCHANGE && idObject == NativeMethods.OBJID_WINDOW && hwnd != IntPtr.Zero)
        {
            // Efficiency filter: only evaluate if the resizing/moving window is currently the foreground window
            IntPtr foregroundHwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == foregroundHwnd)
            {
                EvaluateFullscreenState(hwnd);
            }
        }
    }

    /// <summary>
    /// Evaluates whether the given foreground window represents an active fullscreen application.
    /// Combines SHQueryUserNotificationState with window bounds vs monitor geometry.
    /// </summary>
    public void EvaluateFullscreenState(IntPtr foregroundHwnd)
    {
        try
        {
            bool isFullscreen = false;

            // 1. Check SHQueryUserNotificationState
            int hr = NativeMethods.SHQueryUserNotificationState(out var queryState);
            if (hr == 0 && Core.Windowing.FullscreenDetector.IsNotificationStateFullscreen((int)queryState))
            {
                isFullscreen = true;
            }

            // 2. Check window bounds vs monitor bounds (catches borderless video/games like YouTube, VLC)
            if (!isFullscreen && foregroundHwnd != IntPtr.Zero)
            {
                IntPtr shellHwnd = NativeMethods.GetShellWindow();
                IntPtr desktopHwnd = NativeMethods.GetDesktopWindow();
                bool isShellOrDesktop = foregroundHwnd == shellHwnd || foregroundHwnd == desktopHwnd;

                if (!isShellOrDesktop)
                {
                    if (NativeMethods.GetWindowRect(foregroundHwnd, out var windowRect))
                    {
                        IntPtr hMonitor = NativeMethods.MonitorFromWindow(foregroundHwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
                        if (hMonitor != IntPtr.Zero)
                        {
                            var mi = new NativeMethods.MONITORINFO
                            {
                                cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>()
                            };

                            if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
                            {
                                var rc = mi.rcMonitor;
                                isFullscreen = Core.Windowing.FullscreenDetector.IsWindowBoundsFullscreen(
                                    windowRect.Left, windowRect.Top, windowRect.Right, windowRect.Bottom,
                                    rc.Left, rc.Top, rc.Right, rc.Bottom,
                                    isShellOrDesktop);
                            }
                        }
                    }
                }
            }

            if (isFullscreen != _isFullscreenActive)
            {
                _isFullscreenActive = isFullscreen;
                Log.Information("FullscreenWatcher: Fullscreen state changed -> {IsFullscreen} (QUNS: {QueryState}, HWND: {Hwnd})",
                    isFullscreen, queryState, foregroundHwnd);
                FullscreenChanged?.Invoke(this, isFullscreen);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception while evaluating fullscreen state in FullscreenWatcher.");
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        Stop();
        FullscreenChanged = null;
    }
}

using System.Runtime.InteropServices;
using OpenDynamic.App.Native;
using OpenDynamic.Core.Settings;
using Serilog;

namespace OpenDynamic.App.Windowing;

/// <summary>
/// Reactively detects exclusive fullscreen and borderless maximized fullscreen applications
/// (e.g. DirectX games, YouTube in browser, VLC media player) using SHQueryUserNotificationState
/// and WinEvent hooks (EVENT_SYSTEM_FOREGROUND).
/// Adheres strictly to Golden Rule 1 (CPU ~0% at rest, zero continuous timers).
/// </summary>
public sealed class FullscreenWatcher : IDisposable
{
    private readonly AppSettings _settings;
    private readonly NativeMethods.WinEventProc _winEventProc;
    private IntPtr _hookHandle = IntPtr.Zero;
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
    /// Installs the WinEvent hook for EVENT_SYSTEM_FOREGROUND.
    /// </summary>
    public void Start()
    {
        if (_hookHandle != IntPtr.Zero || _isDisposed)
        {
            return;
        }

        try
        {
            _hookHandle = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                _winEventProc,
                0,
                0,
                NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

            if (_hookHandle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                Log.Warning("FullscreenWatcher: Failed to install SetWinEventHook for EVENT_SYSTEM_FOREGROUND. Error: {ErrorCode}", error);
            }
            else
            {
                Log.Debug("FullscreenWatcher successfully installed SetWinEventHook (HookHandle: {HookHandle})", _hookHandle);
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
    /// Unhooks the WinEvent listener.
    /// </summary>
    public void Stop()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            try
            {
                NativeMethods.UnhookWinEvent(_hookHandle);
                Log.Debug("FullscreenWatcher unhooked successfully.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error unhooking WinEvent in FullscreenWatcher.");
            }
            finally
            {
                _hookHandle = IntPtr.Zero;
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

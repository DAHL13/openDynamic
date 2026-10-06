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
    private IntPtr _islandHwnd = IntPtr.Zero;
    private long _lastLocationEvalTick;
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
    /// Associates the IslandWindow HWND so fullscreen detection only suppresses the island
    /// when the fullscreen window is on the same display monitor as the island (AUD-006).
    /// </summary>
    public void SetIslandWindowHandle(IntPtr islandHwnd)
    {
        _islandHwnd = islandHwnd;
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

        if (_isFullscreenActive)
        {
            _isFullscreenActive = false;
            FullscreenChanged?.Invoke(this, false);
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
        if (eventType == NativeMethods.EVENT_SYSTEM_FOREGROUND)
        {
            IntPtr targetHwnd = (hwnd != IntPtr.Zero && NativeMethods.IsWindow(hwnd)) ? hwnd : NativeMethods.GetForegroundWindow();
            EvaluateFullscreenState(targetHwnd);
        }
        else if (eventType == NativeMethods.EVENT_OBJECT_LOCATIONCHANGE && idObject == NativeMethods.OBJID_WINDOW && idChild == 0)
        {
            long now = Environment.TickCount64;
            if (now - _lastLocationEvalTick < 50)
            {
                return;
            }

            // Efficiency filter: only evaluate if the resizing/moving window is currently the foreground window
            IntPtr foregroundHwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == foregroundHwnd || hwnd == IntPtr.Zero)
            {
                _lastLocationEvalTick = now;
                EvaluateFullscreenState(foregroundHwnd);
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
            if (!_settings.HideOnFullscreen)
            {
                if (_isFullscreenActive)
                {
                    _isFullscreenActive = false;
                    FullscreenChanged?.Invoke(this, false);
                }
                return;
            }

            bool isFullscreen = false;

            // Resolve foreground window if passed handle is null or invalid
            if (foregroundHwnd == IntPtr.Zero || !NativeMethods.IsWindow(foregroundHwnd))
            {
                foregroundHwnd = NativeMethods.GetForegroundWindow();
            }

            IntPtr foregroundMonitor = foregroundHwnd != IntPtr.Zero
                ? NativeMethods.MonitorFromWindow(foregroundHwnd, NativeMethods.MONITOR_DEFAULTTONEAREST)
                : IntPtr.Zero;
            IntPtr islandMonitor = (_islandHwnd != IntPtr.Zero && NativeMethods.IsWindow(_islandHwnd))
                ? NativeMethods.MonitorFromWindow(_islandHwnd, NativeMethods.MONITOR_DEFAULTTONEAREST)
                : IntPtr.Zero;

            bool isOnTargetMonitor = islandMonitor == IntPtr.Zero || foregroundMonitor == IntPtr.Zero || foregroundMonitor == islandMonitor;

            // 1. Check SHQueryUserNotificationState
            int hr = NativeMethods.SHQueryUserNotificationState(out var queryState);
            int qunsValue = (int)queryState;

            // Direct3D exclusive fullscreen or presentation mode on the island's monitor
            if (hr == 0 && isOnTargetMonitor &&
                (qunsValue == Core.Windowing.FullscreenDetector.QUNS_RUNNING_D3D_FULL_SCREEN ||
                 qunsValue == Core.Windowing.FullscreenDetector.QUNS_PRESENTATION_MODE))
            {
                isFullscreen = true;
            }
            // For QUNS_BUSY or normal window check:
            // In Windows 10/11, Focus Assist / Do Not Disturb sets QUNS_BUSY (2) even when idling on the desktop.
            // Therefore, verify whether a valid foreground window actually exists on the target monitor and covers it.
            else if (isOnTargetMonitor && foregroundHwnd != IntPtr.Zero && NativeMethods.IsWindow(foregroundHwnd))
            {
                IntPtr shellHwnd = NativeMethods.GetShellWindow();
                IntPtr desktopHwnd = NativeMethods.GetDesktopWindow();
                bool isShellOrDesktop = foregroundHwnd == shellHwnd || foregroundHwnd == desktopHwnd;

                if (!isShellOrDesktop && foregroundMonitor != IntPtr.Zero)
                {
                    if (NativeMethods.GetWindowRect(foregroundHwnd, out var windowRect))
                    {
                        var mi = new NativeMethods.MONITORINFO
                        {
                            cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>()
                        };

                        if (NativeMethods.GetMonitorInfo(foregroundMonitor, ref mi))
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

using System.Runtime.InteropServices;
using OpenDynamic.App.Native;
using Serilog;

namespace OpenDynamic.App.Windowing;

/// <summary>
/// Reactively monitors foreground window changes using SetWinEventHook.
/// Avoids polling and periodic timers entirely (Golden Rule 1).
/// </summary>
public sealed class ForegroundWatcher : IDisposable
{
    private readonly NativeMethods.WinEventProc _winEventProc;
    private IntPtr _hookHandle = IntPtr.Zero;
    private bool _isDisposed;

    /// <summary>
    /// Event fired when a foreground window change occurs in Windows.
    /// Passes the HWND of the new foreground window.
    /// </summary>
    public event EventHandler<IntPtr>? ForegroundWindowChanged;

    public ForegroundWatcher()
    {
        // Keep a strong reference to the delegate to prevent garbage collection
        _winEventProc = OnWinEvent;
    }

    /// <summary>
    /// Starts listening to EVENT_SYSTEM_FOREGROUND events.
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
                Log.Warning("Failed to install SetWinEventHook for EVENT_SYSTEM_FOREGROUND. Error code: {ErrorCode}", error);
            }
            else
            {
                Log.Debug("ForegroundWatcher successfully hooked EVENT_SYSTEM_FOREGROUND (HookHandle: {HookHandle})", _hookHandle);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected exception while initializing SetWinEventHook in ForegroundWatcher.");
        }
    }

    /// <summary>
    /// Stops listening to foreground events.
    /// </summary>
    public void Stop()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            try
            {
                NativeMethods.UnhookWinEvent(_hookHandle);
                Log.Debug("ForegroundWatcher unhooked successfully.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to unhook WinEvent in ForegroundWatcher.");
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
            try
            {
                ForegroundWindowChanged?.Invoke(this, hwnd);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error processing ForegroundWindowChanged event handler.");
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Stop();
    }
}

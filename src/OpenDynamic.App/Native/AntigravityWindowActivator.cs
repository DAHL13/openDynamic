using System.Runtime.InteropServices;
using System.Text;
using Serilog;

namespace OpenDynamic.App.Native;

/// <summary>
/// Best-effort window activator for bringing the Antigravity client or IDE to foreground.
/// If Windows restricts foreground elevation, flashes the taskbar button using FlashWindowEx.
/// </summary>
public static class AntigravityWindowActivator
{
    public static void TryActivateAntigravity()
    {
        try
        {
            IntPtr targetHwnd = IntPtr.Zero;

            NativeMethods.EnumWindows((hWnd, _) =>
            {
                if (!NativeMethods.IsWindowVisible(hWnd)) return true;

                var sb = new StringBuilder(256);
                int len = NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
                if (len > 0)
                {
                    string title = sb.ToString();
                    if (title.Contains("Antigravity", StringComparison.OrdinalIgnoreCase))
                    {
                        targetHwnd = hWnd;
                        return false; // Stop enumeration
                    }
                }

                return true;
            }, IntPtr.Zero);

            if (targetHwnd == IntPtr.Zero)
            {
                Log.Information("Antigravity window not found for foreground activation.");
                return;
            }

            bool activated = NativeMethods.SetForegroundWindow(targetHwnd);
            if (!activated)
            {
                Log.Information("SetForegroundWindow restricted by Windows. Flashing taskbar icon for HWND {Hwnd}.", targetHwnd);
                var fInfo = new NativeMethods.FLASHWINFO
                {
                    cbSize = (uint)Marshal.SizeOf<NativeMethods.FLASHWINFO>(),
                    hwnd = targetHwnd,
                    dwFlags = NativeMethods.FLASHW_ALL | NativeMethods.FLASHW_TIMERNOFG,
                    uCount = 5,
                    dwTimeout = 0
                };
                NativeMethods.FlashWindowEx(ref fInfo);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to activate Antigravity window.");
        }
    }
}

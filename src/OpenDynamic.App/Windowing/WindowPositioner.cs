using System.Runtime.InteropServices;
using OpenDynamic.App.Native;
using OpenDynamic.Core.Positioning;
using Serilog;

namespace OpenDynamic.App.Windowing;

/// <summary>
/// Positions the island overlay window on the target monitor using Win32 monitor queries
/// and <see cref="IslandPositionCalculator"/> pure logic.
/// </summary>
public class WindowPositioner
{
    public TargetMonitorMode TargetMode { get; set; } = TargetMonitorMode.Primary;

    public double TopMarginDip { get; set; } = IslandPositionCalculator.DefaultTopMarginDip;

    public double OffsetXDip { get; set; } = 0.0;

    public int TargetMonitorIndex { get; set; } = 0;

    public WindowDimensions Dimensions { get; set; } = WindowDimensions.DefaultIsland;

    /// <summary>
    /// Enumerates all connected display monitors using Win32 EnumDisplayMonitors without WinForms.
    /// </summary>
    public static List<IntPtr> GetAllMonitorHandles()
    {
        var monitors = new List<IntPtr>();
        try
        {
            NativeMethods.EnumDisplayMonitors(
                IntPtr.Zero,
                IntPtr.Zero,
                (IntPtr hMon, IntPtr _, ref NativeMethods.RECT _, IntPtr _) =>
                {
                    monitors.Add(hMon);
                    return true;
                },
                IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to enumerate display monitors.");
        }

        return monitors;
    }

    /// <summary>
    /// Gets human-readable monitor information for available displays.
    /// </summary>
    public static List<string> GetAvailableMonitorNames()
    {
        var handles = GetAllMonitorHandles();
        var names = new List<string>();

        for (int i = 0; i < handles.Count; i++)
        {
            var area = QueryMonitorArea(handles[i]);
            names.Add($"Monitor {i + 1} ({area.Width}x{area.Height})");
        }

        if (names.Count == 0)
        {
            names.Add("Monitor Principal");
        }

        return names;
    }

    /// <summary>
    /// Positions and sizes the HWND on the target monitor according to current DPI and margins.
    /// </summary>
    /// <param name="hwnd">The window handle to position.</param>
    /// <returns>The calculated window placement applied to the window.</returns>
    public CalculatedWindowPlacement PositionWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            throw new ArgumentException("Window handle cannot be zero.", nameof(hwnd));
        }

        var hMonitor = GetTargetMonitorHandle();
        var monitorArea = QueryMonitorArea(hMonitor);
        var dpi = QueryDpi(hMonitor, hwnd);

        var placement = IslandPositionCalculator.CalculatePlacement(
            monitorArea,
            dpi,
            Dimensions,
            TopMarginDip,
            OffsetXDip);

        Log.Information(
            "Positioning IslandWindow: X={X}, Y={Y}, Width={Width}, Height={Height} (Monitor: {MonLeft},{MonTop},{MonW}x{MonH}, DPI: {DpiX}x{DpiY}, Scale: {Scale:P0}, OffsetX: {OffsetX})",
            placement.X, placement.Y, placement.Width, placement.Height,
            monitorArea.Left, monitorArea.Top, monitorArea.Width, monitorArea.Height,
            dpi.DpiX, dpi.DpiY, dpi.ScaleX, OffsetXDip);

        bool success = NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOPMOST,
            placement.X,
            placement.Y,
            placement.Width,
            placement.Height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

        if (!success)
        {
            int error = Marshal.GetLastWin32Error();
            Log.Warning("SetWindowPos returned false for hwnd {Hwnd}. Error code: {ErrorCode}", hwnd, error);
        }

        return placement;
    }

    /// <summary>
    /// Reasserts the HWND_TOPMOST z-order for the window without moving or resizing it.
    /// </summary>
    /// <param name="hwnd">The window handle.</param>
    public void ReassertTopmost(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOPMOST,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private IntPtr GetTargetMonitorHandle()
    {
        if (TargetMode == TargetMonitorMode.Cursor)
        {
            if (NativeMethods.GetCursorPos(out var cursorPos))
            {
                var hMon = NativeMethods.MonitorFromPoint(cursorPos, NativeMethods.MONITOR_DEFAULTTONEAREST);
                if (hMon != IntPtr.Zero)
                {
                    return hMon;
                }
            }
        }

        if (TargetMonitorIndex >= 0)
        {
            var monitors = GetAllMonitorHandles();
            if (TargetMonitorIndex < monitors.Count)
            {
                return monitors[TargetMonitorIndex];
            }
        }

        return NativeMethods.MonitorFromWindow(IntPtr.Zero, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
    }

    private static MonitorArea QueryMonitorArea(IntPtr hMonitor)
    {
        var mi = new NativeMethods.MONITORINFO();
        mi.cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>();

        if (hMonitor != IntPtr.Zero && NativeMethods.GetMonitorInfo(hMonitor, ref mi))
        {
            return new MonitorArea(
                mi.rcMonitor.Left,
                mi.rcMonitor.Top,
                mi.rcMonitor.Width,
                mi.rcMonitor.Height);
        }

        Log.Warning("GetMonitorInfo failed for hMonitor {HMonitor}. Falling back to default 1920x1080 display area.", hMonitor);
        return new MonitorArea(0, 0, 1920, 1080);
    }

    private static DisplayDpi QueryDpi(IntPtr hMonitor, IntPtr hwnd)
    {
        // 1. Try GetDpiForMonitor from Shcore.dll
        if (hMonitor != IntPtr.Zero)
        {
            try
            {
                int hr = NativeMethods.GetDpiForMonitor(
                    hMonitor,
                    NativeMethods.MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI,
                    out uint dpiX,
                    out uint dpiY);

                if (hr == 0 && dpiX > 0 && dpiY > 0)
                {
                    return new DisplayDpi(dpiX, dpiY);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "GetDpiForMonitor call failed or not supported. Falling back to GetDpiForWindow.");
            }
        }

        // 2. Try GetDpiForWindow from User32.dll
        if (hwnd != IntPtr.Zero)
        {
            try
            {
                uint winDpi = NativeMethods.GetDpiForWindow(hwnd);
                if (winDpi > 0)
                {
                    return DisplayDpi.FromDpi(winDpi);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "GetDpiForWindow call failed. Falling back to default DPI.");
            }
        }

        // 3. Fallback to standard 96 DPI (100%)
        return DisplayDpi.Default;
    }
}

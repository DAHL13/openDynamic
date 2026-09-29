using System.Runtime.InteropServices;
using OpenDynamic.App.Native;
using OpenDynamic.Core.Positioning;
using Serilog;

namespace OpenDynamic.App.Windowing;

/// <summary>
/// Detailed monitor metadata including primary display status and desktop boundary area.
/// </summary>
public sealed record MonitorDetail(IntPtr Handle, MonitorArea Area, bool IsPrimary);

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
    /// Enumerates all connected display monitors and queries their area and primary status.
    /// Orders displays so that the Windows Primary monitor is always placed first (index 0).
    /// </summary>
    public static List<MonitorDetail> GetAllMonitors()
    {
        var monitors = new List<MonitorDetail>();
        try
        {
            NativeMethods.EnumDisplayMonitors(
                IntPtr.Zero,
                IntPtr.Zero,
                (IntPtr hMon, IntPtr _, ref NativeMethods.RECT _, IntPtr _) =>
                {
                    if (hMon != IntPtr.Zero)
                    {
                        var mi = new NativeMethods.MONITORINFO();
                        mi.cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>();
                        if (NativeMethods.GetMonitorInfo(hMon, ref mi))
                        {
                            bool isPrimary = (mi.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0;
                            var area = new MonitorArea(
                                mi.rcMonitor.Left,
                                mi.rcMonitor.Top,
                                mi.rcMonitor.Width,
                                mi.rcMonitor.Height);
                            monitors.Add(new MonitorDetail(hMon, area, isPrimary));
                        }
                    }
                    return true;
                },
                IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to enumerate display monitors.");
        }

        if (monitors.Count == 0)
        {
            IntPtr primaryHandle = NativeMethods.MonitorFromWindow(
                NativeMethods.GetDesktopWindow(),
                NativeMethods.MONITOR_DEFAULTTOPRIMARY);
            var area = QueryMonitorArea(primaryHandle);
            monitors.Add(new MonitorDetail(primaryHandle, area, IsPrimary: true));
        }

        // Guarantee that the Windows primary monitor is always at index 0
        return monitors.OrderByDescending(m => m.IsPrimary).ToList();
    }

    /// <summary>
    /// Enumerates all connected display monitor handles, with the primary monitor guaranteed first.
    /// </summary>
    public static List<IntPtr> GetAllMonitorHandles()
    {
        return GetAllMonitors().Select(m => m.Handle).ToList();
    }

    /// <summary>
    /// Gets human-readable monitor information for available displays,
    /// clearly labeling the Windows primary monitor (e.g. "Monitor 1 (Principal) (1920x1080)").
    /// </summary>
    public static List<string> GetAvailableMonitorNames()
    {
        var monitors = GetAllMonitors();
        var names = new List<string>();

        for (int i = 0; i < monitors.Count; i++)
        {
            var m = monitors[i];
            string primaryTag = m.IsPrimary ? " (Principal)" : "";
            names.Add($"Monitor {i + 1}{primaryTag} ({m.Area.Width}x{m.Area.Height})");
        }

        if (names.Count == 0)
        {
            names.Add("Monitor 1 (Principal) (1920x1080)");
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

        var monitors = GetAllMonitors();

        // TargetMonitorIndex 0 strictly resolves to the Primary monitor
        if (TargetMonitorIndex == 0)
        {
            var primary = monitors.FirstOrDefault(m => m.IsPrimary);
            if (primary != null && primary.Handle != IntPtr.Zero)
            {
                return primary.Handle;
            }

            return NativeMethods.MonitorFromWindow(
                NativeMethods.GetDesktopWindow(),
                NativeMethods.MONITOR_DEFAULTTOPRIMARY);
        }

        // Secondary / user-selected monitor index
        if (TargetMonitorIndex > 0 && TargetMonitorIndex < monitors.Count)
        {
            return monitors[TargetMonitorIndex].Handle;
        }

        // Safe fallback to Primary monitor
        return NativeMethods.MonitorFromWindow(
            NativeMethods.GetDesktopWindow(),
            NativeMethods.MONITOR_DEFAULTTOPRIMARY);
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

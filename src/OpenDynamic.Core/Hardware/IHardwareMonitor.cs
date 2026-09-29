namespace OpenDynamic.Core.Hardware;

/// <summary>
/// Domain contract for sampling hardware performance metrics.
/// </summary>
public interface IHardwareMonitor
{
    /// <summary>
    /// Current cached snapshot of hardware utilization.
    /// </summary>
    HardwareSnapshot CurrentSnapshot { get; }

    /// <summary>
    /// Samples current hardware metrics (CPU via Win32 GetSystemTimes, RAM via GlobalMemoryStatusEx, optional GPU).
    /// </summary>
    HardwareSnapshot Sample();
}

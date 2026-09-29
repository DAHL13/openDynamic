namespace OpenDynamic.Core.Hardware;

/// <summary>
/// Pure domain calculator for hardware performance deltas and metrics.
/// </summary>
public static class HardwareCalculator
{
    /// <summary>
    /// Computes CPU utilization percentage from Win32 GetSystemTimes delta values.
    /// In Windows, kernel time includes idle time, so total CPU work = (kernelDelta + userDelta).
    /// </summary>
    public static double CalculateCpuUsage(ulong idleDelta, ulong kernelDelta, ulong userDelta)
    {
        ulong totalDelta = kernelDelta + userDelta;
        if (totalDelta == 0)
        {
            return 0.0;
        }

        // When idleDelta exceeds totalDelta due to timestamp rounding, clamp idle ratio
        if (idleDelta >= totalDelta)
        {
            return 0.0;
        }

        double idleFraction = (double)idleDelta / totalDelta;
        double cpuPercent = (1.0 - idleFraction) * 100.0;

        return Math.Clamp(cpuPercent, 0.0, 100.0);
    }

    /// <summary>
    /// Computes RAM utilization percentage from total and available physical memory.
    /// </summary>
    public static double CalculateRamPercentage(ulong totalBytes, ulong availableBytes)
    {
        if (totalBytes == 0)
        {
            return 0.0;
        }

        ulong usedBytes = totalBytes > availableBytes ? totalBytes - availableBytes : 0;
        double percentage = ((double)usedBytes / totalBytes) * 100.0;

        return Math.Clamp(percentage, 0.0, 100.0);
    }

    /// <summary>
    /// Converts raw byte counts into Gigabytes.
    /// </summary>
    public static double ToGigabytes(ulong bytes)
    {
        return bytes / (1024.0 * 1024.0 * 1024.0);
    }
}

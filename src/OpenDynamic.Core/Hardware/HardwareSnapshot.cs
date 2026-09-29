namespace OpenDynamic.Core.Hardware;

/// <summary>
/// Immutable snapshot representing hardware metrics (CPU, RAM, and optional GPU).
/// Adheres strictly to Golden Rule 5 (isolated in Core, zero UI dependencies).
/// </summary>
/// <param name="CpuUsagePercent">CPU utilization percentage in range [0.0, 100.0].</param>
/// <param name="RamUsagePercent">RAM utilization percentage in range [0.0, 100.0].</param>
/// <param name="UsedRamGb">Used physical memory in Gigabytes.</param>
/// <param name="TotalRamGb">Total installed physical memory in Gigabytes.</param>
/// <param name="GpuUsagePercent">Optional GPU utilization percentage if enabled in settings; null if disabled.</param>
public sealed record HardwareSnapshot(
    double CpuUsagePercent,
    double RamUsagePercent,
    double UsedRamGb,
    double TotalRamGb,
    double? GpuUsagePercent);

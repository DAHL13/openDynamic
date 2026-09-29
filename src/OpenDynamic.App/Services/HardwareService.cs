using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenDynamic.App.Native;
using OpenDynamic.Core.Hardware;
using OpenDynamic.Core.Settings;
using Serilog;

namespace OpenDynamic.App.Services;

/// <summary>
/// Native hardware monitoring service implementing <see cref="IHardwareMonitor"/>.
/// Retrieves CPU usage via Win32 GetSystemTimes and physical memory via GlobalMemoryStatusEx.
/// GPU monitoring is strictly optional and disabled by default in settings to preserve zero idle CPU (Golden Rule 1).
/// Isolated from crashes with comprehensive error trapping and logging (Golden Rule 4).
/// </summary>
public sealed class HardwareService : IHardwareMonitor, IDisposable
{
    private readonly AppSettings _settings;
    private NativeMethods.FILETIME _prevIdleTime;
    private NativeMethods.FILETIME _prevKernelTime;
    private NativeMethods.FILETIME _prevUserTime;
    private bool _hasPreviousTimes;
    private HardwareSnapshot _currentSnapshot;

    // Optional GPU performance counters (only created if EnableGpuMonitoring is explicitly set to true)
    private PerformanceCounter[]? _gpuCounters;
    private bool _gpuInitializationAttempted;

    public HardwareSnapshot CurrentSnapshot => _currentSnapshot;

    public HardwareService(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _currentSnapshot = new HardwareSnapshot(0.0, 0.0, 0.0, 0.0, null);
    }

    /// <summary>
    /// Resets the CPU baseline so the next sample recalibrates timestamps without large delta spikes
    /// after an idle/hidden period.
    /// </summary>
    public void ResetCpuBaseline()
    {
        _hasPreviousTimes = false;
    }

    /// <summary>
    /// Samples current hardware metrics. Safe to call on any thread.
    /// </summary>
    public HardwareSnapshot Sample()
    {
        double cpuPercent = _currentSnapshot.CpuUsagePercent;
        double ramPercent = _currentSnapshot.RamUsagePercent;
        double usedGb = _currentSnapshot.UsedRamGb;
        double totalGb = _currentSnapshot.TotalRamGb;
        double? gpuPercent = null;

        // 1. Sample CPU usage via direct Win32 GetSystemTimes (Zero external dependencies)
        try
        {
            if (NativeMethods.GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
            {
                if (_hasPreviousTimes)
                {
                    ulong idleDelta = idleTime.Value - _prevIdleTime.Value;
                    ulong kernelDelta = kernelTime.Value - _prevKernelTime.Value;
                    ulong userDelta = userTime.Value - _prevUserTime.Value;
                    cpuPercent = HardwareCalculator.CalculateCpuUsage(idleDelta, kernelDelta, userDelta);
                }

                _prevIdleTime = idleTime;
                _prevKernelTime = kernelTime;
                _prevUserTime = userTime;
                _hasPreviousTimes = true;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error reading CPU times via Win32 GetSystemTimes.");
        }

        // 2. Sample RAM usage via Win32 GlobalMemoryStatusEx
        try
        {
            var memStatus = new NativeMethods.MEMORYSTATUSEX
            {
                dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>()
            };

            if (NativeMethods.GlobalMemoryStatusEx(ref memStatus))
            {
                ramPercent = memStatus.dwMemoryLoad;
                totalGb = HardwareCalculator.ToGigabytes(memStatus.ullTotalPhys);
                ulong usedBytes = memStatus.ullTotalPhys > memStatus.ullAvailPhys
                    ? memStatus.ullTotalPhys - memStatus.ullAvailPhys
                    : 0;
                usedGb = HardwareCalculator.ToGigabytes(usedBytes);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error reading RAM status via Win32 GlobalMemoryStatusEx.");
        }

        // 3. Optional GPU monitoring (strictly disabled by default)
        if (_settings.EnableGpuMonitoring)
        {
            gpuPercent = SampleGpuUsageSafe();
        }

        _currentSnapshot = new HardwareSnapshot(cpuPercent, ramPercent, usedGb, totalGb, gpuPercent);
        return _currentSnapshot;
    }

    private double? SampleGpuUsageSafe()
    {
        try
        {
            if (!_gpuInitializationAttempted)
            {
                _gpuInitializationAttempted = true;
                InitializeGpuCounters();
            }

            if (_gpuCounters == null || _gpuCounters.Length == 0)
            {
                return null;
            }

            double sum = 0.0;
            foreach (var counter in _gpuCounters)
            {
                try
                {
                    sum += counter.NextValue();
                }
                catch
                {
                    // Ignore single counter read failure
                }
            }

            return Math.Clamp(sum, 0.0, 100.0);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not sample GPU performance counters.");
            return null;
        }
    }

    private void InitializeGpuCounters()
    {
        try
        {
            if (!PerformanceCounterCategory.Exists("GPU Engine"))
            {
                Log.Debug("PerformanceCounterCategory 'GPU Engine' does not exist on this machine.");
                return;
            }

            var category = new PerformanceCounterCategory("GPU Engine");
            var instanceNames = category.GetInstanceNames();
            var counters = new List<PerformanceCounter>();

            foreach (var name in instanceNames)
            {
                // Filter 3D engine counters for representative GPU load
                if (name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                {
                    counters.Add(new PerformanceCounter("GPU Engine", "Utilization Percentage", name, true));
                }
            }

            _gpuCounters = counters.ToArray();
            Log.Information("HardwareService: Initialized {Count} GPU Engine performance counters.", _gpuCounters.Length);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to initialize GPU performance counters. GPU monitoring will be unavailable.");
            _gpuCounters = null;
        }
    }

    public void Dispose()
    {
        if (_gpuCounters != null)
        {
            foreach (var counter in _gpuCounters)
            {
                try
                {
                    counter.Dispose();
                }
                catch
                {
                    // Ignore disposal error
                }
            }
            _gpuCounters = null;
        }
    }
}

using OpenDynamic.Core.Hardware;
using Xunit;

namespace OpenDynamic.Tests.Hardware;

public sealed class HardwareCalculatorTests
{
    [Fact]
    public void CalculateCpuUsage_ZeroTotalDelta_ReturnsZero()
    {
        double cpu = HardwareCalculator.CalculateCpuUsage(0, 0, 0);
        Assert.Equal(0.0, cpu);
    }

    [Fact]
    public void CalculateCpuUsage_NormalDelta_CalculatesExpectedPercentage()
    {
        // Kernel: 800 (includes 800 idle), User: 200 -> Total: 1000, Idle: 800 -> 20% CPU
        ulong idleDelta = 800;
        ulong kernelDelta = 800;
        ulong userDelta = 200;

        double cpu = HardwareCalculator.CalculateCpuUsage(idleDelta, kernelDelta, userDelta);
        Assert.Equal(20.0, cpu, precision: 2);
    }

    [Fact]
    public void CalculateCpuUsage_FullLoad_Calculates100Percent()
    {
        ulong idleDelta = 0;
        ulong kernelDelta = 500;
        ulong userDelta = 500;

        double cpu = HardwareCalculator.CalculateCpuUsage(idleDelta, kernelDelta, userDelta);
        Assert.Equal(100.0, cpu);
    }

    [Fact]
    public void CalculateCpuUsage_IdleExceedsTotal_ClampsToZero()
    {
        ulong idleDelta = 1200;
        ulong kernelDelta = 800;
        ulong userDelta = 200;

        double cpu = HardwareCalculator.CalculateCpuUsage(idleDelta, kernelDelta, userDelta);
        Assert.Equal(0.0, cpu);
    }

    [Fact]
    public void CalculateRamPercentage_CalculatesCorrectly()
    {
        ulong totalBytes = 16UL * 1024 * 1024 * 1024;
        ulong availableBytes = 4UL * 1024 * 1024 * 1024; // 12GB used out of 16GB (75%)

        double ramPercent = HardwareCalculator.CalculateRamPercentage(totalBytes, availableBytes);
        Assert.Equal(75.0, ramPercent, precision: 2);
    }

    [Fact]
    public void ToGigabytes_ConvertsBytesToGb()
    {
        ulong bytes = 8UL * 1024 * 1024 * 1024;
        double gb = HardwareCalculator.ToGigabytes(bytes);
        Assert.Equal(8.0, gb, precision: 2);
    }
}

using OpenDynamic.Core.Settings;
using Xunit;

namespace OpenDynamic.Tests.Hardware;

public sealed class HardwareSettingsTests
{
    [Fact]
    public void EnableHardwareMonitoring_DefaultValue_IsFalse()
    {
        var settings = new AppSettings();
        Assert.False(settings.EnableHardwareMonitoring);
    }

    [Fact]
    public void EnableGpuMonitoring_DefaultValue_IsFalse()
    {
        var settings = new AppSettings();
        Assert.False(settings.EnableGpuMonitoring);
    }

    [Fact]
    public void HardwareDefaults_AreConfiguredAsExpected()
    {
        var settings = new AppSettings();
        Assert.Equal(2.0, settings.HardwareSamplingIntervalSeconds);
        Assert.Equal(10, settings.DefaultHardwarePriority);
    }
}

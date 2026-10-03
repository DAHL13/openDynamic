using OpenDynamic.Core.EnergySaver;
using Xunit;

namespace OpenDynamic.Tests.EnergySaver;

public class EnergySaverStateMapperTests
{
    [Theory]
    // Laptop with physical battery present (hasBattery: true)
    [InlineData(EnergySaverStateMapper.WinRtDisabled, true, EnergySaverState.Off)]
    [InlineData(EnergySaverStateMapper.WinRtOff, true, EnergySaverState.Off)]
    [InlineData(EnergySaverStateMapper.WinRtOn, true, EnergySaverState.On)]
    [InlineData(999, true, EnergySaverState.Unknown)]
    [InlineData(-1, true, EnergySaverState.Unknown)]
    // Desktop PC without physical battery (hasBattery: false)
    [InlineData(EnergySaverStateMapper.WinRtDisabled, false, EnergySaverState.NotSupported)]
    [InlineData(EnergySaverStateMapper.WinRtOff, false, EnergySaverState.NotSupported)]
    [InlineData(EnergySaverStateMapper.WinRtOn, false, EnergySaverState.NotSupported)]
    public void FromWinRt_MapsCorrectly(int rawValue, bool hasBattery, EnergySaverState expected)
    {
        var result = EnergySaverStateMapper.FromWinRt(rawValue, hasBattery);
        Assert.Equal(expected, result);
    }

    [Theory]
    // Laptop with physical battery present (hasBattery: true)
    [InlineData(0, true, EnergySaverState.Off)]
    [InlineData(1, true, EnergySaverState.On)]
    [InlineData(999, true, EnergySaverState.Unknown)]
    // Desktop PC without physical battery (hasBattery: false)
    [InlineData(0, false, EnergySaverState.NotSupported)]
    [InlineData(1, false, EnergySaverState.NotSupported)]
    public void FromWin32_MapsCorrectly(int rawValue, bool hasBattery, EnergySaverState expected)
    {
        var result = EnergySaverStateMapper.FromWin32(rawValue, hasBattery);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FromWinRt_DefaultHasBatteryTrue_MapsDisabledToOff()
    {
        // When hasBattery is omitted (defaults to true), Disabled must map to Off, NEVER NotSupported
        var result = EnergySaverStateMapper.FromWinRt(EnergySaverStateMapper.WinRtDisabled);
        Assert.Equal(EnergySaverState.Off, result);
    }
}

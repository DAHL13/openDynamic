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

    [Theory]
    // Laptop with physical battery present (hasBattery: true)
    [InlineData((byte)0, true, EnergySaverState.Off)]
    [InlineData((byte)1, true, EnergySaverState.On)]
    [InlineData((byte)255, true, EnergySaverState.Unknown)]
    // Desktop PC without physical battery (hasBattery: false)
    [InlineData((byte)0, false, EnergySaverState.NotSupported)]
    [InlineData((byte)1, false, EnergySaverState.NotSupported)]
    public void FromSystemStatusFlag_MapsCorrectly(byte rawValue, bool hasBattery, EnergySaverState expected)
    {
        var result = EnergySaverStateMapper.FromSystemStatusFlag(rawValue, hasBattery);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FromSystemStatusFlag_DefaultHasBatteryTrue_MapsZeroToOffAndOneToOn()
    {
        Assert.Equal(EnergySaverState.Off, EnergySaverStateMapper.FromSystemStatusFlag(0));
        Assert.Equal(EnergySaverState.On, EnergySaverStateMapper.FromSystemStatusFlag(1));
    }

    [Theory]
    // Desktop PC without physical battery -> always NotSupported
    [InlineData(1, 2, (byte)1, false, EnergySaverState.NotSupported)]
    [InlineData(0, 1, (byte)0, false, EnergySaverState.NotSupported)]
    // Windows 11 Quick Settings manual override ON (wnfOverride = 1), even when wnfState = 1 and systemStatusFlag = 0
    [InlineData(1, 1, (byte)0, true, EnergySaverState.On)]
    [InlineData(1, 0, (byte)0, true, EnergySaverState.On)]
    // Windows 11 Quick Settings manual override OFF (wnfOverride = 2), even when wnfState = 2
    [InlineData(2, 2, (byte)1, true, EnergySaverState.Off)]
    [InlineData(2, 1, (byte)0, true, EnergySaverState.Off)]
    // Auto mode (wnfOverride = 0 or null): engaged via WNF_PO_ENERGY_SAVER_STATE = 2
    [InlineData(0, 2, (byte)0, true, EnergySaverState.On)]
    [InlineData(null, 2, (byte)0, true, EnergySaverState.On)]
    // Auto mode (wnfOverride = 0 or null): engaged via legacy SystemStatusFlag = 1
    [InlineData(0, 1, (byte)1, true, EnergySaverState.On)]
    [InlineData(null, null, (byte)1, true, EnergySaverState.On)]
    // Auto mode: all indicators Off -> Off
    [InlineData(0, 1, (byte)0, true, EnergySaverState.Off)]
    [InlineData(null, null, (byte)0, true, EnergySaverState.Off)]
    public void FromWnf_MapsCorrectly(int? wnfOverride, int? wnfState, byte systemStatusFlag, bool hasBattery, EnergySaverState expected)
    {
        var result = EnergySaverStateMapper.FromWnf(wnfOverride, wnfState, systemStatusFlag, hasBattery);
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

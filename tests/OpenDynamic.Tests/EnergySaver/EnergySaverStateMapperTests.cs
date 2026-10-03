using OpenDynamic.Core.EnergySaver;
using Xunit;

namespace OpenDynamic.Tests.EnergySaver;

public class EnergySaverStateMapperTests
{
    [Theory]
    [InlineData(EnergySaverStateMapper.WinRtDisabled, EnergySaverState.NotSupported)]
    [InlineData(EnergySaverStateMapper.WinRtOff, EnergySaverState.Off)]
    [InlineData(EnergySaverStateMapper.WinRtOn, EnergySaverState.On)]
    [InlineData(999, EnergySaverState.Unknown)]
    [InlineData(-1, EnergySaverState.Unknown)]
    public void FromWinRt_MapsCorrectly(int rawValue, EnergySaverState expected)
    {
        var result = EnergySaverStateMapper.FromWinRt(rawValue);
        Assert.Equal(expected, result);
    }
}

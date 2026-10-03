namespace OpenDynamic.Core.EnergySaver;

/// <summary>
/// Deterministic mapper translating Windows Runtime (WinRT) EnergySaverStatus numeric values
/// into domain <see cref="EnergySaverState"/> representations.
/// Pure Core logic (Golden Rule 5).
/// </summary>
public static class EnergySaverStateMapper
{
    public const int WinRtDisabled = 0;
    public const int WinRtOff = 1;
    public const int WinRtOn = 2;

    /// <summary>
    /// Translates raw WinRT Windows.System.Power.EnergySaverStatus integer values to <see cref="EnergySaverState"/>.
    /// </summary>
    public static EnergySaverState FromWinRt(int winRtStatus)
    {
        return winRtStatus switch
        {
            WinRtOn => EnergySaverState.On,
            WinRtOff => EnergySaverState.Off,
            WinRtDisabled => EnergySaverState.NotSupported,
            _ => EnergySaverState.Unknown
        };
    }
}

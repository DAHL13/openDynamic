namespace OpenDynamic.Core.EnergySaver;

/// <summary>
/// Deterministic mapper translating Windows Runtime (WinRT) EnergySaverStatus numeric values
/// and Win32 power setting values into domain <see cref="EnergySaverState"/> representations.
/// Pure Core logic (Golden Rule 5).
/// </summary>
public static class EnergySaverStateMapper
{
    public const int WinRtDisabled = 0;
    public const int WinRtOff = 1;
    public const int WinRtOn = 2;

    /// <summary>
    /// Translates raw WinRT Windows.System.Power.EnergySaverStatus integer values to <see cref="EnergySaverState"/>,
    /// taking into account whether physical battery hardware is present.
    /// 'Disabled' (0) indicates energy saver is inactive (e.g. while plugged into AC power), NOT absence of battery.
    /// <see cref="EnergySaverState.NotSupported"/> is ONLY returned if <paramref name="hasBattery"/> is false.
    /// </summary>
    public static EnergySaverState FromWinRt(int winRtStatus, bool hasBattery = true)
    {
        if (!hasBattery)
        {
            return EnergySaverState.NotSupported;
        }

        return winRtStatus switch
        {
            WinRtOn => EnergySaverState.On,
            WinRtOff => EnergySaverState.Off,
            WinRtDisabled => EnergySaverState.Off,
            _ => EnergySaverState.Unknown
        };
    }

    /// <summary>
    /// Translates Win32 GUID_POWER_SAVING_STATUS raw integer value (0 = Off, 1 = On) to <see cref="EnergySaverState"/>,
    /// taking into account whether physical battery hardware is present.
    /// </summary>
    public static EnergySaverState FromWin32(int rawPowerSavingStatus, bool hasBattery = true)
    {
        if (!hasBattery)
        {
            return EnergySaverState.NotSupported;
        }

        return rawPowerSavingStatus switch
        {
            1 => EnergySaverState.On,
            0 => EnergySaverState.Off,
            _ => EnergySaverState.Unknown
        };
    }
}

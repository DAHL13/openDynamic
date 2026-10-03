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

    /// <summary>
    /// Translates Win32 SYSTEM_POWER_STATUS.SystemStatusFlag (0 = Battery Saver Off, 1 = Battery Saver On)
    /// to <see cref="EnergySaverState"/>, taking into account whether physical battery hardware is present.
    /// </summary>
    public static EnergySaverState FromSystemStatusFlag(byte systemStatusFlag, bool hasBattery = true)
    {
        if (!hasBattery)
        {
            return EnergySaverState.NotSupported;
        }

        return systemStatusFlag switch
        {
            1 => EnergySaverState.On,
            0 => EnergySaverState.Off,
            _ => EnergySaverState.Unknown
        };
    }

    /// <summary>
    /// Translates Windows Notification Facility (WNF) and system power states into domain <see cref="EnergySaverState"/>.
    /// Handles Windows 11 Energy Saver (where user manual override is tracked via WNF_PO_ENERGY_SAVER_OVERRIDE = 1 for On, 2 for Off),
    /// automatic engagement via WNF_PO_ENERGY_SAVER_STATE = 2, and legacy Windows 10 Battery Saver (SystemStatusFlag = 1).
    /// </summary>
    public static EnergySaverState FromWnf(int? wnfOverride, int? wnfState, byte systemStatusFlag = 0, bool hasBattery = true)
    {
        if (!hasBattery)
        {
            return EnergySaverState.NotSupported;
        }

        // 1. Explicit user override takes highest precedence in Windows 11
        // 1 = User forced Energy Saver ON via Quick Settings / Settings (PoEnergySaverOverrideEnabled)
        // 2 = User forced Energy Saver OFF (PoEnergySaverOverrideDisabled)
        // 0 = Auto / No override (follow threshold policy)
        if (wnfOverride.HasValue)
        {
            if (wnfOverride.Value == 1)
            {
                return EnergySaverState.On;
            }
            if (wnfOverride.Value == 2)
            {
                return EnergySaverState.Off;
            }
        }

        // 2. Automatic engagement via threshold or policy
        // In WNF_PO_ENERGY_SAVER_STATE: 2 = On, 1 = Off
        if (wnfState.HasValue && wnfState.Value == 2)
        {
            return EnergySaverState.On;
        }

        // 3. Legacy Windows 10 SYSTEM_POWER_STATUS
        if (systemStatusFlag == 1)
        {
            return EnergySaverState.On;
        }

        return EnergySaverState.Off;
    }
}

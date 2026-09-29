namespace OpenDynamic.Core.Hotkeys;

/// <summary>
/// Modifier flags for global system hotkeys matching Win32 MOD_* constants.
/// </summary>
[Flags]
public enum HotkeyModifiers : uint
{
    None = 0x0000,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008,
    NoRepeat = 0x4000
}

/// <summary>
/// Immutable representation of a parsed global hotkey.
/// </summary>
/// <param name="Modifiers">Active modifier keys.</param>
/// <param name="VirtualKey">Win32 Virtual-Key code.</param>
/// <param name="NormalizedText">Standardized human-readable text (e.g. "Win+Ctrl+I").</param>
public sealed record HotkeyDefinition(HotkeyModifiers Modifiers, uint VirtualKey, string NormalizedText)
{
    public override string ToString() => NormalizedText;
}

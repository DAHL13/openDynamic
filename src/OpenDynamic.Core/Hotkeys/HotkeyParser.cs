namespace OpenDynamic.Core.Hotkeys;

/// <summary>
/// Parser and validator for hotkey string representations (e.g. "Win+Ctrl+I", "Ctrl+Alt+Space").
/// </summary>
public static class HotkeyParser
{
    private static readonly Dictionary<string, uint> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Space", 0x20 },
        { "PageUp", 0x21 },
        { "Prior", 0x21 },
        { "PageDown", 0x22 },
        { "Next", 0x22 },
        { "End", 0x23 },
        { "Home", 0x24 },
        { "Left", 0x25 },
        { "Up", 0x26 },
        { "Right", 0x27 },
        { "Down", 0x28 },
        { "Insert", 0x2D },
        { "Delete", 0x2E },
        { "Del", 0x2E },
        { "Backspace", 0x08 },
        { "Back", 0x08 },
        { "Tab", 0x09 },
        { "Enter", 0x0D },
        { "Return", 0x0D },
        { "Escape", 0x1B },
        { "Esc", 0x1B },
        { "MediaPlayPause", 0xB3 },
        { "MediaNextTrack", 0xB0 },
        { "MediaPrevTrack", 0xB1 },
        { "MediaStop", 0xB2 },
        { "VolumeMute", 0xAD },
        { "VolumeDown", 0xAE },
        { "VolumeUp", 0xAF }
    };

    static HotkeyParser()
    {
        // Add Function keys F1 - F24 (0x70 to 0x87)
        for (int i = 1; i <= 24; i++)
        {
            NamedKeys[$"F{i}"] = (uint)(0x70 + (i - 1));
        }

        // Add Number keys 0 - 9 (0x30 to 0x39)
        for (int i = 0; i <= 9; i++)
        {
            NamedKeys[i.ToString()] = (uint)(0x30 + i);
        }

        // Add Alphabetic keys A - Z (0x41 to 0x5A)
        for (char c = 'A'; c <= 'Z'; c++)
        {
            NamedKeys[c.ToString()] = c;
        }
    }

    /// <summary>
    /// Attempts to parse a hotkey specification string into a <see cref="HotkeyDefinition"/>.
    /// </summary>
    /// <param name="input">The hotkey string (e.g. "Win+Ctrl+I").</param>
    /// <param name="definition">The parsed hotkey definition, if successful.</param>
    /// <param name="errorMessage">Detailed error message if parsing fails.</param>
    /// <returns>True if successfully parsed; otherwise false.</returns>
    public static bool TryParse(string? input, out HotkeyDefinition? definition, out string? errorMessage)
    {
        definition = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            errorMessage = "Hotkey string cannot be empty.";
            return false;
        }

        var parts = input.Split(['+', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            errorMessage = "No valid key parts found in hotkey string.";
            return false;
        }

        HotkeyModifiers modifiers = HotkeyModifiers.None;
        string? primaryKeyToken = null;

        foreach (var part in parts)
        {
            if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Windows", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Super", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= HotkeyModifiers.Windows;
            }
            else if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                     part.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= HotkeyModifiers.Control;
            }
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= HotkeyModifiers.Alt;
            }
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= HotkeyModifiers.Shift;
            }
            else
            {
                if (primaryKeyToken != null)
                {
                    errorMessage = $"Multiple primary keys specified: '{primaryKeyToken}' and '{part}'.";
                    return false;
                }
                primaryKeyToken = part;
            }
        }

        if (primaryKeyToken == null)
        {
            errorMessage = "Missing primary key in hotkey specification.";
            return false;
        }

        if (!NamedKeys.TryGetValue(primaryKeyToken, out uint vk))
        {
            errorMessage = $"Unrecognized key '{primaryKeyToken}'.";
            return false;
        }

        // Always add NoRepeat to prevent duplicate events on key hold
        modifiers |= HotkeyModifiers.NoRepeat;

        // Build normalized representation
        var normalizedParts = new List<string>();
        if ((modifiers & HotkeyModifiers.Windows) != 0) normalizedParts.Add("Win");
        if ((modifiers & HotkeyModifiers.Control) != 0) normalizedParts.Add("Ctrl");
        if ((modifiers & HotkeyModifiers.Alt) != 0) normalizedParts.Add("Alt");
        if ((modifiers & HotkeyModifiers.Shift) != 0) normalizedParts.Add("Shift");

        // Format primary key nicely
        string normalizedKey = primaryKeyToken.Length == 1
            ? primaryKeyToken.ToUpperInvariant()
            : char.ToUpperInvariant(primaryKeyToken[0]) + primaryKeyToken[1..].ToLowerInvariant();

        // Handle special key capitalizations like F1, PageUp, etc.
        if (normalizedKey.StartsWith('f') && normalizedKey.Length >= 2 && char.IsDigit(normalizedKey[1]))
        {
            normalizedKey = "F" + normalizedKey[1..];
        }

        normalizedParts.Add(normalizedKey);
        string normalizedString = string.Join("+", normalizedParts);

        definition = new HotkeyDefinition(modifiers, vk, normalizedString);
        return true;
    }
}

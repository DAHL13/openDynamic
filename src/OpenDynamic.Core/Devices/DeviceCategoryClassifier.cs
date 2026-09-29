namespace OpenDynamic.Core.Devices;

/// <summary>
/// Pure heuristic classifier that deduces a <see cref="DeviceCategory"/> from device names or descriptors.
/// </summary>
public static class DeviceCategoryClassifier
{
    private static readonly string[] AudioKeywords =
    [
        "headphone", "headset", "earbuds", "earbud", "earphone", "airpods", "buds",
        "wh-1000", "wf-1000", "bose", "speaker", "auricular", "auriculares", "altavoz",
        "altavoces", "audio", "soundbar", "sound", "galaxy buds", "freebuds"
    ];

    private static readonly string[] KeyboardKeywords =
    [
        "keyboard", "teclado", "keychron", "k380", "k375", "mx keys", "g915", "g815",
        "keypad", "numpad"
    ];

    private static readonly string[] MouseKeywords =
    [
        "mouse", "raton", "ratón", "pointer", "trackpad", "touchpad", "mx master",
        "mx anywhere", "trackball", "logitech g pro", "deathadder"
    ];

    private static readonly string[] StorageKeywords =
    [
        "storage", "flash drive", "pendrive", "usb drive", "cruzer", "sandisk",
        "kingston", "almacenamiento", "mass storage", "disk", "disco", "harddrive",
        "ssd", "hdd", "sd card", "transcend", "datatraveler"
    ];

    /// <summary>
    /// Classifies device category based on the provided friendly name or device identifier string.
    /// </summary>
    public static DeviceCategory Classify(string? nameOrId)
    {
        if (string.IsNullOrWhiteSpace(nameOrId))
        {
            return DeviceCategory.Other;
        }

        string text = nameOrId.ToLowerInvariant();

        foreach (var keyword in AudioKeywords)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return DeviceCategory.Audio;
            }
        }

        foreach (var keyword in KeyboardKeywords)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return DeviceCategory.Keyboard;
            }
        }

        foreach (var keyword in MouseKeywords)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return DeviceCategory.Mouse;
            }
        }

        foreach (var keyword in StorageKeywords)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return DeviceCategory.Storage;
            }
        }

        return DeviceCategory.Other;
    }
}

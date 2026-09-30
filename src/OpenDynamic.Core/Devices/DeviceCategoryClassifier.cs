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
        "altavoces", "audio", "soundbar", "sound", "galaxy buds", "freebuds", "anc", "tws"
    ];

    private static readonly string[] KeyboardKeywords =
    [
        "keyboard", "teclado", "keychron", "k380", "k375", "k400", "k270", "mx keys", "g915", "g815",
        "keypad", "numpad", "magic keyboard", "type cover", "hid_device_system_keyboard", "kbd"
    ];

    private static readonly string[] MouseKeywords =
    [
        "mouse", "raton", "ratón", "pointer", "pointing", "trackpad", "touchpad", "mx master",
        "mx anywhere", "trackball", "logitech g pro", "deathadder", "pebble", "m185", "m720",
        "g305", "g502", "razer", "hid_device_system_mouse", "cursor"
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
        return Classify(nameOrId, null, null);
    }

    /// <summary>
    /// Classifies device category based on the provided friendly name, Bluetooth Class of Device, and device path.
    /// </summary>
    public static DeviceCategory Classify(string? nameOrId, uint? bluetoothMajorClass, string? devicePath = null)
    {
        // 1. Bluetooth Class of Device Major Class 0x04 is strictly Audio/Video
        if (bluetoothMajorClass == 4)
        {
            return DeviceCategory.Audio;
        }

        string combined = string.Concat(nameOrId ?? "", " ", devicePath ?? "");
        if (string.IsNullOrWhiteSpace(combined))
        {
            if (bluetoothMajorClass == 5) return DeviceCategory.Mouse;
            return DeviceCategory.Other;
        }

        string text = combined.ToLowerInvariant();

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

        // 2. Bluetooth Major Class 0x05 is Peripheral; fallback to Mouse if unclassified
        if (bluetoothMajorClass == 5)
        {
            return DeviceCategory.Mouse;
        }

        return DeviceCategory.Other;
    }
}

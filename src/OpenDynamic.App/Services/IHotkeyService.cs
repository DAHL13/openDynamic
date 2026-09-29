namespace OpenDynamic.App.Services;

/// <summary>
/// Service contract for managing global system hotkeys via Win32 RegisterHotKey without low-level hooks.
/// </summary>
public interface IHotkeyService : IDisposable
{
    /// <summary>
    /// Gets the current active registered hotkey text.
    /// </summary>
    string? CurrentHotkey { get; }

    /// <summary>
    /// Indicates whether the last attempted hotkey collided with another application.
    /// </summary>
    bool HasConflict { get; }

    /// <summary>
    /// Friendly explanation of the conflict, if any.
    /// </summary>
    string? ConflictMessage { get; }

    /// <summary>
    /// Event raised when the registered hotkey is pressed globally.
    /// </summary>
    event EventHandler? HotkeyTriggered;

    /// <summary>
    /// Event raised when a hotkey registration conflict or error occurs.
    /// </summary>
    event EventHandler<string>? HotkeyConflictOccurred;

    /// <summary>
    /// Initializes the hotkey service with the given window handle and message hook.
    /// </summary>
    void Initialize(IntPtr hwnd, System.Windows.Interop.HwndSource hwndSource);

    /// <summary>
    /// Updates and registers a new hotkey specification in real time.
    /// </summary>
    /// <param name="hotkeySpec">String chord representation (e.g. "Win+Ctrl+I").</param>
    /// <returns>True if successfully registered; false if a conflict or parse error occurred.</returns>
    bool UpdateHotkey(string hotkeySpec);

    /// <summary>
    /// Unregisters any currently active hotkey.
    /// </summary>
    void Unregister();
}

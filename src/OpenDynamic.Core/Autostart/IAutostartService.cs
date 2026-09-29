namespace OpenDynamic.Core.Autostart;

/// <summary>
/// Service contract for managing application autostart on Windows logon (HKCU Run key).
/// </summary>
public interface IAutostartService
{
    /// <summary>
    /// Gets whether autostart is currently enabled in the registry.
    /// </summary>
    bool IsEnabled();

    /// <summary>
    /// Enables or disables autostart for the current user.
    /// </summary>
    /// <param name="enable">True to register; false to unregister.</param>
    /// <returns>True if the operation succeeded; otherwise false.</returns>
    bool SetEnabled(bool enable);

    /// <summary>
    /// Gets the currently registered executable path from the registry, if any.
    /// </summary>
    string? GetRegisteredPath();

    /// <summary>
    /// Verifies that the registered autostart path points to the current executable path,
    /// correcting it if the executable location has changed.
    /// </summary>
    /// <returns>True if updated or already up-to-date; false on error.</returns>
    bool VerifyAndCorrectExecutablePath();
}

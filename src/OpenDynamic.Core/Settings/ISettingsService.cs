namespace OpenDynamic.Core.Settings;

/// <summary>
/// Contract for managing application settings persistence, schema migration, and change notifications.
/// </summary>
public interface ISettingsService : IDisposable
{
    /// <summary>
    /// Gets the current loaded application settings.
    /// </summary>
    AppSettings CurrentSettings { get; }

    /// <summary>
    /// Gets the file path of the settings JSON file.
    /// </summary>
    string SettingsFilePath { get; }

    /// <summary>
    /// Gets the file path of the settings backup file.
    /// </summary>
    string BackupFilePath { get; }

    /// <summary>
    /// Raised whenever settings are updated or reloaded.
    /// </summary>
    event EventHandler<AppSettings>? SettingsChanged;

    /// <summary>
    /// Loads settings from disk synchronously. Falls back to defaults or backup if corrupt.
    /// </summary>
    void Load();

    /// <summary>
    /// Loads settings from disk asynchronously.
    /// </summary>
    Task LoadAsync();

    /// <summary>
    /// Schedules a debounced save to disk (default 500ms delay).
    /// Prevents excessive I/O writes during rapid slider or input changes.
    /// </summary>
    void SaveDebounced();

    /// <summary>
    /// Saves current settings to disk immediately, flushing any pending debounced writes.
    /// </summary>
    void SaveImmediate();

    /// <summary>
    /// Saves current settings to disk immediately and asynchronously.
    /// </summary>
    Task SaveImmediateAsync();
}

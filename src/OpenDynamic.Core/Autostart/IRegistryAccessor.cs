namespace OpenDynamic.Core.Autostart;

/// <summary>
/// Abstraction for registry read, write, and delete operations to enable unit testing and decoupling.
/// </summary>
public interface IRegistryAccessor
{
    /// <summary>
    /// Reads a string value from the specified subkey.
    /// </summary>
    string? GetValue(string subKey, string valueName);

    /// <summary>
    /// Sets a string value in the specified subkey.
    /// </summary>
    void SetValue(string subKey, string valueName, string value);

    /// <summary>
    /// Deletes a value from the specified subkey.
    /// </summary>
    void DeleteValue(string subKey, string valueName);
}

namespace OpenDynamic.Core.Autostart;

/// <summary>
/// Core logic for managing Windows Run autostart key without UI or platform dependencies.
/// </summary>
public class AutostartServiceCore : IAutostartService
{
    public const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string AppValueName = "openDynamic";

    private readonly IRegistryAccessor _registry;
    private readonly Func<string?> _getProcessPath;
    private readonly Action<string, Exception?>? _logger;

    public AutostartServiceCore(
        IRegistryAccessor registry,
        Func<string?>? getProcessPath = null,
        Action<string, Exception?>? logger = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _getProcessPath = getProcessPath ?? (() => Environment.ProcessPath);
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsEnabled()
    {
        try
        {
            string? value = _registry.GetValue(RunSubKey, AppValueName);
            return !string.IsNullOrWhiteSpace(value);
        }
        catch (Exception ex)
        {
            _logger?.Invoke("Failed to query autostart registry key.", ex);
            return false;
        }
    }

    /// <inheritdoc />
    public string? GetRegisteredPath()
    {
        try
        {
            string? raw = _registry.GetValue(RunSubKey, AppValueName);
            if (string.IsNullOrWhiteSpace(raw)) return null;

            return raw.Trim('\"', ' ');
        }
        catch (Exception ex)
        {
            _logger?.Invoke("Failed to read autostart registry path.", ex);
            return null;
        }
    }

    /// <inheritdoc />
    public bool SetEnabled(bool enable)
    {
        try
        {
            if (enable)
            {
                string? currentPath = _getProcessPath();
                if (string.IsNullOrWhiteSpace(currentPath))
                {
                    _logger?.Invoke("Cannot enable autostart: Current process path is null or empty.", null);
                    return false;
                }

                string formattedValue = $"\"{currentPath}\"";
                _registry.SetValue(RunSubKey, AppValueName, formattedValue);
                return true;
            }
            else
            {
                _registry.DeleteValue(RunSubKey, AppValueName);
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger?.Invoke($"Failed to set autostart to {enable}.", ex);
            return false;
        }
    }

    /// <inheritdoc />
    public bool VerifyAndCorrectExecutablePath()
    {
        try
        {
            if (!IsEnabled())
            {
                return true; // Not enabled, no path correction needed
            }

            string? registeredPath = GetRegisteredPath();
            string? currentPath = _getProcessPath();

            if (string.IsNullOrWhiteSpace(currentPath))
            {
                return false;
            }

            if (!string.Equals(registeredPath, currentPath, StringComparison.OrdinalIgnoreCase))
            {
                _logger?.Invoke(
                    "Autostart path mismatch detected. Updating registry with current executable path.",
                    null);

                return SetEnabled(true);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger?.Invoke("Failed to verify and correct autostart executable path.", ex);
            return false;
        }
    }
}

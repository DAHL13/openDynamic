using Microsoft.Win32;
using OpenDynamic.Core.Autostart;
using Serilog;

namespace OpenDynamic.App.Services;

/// <summary>
/// Production registry accessor interacting with HKCU without UAC elevation.
/// </summary>
public sealed class WindowsRegistryAccessor : IRegistryAccessor
{
    public string? GetValue(string subKey, string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKey, writable: false);
        return key?.GetValue(valueName) as string;
    }

    public void SetValue(string subKey, string valueName, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(subKey, writable: true);
        key.SetValue(valueName, value, RegistryValueKind.String);
    }

    public void DeleteValue(string subKey, string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKey, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}

/// <summary>
/// Windows autostart service modifying 'HKCU\Software\Microsoft\Windows\CurrentVersion\Run'.
/// Operates without UAC elevation and corrects path if application location changed.
/// </summary>
public sealed class AutostartService : AutostartServiceCore
{
    public AutostartService()
        : base(
            new WindowsRegistryAccessor(),
            () => Environment.ProcessPath,
            (msg, ex) =>
            {
                if (ex != null) Log.Warning(ex, "[Autostart] {Message}", msg);
                else Log.Information("[Autostart] {Message}", msg);
            })
    {
    }

    public AutostartService(IRegistryAccessor registry, Func<string?>? getProcessPath = null)
        : base(
            registry,
            getProcessPath,
            (msg, ex) =>
            {
                if (ex != null) Log.Warning(ex, "[Autostart] {Message}", msg);
                else Log.Information("[Autostart] {Message}", msg);
            })
    {
    }
}

using OpenDynamic.Core.Autostart;
using Xunit;

namespace OpenDynamic.Tests.Autostart;

public sealed class AutostartServiceTests
{
    private sealed class FakeRegistryAccessor : IRegistryAccessor
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

        public string? GetValue(string subKey, string valueName)
        {
            string key = $"{subKey}\\{valueName}";
            return _values.TryGetValue(key, out var val) ? val : null;
        }

        public void SetValue(string subKey, string valueName, string value)
        {
            string key = $"{subKey}\\{valueName}";
            _values[key] = value;
        }

        public void DeleteValue(string subKey, string valueName)
        {
            string key = $"{subKey}\\{valueName}";
            _values.Remove(key);
        }
    }

    [Fact]
    public void IsEnabled_WhenValueDoesNotExist_ReturnsFalse()
    {
        var fakeRegistry = new FakeRegistryAccessor();
        var service = new AutostartServiceCore(fakeRegistry, () => @"C:\App\openDynamic.exe");

        Assert.False(service.IsEnabled());
        Assert.Null(service.GetRegisteredPath());
    }

    [Fact]
    public void SetEnabled_True_SetsQuotedPathInRegistry()
    {
        var fakeRegistry = new FakeRegistryAccessor();
        const string exePath = @"C:\Program Files\openDynamic\openDynamic.exe";
        var service = new AutostartServiceCore(fakeRegistry, () => exePath);

        bool success = service.SetEnabled(true);

        Assert.True(success);
        Assert.True(service.IsEnabled());
        Assert.Equal(exePath, service.GetRegisteredPath());
        Assert.Equal($"\"{exePath}\"", fakeRegistry.GetValue(AutostartServiceCore.RunSubKey, AutostartServiceCore.AppValueName));
    }

    [Fact]
    public void SetEnabled_False_RemovesRegistryValue()
    {
        var fakeRegistry = new FakeRegistryAccessor();
        const string exePath = @"C:\App\openDynamic.exe";
        var service = new AutostartServiceCore(fakeRegistry, () => exePath);

        service.SetEnabled(true);
        Assert.True(service.IsEnabled());

        bool disabled = service.SetEnabled(false);
        Assert.True(disabled);
        Assert.False(service.IsEnabled());
        Assert.Null(fakeRegistry.GetValue(AutostartServiceCore.RunSubKey, AutostartServiceCore.AppValueName));
    }

    [Fact]
    public void VerifyAndCorrectExecutablePath_WhenPathChanged_UpdatesToNewLocation()
    {
        var fakeRegistry = new FakeRegistryAccessor();
        const string oldPath = @"C:\OldLocation\openDynamic.exe";
        const string newPath = @"C:\NewLocation\openDynamic.exe";

        // Setup with old path
        fakeRegistry.SetValue(AutostartServiceCore.RunSubKey, AutostartServiceCore.AppValueName, $"\"{oldPath}\"");

        var service = new AutostartServiceCore(fakeRegistry, () => newPath);
        Assert.True(service.IsEnabled());
        Assert.Equal(oldPath, service.GetRegisteredPath());

        // Act - should detect mismatch and update
        bool corrected = service.VerifyAndCorrectExecutablePath();

        // Assert
        Assert.True(corrected);
        Assert.Equal(newPath, service.GetRegisteredPath());
        Assert.Equal($"\"{newPath}\"", fakeRegistry.GetValue(AutostartServiceCore.RunSubKey, AutostartServiceCore.AppValueName));
    }

    [Fact]
    public void VerifyAndCorrectExecutablePath_WhenNotEnabled_DoesNothing()
    {
        var fakeRegistry = new FakeRegistryAccessor();
        var service = new AutostartServiceCore(fakeRegistry, () => @"C:\App\openDynamic.exe");

        bool result = service.VerifyAndCorrectExecutablePath();

        Assert.True(result);
        Assert.False(service.IsEnabled());
        Assert.Null(fakeRegistry.GetValue(AutostartServiceCore.RunSubKey, AutostartServiceCore.AppValueName));
    }
}

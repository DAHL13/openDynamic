using OpenDynamic.Core.Devices;
using OpenDynamic.Tests.Timer;
using Xunit;

namespace OpenDynamic.Tests.Devices;

public sealed class DeviceAlertPolicyTests
{
    [Theory]
    [InlineData("Sony WH-1000XM5", DeviceCategory.Audio)]
    [InlineData("AirPods Pro 2", DeviceCategory.Audio)]
    [InlineData("Logitech G733 Wireless Headset", DeviceCategory.Audio)]
    [InlineData("Bose SoundLink Flex", DeviceCategory.Audio)]
    [InlineData("Galaxy Buds2 Pro", DeviceCategory.Audio)]
    [InlineData("Keychron K2 Pro", DeviceCategory.Keyboard)]
    [InlineData("Logitech MX Keys Wireless Keyboard", DeviceCategory.Keyboard)]
    [InlineData("Teclado Español USB", DeviceCategory.Keyboard)]
    [InlineData("Logitech MX Master 3S", DeviceCategory.Mouse)]
    [InlineData("Razer DeathAdder V3 Mouse", DeviceCategory.Mouse)]
    [InlineData("Magic Trackpad", DeviceCategory.Mouse)]
    [InlineData("SanDisk Ultra USB 3.0 Flash Drive", DeviceCategory.Storage)]
    [InlineData("Kingston DataTraveler 64GB", DeviceCategory.Storage)]
    [InlineData("Cruzer Blade USB Device", DeviceCategory.Storage)]
    [InlineData("Generic Mass Storage Device", DeviceCategory.Storage)]
    [InlineData("Arduino Uno R3", DeviceCategory.Other)]
    [InlineData("Xbox Wireless Controller", DeviceCategory.Other)]
    [InlineData(null, DeviceCategory.Other)]
    [InlineData("", DeviceCategory.Other)]
    public void DeviceCategoryClassifier_CorrectlyIdentifiesCategory(string? input, DeviceCategory expected)
    {
        var category = DeviceCategoryClassifier.Classify(input);
        Assert.Equal(expected, category);
    }

    [Fact]
    public void EnumerationCompleted_SuppressesPreExistingConnectedDevices()
    {
        var clock = new FakeTimeProvider();
        using var policy = new DeviceAlertPolicy(clock);

        var alerts = new List<DeviceEvent>();
        policy.AlertTriggered += (_, ev) => alerts.Add(ev);

        // Pre-existing devices reported during initial enumeration
        var dev1 = new DeviceEvent(DeviceEventType.Connected, "USB\\VID_046D&PID_C52B", "Logitech Receiver", DeviceCategory.Mouse);
        var dev2 = new DeviceEvent(DeviceEventType.Connected, "BTH\\DEV_94DB56", "Sony WH-1000XM4", DeviceCategory.Audio);

        policy.ProcessDeviceEvent(dev1);
        policy.ProcessDeviceEvent(dev2);

        // Advance beyond coalesce window
        clock.Advance(TimeSpan.FromSeconds(2));

        // Must be zero alerts before enumeration completed
        Assert.Empty(alerts);
        Assert.False(policy.IsEnumerationCompleted);

        // Enumeration completes
        policy.NotifyEnumerationCompleted();
        Assert.True(policy.IsEnumerationCompleted);

        // Newly attached device after enumeration completed
        var dev3 = new DeviceEvent(DeviceEventType.Connected, "USB\\VID_0781&PID_5581", "SanDisk Ultra", DeviceCategory.Storage);
        policy.ProcessDeviceEvent(dev3);

        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Single(alerts);
        Assert.Equal("SanDisk Ultra", alerts[0].DeviceName);
        Assert.Equal(DeviceCategory.Storage, alerts[0].Category);
    }

    [Fact]
    public void BurstCoalescing_EmitsSingleConsolidatedAlert()
    {
        var clock = new FakeTimeProvider();
        using var policy = new DeviceAlertPolicy(clock, coalesceDuration: TimeSpan.FromMilliseconds(800));
        policy.NotifyEnumerationCompleted();

        var alerts = new List<DeviceEvent>();
        policy.AlertTriggered += (_, ev) => alerts.Add(ev);

        string devId = "BTHENUM\\DEV_123456";

        // Flurry of sub-interface and battery events in 400ms
        policy.ProcessDeviceEvent(new DeviceEvent(DeviceEventType.Connected, devId, "AirPods Pro", DeviceCategory.Audio, BatteryPercent: null));
        clock.Advance(TimeSpan.FromMilliseconds(200));

        policy.ProcessDeviceEvent(new DeviceEvent(DeviceEventType.Connected, devId, "AirPods Pro", DeviceCategory.Audio, BatteryPercent: 80));
        clock.Advance(TimeSpan.FromMilliseconds(200));

        policy.ProcessDeviceEvent(new DeviceEvent(DeviceEventType.Connected, devId, "AirPods Pro", DeviceCategory.Audio, BatteryPercent: 85));

        // Advance 400ms (still within 800ms of last event)
        clock.Advance(TimeSpan.FromMilliseconds(400));
        Assert.Empty(alerts);

        // Advance past coalesce window
        clock.Advance(TimeSpan.FromMilliseconds(500));

        Assert.Single(alerts);
        Assert.Equal("AirPods Pro", alerts[0].DeviceName);
        Assert.Equal(85, alerts[0].BatteryPercent);
    }

    [Fact]
    public void IgnoredDevices_AreSuppressed_ByNameOrId()
    {
        var clock = new FakeTimeProvider();
        using var policy = new DeviceAlertPolicy(
            clock,
            initialIgnoredDevices: new[] { "Ignored Mouse", "USB\\VID_IGNORED" });
        policy.NotifyEnumerationCompleted();

        var alerts = new List<DeviceEvent>();
        policy.AlertTriggered += (_, ev) => alerts.Add(ev);

        // Ignored by name
        policy.ProcessDeviceEvent(new DeviceEvent(DeviceEventType.Connected, "USB\\VID_9999", "Ignored Mouse", DeviceCategory.Mouse));
        // Ignored by ID
        policy.ProcessDeviceEvent(new DeviceEvent(DeviceEventType.Connected, "USB\\VID_IGNORED", "Other Device", DeviceCategory.Other));
        // Allowed device
        policy.ProcessDeviceEvent(new DeviceEvent(DeviceEventType.Connected, "USB\\VID_0001", "Allowed Keyboard", DeviceCategory.Keyboard));

        clock.Advance(TimeSpan.FromSeconds(2));

        Assert.Single(alerts);
        Assert.Equal("Allowed Keyboard", alerts[0].DeviceName);
    }

    [Fact]
    public void SuspendResume_SuppressesAlerts_ForTenSeconds()
    {
        var clock = new FakeTimeProvider();
        using var policy = new DeviceAlertPolicy(clock, suspendSuppressionDuration: TimeSpan.FromSeconds(10.0));
        policy.NotifyEnumerationCompleted();

        var alerts = new List<DeviceEvent>();
        policy.AlertTriggered += (_, ev) => alerts.Add(ev);

        // System resumes from suspension
        policy.NotifyResumedFromSuspend();
        Assert.True(policy.IsInSuspensionSuppression);

        // Device reconnections arriving during wakeup period
        clock.Advance(TimeSpan.FromSeconds(2));
        policy.ProcessDeviceEvent(new DeviceEvent(DeviceEventType.Connected, "USB\\VID_WAKE", "Mouse", DeviceCategory.Mouse));
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Empty(alerts);

        // Advance past 10s suppression window
        clock.Advance(TimeSpan.FromSeconds(7)); // t = 11s from resume
        Assert.False(policy.IsInSuspensionSuppression);

        // New event after suppression window triggers alert
        policy.ProcessDeviceEvent(new DeviceEvent(DeviceEventType.Connected, "USB\\VID_NEW", "Flash Drive", DeviceCategory.Storage));
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Single(alerts);
        Assert.Equal("Flash Drive", alerts[0].DeviceName);
    }

    [Fact]
    public void Cooldown_SuppressesDuplicateEvents_WithinWindow()
    {
        var clock = new FakeTimeProvider();
        using var policy = new DeviceAlertPolicy(clock, cooldownDuration: TimeSpan.FromSeconds(3.0), coalesceDuration: TimeSpan.FromMilliseconds(500));
        policy.NotifyEnumerationCompleted();

        var alerts = new List<DeviceEvent>();
        policy.AlertTriggered += (_, ev) => alerts.Add(ev);

        var connectDev = new DeviceEvent(DeviceEventType.Connected, "USB\\VID_TEST", "Headset", DeviceCategory.Audio);
        policy.ProcessDeviceEvent(connectDev);
        clock.Advance(TimeSpan.FromSeconds(1)); // alert emitted at t = 0.5s
        Assert.Single(alerts);

        // Disconnect at t=1.0s (alert emitted at t=1.5s)
        var disconnectDev = new DeviceEvent(DeviceEventType.Disconnected, "USB\\VID_TEST", "Headset", DeviceCategory.Audio);
        policy.ProcessDeviceEvent(disconnectDev);
        clock.Advance(TimeSpan.FromSeconds(1)); // t = 2.0s
        Assert.Equal(2, alerts.Count);

        // Rapid disconnect again at t=2.0s (less than 3s since disconnect at t=1.5s)
        policy.ProcessDeviceEvent(disconnectDev);
        clock.Advance(TimeSpan.FromSeconds(1)); // t = 3.0s

        // Suppressed by cooldown
        Assert.Equal(2, alerts.Count);
    }
}

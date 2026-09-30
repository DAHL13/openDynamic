using OpenDynamic.Core.Privacy;
using OpenDynamic.Core.Settings;
using OpenDynamic.Core.State;
using OpenDynamic.Core.Widgets;
using Xunit;

namespace OpenDynamic.Tests.Widgets;

public sealed class PriorityResolverPhase15Tests
{
    private sealed class MockSource : IActivitySource
    {
        public string Id { get; set; } = "";
        public int Priority { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsTransient { get; set; }
        public DateTimeOffset? LastActivatedUtc { get; set; }
        public TimeSpan? TransientDuration { get; set; }
        public IslandActivity? CurrentActivity => null;
#pragma warning disable CS0067
        public event EventHandler? Changed;
#pragma warning restore CS0067
    }

    [Fact]
    public void Battery_Priority90_WinsOver_Privacy_Priority85()
    {
        var resolver = new PriorityResolver();
        var now = DateTimeOffset.UtcNow;

        var battery = new MockSource
        {
            Id = "battery",
            Priority = ActivityPriority.Battery, // 90
            IsActive = true,
            LastActivatedUtc = now.AddSeconds(-5)
        };

        var privacy = new MockSource
        {
            Id = "privacy",
            Priority = ActivityPriority.Privacy, // 85
            IsActive = true,
            LastActivatedUtc = now.AddSeconds(-2)
        };

        var result = resolver.Resolve(new[] { privacy, battery }, now);

        Assert.Equal("battery", result.Primary?.Id);
        Assert.Equal("privacy", result.Secondary?.Id);
        Assert.Equal(IslandState.Split, result.SuggestedState);
    }

    [Fact]
    public void Privacy_Priority85_WinsOver_Volume_Priority80()
    {
        var resolver = new PriorityResolver();
        var now = DateTimeOffset.UtcNow;

        var privacy = new MockSource
        {
            Id = "privacy",
            Priority = ActivityPriority.Privacy, // 85
            IsActive = true,
            LastActivatedUtc = now.AddSeconds(-3)
        };

        var volume = new MockSource
        {
            Id = "volume",
            Priority = ActivityPriority.Volume, // 80
            IsActive = true,
            LastActivatedUtc = now.AddSeconds(-1)
        };

        var result = resolver.Resolve(new[] { volume, privacy }, now);

        Assert.Equal("privacy", result.Primary?.Id);
        Assert.Equal("volume", result.Secondary?.Id);
        Assert.Equal(IslandState.Split, result.SuggestedState);
    }

    [Fact]
    public void Privacy_Priority85_WinsOver_Network_Device_Clipboard()
    {
        var resolver = new PriorityResolver();
        var now = DateTimeOffset.UtcNow;

        var privacy = new MockSource
        {
            Id = "privacy",
            Priority = ActivityPriority.Privacy, // 85
            IsActive = true,
            LastActivatedUtc = now.AddSeconds(-1)
        };

        var network = new MockSource
        {
            Id = "network",
            Priority = ActivityPriority.Network, // 65
            IsActive = true,
            LastActivatedUtc = now.AddSeconds(-2)
        };

        var result = resolver.Resolve(new[] { network, privacy }, now);

        Assert.Equal("privacy", result.Primary?.Id);
        Assert.Equal("network", result.Secondary?.Id);
        Assert.Equal(IslandState.Split, result.SuggestedState);
    }

    [Fact]
    public void Privacy_TransientExpiration_ResolvesCorrectExpirationTime()
    {
        var resolver = new PriorityResolver();
        var now = DateTimeOffset.UtcNow;
        var duration = TimeSpan.FromSeconds(3);

        var privacy = new MockSource
        {
            Id = "privacy",
            Priority = ActivityPriority.Privacy,
            IsActive = true,
            IsTransient = true,
            LastActivatedUtc = now,
            TransientDuration = duration
        };

        var result = resolver.Resolve(new[] { privacy }, now);

        Assert.Equal("privacy", result.Primary?.Id);
        Assert.Null(result.Secondary);
        Assert.Equal(IslandState.Compact, result.SuggestedState);
        Assert.NotNull(result.NextExpirationUtc);
        Assert.Equal(now.Add(duration), result.NextExpirationUtc!.Value);
    }

    [Fact]
    public void WhenNoWidgetsActive_ResolverReturnsNullPrimaryAndIdleState()
    {
        var resolver = new PriorityResolver();
        var now = DateTimeOffset.UtcNow;

        var result = resolver.Resolve(Array.Empty<IActivitySource>(), now);

        Assert.Null(result.Primary);
        Assert.Null(result.Secondary);
        Assert.Equal(IslandState.Hidden, result.SuggestedState);
    }

    [Fact]
    public void PrivacyAccessState_Properties_ReflectSensorActivityAccurately()
    {
        var stateEmpty = PrivacyAccessState.Empty;
        Assert.False(stateEmpty.IsMicrophoneActive);
        Assert.False(stateEmpty.IsCameraActive);
        Assert.False(stateEmpty.HasActiveResource);

        var stateMicOnly = new PrivacyAccessState(isMicrophoneActive: true, isCameraActive: false, activeMicrophoneApps: new[] { "chrome.exe" });
        Assert.True(stateMicOnly.IsMicrophoneActive);
        Assert.False(stateMicOnly.IsCameraActive);
        Assert.True(stateMicOnly.HasActiveResource);
        Assert.Contains("chrome.exe", stateMicOnly.ActiveMicrophoneApps);

        var stateCamOnly = new PrivacyAccessState(isMicrophoneActive: false, isCameraActive: true, activeCameraApps: new[] { "obs64.exe" });
        Assert.False(stateCamOnly.IsMicrophoneActive);
        Assert.True(stateCamOnly.IsCameraActive);
        Assert.True(stateCamOnly.HasActiveResource);
        Assert.Contains("obs64.exe", stateCamOnly.ActiveCameraApps);

        var stateBoth = new PrivacyAccessState(isMicrophoneActive: true, isCameraActive: true);
        Assert.True(stateBoth.IsMicrophoneActive);
        Assert.True(stateBoth.IsCameraActive);
        Assert.True(stateBoth.HasActiveResource);
    }

    [Fact]
    public void PrivacySettings_ControlSensorIndicators_Deterministically()
    {
        var settings = new AppSettings
        {
            EnableMicrophoneIndicator = true,
            EnableCameraIndicator = true
        };

        var micActiveState = new PrivacyAccessState(isMicrophoneActive: true, isCameraActive: false);

        bool showMic = settings.EnableMicrophoneIndicator && micActiveState.IsMicrophoneActive;
        bool showCam = settings.EnableCameraIndicator && micActiveState.IsCameraActive;
        Assert.True(showMic);
        Assert.False(showCam);

        // When user disables microphone indicator
        settings.EnableMicrophoneIndicator = false;
        showMic = settings.EnableMicrophoneIndicator && micActiveState.IsMicrophoneActive;
        Assert.False(showMic);
    }
}

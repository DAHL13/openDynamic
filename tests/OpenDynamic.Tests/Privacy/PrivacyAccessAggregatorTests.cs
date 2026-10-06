using OpenDynamic.Core.Privacy;
using OpenDynamic.Tests.Timer;
using Xunit;

namespace OpenDynamic.Tests.Privacy;

public sealed class PrivacyAccessAggregatorTests
{
    private readonly FakeTimeProvider _timeProvider;

    public PrivacyAccessAggregatorTests()
    {
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void IsInUse_Evaluation_CorrectlyInterpretsFileTimeSemantics()
    {
        // Start > 0 and Stop == 0 => In Use
        var active1 = new PrivacyAccessEntry
        {
            Resource = PrivacyResourceType.Microphone,
            AppId = "app1",
            DisplayName = "App 1",
            LastUsedTimeStart = 134352748819637082L,
            LastUsedTimeStop = 0L
        };
        Assert.True(active1.IsInUse);

        // Start > Stop => In Use
        var active2 = new PrivacyAccessEntry
        {
            Resource = PrivacyResourceType.Camera,
            AppId = "app2",
            DisplayName = "App 2",
            LastUsedTimeStart = 1000L,
            LastUsedTimeStop = 900L
        };
        Assert.True(active2.IsInUse);

        // Start <= Stop => Inactive
        var inactive1 = new PrivacyAccessEntry
        {
            Resource = PrivacyResourceType.Microphone,
            AppId = "app3",
            DisplayName = "App 3",
            LastUsedTimeStart = 900L,
            LastUsedTimeStop = 1000L
        };
        Assert.False(inactive1.IsInUse);

        // Start == Stop => Inactive
        var inactive2 = new PrivacyAccessEntry
        {
            Resource = PrivacyResourceType.Microphone,
            AppId = "app4",
            DisplayName = "App 4",
            LastUsedTimeStart = 1000L,
            LastUsedTimeStop = 1000L
        };
        Assert.False(inactive2.IsInUse);

        // Start == 0 => Inactive
        var inactive3 = new PrivacyAccessEntry
        {
            Resource = PrivacyResourceType.Camera,
            AppId = "app5",
            DisplayName = "App 5",
            LastUsedTimeStart = 0L,
            LastUsedTimeStop = 0L
        };
        Assert.False(inactive3.IsInUse);
    }

    [Fact]
    public void ProcessEntries_SingleMicrophoneApp_EmitsStartedAlertAndSetsActiveState()
    {
        var aggregator = new PrivacyAccessAggregator(timeProvider: _timeProvider);
        var alerts = new List<PrivacyAccessChange>();
        PrivacyAccessState? stateChange = null;

        aggregator.AccessAlertTriggered += (s, e) => alerts.Add(e);
        aggregator.StateChanged += (s, e) => stateChange = e;

        var entries = new[]
        {
            new PrivacyAccessEntry
            {
                Resource = PrivacyResourceType.Microphone,
                AppId = "obs64.exe",
                DisplayName = "OBS Studio",
                LastUsedTimeStart = 2000L,
                LastUsedTimeStop = 0L
            }
        };

        var state = aggregator.ProcessEntries(entries);

        Assert.True(state.IsMicrophoneActive);
        Assert.False(state.IsCameraActive);
        Assert.True(state.HasActiveResource);
        Assert.Single(state.ActiveMicrophoneApps);
        Assert.Equal("OBS Studio", state.ActiveMicrophoneApps[0]);
        Assert.Empty(state.ActiveCameraApps);

        Assert.NotNull(stateChange);
        Assert.True(stateChange!.IsMicrophoneActive);

        Assert.Single(alerts);
        Assert.Equal(PrivacyResourceType.Microphone, alerts[0].Resource);
        Assert.Equal(PrivacyAccessEventKind.Started, alerts[0].EventKind);
        Assert.Equal("OBS Studio", alerts[0].AppName);
        Assert.Equal(_timeProvider.GetUtcNow(), alerts[0].TimestampUtc);
    }

    [Fact]
    public void ProcessEntries_SingleCameraApp_EmitsStartedAlertAndSetsActiveState()
    {
        var aggregator = new PrivacyAccessAggregator(timeProvider: _timeProvider);
        var alerts = new List<PrivacyAccessChange>();
        aggregator.AccessAlertTriggered += (s, e) => alerts.Add(e);

        var entries = new[]
        {
            new PrivacyAccessEntry
            {
                Resource = PrivacyResourceType.Camera,
                AppId = "Microsoft.WindowsCamera_8wekyb3d8bbwe",
                DisplayName = "Cámara de Windows",
                LastUsedTimeStart = 5000L,
                LastUsedTimeStop = 1000L
            }
        };

        var state = aggregator.ProcessEntries(entries);

        Assert.False(state.IsMicrophoneActive);
        Assert.True(state.IsCameraActive);
        Assert.True(state.HasActiveResource);
        Assert.Single(state.ActiveCameraApps);
        Assert.Equal("Cámara de Windows", state.ActiveCameraApps[0]);

        Assert.Single(alerts);
        Assert.Equal(PrivacyResourceType.Camera, alerts[0].Resource);
        Assert.Equal(PrivacyAccessEventKind.Started, alerts[0].EventKind);
        Assert.Equal("Cámara de Windows", alerts[0].AppName);
    }

    [Fact]
    public void ProcessEntries_MultipleConcurrentSensors_EmitsSeparateAlerts()
    {
        var aggregator = new PrivacyAccessAggregator(timeProvider: _timeProvider);
        var alerts = new List<PrivacyAccessChange>();
        aggregator.AccessAlertTriggered += (s, e) => alerts.Add(e);

        var entries = new[]
        {
            new PrivacyAccessEntry
            {
                Resource = PrivacyResourceType.Microphone,
                AppId = "discord.exe",
                DisplayName = "Discord",
                LastUsedTimeStart = 3000L,
                LastUsedTimeStop = 0L
            },
            new PrivacyAccessEntry
            {
                Resource = PrivacyResourceType.Camera,
                AppId = "teams.exe",
                DisplayName = "Microsoft Teams",
                LastUsedTimeStart = 4000L,
                LastUsedTimeStop = 0L
            }
        };

        var state = aggregator.ProcessEntries(entries);

        Assert.True(state.IsMicrophoneActive);
        Assert.True(state.IsCameraActive);
        Assert.Contains("Discord", state.ActiveMicrophoneApps);
        Assert.Contains("Microsoft Teams", state.ActiveCameraApps);

        Assert.Equal(2, alerts.Count);
        Assert.Contains(alerts, a => a.Resource == PrivacyResourceType.Microphone && a.AppName == "Discord" && a.EventKind == PrivacyAccessEventKind.Started);
        Assert.Contains(alerts, a => a.Resource == PrivacyResourceType.Camera && a.AppName == "Microsoft Teams" && a.EventKind == PrivacyAccessEventKind.Started);
    }

    [Fact]
    public void ProcessEntries_AppReleasesSensor_EmitsStoppedAlertAndClearsState()
    {
        var aggregator = new PrivacyAccessAggregator(timeProvider: _timeProvider);
        var alerts = new List<PrivacyAccessChange>();
        aggregator.AccessAlertTriggered += (s, e) => alerts.Add(e);

        // Step 1: Active
        var entriesActive = new[]
        {
            new PrivacyAccessEntry
            {
                Resource = PrivacyResourceType.Microphone,
                AppId = "obs64.exe",
                DisplayName = "OBS Studio",
                LastUsedTimeStart = 2000L,
                LastUsedTimeStop = 0L
            }
        };
        aggregator.ProcessEntries(entriesActive);
        alerts.Clear();

        // Step 2: Released (Stop time updated > Start time)
        _timeProvider.Advance(TimeSpan.FromSeconds(10));
        var entriesStopped = new[]
        {
            new PrivacyAccessEntry
            {
                Resource = PrivacyResourceType.Microphone,
                AppId = "obs64.exe",
                DisplayName = "OBS Studio",
                LastUsedTimeStart = 2000L,
                LastUsedTimeStop = 3000L
            }
        };
        var finalState = aggregator.ProcessEntries(entriesStopped);

        Assert.False(finalState.IsMicrophoneActive);
        Assert.Empty(finalState.ActiveMicrophoneApps);
        Assert.False(finalState.HasActiveResource);

        Assert.Single(alerts);
        Assert.Equal(PrivacyResourceType.Microphone, alerts[0].Resource);
        Assert.Equal(PrivacyAccessEventKind.Stopped, alerts[0].EventKind);
        Assert.Equal("OBS Studio", alerts[0].AppName);
        Assert.Equal(_timeProvider.GetUtcNow(), alerts[0].TimestampUtc);
    }

    [Fact]
    public void ProcessEntries_IgnoredApps_ExcludedFromDetection()
    {
        var ignored = new[] { "SystemSoundService.exe", "cortana" };
        var aggregator = new PrivacyAccessAggregator(ignoredApps: ignored, timeProvider: _timeProvider);
        var alerts = new List<PrivacyAccessChange>();
        aggregator.AccessAlertTriggered += (s, e) => alerts.Add(e);

        var entries = new[]
        {
            // Ignored with .exe matching
            new PrivacyAccessEntry
            {
                Resource = PrivacyResourceType.Microphone,
                AppId = "SystemSoundService.exe",
                DisplayName = "SystemSoundService",
                LastUsedTimeStart = 1000L,
                LastUsedTimeStop = 0L
            },
            // Ignored direct
            new PrivacyAccessEntry
            {
                Resource = PrivacyResourceType.Microphone,
                AppId = "cortana",
                DisplayName = "cortana",
                LastUsedTimeStart = 1000L,
                LastUsedTimeStop = 0L
            },
            // Allowed
            new PrivacyAccessEntry
            {
                Resource = PrivacyResourceType.Microphone,
                AppId = "audacity.exe",
                DisplayName = "Audacity",
                LastUsedTimeStart = 1000L,
                LastUsedTimeStop = 0L
            }
        };

        var state = aggregator.ProcessEntries(entries);

        Assert.True(state.IsMicrophoneActive);
        Assert.Single(state.ActiveMicrophoneApps);
        Assert.Equal("Audacity", state.ActiveMicrophoneApps[0]);
        Assert.Single(alerts);
        Assert.Equal("Audacity", alerts[0].AppName);
    }

    [Fact]
    public void ProcessEntries_SelfApp_IsAlwaysExcluded()
    {
        var aggregator = new PrivacyAccessAggregator(timeProvider: _timeProvider);
        var entries = new[]
        {
            new PrivacyAccessEntry
            {
                Resource = PrivacyResourceType.Microphone,
                AppId = @"C:\Program Files\openDynamic\openDynamic.exe",
                DisplayName = "openDynamic",
                LastUsedTimeStart = 1000L,
                LastUsedTimeStop = 0L
            }
        };

        var state = aggregator.ProcessEntries(entries);

        Assert.False(state.IsMicrophoneActive);
        Assert.Empty(state.ActiveMicrophoneApps);
    }

    [Fact]
    public void UpdateIgnoredApps_DynamicUpdate_AffectsSubsequentProcessing()
    {
        var aggregator = new PrivacyAccessAggregator(timeProvider: _timeProvider);

        var entries = new[]
        {
            new PrivacyAccessEntry
            {
                Resource = PrivacyResourceType.Microphone,
                AppId = "discord.exe",
                DisplayName = "Discord",
                LastUsedTimeStart = 1000L,
                LastUsedTimeStop = 0L
            }
        };

        var s1 = aggregator.ProcessEntries(entries);
        Assert.True(s1.IsMicrophoneActive);

        // Add Discord to ignored list
        aggregator.UpdateIgnoredApps(new[] { "Discord" });

        var s2 = aggregator.ProcessEntries(entries);
        Assert.False(s2.IsMicrophoneActive);
        Assert.Empty(s2.ActiveMicrophoneApps);
    }

    [Fact]
    public void Reset_ClearsStateAndRaisesEvent()
    {
        var aggregator = new PrivacyAccessAggregator(timeProvider: _timeProvider);
        var entries = new[]
        {
            new PrivacyAccessEntry
            {
                Resource = PrivacyResourceType.Camera,
                AppId = "webcam.exe",
                DisplayName = "WebcamApp",
                LastUsedTimeStart = 1000L,
                LastUsedTimeStop = 0L
            }
        };
        aggregator.ProcessEntries(entries);
        Assert.True(aggregator.CurrentState.IsCameraActive);

        bool resetRaised = false;
        aggregator.StateChanged += (s, e) =>
        {
            if (!e.HasActiveResource) resetRaised = true;
        };

        aggregator.Reset();

        Assert.False(aggregator.CurrentState.IsCameraActive);
        Assert.True(resetRaised);
    }

    [Fact]
    public void ProcessEntries_WithSuppressAlerts_UpdatesStateWithoutEmittingTransientAlerts()
    {
        var aggregator = new PrivacyAccessAggregator(timeProvider: _timeProvider);
        var alerts = new List<PrivacyAccessChange>();
        aggregator.AccessAlertTriggered += (_, e) => alerts.Add(e);

        var entries = new[]
        {
            new PrivacyAccessEntry
            {
                Resource = PrivacyResourceType.Microphone,
                AppId = "obs64.exe",
                DisplayName = "OBS Studio",
                LastUsedTimeStart = 2000L,
                LastUsedTimeStop = 0L
            }
        };

        var state = aggregator.ProcessEntries(entries, suppressAlerts: true);

        Assert.True(state.IsMicrophoneActive);
        Assert.Empty(alerts);
    }

    [Fact]
    public void UpdateIgnoredApps_ImmediatelyUpdatesCurrentStateAndRaisesStateChanged()
    {
        var aggregator = new PrivacyAccessAggregator(timeProvider: _timeProvider);
        var entries = new[]
        {
            new PrivacyAccessEntry
            {
                Resource = PrivacyResourceType.Microphone,
                AppId = "discord.exe",
                DisplayName = "Discord",
                LastUsedTimeStart = 1000L,
                LastUsedTimeStop = 0L
            }
        };

        aggregator.ProcessEntries(entries);
        Assert.True(aggregator.CurrentState.IsMicrophoneActive);

        PrivacyAccessState? updatedState = null;
        aggregator.StateChanged += (_, state) => updatedState = state;

        aggregator.UpdateIgnoredApps(new[] { "Discord" });

        Assert.NotNull(updatedState);
        Assert.False(updatedState!.IsMicrophoneActive);
        Assert.False(aggregator.CurrentState.IsMicrophoneActive);
    }
}

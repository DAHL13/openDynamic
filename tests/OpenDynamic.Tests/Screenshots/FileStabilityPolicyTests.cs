using OpenDynamic.Core.Screenshots;
using OpenDynamic.Tests.Timer;

namespace OpenDynamic.Tests.Screenshots;

public sealed class FileStabilityPolicyTests
{
    [Fact]
    public void Tracker_GrowingFile_BecomesStableOnlyAfter300msOfUnchangedSize()
    {
        var fakeTime = new FakeTimeProvider();
        var policy = new FileStabilityPolicy(fakeTime);
        var tracker = policy.CreateTracker();

        // t = 0 ms: initial chunk written (1024 bytes)
        Assert.Equal(
            FileStabilityStatus.Pending,
            tracker.Evaluate(new FileProbeResult(Exists: true, SizeBytes: 1024, CanOpenSharedRead: true)));

        // t = 150 ms: still growing (4096 bytes) -> resets 300ms stability window
        fakeTime.Advance(TimeSpan.FromMilliseconds(150));
        Assert.Equal(
            FileStabilityStatus.Pending,
            tracker.Evaluate(new FileProbeResult(Exists: true, SizeBytes: 4096, CanOpenSharedRead: true)));

        // t = 350 ms (200 ms since last size change < 300 ms) -> still Pending
        fakeTime.Advance(TimeSpan.FromMilliseconds(200));
        Assert.Equal(
            FileStabilityStatus.Pending,
            tracker.Evaluate(new FileProbeResult(Exists: true, SizeBytes: 4096, CanOpenSharedRead: true)));

        // t = 450 ms (300 ms since last size change) -> Stable!
        fakeTime.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(
            FileStabilityStatus.Stable,
            tracker.Evaluate(new FileProbeResult(Exists: true, SizeBytes: 4096, CanOpenSharedRead: true)));
    }

    [Fact]
    public void Tracker_LockedFile_WaitsUntilSharedReadIsAvailableAndStableFor300ms()
    {
        var fakeTime = new FakeTimeProvider();
        var policy = new FileStabilityPolicy(fakeTime);
        var tracker = policy.CreateTracker();

        // t = 0..500 ms: file exists with size 8192 but is exclusively locked
        Assert.Equal(
            FileStabilityStatus.Pending,
            tracker.Evaluate(new FileProbeResult(Exists: true, SizeBytes: 8192, CanOpenSharedRead: false)));

        fakeTime.Advance(TimeSpan.FromMilliseconds(500));
        Assert.Equal(
            FileStabilityStatus.Pending,
            tracker.Evaluate(new FileProbeResult(Exists: true, SizeBytes: 8192, CanOpenSharedRead: false)));

        // t = 600 ms: lock released, shared read succeeds -> starts 300 ms window
        fakeTime.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(
            FileStabilityStatus.Pending,
            tracker.Evaluate(new FileProbeResult(Exists: true, SizeBytes: 8192, CanOpenSharedRead: true)));

        // t = 900 ms (300 ms of shared read stability) -> Stable!
        fakeTime.Advance(TimeSpan.FromMilliseconds(300));
        Assert.Equal(
            FileStabilityStatus.Stable,
            tracker.Evaluate(new FileProbeResult(Exists: true, SizeBytes: 8192, CanOpenSharedRead: true)));
    }

    [Fact]
    public void Tracker_PermanentlyLockedFile_TimesOutAndDiscardsAfter3Seconds()
    {
        var fakeTime = new FakeTimeProvider();
        var policy = new FileStabilityPolicy(fakeTime);
        var tracker = policy.CreateTracker();

        Assert.Equal(
            FileStabilityStatus.Pending,
            tracker.Evaluate(new FileProbeResult(Exists: true, SizeBytes: 4096, CanOpenSharedRead: false)));

        fakeTime.Advance(TimeSpan.FromMilliseconds(2900));
        Assert.Equal(
            FileStabilityStatus.Pending,
            tracker.Evaluate(new FileProbeResult(Exists: true, SizeBytes: 4096, CanOpenSharedRead: false)));

        // At 3000 ms (3 seconds max wait), it times out and discards the file
        fakeTime.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(
            FileStabilityStatus.TimedOut,
            tracker.Evaluate(new FileProbeResult(Exists: true, SizeBytes: 4096, CanOpenSharedRead: false)));
    }

    [Fact]
    public void Tracker_DisappearingFile_FailsImmediately()
    {
        var fakeTime = new FakeTimeProvider();
        var policy = new FileStabilityPolicy(fakeTime);
        var tracker = policy.CreateTracker();

        Assert.Equal(
            FileStabilityStatus.Pending,
            tracker.Evaluate(new FileProbeResult(Exists: true, SizeBytes: 2048, CanOpenSharedRead: true)));

        fakeTime.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(
            FileStabilityStatus.Failed,
            tracker.Evaluate(new FileProbeResult(Exists: false, SizeBytes: 0, CanOpenSharedRead: false)));
    }

    [Fact]
    public async Task WaitForStabilityAsync_WithRealDiskProbe_LeavesFileCompletelyUnlocked()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"openDynamic_Stability_{Guid.NewGuid():N}.png");
        try
        {
            await File.WriteAllBytesAsync(tempFile, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

            var policy = new FileStabilityPolicy(
                TimeProvider.System,
                stableDuration: TimeSpan.FromMilliseconds(60),
                maxWaitDuration: TimeSpan.FromSeconds(1),
                pollInterval: TimeSpan.FromMilliseconds(20));

            var (isStable, sizeBytes) = await policy.WaitForStabilityAsync(tempFile);

            Assert.True(isStable);
            Assert.Equal(8L, sizeBytes);

            // Immediately deleting the file must succeed without IOException (zero file lock)
            File.Delete(tempFile);
            Assert.False(File.Exists(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}

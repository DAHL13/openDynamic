using OpenDynamic.Core.Screenshots;
using OpenDynamic.Tests.Timer;

namespace OpenDynamic.Tests.Screenshots;

public sealed class ScreenshotHistoryTests
{
    [Fact]
    public void Add_EnforcesDefaultCapacityOfFiveAndOrdersNewestFirst()
    {
        var fakeTime = new FakeTimeProvider();
        var existingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var history = new ScreenshotHistory(
            timeProvider: fakeTime,
            capacity: 5,
            fileExistsProbe: path => existingFiles.Contains(path));

        for (int i = 1; i <= 7; i++)
        {
            string path = $@"C:\Pictures\Screenshots\shot_{i}.png";
            existingFiles.Add(path);
            history.Add(path, fileSizeBytes: i * 1024, pixelWidth: 1920, pixelHeight: 1080);
            fakeTime.Advance(TimeSpan.FromSeconds(1));
        }

        var entries = history.GetRecentEntries();
        Assert.Equal(5, entries.Count);
        Assert.Equal("shot_7.png", entries[0].FileName);
        Assert.Equal("shot_6.png", entries[1].FileName);
        Assert.Equal("shot_5.png", entries[2].FileName);
        Assert.Equal("shot_4.png", entries[3].FileName);
        Assert.Equal("shot_3.png", entries[4].FileName);
    }

    [Fact]
    public void GetRecentEntries_MarksDeletedFilesAsUnavailableAndAllowsRemoval()
    {
        var fakeTime = new FakeTimeProvider();
        string file1 = @"C:\Pictures\Screenshots\shot_1.png";
        string file2 = @"C:\Pictures\Screenshots\shot_2.png";
        var existingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { file1, file2 };

        var history = new ScreenshotHistory(
            timeProvider: fakeTime,
            capacity: 5,
            fileExistsProbe: path => existingFiles.Contains(path));

        history.Add(file1, 10_000, 800, 600);
        history.Add(file2, 20_000, 1920, 1080);

        // Simulate file1 being deleted from disk externally
        existingFiles.Remove(file1);

        var entries = history.GetRecentEntries();
        Assert.Equal(2, entries.Count);
        Assert.True(entries[0].IsAvailable);
        Assert.False(entries[1].IsAvailable);
        Assert.Equal("No disponible", entries[1].StatusLabel);

        // User removes unavailable entry from the list
        Assert.True(history.Remove(file1));
        Assert.Single(history.GetRecentEntries());
    }

    [Fact]
    public void GetRecentEntries_PurgesExpiredItemsPastRetentionDuration()
    {
        var fakeTime = new FakeTimeProvider();
        var history = new ScreenshotHistory(
            timeProvider: fakeTime,
            capacity: 5,
            retentionDuration: TimeSpan.FromMinutes(15),
            fileExistsProbe: _ => true);

        history.Add(@"C:\Pictures\Screenshots\early.png", 12_000);
        fakeTime.Advance(TimeSpan.FromMinutes(10));
        history.Add(@"C:\Pictures\Screenshots\recent.png", 15_000);

        // Advance 6 more minutes -> early.png is 16 min old (> 15 min), recent.png is 6 min old
        fakeTime.Advance(TimeSpan.FromMinutes(6));

        var entries = history.GetRecentEntries();
        Assert.Single(entries);
        Assert.Equal("recent.png", entries[0].FileName);
    }

    [Fact]
    public void ScreenshotEntry_FormatsMetadataCorrectly()
    {
        var entry = new ScreenshotEntry(
            @"C:\Pictures\Screenshots\test.png",
            fileSizeBytes: 256_000,
            capturedAtUtc: DateTimeOffset.UtcNow,
            pixelWidth: 1920,
            pixelHeight: 1080,
            isAvailable: true);

        Assert.Equal("250 KB", entry.FormattedSize);
        Assert.Equal("1920 × 1080", entry.FormattedDimensions);
        Assert.Equal("1920 × 1080 • 250 KB", entry.MetadataSummary);
    }
}

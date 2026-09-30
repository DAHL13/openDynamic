using OpenDynamic.Core.Timer;
using Xunit;

namespace OpenDynamic.Tests.Timer;

public sealed class TimerPersistenceServiceTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly string _testFilePath;

    public TimerPersistenceServiceTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "OpenDynamic_TimerTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
        _testFilePath = Path.Combine(_testDirectory, "timers.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignore test cleanup exceptions
        }
    }

    [Fact]
    public void Restore_WhenNoFileExists_ReturnsEmptyLists()
    {
        var service = new TimerPersistenceService(_testFilePath);
        var result = service.Restore();

        Assert.Empty(result.RestoredTimers);
        Assert.Empty(result.ExpiredWhileClosed);
    }

    [Fact]
    public void Save_WritesOnlyRunningAndPausedTimers_AndRestoreIdentifiesValidAndExpired()
    {
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);

        var timerValid = new TimerController(clock, id: "valid-1", label: "Té verde");
        timerValid.Start(TimeSpan.FromMinutes(10)); // Ends at 12:10

        var timerExpiredSoon = new TimerController(clock, id: "exp-1", label: "Huevos");
        timerExpiredSoon.Start(TimeSpan.FromMinutes(3)); // Ends at 12:03

        var timerStopped = new TimerController(clock, id: "stop-1", label: "Inactivo");

        var service = new TimerPersistenceService(_testFilePath);
        service.Save(new[] { timerValid, timerExpiredSoon, timerStopped });

        Assert.True(File.Exists(_testFilePath));

        // Advance clock past timerExpiredSoon (+5 minutes -> 12:05)
        clock.Advance(TimeSpan.FromMinutes(5));

        // Restore at 12:05
        var result = service.Restore(clock);

        // timerValid still has 5 min left
        Assert.Single(result.RestoredTimers);
        Assert.Equal("valid-1", result.RestoredTimers[0].Id);
        Assert.Equal("Té verde", result.RestoredTimers[0].Label);

        // timerExpiredSoon expired while app was closed (expired at 12:03, current is 12:05)
        Assert.Single(result.ExpiredWhileClosed);
        Assert.Equal("exp-1", result.ExpiredWhileClosed[0].Id);
        Assert.Equal("Huevos", result.ExpiredWhileClosed[0].Label);
    }

    [Fact]
    public void Clear_DeletesPersistedFile()
    {
        var service = new TimerPersistenceService(_testFilePath);
        File.WriteAllText(_testFilePath, "[]");
        Assert.True(File.Exists(_testFilePath));

        service.Clear();
        Assert.False(File.Exists(_testFilePath));
    }
}

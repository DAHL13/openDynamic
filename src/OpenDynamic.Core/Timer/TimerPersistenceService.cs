using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenDynamic.Core.Timer;

/// <summary>
/// DTO representing a persisted timer record in <c>%AppData%\openDynamic\timers.json</c>.
/// </summary>
public sealed class PersistedTimerRecord
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public DateTimeOffset? TargetEndTimeUtc { get; set; }
    public double TotalDurationSeconds { get; set; }
    public double RemainingSeconds { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<TimerMode>))]
    public TimerMode Mode { get; set; } = TimerMode.Standard;

    [JsonConverter(typeof(JsonStringEnumConverter<TimerState>))]
    public TimerState State { get; set; } = TimerState.Stopped;
}

/// <summary>
/// Result of inspecting and restoring persisted timers upon application startup.
/// </summary>
public sealed record TimerRestoreResult(
    IReadOnlyList<PersistedTimerRecord> RestoredTimers,
    IReadOnlyList<PersistedTimerRecord> ExpiredWhileClosed);

/// <summary>
/// Interface for persisting active running timers across application restarts.
/// </summary>
public interface ITimerPersistenceService
{
    string FilePath { get; }
    void Save(IEnumerable<TimerController> timers);
    TimerRestoreResult Restore(TimeProvider? timeProvider = null);
    void Clear();
}

/// <summary>
/// Service persisting running and paused timers to <c>%AppData%\openDynamic\timers.json</c>.
/// Restores valid upcoming timers and identifies timers that expired while the application was closed.
/// Adheres strictly to Golden Rule 5 (isolated in Core) and Golden Rule 7 (privacy: only labels/times).
/// </summary>
public sealed class TimerPersistenceService : ITimerPersistenceService
{
    private readonly string _filePath;
    private readonly Action<string, Exception?>? _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly object _syncLock = new();

    public string FilePath => _filePath;

    public TimerPersistenceService(string? customFilePath = null, Action<string, Exception?>? logger = null)
    {
        _logger = logger;
        if (!string.IsNullOrWhiteSpace(customFilePath))
        {
            _filePath = customFilePath;
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _filePath = Path.Combine(appData, "openDynamic", "timers.json");
        }

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
    }

    public void Save(IEnumerable<TimerController> timers)
    {
        lock (_syncLock)
        {
            try
            {
                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var list = timers
                    .Where(t => t.State is TimerState.Running or TimerState.Paused)
                    .Select(t => new PersistedTimerRecord
                    {
                        Id = t.Id,
                        Label = t.Label,
                        TargetEndTimeUtc = t.TargetEndTimeUtc,
                        TotalDurationSeconds = t.TotalDuration.TotalSeconds,
                        RemainingSeconds = t.RemainingTime.TotalSeconds,
                        Mode = t.Mode,
                        State = t.State
                    })
                    .ToList();

                if (list.Count == 0)
                {
                    if (File.Exists(_filePath))
                    {
                        File.Delete(_filePath);
                    }
                    return;
                }

                string json = JsonSerializer.Serialize(list, _jsonOptions);
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"Could not save timers to '{_filePath}'.", ex);
            }
        }
    }

    public TimerRestoreResult Restore(TimeProvider? timeProvider = null)
    {
        lock (_syncLock)
        {
            var clock = timeProvider ?? TimeProvider.System;
            var now = clock.GetUtcNow();

            var restored = new List<PersistedTimerRecord>();
            var expired = new List<PersistedTimerRecord>();

            if (!File.Exists(_filePath))
            {
                return new TimerRestoreResult(restored, expired);
            }

            try
            {
                string json = File.ReadAllText(_filePath);
                var records = JsonSerializer.Deserialize<List<PersistedTimerRecord>>(json, _jsonOptions);

                if (records != null)
                {
                    foreach (var record in records)
                    {
                        if (record.State == TimerState.Running && record.TargetEndTimeUtc.HasValue)
                        {
                            if (record.TargetEndTimeUtc.Value > now)
                            {
                                // Still valid and running
                                restored.Add(record);
                            }
                            else
                            {
                                // Expired while the app was closed!
                                expired.Add(record);
                            }
                        }
                        else if (record.State == TimerState.Paused)
                        {
                            restored.Add(record);
                        }
                    }
                }

                // Clean file or save remaining restored
                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                }
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"Could not restore timers from '{_filePath}'.", ex);
            }

            return new TimerRestoreResult(restored, expired);
        }
    }

    public void Clear()
    {
        lock (_syncLock)
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                }
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"Could not delete timers file '{_filePath}'.", ex);
            }
        }
    }
}

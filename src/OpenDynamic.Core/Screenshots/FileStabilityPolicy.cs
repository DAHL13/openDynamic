namespace OpenDynamic.Core.Screenshots;

/// <summary>
/// Represents the result of a single non-locking file system probe.
/// </summary>
public readonly record struct FileProbeResult(
    bool Exists,
    long SizeBytes,
    bool CanOpenSharedRead);

/// <summary>
/// Outcome of evaluating a file's write completion status.
/// </summary>
public enum FileStabilityStatus
{
    Pending = 0,
    Stable = 1,
    Failed = 2,
    TimedOut = 3
}

/// <summary>
/// Pure policy with <see cref="TimeProvider"/> that determines when a newly created or renamed screenshot file
/// has finished being written to disk.
/// A file is considered complete when its size (&gt; 0) does not change for <see cref="StableDuration"/> (300 ms)
/// and it can be opened in shared read mode, up to a maximum wait of <see cref="MaxWaitDuration"/> (3 s).
/// </summary>
public sealed class FileStabilityPolicy
{
    public static readonly TimeSpan DefaultStableDuration = TimeSpan.FromMilliseconds(300);
    public static readonly TimeSpan DefaultMaxWaitDuration = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(50);

    private readonly TimeProvider _timeProvider;
    private readonly Func<string, FileProbeResult> _fileProbe;

    public TimeSpan StableDuration { get; }
    public TimeSpan MaxWaitDuration { get; }
    public TimeSpan PollInterval { get; }

    public FileStabilityPolicy(
        TimeProvider? timeProvider = null,
        TimeSpan? stableDuration = null,
        TimeSpan? maxWaitDuration = null,
        TimeSpan? pollInterval = null,
        Func<string, FileProbeResult>? fileProbe = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        StableDuration = stableDuration ?? DefaultStableDuration;
        MaxWaitDuration = maxWaitDuration ?? DefaultMaxWaitDuration;
        PollInterval = pollInterval ?? DefaultPollInterval;
        _fileProbe = fileProbe ?? ProbeFileOnDisk;
    }

    /// <summary>
    /// Creates a deterministic step-by-step stability tracker anchored at the current <see cref="TimeProvider"/> timestamp.
    /// </summary>
    public FileStabilityTracker CreateTracker()
    {
        return new FileStabilityTracker(_timeProvider, StableDuration, MaxWaitDuration);
    }

    /// <summary>
    /// Asynchronously waits until the specified file stabilizes on disk without blocking the calling thread.
    /// Returns <c>(true, finalSizeBytes)</c> if the file stabilized within <see cref="MaxWaitDuration"/>,
    /// or <c>(false, 0)</c> if it disappeared, stayed locked, or timed out.
    /// </summary>
    public async Task<(bool IsStable, long FileSizeBytes)> WaitForStabilityAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return (false, 0L);
        }

        var tracker = CreateTracker();

        while (!cancellationToken.IsCancellationRequested)
        {
            var probe = _fileProbe(filePath);
            var status = tracker.Evaluate(probe);

            switch (status)
            {
                case FileStabilityStatus.Stable:
                    return (true, probe.SizeBytes);

                case FileStabilityStatus.Failed:
                case FileStabilityStatus.TimedOut:
                    return (false, 0L);
            }

            try
            {
                await Task.Delay(PollInterval, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return (false, 0L);
            }
        }

        return (false, 0L);
    }

    /// <summary>
    /// Probes a file on disk for existence, size, and shared read availability,
    /// closing the stream immediately so the file is never left locked.
    /// </summary>
    public static FileProbeResult ProbeFileOnDisk(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return new FileProbeResult(Exists: false, SizeBytes: 0L, CanOpenSharedRead: false);
        }

        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists)
            {
                return new FileProbeResult(Exists: false, SizeBytes: 0L, CanOpenSharedRead: false);
            }

            long length = info.Length;
            if (length <= 0)
            {
                return new FileProbeResult(Exists: true, SizeBytes: 0L, CanOpenSharedRead: false);
            }

            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            return new FileProbeResult(Exists: true, SizeBytes: stream.Length, CanOpenSharedRead: true);
        }
        catch (FileNotFoundException)
        {
            return new FileProbeResult(Exists: false, SizeBytes: 0L, CanOpenSharedRead: false);
        }
        catch (DirectoryNotFoundException)
        {
            return new FileProbeResult(Exists: false, SizeBytes: 0L, CanOpenSharedRead: false);
        }
        catch (IOException)
        {
            // File still exists on disk but is locked exclusively by the writing process
            try
            {
                var info = new FileInfo(filePath);
                return new FileProbeResult(Exists: info.Exists, SizeBytes: info.Exists ? info.Length : 0L, CanOpenSharedRead: false);
            }
            catch
            {
                return new FileProbeResult(Exists: true, SizeBytes: 0L, CanOpenSharedRead: false);
            }
        }
        catch (UnauthorizedAccessException)
        {
            return new FileProbeResult(Exists: true, SizeBytes: 0L, CanOpenSharedRead: false);
        }
        catch
        {
            return new FileProbeResult(Exists: false, SizeBytes: 0L, CanOpenSharedRead: false);
        }
    }
}

/// <summary>
/// Deterministic state tracker for evaluating file stability across time steps.
/// </summary>
public sealed class FileStabilityTracker
{
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _stableDuration;
    private readonly TimeSpan _maxWaitDuration;
    private readonly DateTimeOffset _startedAtUtc;

    private long? _lastObservedSize;
    private DateTimeOffset? _stableSinceUtc;
    private FileStabilityStatus _terminalStatus = FileStabilityStatus.Pending;

    public DateTimeOffset StartedAtUtc => _startedAtUtc;
    public long? LastObservedSize => _lastObservedSize;
    public FileStabilityStatus Status => _terminalStatus;

    public FileStabilityTracker(
        TimeProvider timeProvider,
        TimeSpan stableDuration,
        TimeSpan maxWaitDuration)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _stableDuration = stableDuration;
        _maxWaitDuration = maxWaitDuration;
        _startedAtUtc = _timeProvider.GetUtcNow();
    }

    public FileStabilityStatus Evaluate(FileProbeResult probe)
    {
        if (_terminalStatus != FileStabilityStatus.Pending)
        {
            return _terminalStatus;
        }

        var now = _timeProvider.GetUtcNow();

        // If the file disappeared, fail immediately
        if (!probe.Exists)
        {
            _terminalStatus = FileStabilityStatus.Failed;
            return _terminalStatus;
        }

        // Check if readable and size unchanged for StableDuration
        if (probe.SizeBytes > 0 && probe.CanOpenSharedRead)
        {
            if (_lastObservedSize.HasValue &&
                _lastObservedSize.Value == probe.SizeBytes &&
                _stableSinceUtc.HasValue)
            {
                if ((now - _stableSinceUtc.Value) >= _stableDuration)
                {
                    _terminalStatus = FileStabilityStatus.Stable;
                    return _terminalStatus;
                }
            }
            else
            {
                _lastObservedSize = probe.SizeBytes;
                _stableSinceUtc = now;

                if (_stableDuration <= TimeSpan.Zero)
                {
                    _terminalStatus = FileStabilityStatus.Stable;
                    return _terminalStatus;
                }
            }
        }
        else
        {
            // Reset stability window when locked or 0 bytes
            _lastObservedSize = probe.SizeBytes > 0 ? probe.SizeBytes : null;
            _stableSinceUtc = null;
        }

        // Enforce maximum total wait duration (3s)
        if ((now - _startedAtUtc) >= _maxWaitDuration)
        {
            _terminalStatus = FileStabilityStatus.TimedOut;
            return _terminalStatus;
        }

        return FileStabilityStatus.Pending;
    }
}

using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using OpenDynamic.App.Native;
using OpenDynamic.Core.Screenshots;
using OpenDynamic.Core.Settings;
using Serilog;

namespace OpenDynamic.App.Services;

/// <summary>
/// Event payload emitted when a new screenshot file is stabilized and decoded on disk.
/// </summary>
public sealed record ScreenshotCapturedEventArgs(
    ScreenshotEntry Entry,
    BitmapSource? Thumbnail);

/// <summary>
/// Reactive service that monitors the system Screenshots KnownFolder (and an optional user-configured folder)
/// using <see cref="FileSystemWatcher"/> without any background polling.
/// Strictly adheres to:
/// - Golden Rule 1 &amp; 11: Event-driven FileSystemWatcher, active only when enabled in Settings, disposed immediately when disabled.
/// - Golden Rule 4: Asynchronous write stability verification off the UI thread; non-locking in-memory BitmapImage decoding with Freeze().
/// - Golden Rule 10: Zero PII in logs (never logs paths, filenames, or usernames) and in-memory-only history.
/// </summary>
public sealed class ScreenshotWatcherService : IDisposable
{
    public const int ThumbnailDecodePixelWidth = 320;
    public const long MaxSupportedFileSizeBytes = 100L * 1024L * 1024L; // 100 MB guard

    private readonly AppSettings _settings;
    private readonly ScreenshotHistory _history;
    private readonly FileStabilityPolicy _stabilityPolicy;
    private readonly Dispatcher _dispatcher;
    private readonly object _syncRoot = new();

    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly List<string> _watchedFolders = new();
    private readonly ConcurrentDictionary<string, byte> _inFlightPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _recentlyProcessedUtc = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _cts;
    private DateTimeOffset _watchStartedUtc;
    private bool _isRunning;
    private bool _isDisposed;

    public ScreenshotHistory History => _history;

    public bool IsWatching
    {
        get
        {
            lock (_syncRoot)
            {
                return _isRunning && _watchers.Count > 0;
            }
        }
    }

    public event EventHandler<ScreenshotCapturedEventArgs>? ScreenshotCaptured;
    public event EventHandler? HistoryChanged;

    public ScreenshotWatcherService(
        AppSettings settings,
        ScreenshotHistory history,
        Dispatcher? dispatcher = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _stabilityPolicy = new FileStabilityPolicy();
        _dispatcher = dispatcher ?? (System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher);

        SyncHistorySettings();
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    /// <summary>
    /// Resolves the system Screenshots KnownFolder path (FOLDERID_Screenshots) with fallback to MyPictures\Screenshots.
    /// </summary>
    public static string ResolveDefaultScreenshotsFolder()
    {
        if (NativeMethods.TryGetScreenshotsKnownFolderPath(out string? knownFolder) &&
            !string.IsNullOrWhiteSpace(knownFolder))
        {
            return knownFolder;
        }

        string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (!string.IsNullOrWhiteSpace(pictures))
        {
            return Path.Combine(pictures, "Screenshots");
        }

        return string.Empty;
    }

    /// <summary>
    /// Returns a thread-safe snapshot of currently watched folder paths (or configured folders if stopped).
    /// </summary>
    public IReadOnlyList<string> GetWatchedFoldersSnapshot()
    {
        lock (_syncRoot)
        {
            if (_watchedFolders.Count > 0)
            {
                return new List<string>(_watchedFolders);
            }
        }

        var fallback = new List<string>();
        string defaultFolder = ResolveDefaultScreenshotsFolder();
        if (!string.IsNullOrWhiteSpace(defaultFolder))
        {
            fallback.Add(defaultFolder);
        }

        if (!string.IsNullOrWhiteSpace(_settings.AdditionalScreenshotFolder))
        {
            fallback.Add(_settings.AdditionalScreenshotFolder);
        }

        return fallback;
    }

    /// <summary>
    /// Starts or stops the FileSystemWatcher instances according to current settings.
    /// </summary>
    public void Start()
    {
        if (_isDisposed) return;

        SyncHistorySettings();

        if (!_settings.EnableScreenshotWidget)
        {
            Stop();
            Log.Information("ScreenshotWatcherService inactive (EnableScreenshotWidget=false).");
            return;
        }

        RestartWatchers();
    }

    /// <summary>
    /// Stops and disposes all FileSystemWatcher instances immediately, releasing OS directory handles.
    /// </summary>
    public void Stop()
    {
        CancellationTokenSource? ctsToCancel;
        List<FileSystemWatcher> watchersToDispose;

        lock (_syncRoot)
        {
            _isRunning = false;
            ctsToCancel = _cts;
            _cts = null;
            watchersToDispose = new List<FileSystemWatcher>(_watchers);
            _watchers.Clear();
            _watchedFolders.Clear();
        }

        try
        {
            ctsToCancel?.Cancel();
            ctsToCancel?.Dispose();
        }
        catch
        {
            // Ignore cancellation disposal errors
        }

        foreach (var watcher in watchersToDispose)
        {
            DisposeWatcherSafely(watcher);
        }

        _inFlightPaths.Clear();
    }

    /// <summary>
    /// Synchronizes watcher state and history retention parameters after settings are updated.
    /// </summary>
    public void ApplySettings()
    {
        if (_isDisposed) return;

        SyncHistorySettings();

        if (!_settings.EnableScreenshotWidget)
        {
            Stop();
            return;
        }

        RestartWatchers();
    }

    /// <summary>
    /// Refreshes the availability status of recent history entries against the filesystem.
    /// </summary>
    public IReadOnlyList<ScreenshotEntry> GetRefreshedHistory()
    {
        return _history.GetRecentEntries();
    }

    /// <summary>
    /// Removes a specific screenshot path from the in-memory history.
    /// </summary>
    public bool RemoveFromHistory(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        bool removed = _history.Remove(filePath);
        if (removed)
        {
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    /// <summary>
    /// Clears all in-memory screenshot history entries.
    /// </summary>
    public void ClearHistory()
    {
        _history.Clear();
        _recentlyProcessedUtc.Clear();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        Log.Information("In-memory screenshot history cleared.");
    }

    /// <summary>
    /// Decodes a frozen <see cref="BitmapSource"/> from disk using a transient in-memory buffer
    /// so the file on disk is NEVER locked after this method returns.
    /// </summary>
    public static bool TryLoadFrozenBitmapFromDisk(
        string fullPath,
        int? decodePixelWidth,
        out BitmapSource? bitmap,
        out int originalPixelWidth,
        out int originalPixelHeight)
    {
        bitmap = null;
        originalPixelWidth = 0;
        originalPixelHeight = 0;

        if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
        {
            return false;
        }

        try
        {
            byte[] fileBytes;
            using (var fileStream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            {
                if (fileStream.Length <= 0 || fileStream.Length > MaxSupportedFileSizeBytes)
                {
                    return false;
                }

                fileBytes = new byte[fileStream.Length];
                int totalRead = 0;
                while (totalRead < fileBytes.Length)
                {
                    int read = fileStream.Read(fileBytes, totalRead, fileBytes.Length - totalRead);
                    if (read <= 0) break;
                    totalRead += read;
                }
            }

            using (var metadataStream = new MemoryStream(fileBytes, writable: false))
            {
                var decoder = BitmapDecoder.Create(
                    metadataStream,
                    BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.DelayCreation,
                    BitmapCacheOption.None);

                if (decoder.Frames.Count == 0)
                {
                    return false;
                }

                originalPixelWidth = decoder.Frames[0].PixelWidth;
                originalPixelHeight = decoder.Frames[0].PixelHeight;
            }

            using (var imageStream = new MemoryStream(fileBytes, writable: false))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                if (decodePixelWidth.HasValue && decodePixelWidth.Value > 0 && originalPixelWidth > decodePixelWidth.Value)
                {
                    bmp.DecodePixelWidth = decodePixelWidth.Value;
                }
                bmp.StreamSource = imageStream;
                bmp.EndInit();
                bmp.Freeze();
                bitmap = bmp;
            }

            return originalPixelWidth > 0 && originalPixelHeight > 0;
        }
        catch
        {
            bitmap = null;
            return false;
        }
    }

    private void SyncHistorySettings()
    {
        _history.Capacity = Math.Clamp(_settings.ScreenshotHistoryCapacity, ScreenshotHistory.MinCapacity, ScreenshotHistory.MaxCapacity);
        int retentionMinutes = Math.Max(1, _settings.ScreenshotHistoryRetentionMinutes);
        _history.RetentionDuration = TimeSpan.FromMinutes(retentionMinutes);
    }

    private void RestartWatchers()
    {
        Stop();

        var candidateFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string defaultFolder = ResolveDefaultScreenshotsFolder();
        if (!string.IsNullOrWhiteSpace(defaultFolder) && Directory.Exists(defaultFolder))
        {
            try
            {
                candidateFolders.Add(Path.GetFullPath(defaultFolder));
            }
            catch
            {
                // Ignore invalid path format
            }
        }

        if (!string.IsNullOrWhiteSpace(_settings.AdditionalScreenshotFolder) &&
            Directory.Exists(_settings.AdditionalScreenshotFolder))
        {
            try
            {
                candidateFolders.Add(Path.GetFullPath(_settings.AdditionalScreenshotFolder));
            }
            catch
            {
                // Ignore invalid custom path format
            }
        }

        if (candidateFolders.Count == 0)
        {
            Log.Information("ScreenshotWatcherService found no existing screenshot directories to watch. Remaining idle without polling.");
            return;
        }

        lock (_syncRoot)
        {
            _cts = new CancellationTokenSource();
            _watchStartedUtc = DateTimeOffset.UtcNow;

            foreach (string folder in candidateFolders)
            {
                try
                {
                    var watcher = new FileSystemWatcher(folder)
                    {
                        IncludeSubdirectories = false,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                        Filter = "*.*"
                    };

                    watcher.Created += OnFileSystemEvent;
                    watcher.Renamed += OnFileSystemRenamed;
                    watcher.Error += OnWatcherError;
                    watcher.EnableRaisingEvents = true;

                    _watchers.Add(watcher);
                    _watchedFolders.Add(folder);
                }
                catch (Exception ex)
                {
                    // Privacy: Do not log folder path
                    Log.Warning(ex, "Failed to initialize FileSystemWatcher for a configured screenshot directory.");
                }
            }

            _isRunning = _watchers.Count > 0;
            if (_isRunning)
            {
                Log.Information("ScreenshotWatcherService started watching {Count} directory(ies).", _watchers.Count);
            }
        }
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.FullPath))
        {
            QueueCandidatePath(e.FullPath);
        }
    }

    private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.FullPath))
        {
            QueueCandidatePath(e.FullPath);
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        Log.Warning(e.GetException(), "FileSystemWatcher reported an internal buffer or I/O error.");
    }

    private void QueueCandidatePath(string fullPath)
    {
        if (_isDisposed || !_settings.EnableScreenshotWidget)
        {
            return;
        }

        string fileName = Path.GetFileName(fullPath);
        if (!ScreenshotFileFilter.IsValidCandidateName(fileName))
        {
            return;
        }

        string normalizedPath;
        try
        {
            normalizedPath = Path.GetFullPath(fullPath);
        }
        catch
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (_recentlyProcessedUtc.TryGetValue(normalizedPath, out var lastProcessed) &&
            (now - lastProcessed) < TimeSpan.FromMilliseconds(1500))
        {
            return;
        }

        if (!_inFlightPaths.TryAdd(normalizedPath, 0))
        {
            return;
        }

        CancellationToken token;
        DateTimeOffset watchStart;
        List<string> watchedFoldersSnapshot;

        lock (_syncRoot)
        {
            if (!_isRunning || _cts == null)
            {
                _inFlightPaths.TryRemove(normalizedPath, out _);
                return;
            }

            token = _cts.Token;
            watchStart = _watchStartedUtc;
            watchedFoldersSnapshot = new List<string>(_watchedFolders);
        }

        _ = Task.Run(() => ProcessCandidateFileAsync(normalizedPath, watchedFoldersSnapshot, watchStart, token), token);
    }

    private async Task ProcessCandidateFileAsync(
        string normalizedPath,
        IReadOnlyList<string> watchedFolders,
        DateTimeOffset watchStartUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var (isStable, stabilizedSizeBytes) = await _stabilityPolicy.WaitForStabilityAsync(
                normalizedPath,
                cancellationToken).ConfigureAwait(false);

            if (!isStable || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (!ScreenshotFileFilter.ValidateSafeImageFileOnDisk(normalizedPath, watchedFolders))
            {
                return;
            }

            var fileInfo = new FileInfo(normalizedPath);
            if (!fileInfo.Exists)
            {
                return;
            }

            DateTimeOffset creationUtc = new DateTimeOffset(fileInfo.CreationTimeUtc, TimeSpan.Zero);
            DateTimeOffset lastWriteUtc = new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero);
            DateTimeOffset effectiveTimestampUtc = lastWriteUtc > creationUtc ? lastWriteUtc : creationUtc;

            if (!ScreenshotFileFilter.ShouldAcceptFile(
                normalizedPath,
                stabilizedSizeBytes > 0 ? stabilizedSizeBytes : fileInfo.Length,
                effectiveTimestampUtc,
                watchStartUtc))
            {
                return;
            }

            var nowUtc = DateTimeOffset.UtcNow;
            if (_recentlyProcessedUtc.TryGetValue(normalizedPath, out var recentTime) &&
                (nowUtc - recentTime) < TimeSpan.FromMilliseconds(1500))
            {
                return;
            }

            int? decodeWidth = _settings.ShowScreenshotThumbnail ? ThumbnailDecodePixelWidth : 64;
            if (!TryLoadFrozenBitmapFromDisk(
                normalizedPath,
                decodeWidth,
                out BitmapSource? frozenThumbnail,
                out int pixelWidth,
                out int pixelHeight))
            {
                return;
            }

            _recentlyProcessedUtc[normalizedPath] = nowUtc;

            var entry = _history.Add(
                filePath: normalizedPath,
                fileSizeBytes: fileInfo.Length,
                pixelWidth: pixelWidth,
                pixelHeight: pixelHeight);

            BitmapSource? thumbnailForDisplay = _settings.ShowScreenshotThumbnail ? frozenThumbnail : null;

            // Privacy: Log strictly non-PII metadata (extension, size, dimensions)
            Log.Information(
                "Screenshot captured and stabilized: Ext={Extension}, SizeBytes={SizeBytes}, Dimensions={Width}x{Height}",
                entry.Extension,
                entry.FileSizeBytes,
                entry.PixelWidth,
                entry.PixelHeight);

            await _dispatcher.InvokeAsync(() =>
            {
                if (_isDisposed || !_settings.EnableScreenshotWidget)
                {
                    return;
                }

                HistoryChanged?.Invoke(this, EventArgs.Empty);
                ScreenshotCaptured?.Invoke(this, new ScreenshotCapturedEventArgs(entry, thumbnailForDisplay));
            });
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping or disabling service
        }
        catch (Exception ex)
        {
            // Privacy: Do not include path or filename in log message
            Log.Warning(ex, "Unexpected error while processing stabilized screenshot candidate.");
        }
        finally
        {
            _inFlightPaths.TryRemove(normalizedPath, out _);
        }
    }

    private void DisposeWatcherSafely(FileSystemWatcher watcher)
    {
        try
        {
            watcher.EnableRaisingEvents = false;
            watcher.Created -= OnFileSystemEvent;
            watcher.Renamed -= OnFileSystemRenamed;
            watcher.Error -= OnWatcherError;
            watcher.Dispose();
        }
        catch
        {
            // Ignore disposal exceptions
        }
    }

    public void NotifySuspended()
    {
        Log.Information("Power suspend detected. Clearing in-memory screenshot history.");
        ClearHistory();
    }

    public void NotifyResumed()
    {
        Log.Information("Power resume detected. Screenshot history buffer clean.");
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock)
        {
            Log.Information("Windows session locked (SessionLock). Clearing in-memory screenshot history.");
            ClearHistory();
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        SystemEvents.SessionSwitch -= OnSessionSwitch;
        Stop();
    }
}

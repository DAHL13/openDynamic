using System.Diagnostics;
using System.IO;
using Windows.Media.Control;
using OpenDynamic.App.Native;
using OpenDynamic.Core.Media;
using Serilog;

namespace OpenDynamic.App.Services;

/// <summary>
/// Manages system media playback sessions using Windows.Media.Control GSMTC APIs.
/// Honors Golden Rule 1 (0% CPU at rest, strictly event-driven),
/// Golden Rule 4 (complete WinRT exception isolation),
/// and meticulous event cleanup to prevent memory leaks and ghost callbacks.
/// </summary>
public sealed class MediaService : IMediaService
{
    private readonly object _lock = new();
    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private WinRtMediaSession? _currentSession;
    private bool _isDisposed;
    private bool _isInitializing;

    public IMediaSession? CurrentSession
    {
        get
        {
            lock (_lock)
            {
                return _currentSession;
            }
        }
    }

    /// <summary>
    /// Returns the current typed WinRT session including frozen thumbnail images, if active.
    /// </summary>
    public WinRtMediaSession? CurrentWinRtSession
    {
        get
        {
            lock (_lock)
            {
                return _currentSession;
            }
        }
    }

    public event EventHandler<IMediaSession?>? CurrentSessionChanged;

    public MediaService()
    {
    }

    /// <summary>
    /// Initializes the GSMTC session manager and registers for system session changes.
    /// Safely handles failures without crashing the application.
    /// </summary>
    public async Task<bool> InitializeAsync()
    {
        lock (_lock)
        {
            if (_isDisposed || _isInitializing) return false;
            _isInitializing = true;
        }

        try
        {
            Log.Information("Initializing WinRT GSMTC session manager...");
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();

            if (manager == null)
            {
                Log.Warning("GSMTC session manager returned null. Media integration degraded peacefully.");
                return false;
            }

            lock (_lock)
            {
                _sessionManager = manager;
                _sessionManager.CurrentSessionChanged += OnSessionManagerCurrentSessionChanged;
                _sessionManager.SessionsChanged += OnSessionManagerSessionsChanged;
            }

            Log.Information("WinRT GSMTC session manager hooked successfully.");
            await UpdateCurrentSessionAsync();
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to initialize GlobalSystemMediaTransportControlsSessionManager. Media integration degraded gracefully.");
            return false;
        }
        finally
        {
            lock (_lock)
            {
                _isInitializing = false;
            }
        }
    }

    private async void OnSessionManagerCurrentSessionChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args)
    {
        if (_isDisposed) return;
        await UpdateCurrentSessionAsync();
    }

    private async void OnSessionManagerSessionsChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        SessionsChangedEventArgs args)
    {
        if (_isDisposed) return;
        await UpdateCurrentSessionAsync();
    }

    private async Task UpdateCurrentSessionAsync()
    {
        GlobalSystemMediaTransportControlsSession? winRtSession = null;
        try
        {
            lock (_lock)
            {
                if (_isDisposed || _sessionManager == null) return;
                winRtSession = _sessionManager.GetCurrentSession();
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Exception calling GetCurrentSession on GSMTC manager.");
        }

        WinRtMediaSession? oldSession = null;
        WinRtMediaSession? newSession = null;

        lock (_lock)
        {
            if (_isDisposed) return;

            // If session is unchanged, do not recreate
            if (winRtSession == null)
            {
                if (_currentSession != null)
                {
                    oldSession = _currentSession;
                    _currentSession = null;
                }
            }
            else
            {
                // New session detected
                oldSession = _currentSession;
                newSession = new WinRtMediaSession(winRtSession);
                _currentSession = newSession;
            }
        }

        // Clean up previous session safely outside lock
        if (oldSession != null)
        {
            oldSession.SessionClosed -= OnCurrentSessionClosed;
            oldSession.Dispose();
        }

        if (newSession != null)
        {
            newSession.SessionClosed += OnCurrentSessionClosed;
            await newSession.RefreshAllAsync();
            Log.Information("Active media session switched to '{SourceAppId}'.", newSession.SourceAppId);
        }
        else if (oldSession != null)
        {
            Log.Information("No active media sessions currently playing.");
        }

        try
        {
            CurrentSessionChanged?.Invoke(this, newSession);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception notifying CurrentSessionChanged subscribers.");
        }
    }

    private async void OnCurrentSessionClosed(object? sender, EventArgs e)
    {
        if (_isDisposed) return;
        Log.Information("Current media session closed by player. Updating media state.");
        await UpdateCurrentSessionAsync();
    }

    /// <summary>
    /// Attempts to bring the source media player application to the foreground.
    /// Degrades peacefully without throwing if the window cannot be activated.
    /// </summary>
    public bool TryActivateApp(string sourceAppId)
    {
        if (string.IsNullOrWhiteSpace(sourceAppId)) return false;

        try
        {
            string procName = ExtractProcessName(sourceAppId);
            Log.Debug("Attempting to activate window for media app candidate '{ProcName}' (from '{SourceAppId}')", procName, sourceAppId);

            var processes = Process.GetProcessesByName(procName);
            if (processes.Length == 0)
            {
                // Try searching without trailing digits or packaging prefixes
                var stripped = procName.Split('_')[0];
                processes = Process.GetProcessesByName(stripped);
            }

            foreach (var proc in processes)
            {
                var handle = proc.MainWindowHandle;
                if (handle != IntPtr.Zero && NativeMethods.IsWindowVisible(handle))
                {
                    NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);
                    NativeMethods.SetForegroundWindow(handle);
                    Log.Information("Activated main window for media app '{ProcName}' (HWND: {Handle}).", procName, handle);
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Peaceful failure attempting to activate media application '{SourceAppId}'.", sourceAppId);
            return false;
        }
    }

    private static string ExtractProcessName(string sourceAppId)
    {
        // 1. Packaged AppUserModelId: e.g. "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify"
        if (sourceAppId.Contains('!'))
        {
            var parts = sourceAppId.Split('!');
            if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
            {
                return parts[1];
            }
        }

        // 2. Full path or executable: e.g. "Spotify.exe" or "C:\...\Spotify.exe"
        try
        {
            var fileName = Path.GetFileNameWithoutExtension(sourceAppId);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return fileName;
            }
        }
        catch
        {
            // Fall through
        }

        return sourceAppId;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed) return;
            _isDisposed = true;

            if (_sessionManager != null)
            {
                try
                {
                    _sessionManager.CurrentSessionChanged -= OnSessionManagerCurrentSessionChanged;
                    _sessionManager.SessionsChanged -= OnSessionManagerSessionsChanged;
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Exception unsubscribing from GSMTC session manager.");
                }
                _sessionManager = null;
            }

            if (_currentSession != null)
            {
                _currentSession.SessionClosed -= OnCurrentSessionClosed;
                _currentSession.Dispose();
                _currentSession = null;
            }

            CurrentSessionChanged = null;
            Log.Information("MediaService disposed and unsubscribed from GSMTC.");
        }
    }
}

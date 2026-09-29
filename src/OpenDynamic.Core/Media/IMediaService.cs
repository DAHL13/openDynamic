namespace OpenDynamic.Core.Media;

/// <summary>
/// Service contract managing active system media sessions.
/// </summary>
public interface IMediaService : IDisposable
{
    /// <summary>
    /// Current foreground/primary media session, or null if no media sessions exist.
    /// </summary>
    IMediaSession? CurrentSession { get; }

    /// <summary>
    /// Occurs when the active media session switches or closes.
    /// </summary>
    event EventHandler<IMediaSession?>? CurrentSessionChanged;

    /// <summary>
    /// Initializes session discovery and hooks background transport control managers.
    /// </summary>
    Task<bool> InitializeAsync();

    /// <summary>
    /// Attempts to bring the source media application to the foreground.
    /// </summary>
    bool TryActivateApp(string sourceAppId);
}

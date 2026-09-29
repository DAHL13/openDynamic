namespace OpenDynamic.App.Infrastructure;

/// <summary>
/// Manages a named system Mutex to enforce a single running application instance per session.
/// </summary>
public sealed class SingleInstanceManager : IDisposable
{
    public const string DefaultMutexName = @"Local\openDynamic-single-instance";

    private readonly string _mutexName;
    private Mutex? _mutex;
    private bool _hasAcquired;

    public SingleInstanceManager(string mutexName = DefaultMutexName)
    {
        _mutexName = mutexName;
    }

    /// <summary>
    /// Attempts to acquire the single-instance mutex.
    /// Returns true if this is the only running instance, false if another instance is already running.
    /// </summary>
    public bool TryAcquire()
    {
        try
        {
            _mutex = new Mutex(true, _mutexName, out bool createdNew);
            _hasAcquired = createdNew;
            return _hasAcquired;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Releases the single-instance mutex.
    /// </summary>
    public void Release()
    {
        if (_hasAcquired && _mutex != null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Mutex wasn't owned by this thread or already released
            }
            _hasAcquired = false;
        }
    }

    public void Dispose()
    {
        Release();
        _mutex?.Dispose();
        _mutex = null;
    }
}

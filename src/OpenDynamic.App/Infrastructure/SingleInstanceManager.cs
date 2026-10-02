namespace OpenDynamic.App.Infrastructure;

/// <summary>
/// Manages a named system Mutex to enforce a single running application instance per session.
/// </summary>
public sealed class SingleInstanceManager : IDisposable
{
    public const string DefaultMutexName = @"Local\openDynamic-single-instance";

    private readonly string _mutexName;
    private Mutex? _mutex;
    private EventWaitHandle? _activateEvent;
    private RegisteredWaitHandle? _waitHandleRegistration;
    private bool _hasAcquired;

    /// <summary>
    /// Event raised on the primary instance when another instance attempts to launch.
    /// </summary>
    public event Action? InstanceActivated;

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

            if (_hasAcquired)
            {
                try
                {
                    _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, _mutexName + "-activate", out _);
                    _waitHandleRegistration = ThreadPool.RegisterWaitForSingleObject(
                        _activateEvent,
                        (state, timedOut) =>
                        {
                            if (!timedOut)
                            {
                                InstanceActivated?.Invoke();
                            }
                        },
                        null,
                        -1,
                        false);
                }
                catch (Exception)
                {
                    // Non-fatal if activation event could not be hooked
                }
            }

            return _hasAcquired;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Signals the existing primary instance that another launch attempt was made.
    /// </summary>
    public void SignalExistingInstance()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(_mutexName + "-activate", out var evt))
            {
                evt.Set();
                evt.Dispose();
            }
        }
        catch (Exception)
        {
            // Ignore if event cannot be signaled
        }
    }

    /// <summary>
    /// Releases the single-instance mutex.
    /// </summary>
    public void Release()
    {
        if (_waitHandleRegistration != null)
        {
            _waitHandleRegistration.Unregister(null);
            _waitHandleRegistration = null;
        }

        _activateEvent?.Dispose();
        _activateEvent = null;

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

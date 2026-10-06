using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using OpenDynamic.Core.Audio;
using Serilog;

namespace OpenDynamic.App.Services;

#region COM Interface Definitions for IMMNotificationClient

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(DataFlow dataFlow, DeviceState dwStateMask, out IntPtr ppDevices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(DataFlow dataFlow, Role role, out IntPtr ppEndpoint);

    [PreserveSig]
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IntPtr ppDevice);

    [PreserveSig]
    int RegisterEndpointNotificationCallback(IMMNotificationClient pClient);

    [PreserveSig]
    int UnregisterEndpointNotificationCallback(IMMNotificationClient pClient);
}

[ComImport]
[Guid("7991EEC9-7E89-4D85-8390-6C703C660F01")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMNotificationClient
{
    [PreserveSig]
    int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int newState);

    [PreserveSig]
    int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);

    [PreserveSig]
    int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);

    [PreserveSig]
    int OnDefaultDeviceChanged(DataFlow flow, Role role, [MarshalAs(UnmanagedType.LPWStr)] string defaultDeviceId);

    [PreserveSig]
    int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, IntPtr key);
}

#endregion

/// <summary>
/// Monitors and controls system audio volume using NAudio (CoreAudio API).
/// Listens to audio endpoint volume notifications and hot-device changes via <see cref="IMMNotificationClient"/>.
/// All COM calls are protected in try/catch blocks with Serilog logging (Golden Rule 4).
/// </summary>
public sealed class VolumeService : IVolumeController, IMMNotificationClient, IDisposable
{
    private readonly object _syncLock = new();
    private IMMDeviceEnumerator? _nativeEnumerator;
    private MMDevice? _currentDevice;
    private string? _currentDeviceId;
    private AudioEndpointVolume? _endpointVolume;

    private float _volume;
    private bool _isMuted;
    private bool _isDisposed;

    public float Volume
    {
        get
        {
            lock (_syncLock)
            {
                return _volume;
            }
        }
    }

    public bool IsMuted
    {
        get
        {
            lock (_syncLock)
            {
                return _isMuted;
            }
        }
    }

    public string? CurrentDeviceFriendlyName
    {
        get
        {
            lock (_syncLock)
            {
                return _currentDevice?.FriendlyName;
            }
        }
    }

    public event EventHandler<VolumeChangedEventArgs>? VolumeChanged;
    public event EventHandler? DefaultDeviceChanged;

    public VolumeService()
    {
        Initialize();
    }

    private void Initialize()
    {
        lock (_syncLock)
        {
            try
            {
                // Register native COM IMMNotificationClient for hot device changes
                var enumeratorType = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"));
                if (enumeratorType != null)
                {
                    var comObj = Activator.CreateInstance(enumeratorType);
                    _nativeEnumerator = comObj as IMMDeviceEnumerator;
                    if (_nativeEnumerator != null)
                    {
                        int hr = _nativeEnumerator.RegisterEndpointNotificationCallback(this);
                        if (hr == 0)
                        {
                            Log.Debug("Registered IMMNotificationClient callback successfully for hot device switching.");
                        }
                        else
                        {
                            Log.Warning("Failed to register IMMNotificationClient callback. HRESULT: 0x{Hr:X8}", hr);
                        }
                    }
                }

                HookDefaultDevice_NoLock();
                Log.Information("VolumeService initialized successfully (Initial Volume: {Volume:P0}, Muted: {Muted})",
                    _volume, _isMuted);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to initialize CoreAudio VolumeService.");
            }
        }
    }

    private void HookDefaultDevice_NoLock()
    {
        try
        {
            // Unhook previous endpoint volume
            if (_endpointVolume != null)
            {
                try
                {
                    _endpointVolume.OnVolumeNotification -= OnVolumeNotificationHandler;
                    _endpointVolume.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error unhooking previous AudioEndpointVolume.");
                }
                finally
                {
                    _endpointVolume = null;
                }
            }

            if (_currentDevice != null)
            {
                try
                {
                    _currentDevice.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error disposing previous MMDevice.");
                }
                finally
                {
                    _currentDevice = null;
                    _currentDeviceId = null;
                }
            }

            // Retrieve default multimedia audio rendering device safely with a fresh enumerator
            using var enumerator = new MMDeviceEnumerator();
            MMDevice? device = null;
            try
            {
                device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }
            catch
            {
                // Fallback to Console role if Multimedia role is unavailable
                try
                {
                    device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Could not acquire default audio endpoint (Console fallback).");
                }
            }

            if (device != null)
            {
                _currentDevice = device;
                _currentDeviceId = device.ID;
                _endpointVolume = device.AudioEndpointVolume;
                if (_endpointVolume != null)
                {
                    _endpointVolume.OnVolumeNotification += OnVolumeNotificationHandler;
                    _volume = VolumeCalculator.Normalize(_endpointVolume.MasterVolumeLevelScalar);
                    _isMuted = _endpointVolume.Mute;
                    Log.Information("Hooked default audio endpoint (Initial Volume: {Volume:P0}, Muted: {Muted})",
                        _volume, _isMuted);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "COM exception occurred while hooking default audio endpoint device.");
        }
    }

    private void OnVolumeNotificationHandler(AudioVolumeNotificationData data)
    {
        float newVolume;
        bool newMute;

        lock (_syncLock)
        {
            _volume = VolumeCalculator.Normalize(data.MasterVolume);
            _isMuted = data.Muted;
            newVolume = _volume;
            newMute = _isMuted;
        }

        Log.Information("AudioEndpointVolume notification received: Volume={Volume:P0}, Muted={Muted}", newVolume, newMute);
        VolumeChanged?.Invoke(this, new VolumeChangedEventArgs(newVolume, newMute));
    }

    public void SetVolume(float level)
    {
        float target = VolumeCalculator.Normalize(level);

        lock (_syncLock)
        {
            if (_endpointVolume != null)
            {
                try
                {
                    _endpointVolume.MasterVolumeLevelScalar = target;
                    _volume = target;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "COM exception while setting master volume to {Target}.", target);
                }
            }
        }
    }

    public void ChangeVolume(float step)
    {
        lock (_syncLock)
        {
            if (_endpointVolume != null)
            {
                try
                {
                    float current = _endpointVolume.MasterVolumeLevelScalar;
                    float target = VolumeCalculator.Normalize(current + step);
                    _endpointVolume.MasterVolumeLevelScalar = target;
                    _volume = target;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "COM exception while stepping volume by {Step}.", step);
                }
            }
        }
    }

    public void ToggleMute()
    {
        lock (_syncLock)
        {
            if (_endpointVolume != null)
            {
                try
                {
                    bool target = !_endpointVolume.Mute;
                    _endpointVolume.Mute = target;
                    _isMuted = target;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "COM exception while toggling mute.");
                }
            }
        }
    }

    public void SetMute(bool isMuted)
    {
        lock (_syncLock)
        {
            if (_endpointVolume != null)
            {
                try
                {
                    _endpointVolume.Mute = isMuted;
                    _isMuted = isMuted;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "COM exception while setting mute to {IsMuted}.", isMuted);
                }
            }
        }
    }

    #region IMMNotificationClient Implementation (Hot Audio Device Switching)

    public int OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow != DataFlow.Render || (role != Role.Multimedia && role != Role.Console))
        {
            return 0;
        }

        Log.Information("IMMNotificationClient: Default audio endpoint changed (Role: {Role}). Re-hooking.", role);

        _ = Task.Run(() =>
        {
            float currentVol;
            bool isMuted;

            lock (_syncLock)
            {
                HookDefaultDevice_NoLock();
                currentVol = _volume;
                isMuted = _isMuted;
            }

            VolumeChanged?.Invoke(this, new VolumeChangedEventArgs(currentVol, isMuted));
            DefaultDeviceChanged?.Invoke(this, EventArgs.Empty);
        });

        return 0;
    }

    public int OnDeviceStateChanged(string deviceId, int newState)
    {
        // Only re-hook if the currently hooked device changed state or if no device is currently hooked
        lock (_syncLock)
        {
            if (!string.IsNullOrEmpty(_currentDeviceId) &&
                !string.Equals(deviceId, _currentDeviceId, StringComparison.OrdinalIgnoreCase) &&
                newState != 1)
            {
                return 0;
            }
        }

        Log.Debug("IMMNotificationClient: OnDeviceStateChanged (State: {State})", newState);
        _ = Task.Run(() =>
        {
            lock (_syncLock)
            {
                HookDefaultDevice_NoLock();
            }
        });
        return 0;
    }

    public int OnDeviceAdded(string pwstrDeviceId)
    {
        Log.Debug("IMMNotificationClient: OnDeviceAdded");
        return 0;
    }

    public int OnDeviceRemoved(string pwstrDeviceId)
    {
        lock (_syncLock)
        {
            if (!string.IsNullOrEmpty(_currentDeviceId) &&
                !string.Equals(pwstrDeviceId, _currentDeviceId, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }
        }

        Log.Debug("IMMNotificationClient: OnDeviceRemoved for active endpoint");
        _ = Task.Run(() =>
        {
            lock (_syncLock)
            {
                HookDefaultDevice_NoLock();
            }
        });
        return 0;
    }

    public int OnPropertyValueChanged(string pwstrDeviceId, IntPtr key)
    {
        return 0;
    }

    #endregion

    public void Dispose()
    {
        lock (_syncLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;

            if (_nativeEnumerator != null)
            {
                try
                {
                    _nativeEnumerator.UnregisterEndpointNotificationCallback(this);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error unregistering IMMNotificationClient.");
                }
                finally
                {
                    if (Marshal.IsComObject(_nativeEnumerator))
                    {
                        Marshal.ReleaseComObject(_nativeEnumerator);
                    }
                    _nativeEnumerator = null;
                }
            }

            if (_endpointVolume != null)
            {
                try
                {
                    _endpointVolume.OnVolumeNotification -= OnVolumeNotificationHandler;
                    _endpointVolume.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error disposing endpoint volume.");
                }
                finally
                {
                    _endpointVolume = null;
                }
            }

            if (_currentDevice != null)
            {
                try
                {
                    _currentDevice.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error disposing MMDevice.");
                }
                finally
                {
                    _currentDevice = null;
                    _currentDeviceId = null;
                }
            }

            VolumeChanged = null;
        }
    }
}

using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32;
using OpenDynamic.App.Native;
using OpenDynamic.Core.Clipboard;
using OpenDynamic.Core.Settings;
using Serilog;

namespace OpenDynamic.App.Services;

/// <summary>
/// Reactive service that listens to Windows clipboard changes using Win32 <c>AddClipboardFormatListener</c>
/// and <c>WM_CLIPBOARDUPDATE</c>.
/// Adheres strictly to:
/// - Golden Rule 1: Zero polling, 100% event-driven.
/// - Golden Rule 4: Protected with try/catch and 3x 50ms retry on COM exceptions (CLIPBRD_E_CANT_OPEN).
/// - Golden Rule 10: Strict RAM residency. Zero persistence to disk or logs. Ignored password managers.
/// - Clears buffer on Windows SessionLock or system suspend.
/// </summary>
public sealed class ClipboardService : IDisposable
{
    private readonly ClipboardHistoryManager _historyManager;
    private readonly AppSettings _settings;
    private readonly Dispatcher _dispatcher;

    private IntPtr _hwnd = IntPtr.Zero;
    private bool _isHooked;
    private bool _isDisposed;
    private uint _internalCopySequenceNumber;
    private string? _lastInternalCopyText;
    private long _lastInternalImageCopyTicks;

    // Password manager exclusion formats
    private static readonly uint FormatExclude = NativeMethods.RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
    private static readonly uint FormatViewerIgnore = NativeMethods.RegisterClipboardFormat("Clipboard Viewer Ignore");
    private static readonly uint FormatCanIncludeHistory = NativeMethods.RegisterClipboardFormat("CanIncludeInClipboardHistory");
    private static readonly uint FormatCanUploadCloud = NativeMethods.RegisterClipboardFormat("CanUploadToCloudClipboard");

    public ClipboardHistoryManager HistoryManager => _historyManager;
    public bool IsHooked => _isHooked;

    public event EventHandler<ClipboardItem>? ClipboardItemCaptured;
    public event EventHandler? HistoryCleared;

    public ClipboardService(
        ClipboardHistoryManager historyManager,
        AppSettings settings,
        Dispatcher? dispatcher = null)
    {
        _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _dispatcher = dispatcher ?? (System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher);

        _historyManager.Capacity = _settings.ClipboardHistoryCapacity;
        _historyManager.Expiration = TimeSpan.FromMinutes(Math.Max(1, _settings.ClipboardExpirationMinutes));
    }

    /// <summary>
    /// Initializes clipboard format listening on the given window handle if enabled in settings.
    /// </summary>
    public void Start(IntPtr hwnd)
    {
        if (_isDisposed) return;
        _hwnd = hwnd;

        try
        {
            SystemEvents.SessionSwitch += OnSessionSwitch;

            if (_settings.EnableClipboardWidget)
            {
                HookListener();
            }
            else
            {
                Log.Information("ClipboardService started but format listener is inactive (EnableClipboardWidget=false).");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start ClipboardService.");
        }
    }

    /// <summary>
    /// Hooks the native clipboard format listener.
    /// </summary>
    public void HookListener()
    {
        if (_isHooked || _hwnd == IntPtr.Zero || _isDisposed) return;

        try
        {
            bool success = NativeMethods.AddClipboardFormatListener(_hwnd);
            if (success)
            {
                _isHooked = true;
                Log.Information("Clipboard format listener hooked successfully on HWND {Hwnd}.", _hwnd);
            }
            else
            {
                int error = Marshal.GetLastWin32Error();
                Log.Warning("AddClipboardFormatListener returned false. LastError={LastError}", error);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error calling AddClipboardFormatListener.");
        }
    }

    /// <summary>
    /// Unhooks the native clipboard format listener and clears all in-memory entries.
    /// </summary>
    public void Stop()
    {
        if (_isHooked && _hwnd != IntPtr.Zero)
        {
            try
            {
                NativeMethods.RemoveClipboardFormatListener(_hwnd);
                Log.Information("Clipboard format listener unhooked from HWND {Hwnd}.", _hwnd);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error calling RemoveClipboardFormatListener.");
            }
            finally
            {
                _isHooked = false;
            }
        }

        ClearHistory();
    }

    /// <summary>
    /// Synchronizes the enabled state according to settings change.
    /// </summary>
    public void UpdateEnabledState(bool enabled)
    {
        if (enabled)
        {
            if (!_isHooked && _hwnd != IntPtr.Zero)
            {
                HookListener();
            }
        }
        else
        {
            Stop();
        }
    }

    /// <summary>
    /// Reacts to the WM_CLIPBOARDUPDATE native window message.
    /// </summary>
    public void HandleClipboardUpdate()
    {
        if (!_settings.EnableClipboardWidget || !_isHooked || _isDisposed)
        {
            return;
        }

        _ = ProcessClipboardUpdateAsync();
    }

    private async Task ProcessClipboardUpdateAsync()
    {
        // Brief asynchronous yield (15ms) to give copying application time to finish writing to clipboard
        await Task.Delay(15);

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                // Check if update was triggered internally by our own re-copy
                uint seq = NativeMethods.GetClipboardSequenceNumber();
                if (_internalCopySequenceNumber != 0 && seq == _internalCopySequenceNumber)
                {
                    _internalCopySequenceNumber = 0;
                    Log.Debug("WM_CLIPBOARDUPDATE sequence matched internal copy ({Seq}). Ignoring.", seq);
                    return;
                }

                // Check password manager exclusions
                if (IsPasswordManagerExcluded())
                {
                    Log.Debug("WM_CLIPBOARDUPDATE excluded by password manager clipboard flag.");
                    return;
                }

                bool handled = await _dispatcher.InvokeAsync(() =>
                {
                    if (_isDisposed) return false;

                    try
                    {
                        if (System.Windows.Clipboard.ContainsText())
                        {
                            string text = System.Windows.Clipboard.GetText();
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                if (!string.IsNullOrEmpty(_lastInternalCopyText) && string.Equals(text, _lastInternalCopyText, StringComparison.Ordinal))
                                {
                                    _lastInternalCopyText = null;
                                    Log.Debug("WM_CLIPBOARDUPDATE matched last internal copy text. Suppressing notice.");
                                    return true;
                                }

                                if (_historyManager.TryAddText(text, out var item) && item != null)
                                {
                                    // Privacy: Log strictly metadata, never user text
                                    Log.Information("Clipboard item added: Kind={Kind}, Length={Length}", item.Kind, item.Length);
                                    ClipboardItemCaptured?.Invoke(this, item);
                                }
                                return true;
                            }
                        }
                        else if (System.Windows.Clipboard.ContainsFileDropList())
                        {
                            var files = System.Windows.Clipboard.GetFileDropList();
                            int count = files?.Count ?? 0;
                            if (count > 0)
                            {
                                if (_historyManager.TryAddFiles(count, out var item) && item != null)
                                {
                                    Log.Information("Clipboard item added: Kind=Files, Count={Count}", count);
                                    ClipboardItemCaptured?.Invoke(this, item);
                                }
                                return true;
                            }
                        }
                        else if (System.Windows.Clipboard.ContainsImage())
                        {
                            if (_lastInternalImageCopyTicks != 0 && (Environment.TickCount64 - _lastInternalImageCopyTicks) < 500)
                            {
                                _lastInternalImageCopyTicks = 0;
                                Log.Debug("WM_CLIPBOARDUPDATE matched recent internal image copy. Suppressing notice.");
                                return true;
                            }

                            if (_historyManager.TryAddImage(out var item) && item != null)
                            {
                                Log.Information("Clipboard item added: Kind=Image");
                                ClipboardItemCaptured?.Invoke(this, item);
                            }
                            return true;
                        }
                    }
                    catch (COMException ex) when ((uint)ex.ErrorCode == 0x800401D0 || ex.ErrorCode == NativeMethods.CLIPBRD_E_CANT_OPEN)
                    {
                        // Bubble up to trigger outer loop retry
                        throw;
                    }

                    return false;
                });

                if (handled)
                {
                    return;
                }
            }
            catch (COMException ex) when ((uint)ex.ErrorCode == 0x800401D0 || ex.ErrorCode == NativeMethods.CLIPBRD_E_CANT_OPEN)
            {
                if (attempt < 3)
                {
                    await Task.Delay(50);
                }
                else
                {
                    Log.Debug("Clipboard busy (CLIPBRD_E_CANT_OPEN) after 3 attempts. Skipping update.");
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Unexpected error processing clipboard update.");
                break;
            }
        }
    }

    /// <summary>
    /// Writes text back to Windows Clipboard from expanded recent item click,
    /// tracking sequence numbers to prevent duplicate self-notifications.
    /// </summary>
    public void CopyToClipboard(string text)
    {
        if (string.IsNullOrEmpty(text) || _isDisposed) return;

        _dispatcher.Invoke(() =>
        {
            try
            {
                _lastInternalCopyText = text;
                System.Windows.Clipboard.SetText(text);
                _internalCopySequenceNumber = NativeMethods.GetClipboardSequenceNumber();
                Log.Information("Copied item to clipboard internally (Length={Length}, Seq={Seq}).",
                    text.Length, _internalCopySequenceNumber);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to copy item to Windows Clipboard.");
            }
        });
    }

    /// <summary>
    /// Writes a bitmap image to the Windows Clipboard with short retries on COMException
    /// and tracks sequence number / timestamp to prevent Phase 14 from firing a duplicate alert.
    /// </summary>
    public async Task<bool> CopyImageToClipboardAsync(System.Windows.Media.Imaging.BitmapSource image)
    {
        if (image == null || _isDisposed)
        {
            return false;
        }

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                bool copied = await _dispatcher.InvokeAsync(() =>
                {
                    if (_isDisposed) return false;

                    _lastInternalImageCopyTicks = Environment.TickCount64;
                    System.Windows.Clipboard.SetImage(image);
                    _internalCopySequenceNumber = NativeMethods.GetClipboardSequenceNumber();
                    Log.Information("Copied screenshot image to clipboard internally (Width={Width}, Height={Height}, Seq={Seq}).",
                        image.PixelWidth, image.PixelHeight, _internalCopySequenceNumber);
                    return true;
                });

                if (copied)
                {
                    return true;
                }
            }
            catch (COMException ex) when ((uint)ex.ErrorCode == 0x800401D0 || ex.ErrorCode == NativeMethods.CLIPBRD_E_CANT_OPEN)
            {
                if (attempt < 3)
                {
                    await Task.Delay(50);
                }
                else
                {
                    Log.Warning("Clipboard busy (CLIPBRD_E_CANT_OPEN) after 3 attempts when copying screenshot image.");
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to copy screenshot image to Windows Clipboard.");
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Clears all stored clipboard entries from RAM immediately.
    /// </summary>
    public void ClearHistory()
    {
        _historyManager.Clear();
        HistoryCleared?.Invoke(this, EventArgs.Empty);
        Log.Information("In-memory clipboard history cleared.");
    }

    /// <summary>
    /// Called when the system enters sleep or suspension.
    /// </summary>
    public void NotifySuspended()
    {
        Log.Information("Power suspend detected. Purging clipboard memory.");
        ClearHistory();
    }

    /// <summary>
    /// Called when the system resumes from sleep.
    /// </summary>
    public void NotifyResumed()
    {
        Log.Information("Power resume detected. Clipboard memory clean.");
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock)
        {
            Log.Information("Windows session locked (SessionLock). Purging in-memory clipboard buffer.");
            ClearHistory();
        }
    }

    private static bool IsPasswordManagerExcluded()
    {
        try
        {
            if (FormatExclude != 0 && NativeMethods.IsClipboardFormatAvailable(FormatExclude))
            {
                return true;
            }

            if (FormatViewerIgnore != 0 && NativeMethods.IsClipboardFormatAvailable(FormatViewerIgnore))
            {
                return true;
            }

            if (FormatCanIncludeHistory != 0 && NativeMethods.IsClipboardFormatAvailable(FormatCanIncludeHistory))
            {
                uint? val = ReadDwordFormat(FormatCanIncludeHistory);
                if (val.HasValue && val.Value == 0)
                {
                    return true;
                }
            }

            if (FormatCanUploadCloud != 0 && NativeMethods.IsClipboardFormatAvailable(FormatCanUploadCloud))
            {
                uint? val = ReadDwordFormat(FormatCanUploadCloud);
                if (val.HasValue && val.Value == 0)
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error checking password manager clipboard exclusion formats.");
        }

        return false;
    }

    private static uint? ReadDwordFormat(uint format)
    {
        if (!NativeMethods.OpenClipboard(IntPtr.Zero))
        {
            return null;
        }

        try
        {
            IntPtr hData = NativeMethods.GetClipboardData(format);
            if (hData == IntPtr.Zero) return null;

            IntPtr pData = NativeMethods.GlobalLock(hData);
            if (pData == IntPtr.Zero) return null;

            try
            {
                UIntPtr size = NativeMethods.GlobalSize(hData);
                if ((ulong)size >= sizeof(uint))
                {
                    return (uint)Marshal.ReadInt32(pData);
                }
            }
            finally
            {
                NativeMethods.GlobalUnlock(hData);
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }

        return null;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        SystemEvents.SessionSwitch -= OnSessionSwitch;
        Stop();
    }
}

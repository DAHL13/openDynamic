using System.Runtime.InteropServices;
using System.Windows.Interop;
using OpenDynamic.App.Native;
using OpenDynamic.Core.Hotkeys;
using Serilog;

namespace OpenDynamic.App.Services;

/// <summary>
/// Implements global hotkeys using native Win32 RegisterHotKey and WndProc message routing.
/// Strictly forbids low-level hooks (WH_KEYBOARD_LL) per Golden Rule 2.
/// Gracefully handles registration collisions without throwing unhandled exceptions.
/// </summary>
public sealed class HotkeyService : IHotkeyService
{
    private const int ToggleIslandHotkeyId = 0x9001;

    private readonly object _syncLock = new();
    private IntPtr _hwnd = IntPtr.Zero;
    private HwndSource? _hwndSource;
    private bool _isHookAttached;
    private bool _isDisposed;

    /// <inheritdoc />
    public string? CurrentHotkey { get; private set; }

    /// <inheritdoc />
    public bool HasConflict { get; private set; }

    /// <inheritdoc />
    public string? ConflictMessage { get; private set; }

    /// <inheritdoc />
    public event EventHandler? HotkeyTriggered;

    /// <inheritdoc />
    public event EventHandler<string>? HotkeyConflictOccurred;

    /// <inheritdoc />
    public void Initialize(IntPtr hwnd, HwndSource hwndSource)
    {
        lock (_syncLock)
        {
            if (_isDisposed) return;

            _hwnd = hwnd;
            _hwndSource = hwndSource ?? throw new ArgumentNullException(nameof(hwndSource));

            if (!_isHookAttached)
            {
                _hwndSource.AddHook(HwndHook);
                _isHookAttached = true;
                Log.Debug("HotkeyService attached WndProc hook to HWND {Hwnd}.", hwnd);
            }
        }
    }

    /// <inheritdoc />
    public bool UpdateHotkey(string hotkeySpec)
    {
        lock (_syncLock)
        {
            if (_isDisposed || _hwnd == IntPtr.Zero)
            {
                Log.Warning("Cannot update hotkey: service is disposed or not initialized with a valid HWND.");
                return false;
            }

            // Always unregister previous hotkey first
            UnregisterInternal();

            if (string.IsNullOrWhiteSpace(hotkeySpec))
            {
                CurrentHotkey = null;
                HasConflict = false;
                ConflictMessage = null;
                return true;
            }

            if (!HotkeyParser.TryParse(hotkeySpec, out var definition, out var parseError))
            {
                HasConflict = true;
                ConflictMessage = $"Formato de atajo inválido: {parseError}";
                Log.Warning("Hotkey parsing failed for '{Spec}': {Error}", hotkeySpec, parseError);
                HotkeyConflictOccurred?.Invoke(this, ConflictMessage);
                return false;
            }

            bool registered = NativeMethods.RegisterHotKey(
                _hwnd,
                ToggleIslandHotkeyId,
                (uint)definition!.Modifiers,
                definition.VirtualKey);

            if (!registered)
            {
                int errorCode = Marshal.GetLastWin32Error();
                HasConflict = true;
                ConflictMessage = errorCode == NativeMethods.ERROR_HOTKEY_ALREADY_REGISTERED
                    ? $"El atajo '{definition.NormalizedText}' ya está en uso por otra aplicación (Código 1409)."
                    : $"Error del sistema al registrar el atajo '{definition.NormalizedText}' (Código {errorCode}).";

                Log.Warning("Failed to register hotkey {Hotkey}: Win32 Error {Error}. Peaceful collision handling applied.",
                    definition.NormalizedText, errorCode);

                HotkeyConflictOccurred?.Invoke(this, ConflictMessage);
                return false;
            }

            HasConflict = false;
            ConflictMessage = null;
            CurrentHotkey = definition.NormalizedText;
            Log.Information("Global hotkey registered successfully: {Hotkey} (HWND {Hwnd})", CurrentHotkey, _hwnd);
            return true;
        }
    }

    /// <inheritdoc />
    public void Unregister()
    {
        lock (_syncLock)
        {
            UnregisterInternal();
            CurrentHotkey = null;
            HasConflict = false;
            ConflictMessage = null;
        }
    }

    private void UnregisterInternal()
    {
        if (_hwnd != IntPtr.Zero)
        {
            try
            {
                NativeMethods.UnregisterHotKey(_hwnd, ToggleIslandHotkeyId);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Exception unregistering hotkey ID {Id} on HWND {Hwnd}", ToggleIslandHotkeyId, _hwnd);
            }
        }
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            int hotkeyId = wParam.ToInt32();
            if (hotkeyId == ToggleIslandHotkeyId)
            {
                Log.Information("Global hotkey triggered (ID {Id}). Notifying subscribers.", hotkeyId);
                handled = true;
                HotkeyTriggered?.Invoke(this, EventArgs.Empty);
            }
        }

        return IntPtr.Zero;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_syncLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;

            UnregisterInternal();

            if (_isHookAttached && _hwndSource != null)
            {
                try
                {
                    _hwndSource.RemoveHook(HwndHook);
                }
                catch
                {
                    // Ignore disposal hook removal errors
                }
                _isHookAttached = false;
            }

            _hwnd = IntPtr.Zero;
            _hwndSource = null;
            Log.Information("HotkeyService disposed and hotkeys released.");
        }
    }
}

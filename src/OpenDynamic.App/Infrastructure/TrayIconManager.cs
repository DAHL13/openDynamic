using System.IO;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using OpenDynamic.App.Orchestration;
using OpenDynamic.App.Widgets.Hardware;
using OpenDynamic.App.Windowing;
using OpenDynamic.Core.Settings;
using OpenDynamic.Core.State;
using Serilog;

namespace OpenDynamic.App.Infrastructure;

/// <summary>
/// Manages the system notification tray icon using H.NotifyIcon.Wpf.
/// Strictly forbids System.Windows.Forms per Golden Rule 3 (zero WinForms).
/// Handles left-click toggling, contextual menu actions, and clean disposal without ghost icons.
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private readonly IslandOrchestrator _orchestrator;
    private readonly WindowPositioner _windowPositioner;
    private readonly ISettingsService _settingsService;
    private readonly Func<Window>? _getIslandWindow;
    private readonly Action? _openSettingsAction;

    private TaskbarIcon? _taskbarIcon;
    private MenuItem? _hardwareMenuItem;
    private bool _isDisposed;

    /// <summary>
    /// Event raised when the user clicks 'Abrir Ajustes' in the context menu.
    /// </summary>
    public event EventHandler? OpenSettingsRequested;

    public TrayIconManager(
        IslandOrchestrator orchestrator,
        WindowPositioner windowPositioner,
        ISettingsService settingsService,
        Func<Window>? getIslandWindow = null,
        Action? openSettingsAction = null)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _windowPositioner = windowPositioner ?? throw new ArgumentNullException(nameof(windowPositioner));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _getIslandWindow = getIslandWindow;
        _openSettingsAction = openSettingsAction;
    }

    /// <summary>
    /// Initializes and renders the system tray icon on the Windows taskbar.
    /// </summary>
    public void Initialize()
    {
        if (_taskbarIcon != null || _isDisposed) return;

        try
        {
            var icon = GetOrCreateIcon();
            _taskbarIcon = new TaskbarIcon
            {
                ToolTipText = "openDynamic - Dynamic Island para Windows",
                Icon = icon
            };

            _taskbarIcon.TrayLeftMouseDown += OnTrayLeftMouseDown;
            _taskbarIcon.ContextMenu = BuildContextMenu();

            // Register taskbar icon in Application.Current.Resources so WPF visual/resource tree anchors it
            if (Application.Current?.Resources != null)
            {
                Application.Current.Resources["OpenDynamicTaskbarIcon"] = _taskbarIcon;
            }

            // Explicitly force creation of native taskbar icon (Shell_NotifyIcon NIM_ADD)
            try
            {
                _taskbarIcon.ForceCreate();
                Log.Information("System tray icon initialized successfully using H.NotifyIcon.Wpf.");
            }
            catch (InvalidOperationException ex)
            {
                // In automated CI, headless test environments, or when Explorer taskbar is not yet ready,
                // Shell_NotifyIcon returns E_FAIL. The icon will be registered upon receiving TaskbarCreated.
                Log.Warning(ex, "System tray icon registration deferred: shell notification area not ready. Will be registered upon TaskbarCreated.");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize system tray icon.");
        }
    }

    /// <summary>
    /// Recreates or re-registers the tray icon in the Windows taskbar (e.g., when explorer.exe restarts).
    /// </summary>
    public void Recreate()
    {
        if (_isDisposed) return;

        try
        {
            Log.Information("Recreating system tray icon (TaskbarCreated handled)...");
            if (_taskbarIcon == null)
            {
                Initialize();
            }
            else
            {
                _taskbarIcon.ForceCreate(true);
                Log.Information("System tray icon recreated successfully.");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to recreate system tray icon after taskbar event.");
        }
    }

    private void OnTrayLeftMouseDown(object? sender, RoutedEventArgs e)
    {
        ToggleIslandVisibility();
    }

    /// <summary>
    /// Toggles the island between visible (Compact or active mode) and Hidden.
    /// </summary>
    public void ToggleIslandVisibility()
    {
        var currentState = _orchestrator.StateMachine.CurrentState;
        Log.Information("Toggling island visibility from tray icon (Current state: {State}).", currentState);

        if (currentState == IslandState.Hidden)
        {
            _orchestrator.RequestRestore();
        }
        else
        {
            _orchestrator.RequestHide();
        }
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        // 1. Abrir Ajustes
        var settingsItem = new MenuItem
        {
            Header = "⚙ Abrir Ajustes...",
            FontWeight = FontWeights.SemiBold
        };
        settingsItem.Click += (_, _) =>
        {
            Log.Information("Abrir Ajustes requested from tray icon context menu.");
            _openSettingsAction?.Invoke();
            OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
        };
        menu.Items.Add(settingsItem);

        menu.Items.Add(new Separator());

        // 2. Conmutar Monitor de Hardware
        _hardwareMenuItem = new MenuItem
        {
            Header = "📊 Conmutar Monitor de Hardware",
            IsCheckable = true,
            IsChecked = _settingsService.CurrentSettings.EnableHardwareMonitoring
        };
        _hardwareMenuItem.Click += (_, _) =>
        {
            var hwWidget = _orchestrator.RegisteredWidgets.OfType<HardwareWidget>().FirstOrDefault();
            if (hwWidget != null)
            {
                hwWidget.ToggleMonitoring();
                _settingsService.CurrentSettings.EnableHardwareMonitoring = hwWidget.IsActive;
                _hardwareMenuItem.IsChecked = hwWidget.IsActive;
                _settingsService.SaveDebounced();
                Log.Information("Hardware monitor toggled from tray icon. New active status: {IsActive}", hwWidget.IsActive);
            }
        };
        menu.Items.Add(_hardwareMenuItem);

        // 3. Reiniciar Posición
        var resetPosItem = new MenuItem
        {
            Header = "🔄 Reiniciar Posición"
        };
        resetPosItem.Click += (_, _) =>
        {
            Log.Information("Reiniciar Posición requested from tray context menu.");
            _settingsService.CurrentSettings.OffsetX = 0.0;
            _settingsService.CurrentSettings.OffsetY = 0.0;
            _settingsService.CurrentSettings.CapsuleWidth = 200.0;
            _settingsService.CurrentSettings.CapsuleCornerRadius = 14.0;
            _settingsService.CurrentSettings.TargetMonitorIndex = 0;
            _settingsService.SaveDebounced();

            var window = _getIslandWindow?.Invoke();
            if (window != null)
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(window);
                if (helper.Handle != IntPtr.Zero)
                {
                    _windowPositioner.PositionWindow(helper.Handle);
                }
            }
        };
        menu.Items.Add(resetPosItem);

        menu.Items.Add(new Separator());

        // 4. Salir de openDynamic
        var exitItem = new MenuItem
        {
            Header = "❌ Salir de openDynamic"
        };
        exitItem.Click += (_, _) =>
        {
            Log.Information("Exit requested via tray icon context menu. Initiating graceful shutdown.");
            Dispose();
            Application.Current.Dispatcher.Invoke(Application.Current.Shutdown);
        };
        menu.Items.Add(exitItem);

        // Keep checkbox in sync when context menu opens
        menu.Opened += (_, _) =>
        {
            var hwWidget = _orchestrator.RegisteredWidgets.OfType<HardwareWidget>().FirstOrDefault();
            if (_hardwareMenuItem != null && hwWidget != null)
            {
                _hardwareMenuItem.IsChecked = hwWidget.IsActive;
            }
        };

        return menu;
    }

    private static System.Drawing.Icon GetOrCreateIcon()
    {
        try
        {
            string diskPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "app.ico");
            if (File.Exists(diskPath))
            {
                var icon = new System.Drawing.Icon(diskPath);
                Log.Information("Loaded tray icon from disk path: {Path}", diskPath);
                return icon;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not load icon directly from disk path.");
        }

        try
        {
            var iconUri = new Uri("pack://application:,,,/OpenDynamic.App;component/Resources/app.ico");
            var resourceStream = Application.GetResourceStream(iconUri)?.Stream;
            if (resourceStream != null)
            {
                using var ms = new MemoryStream();
                resourceStream.CopyTo(ms);
                resourceStream.Dispose();
                ms.Position = 0;
                var icon = new System.Drawing.Icon(ms);
                Log.Information("Loaded tray icon from pack URI resource stream.");
                return icon;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not load icon from pack URI resource stream.");
        }

        // Fallback: draw in-memory 32x32 icon with native handle
        try
        {
            using var bmp = new System.Drawing.Bitmap(32, 32);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(System.Drawing.Color.Transparent);
                using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 20, 24, 33));
                using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(255, 96, 165, 250), 2);
                using var path = new System.Drawing.Drawing2D.GraphicsPath();
                path.AddArc(2, 8, 16, 16, 90, 180);
                path.AddArc(14, 8, 16, 16, 270, 180);
                path.CloseFigure();
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
                using var dotBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 240, 246, 252));
                g.FillEllipse(dotBrush, 14, 14, 4, 4);
            }

            IntPtr hIcon = bmp.GetHicon();
            return System.Drawing.Icon.FromHandle(hIcon);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to create fallback GDI icon. Using SystemIcons.Application.");
            return (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_taskbarIcon != null)
        {
            try
            {
                _taskbarIcon.TrayLeftMouseDown -= OnTrayLeftMouseDown;
                _taskbarIcon.Visibility = Visibility.Collapsed;

                if (Application.Current?.Resources != null && Application.Current.Resources.Contains("OpenDynamicTaskbarIcon"))
                {
                    Application.Current.Resources.Remove("OpenDynamicTaskbarIcon");
                }

                _taskbarIcon.Dispose();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Exception during TaskbarIcon disposal.");
            }
            finally
            {
                _taskbarIcon = null;
            }
        }

        Log.Information("TrayIconManager disposed cleanly without leaving ghost icons in system tray.");
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
            _taskbarIcon = new TaskbarIcon
            {
                ToolTipText = "openDynamic - Dynamic Island para Windows",
                Icon = GetOrCreateIcon()
            };

            _taskbarIcon.TrayLeftMouseDown += OnTrayLeftMouseDown;
            _taskbarIcon.ContextMenu = BuildContextMenu();

            Log.Information("System tray icon initialized successfully using H.NotifyIcon.Wpf.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize system tray icon.");
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
            _settingsService.CurrentSettings.OffsetY = 8.0;
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

    /// <summary>
    /// Generates a sharp 32x32 BitmapImage representing the Dynamic Island capsule for the tray icon.
    /// Encodes via PNG stream to ensure compatibility with H.NotifyIcon.Wpf ImageExtensions.
    /// </summary>
    private static ImageSource CreateCapsuleIconSource()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // Dark capsule background (pill shape)
            var pillRect = new Rect(2, 8, 28, 16);
            var pillBrush = new SolidColorBrush(Color.FromRgb(20, 20, 24));
            var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(96, 165, 250)), 1.5);
            dc.DrawRoundedRectangle(pillBrush, borderPen, pillRect, 8, 8);

            // Center glow dot
            var dotBrush = new SolidColorBrush(Color.FromRgb(240, 246, 252));
            dc.DrawEllipse(dotBrush, null, new Point(16, 16), 3, 3);
        }

        var rtb = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new System.IO.MemoryStream();
        encoder.Save(ms);
        ms.Position = 0;

        var bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.StreamSource = ms;
        bitmapImage.EndInit();
        bitmapImage.Freeze();

        return bitmapImage;
    }

    private static System.Drawing.Icon GetOrCreateIcon()
    {
        try
        {
            var iconUri = new Uri("pack://application:,,,/OpenDynamic.App;component/Resources/app.ico");
            var resourceStream = Application.GetResourceStream(iconUri)?.Stream;
            if (resourceStream != null)
            {
                using (resourceStream)
                {
                    return new System.Drawing.Icon(resourceStream);
                }
            }
        }
        catch { }

        try
        {
            string diskPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "app.ico");
            if (System.IO.File.Exists(diskPath))
            {
                using var fs = System.IO.File.OpenRead(diskPath);
                return new System.Drawing.Icon(fs);
            }
        }
        catch { }

        // Fallback: draw in-memory 32x32 icon
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

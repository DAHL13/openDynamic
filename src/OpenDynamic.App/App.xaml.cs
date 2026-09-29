using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using OpenDynamic.App.Infrastructure;
using Serilog;

namespace OpenDynamic.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private SingleInstanceManager? _singleInstance;
    private TrayIconManager? _trayIconManager;
    private Services.IHotkeyService? _hotkeyService;

    public static new App Current => (App)Application.Current;

    public IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Run without requiring an active window
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        LoggingConfiguration.ConfigureLogging();
        RegisterGlobalExceptionHandlers();
        AccessibilityThemeManager.Initialize();

        // Configure dependency injection
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddAppServices();
        Services = serviceCollection.BuildServiceProvider();

        _singleInstance = Services.GetRequiredService<SingleInstanceManager>();
        if (!_singleInstance.TryAcquire())
        {
            Log.Information("Another instance of openDynamic is already running. Exiting silently.");
            Shutdown();
            return;
        }

        Log.Information("openDynamic initialized successfully (Single instance acquired, ShutdownMode=OnExplicitShutdown).");

        var islandWindow = Services.GetRequiredService<Windowing.IslandWindow>();
        islandWindow.Show();

        var orchestrator = Services.GetRequiredService<Orchestration.IslandOrchestrator>();

        // Initialize Media GSMTC Service & Register MediaWidget
        var mediaService = Services.GetRequiredService<Services.MediaService>();
        _ = mediaService.InitializeAsync();

        var mediaWidget = Services.GetRequiredService<Widgets.Media.MediaWidget>();
        orchestrator.RegisterWidget(mediaWidget);

        // Register VolumeWidget (Priority 80, Transient)
        var volumeWidget = Services.GetRequiredService<Widgets.Volume.VolumeWidget>();
        orchestrator.RegisterWidget(volumeWidget);

        // Register BatteryWidget (Priority 90, Transient)
        var batteryWidget = Services.GetRequiredService<Widgets.Battery.BatteryWidget>();
        orchestrator.RegisterWidget(batteryWidget);

        // Register HardwareWidget (Priority 10)
        var hardwareWidget = Services.GetRequiredService<Widgets.Hardware.HardwareWidget>();
        orchestrator.RegisterWidget(hardwareWidget);

        // Register TimerWidget (Priority 50 running, Priority 100 on alert)
        var timerWidget = Services.GetRequiredService<Widgets.Timer.TimerWidget>();
        orchestrator.RegisterWidget(timerWidget);

        // Initialize System Tray Icon Manager (H.NotifyIcon.Wpf) stored in class field to prevent GC collection
        _trayIconManager = Services.GetRequiredService<TrayIconManager>();
        _trayIconManager.Initialize();

        // Initialize Global Hotkey Service using native Win32 RegisterHotKey
        _hotkeyService = Services.GetRequiredService<Services.IHotkeyService>();
        var hwndSource = System.Windows.Interop.HwndSource.FromHwnd(islandWindow.Hwnd);
        if (hwndSource != null)
        {
            uint taskbarCreatedMsg = Native.NativeMethods.RegisterWindowMessage("TaskbarCreated");
            hwndSource.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (msg != 0 && (uint)msg == taskbarCreatedMsg)
                {
                    Log.Information("TaskbarCreated broadcast received from Windows Shell. Recreating tray icon...");
                    _trayIconManager?.Recreate();
                }
                return IntPtr.Zero;
            });

            _hotkeyService.Initialize(islandWindow.Hwnd, hwndSource);
            var settings = Services.GetRequiredService<Core.Settings.AppSettings>();
            if (settings.EnableGlobalHotkeys && !string.IsNullOrWhiteSpace(settings.ToggleIslandHotkey))
            {
                _hotkeyService.UpdateHotkey(settings.ToggleIslandHotkey);
            }

            _hotkeyService.HotkeyTriggered += (s, ev) =>
            {
                if (orchestrator.StateMachine.CurrentState == Core.State.IslandState.Hidden)
                {
                    orchestrator.RequestRestore();
                }
                else
                {
                    orchestrator.RequestHide();
                }
            };
        }

        // Verify and correct autostart executable path if registered
        var autostartService = Services.GetRequiredService<Core.Autostart.IAutostartService>();
        autostartService.VerifyAndCorrectExecutablePath();

#if DEBUG
        // Register demo widgets for manual testing/fault injection from debug window
        var demoA = new Widgets.Demo.DemoWidgetA(priority: 70, startActive: false);
        var demoB = new Widgets.Demo.DemoWidgetB(priority: 50, startActive: false);
        orchestrator.RegisterWidget(demoA);
        orchestrator.RegisterWidget(demoB);
#endif

        ProcessCommandLineArgs(e.Args);
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (s, e) =>
        {
            Log.Error(e.Exception, "Unhandled exception intercepted on WPF Dispatcher. Preserving application state.");
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                Log.Fatal(ex, "Unhandled AppDomain exception (IsTerminating: {IsTerminating})", e.IsTerminating);
            }
            else
            {
                Log.Fatal("Unhandled AppDomain exception: {ExceptionObject} (IsTerminating: {IsTerminating})", e.ExceptionObject, e.IsTerminating);
            }
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Log.Error(e.Exception, "Unobserved Task exception intercepted. Preserving application state.");
            e.SetObserved();
        };
    }

    private void ProcessCommandLineArgs(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--trigger-test-exception")
            {
                Log.Information("Triggering test exception on Dispatcher to verify global exception handling...");
                Dispatcher.BeginInvoke(() =>
                {
                    throw new InvalidOperationException("Test intentional exception handled by DispatcherUnhandledException");
                });
            }

            if (args[i] == "--exit-after-ms" && i + 1 < args.Length && int.TryParse(args[i + 1], out int ms))
            {
                Log.Information("Scheduled shutdown configured after {Milliseconds} ms.", ms);
                _ = Task.Run(async () =>
                {
                    await Task.Delay(ms);
                    Log.Information("Scheduled delay elapsed. Shutting down...");
                    Dispatcher.Invoke(Shutdown);
                });
            }

            if (args[i] == "--smoke-test")
            {
                Log.Information("Smoke test flag present. Shutting down after 1000 ms...");
                _ = Task.Run(async () =>
                {
                    await Task.Delay(1000);
                    Dispatcher.Invoke(Shutdown);
                });
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _trayIconManager?.Dispose();
            _trayIconManager = null;

            _hotkeyService?.Dispose();
            _hotkeyService = null;

            var settingsService = Services?.GetService<Core.Settings.ISettingsService>();
            settingsService?.SaveImmediate();
            settingsService?.Dispose();

            var settingsWindow = Services?.GetService<Views.SettingsWindow>();
            settingsWindow?.ForceClose();

            if (Services is IDisposable disposableServices)
            {
                disposableServices.Dispose();
            }
            _singleInstance?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Exception encountered during OnExit cleanup.");
        }
        finally
        {
            LoggingConfiguration.CloseAndFlush();
            base.OnExit(e);
        }
    }
}

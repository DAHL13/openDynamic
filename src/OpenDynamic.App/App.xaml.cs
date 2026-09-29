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

    public static new App Current => (App)Application.Current;

    public IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Run without requiring an active window
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        LoggingConfiguration.ConfigureLogging();
        RegisterGlobalExceptionHandlers();

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

#if DEBUG
        // Register demo widgets for manual testing/fault injection from debug window
        var demoA = new Widgets.Demo.DemoWidgetA(priority: 70, startActive: false);
        var demoB = new Widgets.Demo.DemoWidgetB(priority: 50, startActive: false);
        orchestrator.RegisterWidget(demoA);
        orchestrator.RegisterWidget(demoB);
#endif

        SetupTemporaryShutdownMechanisms(e.Args);
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (s, e) =>
        {
            Log.Error(e.Exception, "Unhandled exception intercepted on WPF Dispatcher. Preserving application state.");
            // Do not close app for widget/UI failures
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
            // Do not crash on background task exceptions
            e.SetObserved();
        };
    }

    private void SetupTemporaryShutdownMechanisms(string[] args)
    {
        // 1. Console Ctrl+C hook (when attached or running in terminal)
        try
        {
            Console.CancelKeyPress += (s, e) =>
            {
                Log.Information("Ctrl+C received. Initiating graceful shutdown...");
                e.Cancel = true;
                Dispatcher.Invoke(Shutdown);
            };
        }
        catch
        {
            // Ignore if console is not available
        }

        // 2. Interactive console input listener (typing 'exit', 'quit' or 'q')
        Task.Run(() =>
        {
            try
            {
                while (Console.ReadLine() is { } line)
                {
                    var trimmed = line.Trim();
                    if (trimmed.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.Equals("quit", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.Equals("q", StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Information("Exit command received from console input. Initiating shutdown...");
                        Dispatcher.Invoke(Shutdown);
                        break;
                    }
                }
            }
            catch
            {
                // Console input not accessible
            }
        });

        // 3. Command line triggers for verification / CI
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
            if (Services is IDisposable disposableServices)
            {
                disposableServices.Dispose();
            }
            _singleInstance?.Dispose();
        }
        finally
        {
            LoggingConfiguration.CloseAndFlush();
            base.OnExit(e);
        }
    }
}

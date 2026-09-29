using System.Windows;
using OpenDynamic.App.Infrastructure;
using Serilog;

namespace OpenDynamic.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private readonly SingleInstanceManager _singleInstance = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        LoggingConfiguration.ConfigureLogging();
        RegisterGlobalExceptionHandlers();

        if (!_singleInstance.TryAcquire())
        {
            Log.Information("Another instance of openDynamic is already running. Exiting silently.");
            Shutdown();
            return;
        }

        Log.Information("openDynamic single-instance lock acquired successfully.");
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (s, e) =>
        {
            Log.Error(e.Exception, "Unhandled exception in WPF Dispatcher.");
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
            Log.Error(e.Exception, "Unobserved Task exception.");
            // Do not crash on background task exceptions
            e.SetObserved();
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _singleInstance.Dispose();
        }
        finally
        {
            LoggingConfiguration.CloseAndFlush();
            base.OnExit(e);
        }
    }
}




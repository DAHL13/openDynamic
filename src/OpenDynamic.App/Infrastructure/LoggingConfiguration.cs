using System.IO;
using Serilog;

namespace OpenDynamic.App.Infrastructure;

/// <summary>
/// Configures Serilog file logging to %LocalAppData%\openDynamic\logs.
/// </summary>
public static class LoggingConfiguration
{
    public static string LogDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "openDynamic", "logs");

    public static void ConfigureLogging()
    {
        Directory.CreateDirectory(LogDirectory);

        string logFilePath = Path.Combine(LogDirectory, "openDynamic-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                path: logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("openDynamic logging initialized. Logs directory: {LogDir}", LogDirectory);
    }

    public static void CloseAndFlush()
    {
        Log.Information("openDynamic application shutting down. Flushing logs.");
        Log.CloseAndFlush();
    }
}

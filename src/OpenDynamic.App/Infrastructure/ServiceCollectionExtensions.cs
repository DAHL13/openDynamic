using Microsoft.Extensions.DependencyInjection;
using OpenDynamic.App.Animation;
using OpenDynamic.App.Infrastructure;
using OpenDynamic.App.Orchestration;
using OpenDynamic.App.Services;
using OpenDynamic.App.ViewModels;
using OpenDynamic.App.Views;
using OpenDynamic.App.Windowing;
using OpenDynamic.Core.Autostart;
using OpenDynamic.Core.Settings;
using OpenDynamic.Core.State;
using OpenDynamic.Core.Widgets;
using Serilog;

namespace OpenDynamic.App.Infrastructure;

/// <summary>
/// Configures dependency injection for the openDynamic application.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAppServices(this IServiceCollection services)
    {
        services.AddSingleton<SingleInstanceManager>();
        services.AddSingleton<WindowPositioner>();
        services.AddSingleton<ForegroundWatcher>();
        services.AddSingleton<FullscreenWatcher>();
        services.AddSingleton<IslandStateMachine>();
        services.AddSingleton<IslandLayout>();
        services.AddSingleton<IslandAnimator>();
        services.AddSingleton<PriorityResolver>();
        services.AddSingleton<IslandOrchestrator>(sp => new IslandOrchestrator(
            sp.GetRequiredService<IslandStateMachine>(),
            sp.GetRequiredService<IslandAnimator>(),
            sp.GetRequiredService<PriorityResolver>(),
            sp.GetRequiredService<AppSettings>()));
        services.AddSingleton<IslandWindow>(sp => new IslandWindow(
            sp.GetRequiredService<WindowPositioner>(),
            sp.GetRequiredService<ForegroundWatcher>(),
            sp.GetRequiredService<IslandOrchestrator>(),
            sp.GetRequiredService<PowerService>(),
            sp.GetRequiredService<FullscreenWatcher>(),
            sp.GetRequiredService<AppSettings>(),
            sp.GetRequiredService<NetworkService>(),
            sp.GetRequiredService<DeviceService>(),
            sp.GetRequiredService<ClipboardService>()));

        // Settings Service & Persistence
        services.AddSingleton<ISettingsService>(sp =>
        {
            var service = new SettingsService(
                warningLogger: (msg, ex) =>
                {
                    if (ex != null) Log.Warning(ex, "[Settings] {Message}", msg);
                    else Log.Information("[Settings] {Message}", msg);
                });
            service.Load();
            return service;
        });
        services.AddSingleton<AppSettings>(sp => sp.GetRequiredService<ISettingsService>().CurrentSettings);

        // Hotkey Service
        services.AddSingleton<HotkeyService>();
        services.AddSingleton<IHotkeyService>(sp => sp.GetRequiredService<HotkeyService>());

        // Autostart Service
        services.AddSingleton<AutostartService>();
        services.AddSingleton<IAutostartService>(sp => sp.GetRequiredService<AutostartService>());

        // Media GSMTC Services
        services.AddSingleton<MediaColorService>();
        services.AddSingleton<MediaService>();
        services.AddSingleton<Core.Media.IMediaService>(sp => sp.GetRequiredService<MediaService>());
        services.AddSingleton<Widgets.Media.MediaWidget>(sp => new Widgets.Media.MediaWidget(
            sp.GetRequiredService<Core.Media.IMediaService>(),
            sp.GetRequiredService<AppSettings>(),
            System.Windows.Application.Current?.Dispatcher,
            sp.GetRequiredService<MediaColorService>()));

        // Audio & Volume Services
        services.AddSingleton<VolumeService>();
        services.AddSingleton<Core.Audio.IVolumeController>(sp => sp.GetRequiredService<VolumeService>());
        services.AddSingleton<Widgets.Volume.VolumeWidget>();

        // Power & Battery Services
        services.AddSingleton<PowerService>();
        services.AddSingleton<Core.Power.IBatteryMonitor>(sp => sp.GetRequiredService<PowerService>());
        services.AddSingleton<Widgets.Battery.BatteryWidget>();

        // Hardware Monitoring Services
        services.AddSingleton<HardwareService>();
        services.AddSingleton<Core.Hardware.IHardwareMonitor>(sp => sp.GetRequiredService<HardwareService>());
        services.AddSingleton<Widgets.Hardware.HardwareWidget>();

        // Timer & Pomodoro Services
        services.AddSingleton<Core.Timer.TimerController>();
        services.AddSingleton<Core.Timer.ITimerController>(sp => sp.GetRequiredService<Core.Timer.TimerController>());
        services.AddSingleton<Core.Timer.ITimerPersistenceService, Core.Timer.TimerPersistenceService>();
        services.AddSingleton<Core.Timer.TimerCollection>();
        services.AddSingleton<Core.Timer.ITimerCollection>(sp => sp.GetRequiredService<Core.Timer.TimerCollection>());
        services.AddSingleton<Widgets.Timer.TimerWidget>();

        // Stopwatch Services (Priority 45)
        services.AddSingleton<Core.Stopwatch.StopwatchController>();
        services.AddSingleton<Core.Stopwatch.IStopwatchController>(sp => sp.GetRequiredService<Core.Stopwatch.StopwatchController>());
        services.AddSingleton<TimingUiCoordinator>();
        services.AddSingleton<Widgets.Stopwatch.StopwatchWidget>();

        // Network Services & Widget (Priority 65, Transient)
        services.AddSingleton<NetworkService>();
        services.AddSingleton<Widgets.Network.NetworkWidget>();

        // Device Services & Widget (Priority 60, Transient)
        services.AddSingleton<DeviceService>();
        services.AddSingleton<Widgets.Device.DeviceWidget>();

        // Clipboard History Services (Priority 55, Strict RAM, Opt-In)
        services.AddSingleton<Core.Clipboard.ClipboardHistoryManager>(sp =>
        {
            var settings = sp.GetRequiredService<AppSettings>();
            return new Core.Clipboard.ClipboardHistoryManager(
                capacity: settings.ClipboardHistoryCapacity,
                expiration: TimeSpan.FromMinutes(Math.Max(1, settings.ClipboardExpirationMinutes)));
        });
        services.AddSingleton<ClipboardService>();
        services.AddSingleton<Widgets.Clipboard.ClipboardWidget>();

        // Settings Window & ViewModel
        services.AddSingleton<SettingsViewModel>(sp => new SettingsViewModel(
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<IslandOrchestrator>(),
            sp.GetRequiredService<WindowPositioner>(),
            sp.GetRequiredService<IHotkeyService>(),
            sp.GetRequiredService<IAutostartService>(),
            () => sp.GetRequiredService<IslandWindow>(),
            sp.GetRequiredService<NetworkService>(),
            sp.GetRequiredService<DeviceService>(),
            sp.GetRequiredService<Core.Timer.ITimerCollection>(),
            sp.GetRequiredService<ClipboardService>()));
        services.AddSingleton<SettingsWindow>();

        // System Tray Icon Manager
        services.AddSingleton<TrayIconManager>(sp => new TrayIconManager(
            sp.GetRequiredService<IslandOrchestrator>(),
            sp.GetRequiredService<WindowPositioner>(),
            sp.GetRequiredService<ISettingsService>(),
            () => sp.GetRequiredService<IslandWindow>(),
            () => sp.GetRequiredService<SettingsWindow>().ShowSettings(),
            sp.GetRequiredService<ClipboardService>()));

        return services;
    }
}

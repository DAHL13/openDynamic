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
        services.AddSingleton<IslandOrchestrator>();
        services.AddSingleton<IslandWindow>();

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
        services.AddSingleton<MediaService>();
        services.AddSingleton<Core.Media.IMediaService>(sp => sp.GetRequiredService<MediaService>());
        services.AddSingleton<Widgets.Media.MediaWidget>();

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
        services.AddSingleton<Widgets.Timer.TimerWidget>();

        // Network Services & Widget (Priority 65, Transient)
        services.AddSingleton<NetworkService>();
        services.AddSingleton<Widgets.Network.NetworkWidget>();

        // Device Services & Widget (Priority 60, Transient)
        services.AddSingleton<DeviceService>();
        services.AddSingleton<Widgets.Device.DeviceWidget>();

        // Settings Window & ViewModel
        services.AddSingleton<SettingsViewModel>(sp => new SettingsViewModel(
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<IslandOrchestrator>(),
            sp.GetRequiredService<WindowPositioner>(),
            sp.GetRequiredService<IHotkeyService>(),
            sp.GetRequiredService<IAutostartService>(),
            () => sp.GetRequiredService<IslandWindow>(),
            sp.GetRequiredService<NetworkService>(),
            sp.GetRequiredService<DeviceService>()));
        services.AddSingleton<SettingsWindow>();

        // System Tray Icon Manager
        services.AddSingleton<TrayIconManager>(sp => new TrayIconManager(
            sp.GetRequiredService<IslandOrchestrator>(),
            sp.GetRequiredService<WindowPositioner>(),
            sp.GetRequiredService<ISettingsService>(),
            () => sp.GetRequiredService<IslandWindow>(),
            () => sp.GetRequiredService<SettingsWindow>().ShowSettings()));

        return services;
    }
}

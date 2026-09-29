using Microsoft.Extensions.DependencyInjection;
using OpenDynamic.App.Animation;
using OpenDynamic.App.Orchestration;
using OpenDynamic.App.Windowing;
using OpenDynamic.Core.State;
using OpenDynamic.Core.Widgets;

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

        // Media & Settings Services
        services.AddSingleton<OpenDynamic.Core.Settings.AppSettings>();
        services.AddSingleton<Services.MediaService>();
        services.AddSingleton<OpenDynamic.Core.Media.IMediaService>(sp => sp.GetRequiredService<Services.MediaService>());
        services.AddSingleton<Widgets.Media.MediaWidget>();

        // Audio & Volume Services
        services.AddSingleton<Services.VolumeService>();
        services.AddSingleton<OpenDynamic.Core.Audio.IVolumeController>(sp => sp.GetRequiredService<Services.VolumeService>());
        services.AddSingleton<Widgets.Volume.VolumeWidget>();

        // Power & Battery Services
        services.AddSingleton<Services.PowerService>();
        services.AddSingleton<OpenDynamic.Core.Power.IBatteryMonitor>(sp => sp.GetRequiredService<Services.PowerService>());
        services.AddSingleton<Widgets.Battery.BatteryWidget>();

        // Hardware Monitoring Services
        services.AddSingleton<Services.HardwareService>();
        services.AddSingleton<OpenDynamic.Core.Hardware.IHardwareMonitor>(sp => sp.GetRequiredService<Services.HardwareService>());
        services.AddSingleton<Widgets.Hardware.HardwareWidget>();

        return services;
    }
}

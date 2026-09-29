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
        services.AddSingleton<IslandStateMachine>();
        services.AddSingleton<IslandLayout>();
        services.AddSingleton<IslandAnimator>();
        services.AddSingleton<PriorityResolver>();
        services.AddSingleton<IslandOrchestrator>();
        services.AddSingleton<IslandWindow>();
        return services;
    }
}

using Microsoft.Extensions.DependencyInjection;
using OpenDynamic.App.Windowing;

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
        services.AddSingleton<IslandWindow>();
        return services;
    }
}

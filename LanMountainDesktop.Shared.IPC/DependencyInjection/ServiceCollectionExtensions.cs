using Microsoft.Extensions.DependencyInjection;

namespace LanMountainDesktop.Shared.IPC.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLanMountainDesktopIpcHost(
        this IServiceCollection services,
        string? pipeName = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(provider =>
        {
            var host = new PublicIpcHostService(pipeName ?? IpcConstants.DefaultPipeName);
            foreach (var registration in provider.GetServices<PublicIpcServiceRegistration>())
            {
                var implementation = registration.ImplementationFactory(provider);
                host.RegisterPublicService(
                    registration.ContractType,
                    implementation,
                    registration.ObjectId,
                    registration.PluginId,
                    registration.NotifyIds);
            }

            host.Start();
            return host;
        });

        services.AddSingleton<IExternalIpcNotificationPublisher>(provider =>
            provider.GetRequiredService<PublicIpcHostService>());

        return services;
    }

    public static IServiceCollection AddPublicIpcService<TContract, TImplementation>(
        this IServiceCollection services,
        string? objectId = null,
        string? pluginId = null,
        params string[] notifyIds)
        where TContract : class
        where TImplementation : class, TContract
    {
        ArgumentNullException.ThrowIfNull(services);

        EnsureSingletonRegistration<TContract, TImplementation>(services);

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(PublicIpcServiceRegistration) &&
                descriptor.ImplementationInstance is PublicIpcServiceRegistration existing &&
                existing.ContractType == typeof(TContract) &&
                string.Equals(existing.ObjectId, objectId, StringComparison.Ordinal)))
        {
            services.AddSingleton(new PublicIpcServiceRegistration(
                typeof(TContract),
                provider => provider.GetRequiredService<TContract>(),
                objectId,
                pluginId,
                notifyIds ?? []));
        }

        return services;
    }

    private static void EnsureSingletonRegistration<TContract, TImplementation>(IServiceCollection services)
        where TContract : class
        where TImplementation : class, TContract
    {
        var descriptor = services.LastOrDefault(item => item.ServiceType == typeof(TContract));
        if (descriptor is null)
        {
            services.AddSingleton<TContract, TImplementation>();
            return;
        }

        if (descriptor.Lifetime != ServiceLifetime.Singleton)
        {
            throw new InvalidOperationException(
                $"Public IPC contract '{typeof(TContract).FullName}' must be registered as Singleton.");
        }
    }
}

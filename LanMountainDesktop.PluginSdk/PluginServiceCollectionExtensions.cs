using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace LanMountainDesktop.PluginSdk;

public static class PluginServiceCollectionExtensions
{
    public static IServiceCollection AddPluginSettingsPage<TControl>(
        this IServiceCollection services,
        string id,
        string title,
        int sortOrder = 0)
        where TControl : Control
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(new PluginSettingsPageRegistration(
            id,
            title,
            provider => ActivatorUtilities.CreateInstance<TControl>(provider),
            sortOrder));
        return services;
    }

    public static IServiceCollection AddPluginDesktopComponent<TControl>(
        this IServiceCollection services,
        string componentId,
        string displayName,
        string iconKey = "PuzzlePiece",
        string category = "Plugins",
        int minWidthCells = 2,
        int minHeightCells = 2,
        bool allowDesktopPlacement = true,
        bool allowStatusBarPlacement = false,
        PluginDesktopComponentResizeMode resizeMode = PluginDesktopComponentResizeMode.Proportional,
        string? displayNameLocalizationKey = null,
        Func<double, double>? cornerRadiusResolver = null)
        where TControl : Control
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(new PluginDesktopComponentRegistration(
            componentId,
            displayName,
            (provider, context) => ActivatorUtilities.CreateInstance<TControl>(provider, context),
            iconKey,
            category,
            minWidthCells,
            minHeightCells,
            allowDesktopPlacement,
            allowStatusBarPlacement,
            resizeMode,
            displayNameLocalizationKey,
            cornerRadiusResolver));
        return services;
    }

    public static IServiceCollection AddPluginExport<TContract, TImplementation>(this IServiceCollection services)
        where TContract : class
        where TImplementation : class, TContract
    {
        ArgumentNullException.ThrowIfNull(services);

        EnsureSingletonRegistration<TContract, TImplementation>(services);

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(PluginServiceExportRegistration) &&
                descriptor.ImplementationInstance is PluginServiceExportRegistration existing &&
                existing.ContractType == typeof(TContract) &&
                existing.ImplementationType == typeof(TImplementation)))
        {
            services.AddSingleton(new PluginServiceExportRegistration(typeof(TContract), typeof(TImplementation)));
        }

        return services;
    }

    private static void EnsureSingletonRegistration<TContract, TImplementation>(IServiceCollection services)
        where TContract : class
        where TImplementation : class, TContract
    {
        var contractDescriptor = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(TContract));
        if (contractDescriptor is null)
        {
            services.AddSingleton<TContract, TImplementation>();
            return;
        }

        if (contractDescriptor.Lifetime != ServiceLifetime.Singleton)
        {
            throw new InvalidOperationException(
                $"Exported contract '{typeof(TContract).FullName}' must be registered as Singleton.");
        }
    }
}

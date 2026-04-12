using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace LanMountainDesktop.PluginSdk;

public static class PluginServiceCollectionExtensions
{
    public static IServiceCollection AddPluginSettingsSection(
        this IServiceCollection services,
        string id,
        string titleLocalizationKey,
        Action<PluginSettingsSectionBuilder> configure,
        string? descriptionLocalizationKey = null,
        string iconKey = "PuzzlePiece",
        int sortOrder = 0)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new PluginSettingsSectionBuilder(
            id,
            titleLocalizationKey,
            descriptionLocalizationKey,
            iconKey,
            sortOrder);
        configure(builder);
        services.AddSingleton(builder.Build());
        return services;
    }

    /// <summary>
    /// Registers a plugin settings section with a custom AXAML view.
    /// The host application will display <typeparamref name="TView"/> directly
    /// in the settings window, allowing the plugin to use any Fluent Avalonia controls
    /// and custom layouts — just like built-in settings pages.
    /// </summary>
    /// <typeparam name="TView">A <see cref="SettingsPageBase"/> subclass that defines the settings UI using AXAML.</typeparam>
    public static IServiceCollection AddPluginSettingsSection<TView>(
        this IServiceCollection services,
        string id,
        string titleLocalizationKey,
        string? descriptionLocalizationKey = null,
        string iconKey = "PuzzlePiece",
        int sortOrder = 0)
        where TView : SettingsPageBase
    {
        ArgumentNullException.ThrowIfNull(services);

        var builder = new PluginSettingsSectionBuilder(
            id,
            titleLocalizationKey,
            descriptionLocalizationKey,
            iconKey,
            sortOrder);
        builder.SetCustomView<TView>();
        services.AddSingleton(builder.Build());
        return services;
    }

    public static IServiceCollection AddPluginDesktopComponent<TControl>(
        this IServiceCollection services,
        PluginDesktopComponentOptions options)
        where TControl : Control
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(new PluginDesktopComponentRegistration(
            (provider, context) => ActivatorUtilities.CreateInstance<TControl>(provider, context),
            options));
        return services;
    }

    public static IServiceCollection AddPluginDesktopComponentEditor<TControl>(
        this IServiceCollection services,
        string componentId,
        double preferredWidth = 720d,
        double preferredHeight = 540d,
        double minScale = 0.85d,
        double maxScale = 1.45d)
        where TControl : Control
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(new PluginDesktopComponentEditorRegistration(
            componentId,
            (provider, context) => ActivatorUtilities.CreateInstance<TControl>(provider, context),
            preferredWidth,
            preferredHeight,
            minScale,
            maxScale));
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

using Avalonia.Controls;
using dotnetCampus.Ipc.CompilerServices.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace LanMountainDesktop.AirAppSdk;

public static class AirAppServiceCollectionExtensions
{
    public static IServiceCollection AddAirAppSettingsSection(
        this IServiceCollection services,
        string id,
        string titleLocalizationKey,
        Action<AirAppSettingsSectionBuilder> configure,
        string? descriptionLocalizationKey = null,
        string iconKey = "PuzzlePiece",
        int sortOrder = 0)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new AirAppSettingsSectionBuilder(
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
    /// <typeparam name="TView">A <see cref="AirAppSettingsPageBase"/> subclass that defines the settings UI using AXAML.</typeparam>
    public static IServiceCollection AddAirAppSettingsSection<TView>(
        this IServiceCollection services,
        string id,
        string titleLocalizationKey,
        string? descriptionLocalizationKey = null,
        string iconKey = "PuzzlePiece",
        int sortOrder = 0)
        where TView : AirAppSettingsPageBase
    {
        ArgumentNullException.ThrowIfNull(services);

        var builder = new AirAppSettingsSectionBuilder(
            id,
            titleLocalizationKey,
            descriptionLocalizationKey,
            iconKey,
            sortOrder);
        builder.SetCustomView<TView>();
        services.AddSingleton(builder.Build());
        return services;
    }

    public static IServiceCollection AddAirAppComponent<TControl>(
        this IServiceCollection services,
        AirAppComponentOptions options)
        where TControl : Control
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(new AirAppComponentRegistration(
            (provider, context) => ActivatorUtilities.CreateInstance<TControl>(provider, context),
            options));
        return services;
    }

    public static IServiceCollection AddAirAppComponentEditor<TControl>(
        this IServiceCollection services,
        string componentId,
        double preferredWidth = 720d,
        double preferredHeight = 540d,
        double minScale = 0.85d,
        double maxScale = 1.45d)
        where TControl : Control
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(new AirAppComponentEditorRegistration(
            componentId,
            (provider, context) => ActivatorUtilities.CreateInstance<TControl>(provider, context),
            preferredWidth,
            preferredHeight,
            minScale,
            maxScale));
        return services;
    }

    /// <summary>
    /// Registers a window AirApp entry.
    /// The host resolves the window by <paramref name="id"/> and hosts its
    /// <see cref="IAirAppWindow"/> content inside the AirAppHost window shell.
    /// </summary>
    /// <typeparam name="TWindow">A class implementing <see cref="IAirAppWindow"/>.</typeparam>
    /// <param name="id">Unique window identifier.</param>
    /// <param name="name">Display name.</param>
    public static IServiceCollection AddAirAppWindow<TWindow>(
        this IServiceCollection services,
        string id,
        string name)
        where TWindow : class, IAirAppWindow
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<TWindow>();
        services.AddSingleton(new AirAppWindowRegistration(id, name, typeof(TWindow)));
        return services;
    }

    public static IServiceCollection AddAirAppExport<TContract, TImplementation>(this IServiceCollection services)
        where TContract : class
        where TImplementation : class, TContract
    {
        ArgumentNullException.ThrowIfNull(services);

        EnsureSingletonRegistration<TContract, TImplementation>(services);

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(AirAppServiceExportRegistration) &&
                descriptor.ImplementationInstance is AirAppServiceExportRegistration existing &&
                existing.ContractType == typeof(TContract) &&
                existing.ImplementationType == typeof(TImplementation)))
        {
            services.AddSingleton(new AirAppServiceExportRegistration(typeof(TContract), typeof(TImplementation)));
        }

        return services;
    }

    public static IServiceCollection AddAirAppPublicIpc<TContract, TImplementation>(
        this IServiceCollection services,
        string? objectId = null,
        params string[] notifyIds)
        where TContract : class
        where TImplementation : class, TContract
    {
        ArgumentNullException.ThrowIfNull(services);
        EnsurePublicIpcContract(typeof(TContract));
        EnsureSingletonRegistration<TContract, TImplementation>(services);

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(AirAppPublicIpcServiceRegistration) &&
                descriptor.ImplementationInstance is AirAppPublicIpcServiceRegistration existing &&
                existing.ContractType == typeof(TContract) &&
                string.Equals(existing.ObjectId, objectId, StringComparison.Ordinal)))
        {
            services.AddSingleton(new AirAppPublicIpcServiceRegistration(
                typeof(TContract),
                objectId,
                notifyIds ?? []));
        }

        return services;
    }

    public static IServiceCollection AddAirAppPublicIpcContributor<TContributor>(this IServiceCollection services)
        where TContributor : class, IAirAppPublicIpcContributor
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IAirAppPublicIpcContributor, TContributor>();
        return services;
    }

    private static void EnsurePublicIpcContract(Type contractType)
    {
        if (!contractType.IsInterface)
        {
            throw new InvalidOperationException(
                $"Public IPC contract '{contractType.FullName}' must be an interface.");
        }

        if (!Attribute.IsDefined(contractType, typeof(IpcPublicAttribute), inherit: false))
        {
            throw new InvalidOperationException(
                $"Public IPC contract '{contractType.FullName}' must be marked with '{nameof(IpcPublicAttribute)}'.");
        }
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

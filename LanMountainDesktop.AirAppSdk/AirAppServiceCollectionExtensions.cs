using Microsoft.Extensions.DependencyInjection;

namespace LanMountainDesktop.AirAppSdk;

/// <summary>
/// Extension methods for registering AirApp services.
/// </summary>
public static class AirAppServiceCollectionExtensions
{
    /// <summary>
    /// Register a desktop component.
    /// </summary>
    public static IServiceCollection AddAirAppComponent<TWidget>(
        this IServiceCollection services,
        string id,
        string name,
        Action<AirAppComponentOptions>? configure = null)
        where TWidget : class, IAirAppWidget
    {
        var options = new AirAppComponentOptions
        {
            Id = id,
            Name = name,
            WidgetType = typeof(TWidget)
        };

        configure?.Invoke(options);

        // Register the widget as transient (new instance per placement)
        services.AddTransient<TWidget>();

        // Register the component options (will be picked up by the host)
        services.AddSingleton(options);

        return services;
    }

    /// <summary>
    /// Register a window.
    /// </summary>
    public static IServiceCollection AddAirAppWindow<TWindow>(
        this IServiceCollection services,
        string id,
        string name)
        where TWindow : class, IAirAppWindow
    {
        // Register the window as transient (new instance per open)
        services.AddTransient<TWindow>();

        // TODO: Register window metadata
        return services;
    }

    /// <summary>
    /// Register a settings section (declarative).
    /// </summary>
    public static IServiceCollection AddAirAppSettings(
        this IServiceCollection services,
        string id,
        string name,
        Action<AirAppSettingsSectionBuilder>? configure = null)
    {
        var builder = new AirAppSettingsSectionBuilder(id, name);
        configure?.Invoke(builder);

        // Register the settings section
        services.AddSingleton(builder.Build());

        return services;
    }
}

/// <summary>
/// Builder for settings sections.
/// </summary>
public sealed class AirAppSettingsSectionBuilder
{
    private readonly string _id;
    private readonly string _name;
    private readonly List<AirAppSettingOption> _options = new();

    internal AirAppSettingsSectionBuilder(string id, string name)
    {
        _id = id;
        _name = name;
    }

    public AirAppSettingsSectionBuilder AddToggle(string key, string label, bool defaultValue = false)
    {
        _options.Add(new AirAppSettingOption
        {
            Key = key,
            Label = label,
            Type = "toggle",
            DefaultValue = defaultValue
        });
        return this;
    }

    public AirAppSettingsSectionBuilder AddText(string key, string label, string? defaultValue = null)
    {
        _options.Add(new AirAppSettingOption
        {
            Key = key,
            Label = label,
            Type = "text",
            DefaultValue = defaultValue
        });
        return this;
    }

    public AirAppSettingsSectionBuilder AddNumber(string key, string label, double defaultValue = 0, double? minimum = null, double? maximum = null)
    {
        _options.Add(new AirAppSettingOption
        {
            Key = key,
            Label = label,
            Type = "number",
            DefaultValue = defaultValue,
            Minimum = minimum,
            Maximum = maximum
        });
        return this;
    }

    internal AirAppSettingsSection Build()
    {
        return new AirAppSettingsSection
        {
            Id = _id,
            Name = _name,
            Options = _options
        };
    }
}

/// <summary>
/// Settings section metadata.
/// </summary>
public sealed class AirAppSettingsSection
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required List<AirAppSettingOption> Options { get; init; }
}

/// <summary>
/// Individual setting option.
/// </summary>
public sealed class AirAppSettingOption
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required string Type { get; init; }
    public object? DefaultValue { get; init; }
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
}

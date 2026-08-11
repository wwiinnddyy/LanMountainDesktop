using System;
using System.Collections.Generic;
using Avalonia.Controls;

namespace LanMountainDesktop.PluginSdk;

public sealed class PluginSettingsSectionBuilder
{
    private readonly List<SettingsOptionDefinition> _options = [];
    private Type? _customViewType;

    internal PluginSettingsSectionBuilder(
        string id,
        string titleLocalizationKey,
        string? descriptionLocalizationKey,
        string iconKey,
        int sortOrder)
    {
        Id = id;
        TitleLocalizationKey = titleLocalizationKey;
        DescriptionLocalizationKey = descriptionLocalizationKey;
        IconKey = iconKey;
        SortOrder = sortOrder;
    }

    public string Id { get; }

    public string TitleLocalizationKey { get; }

    public string? DescriptionLocalizationKey { get; }

    public string IconKey { get; }

    public int SortOrder { get; }

    public Type? CustomViewType => _customViewType;

    public IReadOnlyList<SettingsOptionDefinition> Options => _options;

    /// <summary>
    /// Sets a custom AXAML view for this settings section.
    /// The view type must be a subclass of <see cref="SettingsPageBase"/>.
    /// When a custom view is provided, the host application will use it directly
    /// instead of generating a page from the declared options, allowing the plugin
    /// to use any Fluent Avalonia controls and custom layouts.
    /// </summary>
    /// <typeparam name="TView">A <see cref="SettingsPageBase"/> subclass that defines the settings UI.</typeparam>
    public PluginSettingsSectionBuilder SetCustomView<TView>() where TView : SettingsPageBase
    {
        _customViewType = typeof(TView);
        return this;
    }

    /// <summary>
    /// Sets a custom AXAML view for this settings section.
    /// The view type must be a subclass of <see cref="SettingsPageBase"/>.
    /// When a custom view is provided, the host application will use it directly
    /// instead of generating a page from the declared options.
    /// </summary>
    /// <param name="viewType">A <see cref="SettingsPageBase"/> subclass type that defines the settings UI.</param>
    public PluginSettingsSectionBuilder SetCustomView(Type viewType)
    {
        ArgumentNullException.ThrowIfNull(viewType);

        if (!typeof(SettingsPageBase).IsAssignableFrom(viewType))
        {
            throw new ArgumentException(
                $"Custom view type must be a subclass of {nameof(SettingsPageBase)}.",
                nameof(viewType));
        }

        _customViewType = viewType;
        return this;
    }

    public PluginSettingsSectionBuilder AddOption(SettingsOptionDefinition option)
    {
        ArgumentNullException.ThrowIfNull(option);
        _options.Add(option);
        return this;
    }

    public PluginSettingsSectionBuilder AddToggle(
        string key,
        string titleLocalizationKey,
        string? descriptionLocalizationKey = null,
        bool defaultValue = false)
    {
        return AddOption(new SettingsOptionDefinition(
            key,
            SettingsOptionType.Toggle,
            titleLocalizationKey,
            descriptionLocalizationKey,
            defaultValue));
    }

    public PluginSettingsSectionBuilder AddText(
        string key,
        string titleLocalizationKey,
        string? descriptionLocalizationKey = null,
        string defaultValue = "",
        string? validationPattern = null)
    {
        return AddOption(new SettingsOptionDefinition(
            key,
            SettingsOptionType.Text,
            titleLocalizationKey,
            descriptionLocalizationKey,
            defaultValue,
            validationPattern: validationPattern));
    }

    public PluginSettingsSectionBuilder AddNumber(
        string key,
        string titleLocalizationKey,
        string? descriptionLocalizationKey = null,
        double defaultValue = 0,
        double? minimum = null,
        double? maximum = null)
    {
        return AddOption(new SettingsOptionDefinition(
            key,
            SettingsOptionType.Number,
            titleLocalizationKey,
            descriptionLocalizationKey,
            defaultValue,
            minimum: minimum,
            maximum: maximum));
    }

    public PluginSettingsSectionBuilder AddSelect(
        string key,
        string titleLocalizationKey,
        IEnumerable<SettingsOptionChoice> choices,
        string? descriptionLocalizationKey = null,
        string? defaultValue = null)
    {
        ArgumentNullException.ThrowIfNull(choices);
        var normalizedChoices = choices.ToArray();

        return AddOption(new SettingsOptionDefinition(
            key,
            SettingsOptionType.Select,
            titleLocalizationKey,
            descriptionLocalizationKey,
            defaultValue,
            normalizedChoices));
    }

    public PluginSettingsSectionBuilder AddPath(
        string key,
        string titleLocalizationKey,
        string? descriptionLocalizationKey = null,
        string defaultValue = "")
    {
        return AddOption(new SettingsOptionDefinition(
            key,
            SettingsOptionType.Path,
            titleLocalizationKey,
            descriptionLocalizationKey,
            defaultValue));
    }

    public PluginSettingsSectionBuilder AddList(
        string key,
        string titleLocalizationKey,
        string? descriptionLocalizationKey = null,
        IReadOnlyList<string>? defaultValue = null)
    {
        return AddOption(new SettingsOptionDefinition(
            key,
            SettingsOptionType.List,
            titleLocalizationKey,
            descriptionLocalizationKey,
            defaultValue ?? Array.Empty<string>()));
    }

    internal PluginSettingsSectionRegistration Build()
    {
        return new PluginSettingsSectionRegistration(
            Id,
            TitleLocalizationKey,
            _options.ToArray(),
            DescriptionLocalizationKey,
            IconKey,
            SortOrder,
            _customViewType);
    }
}

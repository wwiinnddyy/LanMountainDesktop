using System;
using System.Collections.Generic;
using Avalonia.Controls;

namespace LanMountainDesktop.AirAppSdk;

public sealed class AirAppSettingsSectionBuilder
{
    private readonly List<AirAppSettingOptionDefinition> _options = [];
    private Type? _customViewType;

    internal AirAppSettingsSectionBuilder(
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

    public IReadOnlyList<AirAppSettingOptionDefinition> Options => _options;

    /// <summary>
    /// Sets a custom AXAML view for this settings section.
    /// The view type must be a subclass of <see cref="AirAppSettingsPageBase"/>.
    /// When a custom view is provided, the host application will use it directly
    /// instead of generating a page from the declared options, allowing the plugin
    /// to use any Fluent Avalonia controls and custom layouts.
    /// </summary>
    /// <typeparam name="TView">A <see cref="AirAppSettingsPageBase"/> subclass that defines the settings UI.</typeparam>
    public AirAppSettingsSectionBuilder SetCustomView<TView>() where TView : AirAppSettingsPageBase
    {
        _customViewType = typeof(TView);
        return this;
    }

    /// <summary>
    /// Sets a custom AXAML view for this settings section.
    /// The view type must be a subclass of <see cref="AirAppSettingsPageBase"/>.
    /// When a custom view is provided, the host application will use it directly
    /// instead of generating a page from the declared options.
    /// </summary>
    /// <param name="viewType">A <see cref="AirAppSettingsPageBase"/> subclass type that defines the settings UI.</param>
    public AirAppSettingsSectionBuilder SetCustomView(Type viewType)
    {
        ArgumentNullException.ThrowIfNull(viewType);

        if (!typeof(AirAppSettingsPageBase).IsAssignableFrom(viewType))
        {
            throw new ArgumentException(
                $"Custom view type must be a subclass of {nameof(AirAppSettingsPageBase)}.",
                nameof(viewType));
        }

        _customViewType = viewType;
        return this;
    }

    public AirAppSettingsSectionBuilder AddOption(AirAppSettingOptionDefinition option)
    {
        ArgumentNullException.ThrowIfNull(option);
        _options.Add(option);
        return this;
    }

    public AirAppSettingsSectionBuilder AddToggle(
        string key,
        string titleLocalizationKey,
        string? descriptionLocalizationKey = null,
        bool defaultValue = false)
    {
        return AddOption(new AirAppSettingOptionDefinition(
            key,
            AirAppSettingOptionType.Toggle,
            titleLocalizationKey,
            descriptionLocalizationKey,
            defaultValue));
    }

    public AirAppSettingsSectionBuilder AddText(
        string key,
        string titleLocalizationKey,
        string? descriptionLocalizationKey = null,
        string defaultValue = "",
        string? validationPattern = null)
    {
        return AddOption(new AirAppSettingOptionDefinition(
            key,
            AirAppSettingOptionType.Text,
            titleLocalizationKey,
            descriptionLocalizationKey,
            defaultValue,
            validationPattern: validationPattern));
    }

    public AirAppSettingsSectionBuilder AddNumber(
        string key,
        string titleLocalizationKey,
        string? descriptionLocalizationKey = null,
        double defaultValue = 0,
        double? minimum = null,
        double? maximum = null)
    {
        return AddOption(new AirAppSettingOptionDefinition(
            key,
            AirAppSettingOptionType.Number,
            titleLocalizationKey,
            descriptionLocalizationKey,
            defaultValue,
            minimum: minimum,
            maximum: maximum));
    }

    public AirAppSettingsSectionBuilder AddSelect(
        string key,
        string titleLocalizationKey,
        IEnumerable<AirAppSettingOptionChoice> choices,
        string? descriptionLocalizationKey = null,
        string? defaultValue = null)
    {
        ArgumentNullException.ThrowIfNull(choices);
        var normalizedChoices = choices.ToArray();

        return AddOption(new AirAppSettingOptionDefinition(
            key,
            AirAppSettingOptionType.Select,
            titleLocalizationKey,
            descriptionLocalizationKey,
            defaultValue,
            normalizedChoices));
    }

    public AirAppSettingsSectionBuilder AddPath(
        string key,
        string titleLocalizationKey,
        string? descriptionLocalizationKey = null,
        string defaultValue = "")
    {
        return AddOption(new AirAppSettingOptionDefinition(
            key,
            AirAppSettingOptionType.Path,
            titleLocalizationKey,
            descriptionLocalizationKey,
            defaultValue));
    }

    public AirAppSettingsSectionBuilder AddList(
        string key,
        string titleLocalizationKey,
        string? descriptionLocalizationKey = null,
        IReadOnlyList<string>? defaultValue = null)
    {
        return AddOption(new AirAppSettingOptionDefinition(
            key,
            AirAppSettingOptionType.List,
            titleLocalizationKey,
            descriptionLocalizationKey,
            defaultValue ?? Array.Empty<string>()));
    }

    internal AirAppSettingsSectionRegistration Build()
    {
        return new AirAppSettingsSectionRegistration(
            Id,
            TitleLocalizationKey,
            _options.ToArray(),
            DescriptionLocalizationKey,
            IconKey,
            SortOrder,
            _customViewType);
    }
}

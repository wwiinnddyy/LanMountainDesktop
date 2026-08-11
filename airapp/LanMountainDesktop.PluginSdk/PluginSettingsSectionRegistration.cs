using System;
using System.Collections.Generic;

namespace LanMountainDesktop.PluginSdk;

public sealed class PluginSettingsSectionRegistration
{
    public PluginSettingsSectionRegistration(
        string id,
        string titleLocalizationKey,
        IReadOnlyList<SettingsOptionDefinition> options,
        string? descriptionLocalizationKey = null,
        string iconKey = "PuzzlePiece",
        int sortOrder = 0,
        Type? customViewType = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(titleLocalizationKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(iconKey);

        Id = id.Trim();
        TitleLocalizationKey = titleLocalizationKey.Trim();
        DescriptionLocalizationKey = string.IsNullOrWhiteSpace(descriptionLocalizationKey)
            ? null
            : descriptionLocalizationKey.Trim();
        IconKey = iconKey.Trim();
        SortOrder = sortOrder;
        Options = options ?? [];

        if (customViewType is not null && !typeof(SettingsPageBase).IsAssignableFrom(customViewType))
        {
            throw new ArgumentException(
                $"Custom view type must be a subclass of {nameof(SettingsPageBase)}.",
                nameof(customViewType));
        }

        CustomViewType = customViewType;
    }

    public string Id { get; }

    public string TitleLocalizationKey { get; }

    public string? DescriptionLocalizationKey { get; }

    public string IconKey { get; }

    public int SortOrder { get; }

    public IReadOnlyList<SettingsOptionDefinition> Options { get; }

    /// <summary>
    /// When set, the host application will instantiate this <see cref="SettingsPageBase"/> subclass
    /// instead of generating a page from <see cref="Options"/>.
    /// This allows plugins to provide fully custom AXAML views with any Fluent Avalonia controls.
    /// </summary>
    public Type? CustomViewType { get; }
}

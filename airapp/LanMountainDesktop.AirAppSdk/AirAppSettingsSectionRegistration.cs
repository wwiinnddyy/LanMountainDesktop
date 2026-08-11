using System;
using System.Collections.Generic;

namespace LanMountainDesktop.AirAppSdk;

public sealed class AirAppSettingsSectionRegistration
{
    public AirAppSettingsSectionRegistration(
        string id,
        string titleLocalizationKey,
        IReadOnlyList<AirAppSettingOptionDefinition> options,
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

        if (customViewType is not null && !typeof(AirAppSettingsPageBase).IsAssignableFrom(customViewType))
        {
            throw new ArgumentException(
                $"Custom view type must be a subclass of {nameof(AirAppSettingsPageBase)}.",
                nameof(customViewType));
        }

        CustomViewType = customViewType;
    }

    public string Id { get; }

    public string TitleLocalizationKey { get; }

    public string? DescriptionLocalizationKey { get; }

    public string IconKey { get; }

    public int SortOrder { get; }

    public IReadOnlyList<AirAppSettingOptionDefinition> Options { get; }

    /// <summary>
    /// When set, the host application will instantiate this <see cref="AirAppSettingsPageBase"/> subclass
    /// instead of generating a page from <see cref="Options"/>.
    /// This allows plugins to provide fully custom AXAML views with any Fluent Avalonia controls.
    /// </summary>
    public Type? CustomViewType { get; }
}

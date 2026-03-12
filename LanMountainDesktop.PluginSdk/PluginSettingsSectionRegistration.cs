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
        int sortOrder = 0)
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
    }

    public string Id { get; }

    public string TitleLocalizationKey { get; }

    public string? DescriptionLocalizationKey { get; }

    public string IconKey { get; }

    public int SortOrder { get; }

    public IReadOnlyList<SettingsOptionDefinition> Options { get; }
}

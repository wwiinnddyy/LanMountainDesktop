using System.Collections.Generic;

namespace LanMountainDesktop.PluginSdk;

public sealed class SettingsSectionDefinition
{
    public SettingsSectionDefinition(
        string id,
        string category,
        SettingsScope scope,
        string titleLocalizationKey,
        string? descriptionLocalizationKey = null,
        string iconKey = "Settings",
        int sortOrder = 0,
        string? subjectId = null,
        IReadOnlyList<SettingsOptionDefinition>? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(titleLocalizationKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(iconKey);

        Id = id.Trim();
        Category = category.Trim();
        Scope = scope;
        TitleLocalizationKey = titleLocalizationKey.Trim();
        DescriptionLocalizationKey = string.IsNullOrWhiteSpace(descriptionLocalizationKey)
            ? null
            : descriptionLocalizationKey.Trim();
        IconKey = iconKey.Trim();
        SortOrder = sortOrder;
        SubjectId = string.IsNullOrWhiteSpace(subjectId) ? null : subjectId.Trim();
        Options = options ?? [];
    }

    public string Id { get; }

    public string Category { get; }

    public SettingsScope Scope { get; }

    public string TitleLocalizationKey { get; }

    public string? DescriptionLocalizationKey { get; }

    public string IconKey { get; }

    public int SortOrder { get; }

    public string? SubjectId { get; }

    public IReadOnlyList<SettingsOptionDefinition> Options { get; }
}

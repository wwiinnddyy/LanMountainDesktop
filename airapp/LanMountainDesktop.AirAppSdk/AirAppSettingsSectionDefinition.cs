using System.Collections.Generic;

namespace LanMountainDesktop.AirAppSdk;

public sealed class AirAppSettingsSectionDefinition
{
    public AirAppSettingsSectionDefinition(
        string id,
        string category,
        AirAppSettingsScope scope,
        string titleLocalizationKey,
        string? descriptionLocalizationKey = null,
        string iconKey = "Settings",
        int sortOrder = 0,
        string? subjectId = null,
        IReadOnlyList<AirAppSettingOptionDefinition>? options = null)
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

    public AirAppSettingsScope Scope { get; }

    public string TitleLocalizationKey { get; }

    public string? DescriptionLocalizationKey { get; }

    public string IconKey { get; }

    public int SortOrder { get; }

    public string? SubjectId { get; }

    public IReadOnlyList<AirAppSettingOptionDefinition> Options { get; }
}

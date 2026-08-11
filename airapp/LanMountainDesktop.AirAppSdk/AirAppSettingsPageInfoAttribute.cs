using System;

namespace LanMountainDesktop.AirAppSdk;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AirAppSettingsPageInfoAttribute : Attribute
{
    public AirAppSettingsPageInfoAttribute(
        string id,
        string name,
        AirAppSettingsPageCategory category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id.Trim();
        Name = name.Trim();
        Category = category;
    }

    public string Id { get; }

    public string Name { get; }

    public AirAppSettingsPageCategory Category { get; }

    public string? TitleLocalizationKey { get; init; }

    public string? DescriptionLocalizationKey { get; init; }

    public string IconKey { get; init; } = "Settings";

    public string? SelectedIconKey { get; init; }

    public int SortOrder { get; init; }

    public bool HideDefault { get; init; }

    public bool HidePageTitle { get; init; }

    public bool UseFullWidth { get; init; }

    public string? GroupId { get; init; }

    public AirAppSettingsScope Scope { get; init; } = AirAppSettingsScope.App;
}

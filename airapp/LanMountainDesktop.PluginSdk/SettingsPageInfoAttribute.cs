using System;

namespace LanMountainDesktop.PluginSdk;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SettingsPageInfoAttribute : Attribute
{
    public SettingsPageInfoAttribute(
        string id,
        string name,
        SettingsPageCategory category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id.Trim();
        Name = name.Trim();
        Category = category;
    }

    public string Id { get; }

    public string Name { get; }

    public SettingsPageCategory Category { get; }

    public string? TitleLocalizationKey { get; init; }

    public string? DescriptionLocalizationKey { get; init; }

    public string IconKey { get; init; } = "Settings";

    public string? SelectedIconKey { get; init; }

    public int SortOrder { get; init; }

    public bool HideDefault { get; init; }

    public bool HidePageTitle { get; init; }

    public bool UseFullWidth { get; init; }

    public string? GroupId { get; init; }

    public SettingsScope Scope { get; init; } = SettingsScope.App;
}

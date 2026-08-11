using System;
using System.Collections.Generic;
using System.Linq;
using LanMountainDesktop.AirAppSdk;

namespace LanMountainDesktop.Services.Settings;

internal sealed class SettingsCatalogService : ISettingsCatalog
{
    private readonly List<AirAppSettingsSectionDefinition> _sections = [];
    private readonly object _gate = new();

    public SettingsCatalogService()
    {
        // Built-in host sections for the next settings UI.
        _sections.AddRange(
        [
            new AirAppSettingsSectionDefinition("general", AirAppSettingsCategories.General, AirAppSettingsScope.App, "settings.general.title", iconKey: "Settings", sortOrder: 0),
            new AirAppSettingsSectionDefinition("material-color", AirAppSettingsCategories.Appearance, AirAppSettingsScope.App, "settings.material_color.title", iconKey: "Color", sortOrder: 8),
            new AirAppSettingsSectionDefinition("appearance", AirAppSettingsCategories.Appearance, AirAppSettingsScope.App, "settings.appearance.title", iconKey: "DesignIdeas", sortOrder: 10),
            new AirAppSettingsSectionDefinition("wallpaper", AirAppSettingsCategories.Appearance, AirAppSettingsScope.App, "settings.wallpaper.title", iconKey: "Image", sortOrder: 15),
            new AirAppSettingsSectionDefinition("components", AirAppSettingsCategories.Components, AirAppSettingsScope.ComponentInstance, "settings.components.title", iconKey: "Apps", sortOrder: 20),
            new AirAppSettingsSectionDefinition("plugins", AirAppSettingsCategories.AirApps, AirAppSettingsScope.AirApp, "settings.plugins.title", iconKey: "PuzzlePiece", sortOrder: 30),
            new AirAppSettingsSectionDefinition("about", AirAppSettingsCategories.About, AirAppSettingsScope.App, "settings.about.title", iconKey: "Info", sortOrder: 40)
        ]);
    }

    public IReadOnlyList<AirAppSettingsSectionDefinition> GetSections()
    {
        lock (_gate)
        {
            return _sections
                .OrderBy(section => section.SortOrder)
                .ThenBy(section => section.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public IReadOnlyList<AirAppSettingsSectionDefinition> GetSections(AirAppSettingsScope scope)
    {
        lock (_gate)
        {
            return _sections
                .Where(section => section.Scope == scope)
                .OrderBy(section => section.SortOrder)
                .ThenBy(section => section.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public void RegisterAirAppSections(string pluginId, IReadOnlyList<AirAppSettingsSectionRegistration> sections)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        var normalizedAirAppId = pluginId.Trim();

        lock (_gate)
        {
            _sections.RemoveAll(section =>
                section.Scope == AirAppSettingsScope.AirApp &&
                string.Equals(section.SubjectId, normalizedAirAppId, StringComparison.OrdinalIgnoreCase));

            foreach (var registration in sections)
            {
                var definition = new AirAppSettingsSectionDefinition(
                    id: $"{normalizedAirAppId}:{registration.Id}",
                    category: AirAppSettingsCategories.External,
                    scope: AirAppSettingsScope.AirApp,
                    titleLocalizationKey: registration.TitleLocalizationKey,
                    descriptionLocalizationKey: registration.DescriptionLocalizationKey,
                    iconKey: registration.IconKey,
                    sortOrder: registration.SortOrder,
                    subjectId: normalizedAirAppId,
                    options: registration.Options);
                _sections.Add(definition);
            }
        }
    }

    public void RemoveAirAppSections(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            return;
        }

        lock (_gate)
        {
            _sections.RemoveAll(section =>
                section.Scope == AirAppSettingsScope.AirApp &&
                string.Equals(section.SubjectId, pluginId, StringComparison.OrdinalIgnoreCase));
        }
    }
}

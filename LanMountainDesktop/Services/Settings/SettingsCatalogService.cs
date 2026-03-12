using System;
using System.Collections.Generic;
using System.Linq;
using LanMountainDesktop.PluginSdk;

namespace LanMountainDesktop.Services.Settings;

internal sealed class SettingsCatalogService : ISettingsCatalog
{
    private readonly List<SettingsSectionDefinition> _sections = [];
    private readonly object _gate = new();

    public SettingsCatalogService()
    {
        // Built-in host sections for the next settings UI.
        _sections.AddRange(
        [
            new SettingsSectionDefinition("general", SettingsCategories.General, SettingsScope.App, "settings.general.title", iconKey: "Settings", sortOrder: 0),
            new SettingsSectionDefinition("appearance", SettingsCategories.Appearance, SettingsScope.App, "settings.appearance.title", iconKey: "DesignIdeas", sortOrder: 10),
            new SettingsSectionDefinition("components", SettingsCategories.Components, SettingsScope.ComponentInstance, "settings.components.title", iconKey: "GridDots", sortOrder: 20),
            new SettingsSectionDefinition("plugins", SettingsCategories.Plugins, SettingsScope.Plugin, "settings.plugins.title", iconKey: "PuzzlePiece", sortOrder: 30),
            new SettingsSectionDefinition("plugin-market", SettingsCategories.PluginMarket, SettingsScope.Plugin, "settings.plugin_market.title", iconKey: "Shop", sortOrder: 40),
            new SettingsSectionDefinition("update", SettingsCategories.Update, SettingsScope.App, "settings.update.title", iconKey: "ArrowSync", sortOrder: 50),
            new SettingsSectionDefinition("about", SettingsCategories.About, SettingsScope.App, "settings.about.title", iconKey: "Info", sortOrder: 60),
            new SettingsSectionDefinition("advanced", SettingsCategories.Advanced, SettingsScope.App, "settings.advanced.title", iconKey: "DeveloperBoard", sortOrder: 70)
        ]);
    }

    public IReadOnlyList<SettingsSectionDefinition> GetSections()
    {
        lock (_gate)
        {
            return _sections
                .OrderBy(section => section.SortOrder)
                .ThenBy(section => section.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public IReadOnlyList<SettingsSectionDefinition> GetSections(SettingsScope scope)
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

    public void RegisterPluginSections(string pluginId, IReadOnlyList<PluginSettingsSectionRegistration> sections)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        var normalizedPluginId = pluginId.Trim();

        lock (_gate)
        {
            _sections.RemoveAll(section =>
                section.Scope == SettingsScope.Plugin &&
                string.Equals(section.SubjectId, normalizedPluginId, StringComparison.OrdinalIgnoreCase));

            foreach (var registration in sections)
            {
                var definition = new SettingsSectionDefinition(
                    id: $"{normalizedPluginId}:{registration.Id}",
                    category: SettingsCategories.External,
                    scope: SettingsScope.Plugin,
                    titleLocalizationKey: registration.TitleLocalizationKey,
                    descriptionLocalizationKey: registration.DescriptionLocalizationKey,
                    iconKey: registration.IconKey,
                    sortOrder: registration.SortOrder,
                    subjectId: normalizedPluginId,
                    options: registration.Options);
                _sections.Add(definition);
            }
        }
    }

    public void RemovePluginSections(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            return;
        }

        lock (_gate)
        {
            _sections.RemoveAll(section =>
                section.Scope == SettingsScope.Plugin &&
                string.Equals(section.SubjectId, pluginId, StringComparison.OrdinalIgnoreCase));
        }
    }
}

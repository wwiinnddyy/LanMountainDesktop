using System.Collections.Generic;

namespace LanMountainDesktop.PluginSdk;

public interface ISettingsCatalog
{
    IReadOnlyList<SettingsSectionDefinition> GetSections();

    IReadOnlyList<SettingsSectionDefinition> GetSections(SettingsScope scope);
}

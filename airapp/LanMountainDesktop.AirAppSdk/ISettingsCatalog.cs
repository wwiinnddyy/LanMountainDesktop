using System.Collections.Generic;

namespace LanMountainDesktop.AirAppSdk;

public interface ISettingsCatalog
{
    IReadOnlyList<AirAppSettingsSectionDefinition> GetSections();

    IReadOnlyList<AirAppSettingsSectionDefinition> GetSections(AirAppSettingsScope scope);
}

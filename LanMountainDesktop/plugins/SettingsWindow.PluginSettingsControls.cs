using Avalonia.Controls;

namespace LanMountainDesktop.Views;

public partial class SettingsWindow
{
    internal TextBlock PluginSettingsPanelTitleTextBlock => PluginSettingsPanel.FindControl<TextBlock>("PluginSettingsPanelTitleTextBlock")!;
    internal FluentAvalonia.UI.Controls.SettingsExpander PluginSystemSettingsExpander => PluginSettingsPanel.FindControl<FluentAvalonia.UI.Controls.SettingsExpander>("PluginSystemSettingsExpander")!;
    internal TextBlock PluginSystemDescriptionTextBlock => PluginSettingsPanel.FindControl<TextBlock>("PluginSystemDescriptionTextBlock")!;
    internal TextBlock PluginSystemStatusTextBlock => PluginSettingsPanel.FindControl<TextBlock>("PluginSystemStatusTextBlock")!;
    internal FluentAvalonia.UI.Controls.SettingsExpander InstalledPluginsSettingsExpander => PluginSettingsPanel.FindControl<FluentAvalonia.UI.Controls.SettingsExpander>("InstalledPluginsSettingsExpander")!;
    internal TextBlock PluginRestartHintTextBlock => PluginSettingsPanel.FindControl<TextBlock>("PluginRestartHintTextBlock")!;
    internal TextBlock PluginCatalogEmptyTextBlock => PluginSettingsPanel.FindControl<TextBlock>("PluginCatalogEmptyTextBlock")!;
}

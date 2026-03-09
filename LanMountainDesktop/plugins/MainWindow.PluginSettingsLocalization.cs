namespace LanMountainDesktop.Views;

public partial class MainWindow
{
    private void ApplyPluginSettingsLocalization()
    {
        PluginSettingsPanelTitleTextBlock.Text = L("settings.plugins.title", "Plugins");
        PluginSystemSettingsExpander.Header = L("settings.plugins.runtime_header", "Plugin Runtime");
        PluginSystemSettingsExpander.Description = L(
            "settings.plugins.runtime_desc",
            "Review plugin runtime state and load results.");
        PluginSystemDescriptionTextBlock.Text = L(
            "settings.plugins.runtime_hint",
            "This page shows discovery status, load results, and runtime diagnostics for installed plugins.");
        PluginSystemStatusTextBlock.Text = L(
            "settings.plugins.runtime_status",
            "Plugin runtime status will appear here after plugin discovery completes.");
        InstalledPluginsSettingsExpander.Header = L("settings.plugins.installed_header", "Installed Plugins");
        InstalledPluginsSettingsExpander.Description = L(
            "settings.plugins.installed_desc",
            "Enable or disable plugins here. Detailed plugin settings appear as separate settings pages.");
        PluginRestartHintTextBlock.Text = L(
            "settings.plugins.restart_hint",
            "Plugin enable state changes take effect after restarting the app.");
        PluginCatalogEmptyTextBlock.Text = L("settings.plugins.empty", "No plugins found.");
        PluginSettingsPanel.RefreshFromRuntime();
    }
}

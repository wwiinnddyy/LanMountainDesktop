namespace LanMountainDesktop.PluginIsolation.Contracts;

public sealed record PluginCapabilityDeclaration(
    string Name,
    string Version,
    string? Description = null);

public static class PluginCapabilityNames
{
    public const string Settings = "settings";
    public const string Appearance = "appearance";
    public const string DesktopComponentUi = "ui.desktop-component";
    public const string ComponentEditorUi = "ui.component-editor";
    public const string SettingsPageUi = "ui.settings-page";
    public const string Logging = "diagnostics.log";
    public const string FaultReporting = "diagnostics.fault";
}

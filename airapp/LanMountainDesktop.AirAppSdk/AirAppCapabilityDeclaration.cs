namespace LanMountainDesktop.AirAppIsolation.Contracts;

public sealed record AirAppCapabilityDeclaration(
    string Name,
    string Version,
    string? Description = null);

public static class AirAppCapabilityNames
{
    public const string Settings = "settings";
    public const string Appearance = "appearance";
    public const string DesktopComponentUi = "ui.desktop-component";
    public const string ComponentEditorUi = "ui.component-editor";
    public const string SettingsPageUi = "ui.settings-page";
    public const string Logging = "diagnostics.log";
    public const string FaultReporting = "diagnostics.fault";
}

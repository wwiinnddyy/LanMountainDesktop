using LanMountainDesktop.PluginSdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LanMountainDesktop.PluginTemplate;

[PluginEntrance]
public sealed class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        _ = context;

        // ── Option 1: Declarative settings (simple key-value options) ──────────
        // The host generates a settings page automatically from the declared options.
        // Supported option types: Toggle, Text, Number, Select, Path, List.
        //
        // services.AddPluginSettingsSection(
        //     "my-plugin-settings",
        //     "My Plugin Settings",
        //     section => section
        //         .AddToggle("enable_feature", "Enable Feature", defaultValue: true)
        //         .AddNumber("refresh_interval", "Refresh Interval", defaultValue: 30, minimum: 5, maximum: 120),
        //     iconKey: "PuzzlePiece");

        // ── Option 2: Custom AXAML view (full Fluent Avalonia controls) ────────
        // Provide a SettingsPageBase subclass to use any Fluent Avalonia control
        // (SettingsExpander, ColorPicker, Slider, etc.) — just like built-in pages.
        //
        // services.AddPluginSettingsSection<MyCustomSettingsPage>(
        //     "my-plugin-settings",
        //     "My Plugin Settings",
        //     iconKey: "PuzzlePiece");
        //
        // Or mix both: declare options AND set a custom view on the builder:
        //
        // services.AddPluginSettingsSection(
        //     "my-plugin-settings",
        //     "My Plugin Settings",
        //     section => section
        //         .SetCustomView<MyCustomSettingsPage>()
        //         .AddToggle("enable_feature", "Enable Feature"),
        //     iconKey: "PuzzlePiece");

        _ = services;
    }
}

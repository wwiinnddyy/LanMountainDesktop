using Avalonia;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Settings.Core;
using LanMountainDesktop.Shared.Contracts;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class CornerRadiusStyleTests
{
    [Theory]
    [InlineData("Sharp", "Sharp")]
    [InlineData("Balanced", "Balanced")]
    [InlineData("Rounded", "Rounded")]
    [InlineData("Open", "Open")]
    [InlineData("Unknown", "Balanced")]
    [InlineData(null, "Balanced")]
    public void NormalizeCornerRadiusStyle_ReturnsValidStyleOrDefault(string? input, string expected)
    {
        Assert.Equal(expected, GlobalAppearanceSettings.NormalizeCornerRadiusStyle(input));
    }

    [Fact]
    public void PluginAppearanceContext_ResolveCornerRadius_ReturnsFixedTokenValues()
    {
        var context = new PluginAppearanceContext(new PluginAppearanceSnapshot(
            CornerRadiusTokens: new PluginCornerRadiusTokens(
                Micro: 6d,
                Xs: 12d,
                Sm: 14d,
                Md: 20d,
                Lg: 28d,
                Xl: 32d,
                Island: 36d,
                Component: 24d),
            ThemeVariant: "Light"));

        // Preset resolution should return fixed values from tokens regardless of any legacy scale
        Assert.Equal(20d, context.ResolveCornerRadius(PluginCornerRadiusPreset.Md), 3);
        Assert.Equal(20d, context.ResolveCornerRadius(PluginCornerRadiusPreset.Md, maximum: 15d), 3);
        Assert.Equal(20d, context.ResolveScaledCornerRadius(18d), 3);
        Assert.Equal(24d, context.ResolveCornerRadius(PluginCornerRadiusPreset.Component), 3);
    }

    [Fact]
    public void PluginDesktopComponentContext_ProvidesDirectTokenAccess()
    {
        var appearanceContext = new PluginAppearanceContext(new PluginAppearanceSnapshot(
            CornerRadiusTokens: new PluginCornerRadiusTokens(6, 12, 14, 20, 28, 32, 36, 24),
            ThemeVariant: "Dark"));

        var context = new PluginDesktopComponentContext(
            new PluginManifest("plugin.id", "Plugin Name", "plugin.dll"),
            "C:\\Plugins\\plugin.id",
            "C:\\Data\\plugin.id",
            new NullServiceProvider(),
            new Dictionary<string, object?>(),
            "component-1",
            null,
            96d,
            appearanceContext);

        Assert.Equal(24d, context.ResolveScaledCornerRadius(12d), 3);
        Assert.Equal(24d, context.ResolveScaledCornerRadius(12d, 8d, 18d), 3);
    }

    private sealed class NullServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}

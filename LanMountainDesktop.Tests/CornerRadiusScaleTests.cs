using Avalonia;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Settings.Core;
using LanMountainDesktop.Shared.Contracts;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class CornerRadiusScaleTests
{
    [Theory]
    [InlineData(-1d, 0d)]
    [InlineData(0d, 0d)]
    [InlineData(0.33d, 0.33d)]
    [InlineData(1.234d, 1.234d)]
    [InlineData(2.5d, 2.5d)]
    [InlineData(3d, 2.5d)]
    public void NormalizeCornerRadiusScale_ClampsWithoutSnapping(double input, double expected)
    {
        Assert.Equal(expected, GlobalAppearanceSettings.NormalizeCornerRadiusScale(input), 3);
    }

    [Fact]
    public void NormalizeCornerRadiusScale_UsesDefaultForInvalidValues()
    {
        Assert.Equal(
            GlobalAppearanceSettings.DefaultCornerRadiusScale,
            GlobalAppearanceSettings.NormalizeCornerRadiusScale(double.NaN),
            3);
        Assert.Equal(
            GlobalAppearanceSettings.DefaultCornerRadiusScale,
            GlobalAppearanceSettings.NormalizeCornerRadiusScale(double.PositiveInfinity),
            3);
    }

    [Fact]
    public void PluginDesktopComponentContext_AllowsZeroRadiusScaling()
    {
        var appearanceContext = new PluginAppearanceContext(new PluginAppearanceSnapshot(
            GlobalCornerRadiusScale: 0d,
            CornerRadiusTokens: PluginCornerRadiusTokens.FromShared(new AppearanceCornerRadiusTokens(
                new CornerRadius(6),
                new CornerRadius(12),
                new CornerRadius(14),
                new CornerRadius(20),
                new CornerRadius(28),
                new CornerRadius(32),
                new CornerRadius(36),
                new CornerRadius(8))),
            ThemeVariant: "Unknown"));

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

        Assert.Equal(0d, context.GlobalCornerRadiusScale, 3);
        Assert.Equal(0d, context.ResolveScaledCornerRadius(12d), 3);
        Assert.Equal(0d, context.ResolveScaledCornerRadius(12d, 8d, 18d), 3);
    }

    [Fact]
    public void PluginAppearanceContext_ResolveCornerRadius_DoesNotDoubleScalePresetTokens()
    {
        var context = new PluginAppearanceContext(new PluginAppearanceSnapshot(
            GlobalCornerRadiusScale: 2d,
            CornerRadiusTokens: new PluginCornerRadiusTokens(
                Micro: 12d,
                Xs: 20d,
                Sm: 28d,
                Md: 36d,
                Lg: 48d,
                Xl: 60d,
                Island: 72d,
                Component: 16d),
            ThemeVariant: "Light"));

        Assert.Equal(36d, context.ResolveCornerRadius(PluginCornerRadiusPreset.Md), 3);
        Assert.Equal(36d, context.ResolveCornerRadius(PluginCornerRadiusPreset.Md, maximum: 40d), 3);
        Assert.Equal(36d, context.ResolveScaledCornerRadius(18d), 3);
    }

    private sealed class NullServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}

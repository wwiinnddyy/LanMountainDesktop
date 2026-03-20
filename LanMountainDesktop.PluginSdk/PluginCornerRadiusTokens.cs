using Avalonia;
using LanMountainDesktop.Shared.Contracts;

namespace LanMountainDesktop.PluginSdk;

public sealed record PluginCornerRadiusTokens(
    double Micro,
    double Xs,
    double Sm,
    double Md,
    double Lg,
    double Xl,
    double Island)
{
    public double Get(PluginCornerRadiusPreset preset)
    {
        return preset switch
        {
            PluginCornerRadiusPreset.Default => Md,
            PluginCornerRadiusPreset.Micro => Micro,
            PluginCornerRadiusPreset.Xs => Xs,
            PluginCornerRadiusPreset.Sm => Sm,
            PluginCornerRadiusPreset.Md => Md,
            PluginCornerRadiusPreset.Lg => Lg,
            PluginCornerRadiusPreset.Xl => Xl,
            PluginCornerRadiusPreset.Island => Island,
            _ => Md
        };
    }

    public CornerRadius ToCornerRadius(PluginCornerRadiusPreset preset)
    {
        return new CornerRadius(Get(preset));
    }

    public static PluginCornerRadiusTokens FromShared(AppearanceCornerRadiusTokens tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        return new PluginCornerRadiusTokens(
            tokens.Micro.TopLeft,
            tokens.Xs.TopLeft,
            tokens.Sm.TopLeft,
            tokens.Md.TopLeft,
            tokens.Lg.TopLeft,
            tokens.Xl.TopLeft,
            tokens.Island.TopLeft);
    }
}

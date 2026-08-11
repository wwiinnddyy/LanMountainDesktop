using Avalonia;
using LanMountainDesktop.Shared.Contracts;

namespace LanMountainDesktop.AirAppSdk;

public sealed record AirAppCornerRadiusTokens(
    double Micro,
    double Xs,
    double Sm,
    double Md,
    double Lg,
    double Xl,
    double Island,
    double Component)
{
    public double Get(AirAppCornerRadiusPreset preset)
    {
        return preset switch
        {
            AirAppCornerRadiusPreset.Default => Component,
            AirAppCornerRadiusPreset.Micro => Micro,
            AirAppCornerRadiusPreset.Xs => Xs,
            AirAppCornerRadiusPreset.Sm => Sm,
            AirAppCornerRadiusPreset.Md => Md,
            AirAppCornerRadiusPreset.Lg => Lg,
            AirAppCornerRadiusPreset.Xl => Xl,
            AirAppCornerRadiusPreset.Island => Island,
            AirAppCornerRadiusPreset.Component => Component,
            _ => Component
        };
    }

    public CornerRadius ToCornerRadius(AirAppCornerRadiusPreset preset)
    {
        return new CornerRadius(Get(preset));
    }

    public static AirAppCornerRadiusTokens FromShared(AppearanceCornerRadiusTokens tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        return new AirAppCornerRadiusTokens(
            tokens.Micro.TopLeft,
            tokens.Xs.TopLeft,
            tokens.Sm.TopLeft,
            tokens.Md.TopLeft,
            tokens.Lg.TopLeft,
            tokens.Xl.TopLeft,
            tokens.Island.TopLeft,
            tokens.Component.TopLeft);
    }
}

using System;
using Avalonia.Controls;
using Avalonia.Platform;

namespace LanMountainDesktop.Services;

public enum AppLogoVariant
{
    Auto = 0,
    Day = 1,
    Night = 2
}

public interface IAppLogoService
{
    WindowIcon CreateWindowIcon(AppLogoVariant variant = AppLogoVariant.Auto);
    WindowIcon CreateTrayIcon(AppLogoVariant variant = AppLogoVariant.Auto);
    Uri GetVectorLogoUri(AppLogoVariant variant = AppLogoVariant.Auto);
}

internal sealed class AppLogoService : IAppLogoService
{
    private static readonly Uri NightVectorLogoUri = new("avares://LanMountainDesktop/Assets/logo_nightly.svg");
    private static readonly Uri DayVectorLogoUri = new("avares://LanMountainDesktop/Assets/logo_nightly.svg");
    private static readonly Uri NightIconUri = new("avares://LanMountainDesktop/Assets/logo_nightly.ico");
    private static readonly Uri DayIconUri = new("avares://LanMountainDesktop/Assets/logo_nightly.ico");

    public WindowIcon CreateWindowIcon(AppLogoVariant variant = AppLogoVariant.Auto) => CreateIcon(ResolveIconUri(variant));

    public WindowIcon CreateTrayIcon(AppLogoVariant variant = AppLogoVariant.Auto) => CreateIcon(ResolveIconUri(variant));

    public Uri GetVectorLogoUri(AppLogoVariant variant = AppLogoVariant.Auto) => ResolveVectorLogoUri(variant);

    private static WindowIcon CreateIcon(Uri assetUri)
    {
        using var stream = AssetLoader.Open(assetUri);
        return new WindowIcon(stream);
    }

    private static Uri ResolveIconUri(AppLogoVariant variant) => ResolveVariant(variant) switch
    {
        AppLogoVariant.Day => DayIconUri,
        _ => NightIconUri
    };

    private static Uri ResolveVectorLogoUri(AppLogoVariant variant) => ResolveVariant(variant) switch
    {
        AppLogoVariant.Day => DayVectorLogoUri,
        _ => NightVectorLogoUri
    };

    private static AppLogoVariant ResolveVariant(AppLogoVariant variant) => variant switch
    {
        AppLogoVariant.Day => AppLogoVariant.Day,
        AppLogoVariant.Night => AppLogoVariant.Night,
        _ => AppLogoVariant.Night
    };
}

internal static class HostAppLogoProvider
{
    private static readonly object Gate = new();
    private static IAppLogoService? _instance;

    public static IAppLogoService GetOrCreate()
    {
        lock (Gate)
        {
            return _instance ??= new AppLogoService();
        }
    }
}

using System;
using System.Collections.Concurrent;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace LanMontainDesktop.Views.Components;

internal static class HyperOS3WeatherAssetLoader
{
    private static readonly ConcurrentDictionary<string, IImage?> ImageCache = new(StringComparer.OrdinalIgnoreCase);

    public static IImage? LoadImage(string? uriText)
    {
        if (string.IsNullOrWhiteSpace(uriText))
        {
            return null;
        }

        return ImageCache.GetOrAdd(uriText, static key =>
        {
            try
            {
                var uri = new Uri(key, UriKind.Absolute);
                using var stream = AssetLoader.Open(uri);
                return new Bitmap(stream);
            }
            catch
            {
                return null;
            }
        });
    }
}

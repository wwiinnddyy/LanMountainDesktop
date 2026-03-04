using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using LanMountainDesktop.Models;
using Microsoft.Win32;

namespace LanMountainDesktop.Services;

public sealed class MonetColorService
{
    public MonetPalette BuildPalette(Bitmap? wallpaper, bool nightMode)
    {
        var recommended = BuildRecommendedPalette(nightMode);
        var seed = TryExtractSeedColor(wallpaper) ?? TryGetSystemMonetSeedColor() ?? Color.Parse("#FF3B82F6");
        var monet = BuildMonetPalette(seed, nightMode);
        return new MonetPalette(recommended, monet);
    }

    private static IReadOnlyList<Color> BuildRecommendedPalette(bool nightMode)
    {
        if (nightMode)
        {
            return
            [
                Color.Parse("#FF3B82F6"),
                Color.Parse("#FF22C55E"),
                Color.Parse("#FFF59E0B"),
                Color.Parse("#FFF97316"),
                Color.Parse("#FFA855F7"),
                Color.Parse("#FFEF4444")
            ];
        }

        return
        [
            Color.Parse("#FF1D4ED8"),
            Color.Parse("#FF15803D"),
            Color.Parse("#FFB45309"),
            Color.Parse("#FFC2410C"),
            Color.Parse("#FF7E22CE"),
            Color.Parse("#FFB91C1C")
        ];
    }

    private static IReadOnlyList<Color> BuildMonetPalette(Color seed, bool nightMode)
    {
        var (hue, saturation, value) = ToHsv(seed);
        var valueBase = nightMode ? Math.Max(0.70, value) : Math.Min(0.72, Math.Max(0.35, value));
        var saturationBase = Math.Clamp(saturation, 0.22, 0.74);
        var offsets = new[] { 0d, 16d, -16d, 36d, -36d, 180d };
        var palette = new List<Color>(offsets.Length);

        for (var i = 0; i < offsets.Length; i++)
        {
            var hueShift = NormalizeHue(hue + offsets[i]);
            var sat = Math.Clamp(saturationBase + ((i % 2 == 0) ? 0.05 : -0.05), 0.18, 0.86);
            var val = Math.Clamp(valueBase + ((i < 3) ? 0.06 : -0.04), 0.32, 0.92);
            palette.Add(FromHsv(hueShift, sat, val));
        }

        return palette;
    }

    private static Color? TryExtractSeedColor(Bitmap? wallpaper)
    {
        if (wallpaper is null)
        {
            return null;
        }

        try
        {
            var sampleWidth = Math.Clamp(wallpaper.PixelSize.Width, 1, 48);
            var sampleHeight = Math.Clamp(wallpaper.PixelSize.Height, 1, 48);

            using var scaledBitmap = wallpaper.CreateScaledBitmap(
                new PixelSize(sampleWidth, sampleHeight),
                BitmapInterpolationMode.MediumQuality);
            using var writeable = new WriteableBitmap(
                scaledBitmap.PixelSize,
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);
            using var framebuffer = writeable.Lock();
            scaledBitmap.CopyPixels(framebuffer, AlphaFormat.Premul);

            var byteCount = framebuffer.RowBytes * framebuffer.Size.Height;
            if (byteCount <= 0 || framebuffer.Address == IntPtr.Zero)
            {
                return null;
            }

            var pixelBuffer = new byte[byteCount];
            Marshal.Copy(framebuffer.Address, pixelBuffer, 0, byteCount);

            double bestScore = double.MinValue;
            Color? bestColor = null;

            for (var y = 0; y < framebuffer.Size.Height; y++)
            {
                var rowOffset = y * framebuffer.RowBytes;
                for (var x = 0; x < framebuffer.Size.Width; x++)
                {
                    var index = rowOffset + (x * 4);
                    var alpha = pixelBuffer[index + 3] / 255d;
                    if (alpha <= 0.15)
                    {
                        continue;
                    }

                    var blue = (pixelBuffer[index] / 255d) / alpha;
                    var green = (pixelBuffer[index + 1] / 255d) / alpha;
                    var red = (pixelBuffer[index + 2] / 255d) / alpha;
                    red = Math.Clamp(red, 0, 1);
                    green = Math.Clamp(green, 0, 1);
                    blue = Math.Clamp(blue, 0, 1);

                    var color = Color.FromRgb(
                        (byte)Math.Round(red * 255),
                        (byte)Math.Round(green * 255),
                        (byte)Math.Round(blue * 255));
                    var (_, saturation, value) = ToHsv(color);
                    var score = (saturation * 1.8) + (value * 0.6);
                    if (score <= bestScore)
                    {
                        continue;
                    }

                    bestScore = score;
                    bestColor = color;
                }
            }

            return bestColor;
        }
        catch
        {
            return null;
        }
    }

    private static Color? TryGetSystemMonetSeedColor()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM",
                "AccentColor",
                null);
            if (value is not int accentDword)
            {
                return null;
            }

            var bytes = BitConverter.GetBytes(accentDword);
            var blue = bytes[0];
            var green = bytes[1];
            var red = bytes[2];
            return Color.FromRgb(red, green, blue);
        }
        catch
        {
            return null;
        }
    }

    private static (double Hue, double Saturation, double Value) ToHsv(Color color)
    {
        var red = color.R / 255d;
        var green = color.G / 255d;
        var blue = color.B / 255d;
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var delta = max - min;

        double hue;
        if (delta < 0.0001)
        {
            hue = 0;
        }
        else if (Math.Abs(max - red) < 0.0001)
        {
            hue = 60 * (((green - blue) / delta) % 6);
        }
        else if (Math.Abs(max - green) < 0.0001)
        {
            hue = 60 * (((blue - red) / delta) + 2);
        }
        else
        {
            hue = 60 * (((red - green) / delta) + 4);
        }

        hue = NormalizeHue(hue);
        var saturation = max <= 0.0001 ? 0 : delta / max;
        return (hue, saturation, max);
    }

    private static Color FromHsv(double hue, double saturation, double value)
    {
        hue = NormalizeHue(hue);
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);

        if (saturation <= 0.0001)
        {
            var gray = (byte)Math.Round(value * 255);
            return Color.FromRgb(gray, gray, gray);
        }

        var chroma = value * saturation;
        var x = chroma * (1 - Math.Abs(((hue / 60d) % 2) - 1));
        var m = value - chroma;

        (double r, double g, double b) = hue switch
        {
            >= 0 and < 60 => (chroma, x, 0d),
            >= 60 and < 120 => (x, chroma, 0d),
            >= 120 and < 180 => (0d, chroma, x),
            >= 180 and < 240 => (0d, x, chroma),
            >= 240 and < 300 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };

        var red = (byte)Math.Round((r + m) * 255);
        var green = (byte)Math.Round((g + m) * 255);
        var blue = (byte)Math.Round((b + m) * 255);
        return Color.FromRgb(red, green, blue);
    }

    private static double NormalizeHue(double hue)
    {
        hue %= 360;
        if (hue < 0)
        {
            hue += 360;
        }

        return hue;
    }
}

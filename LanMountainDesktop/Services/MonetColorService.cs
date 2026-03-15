using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using LanMountainDesktop.Models;
using MaterialColorUtilities.Palettes;
using MaterialColorUtilities.Utils;
using Microsoft.Win32;

namespace LanMountainDesktop.Services;

public sealed class MonetColorService
{
    private static readonly Color DefaultSeedColor = Color.Parse("#FF3B82F6");

    public MonetPalette BuildPalette(Bitmap? wallpaper, bool nightMode, Color? preferredSeed = null)
    {
        var wallpaperCandidates = wallpaper is null
            ? []
            : ExtractSeedCandidates(wallpaper);
        return BuildPaletteCore(wallpaperCandidates, nightMode, preferredSeed);
    }

    public MonetPalette BuildPaletteFromSeedCandidates(
        IReadOnlyList<Color>? seedCandidates,
        bool nightMode,
        Color? preferredSeed = null)
    {
        return BuildPaletteCore(seedCandidates ?? [], nightMode, preferredSeed);
    }

    public IReadOnlyList<Color> ExtractSeedCandidates(Bitmap wallpaper)
    {
        ArgumentNullException.ThrowIfNull(wallpaper);
        return ExtractWallpaperSeedCandidates(wallpaper);
    }

    private static Color? ResolveSeedColor(
        IReadOnlyList<Color> wallpaperCandidates,
        Color? preferredSeed)
    {
        if (wallpaperCandidates.Count == 0)
        {
            return null;
        }

        if (preferredSeed is { } explicitSeed)
        {
            var exact = wallpaperCandidates.FirstOrDefault(candidate => candidate == explicitSeed);
            if (exact != default)
            {
                return exact;
            }
        }

        return wallpaperCandidates[0];
    }

    private static IReadOnlyList<Color> BuildFallbackSeedCandidates()
    {
        return
        [
            Color.Parse("#FF3B82F6"),
            Color.Parse("#FF22C55E"),
            Color.Parse("#FFF59E0B"),
            Color.Parse("#FFF97316"),
            Color.Parse("#FFA855F7")
        ];
    }

    private static IReadOnlyList<Color> ExtractWallpaperSeedCandidates(Bitmap wallpaper)
    {
        try
        {
            var width = Math.Clamp(wallpaper.PixelSize.Width, 1, 96);
            var height = Math.Clamp(wallpaper.PixelSize.Height, 1, 96);

            using var scaledBitmap = wallpaper.CreateScaledBitmap(
                new PixelSize(width, height),
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
                return [];
            }

            var pixelBuffer = new byte[byteCount];
            Marshal.Copy(framebuffer.Address, pixelBuffer, 0, byteCount);

            var argbPixels = new List<uint>(framebuffer.Size.Width * framebuffer.Size.Height);
            for (var y = 0; y < framebuffer.Size.Height; y++)
            {
                var rowOffset = y * framebuffer.RowBytes;
                for (var x = 0; x < framebuffer.Size.Width; x++)
                {
                    var index = rowOffset + (x * 4);
                    var alpha = pixelBuffer[index + 3];
                    if (alpha <= 32)
                    {
                        continue;
                    }

                    var blue = pixelBuffer[index];
                    var green = pixelBuffer[index + 1];
                    var red = pixelBuffer[index + 2];
                    argbPixels.Add(
                        ((uint)alpha << 24) |
                        ((uint)red << 16) |
                        ((uint)green << 8) |
                        blue);
                }
            }

            if (argbPixels.Count == 0)
            {
                return [];
            }

            var extracted = ImageUtils.ColorsFromImage(argbPixels.ToArray());
            return extracted
                .Select(FromArgb)
                .Distinct()
                .Take(6)
                .ToArray();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Appearance.WallpaperPalette", "Failed to extract wallpaper seed candidates.", ex);
            return [];
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

            var accentColor = unchecked((uint)accentDword);
            var a = (byte)((accentColor >> 24) & 0xFF);
            var b = (byte)((accentColor >> 16) & 0xFF);
            var g = (byte)((accentColor >> 8) & 0xFF);
            var r = (byte)(accentColor & 0xFF);
            if (a == 0)
            {
                a = 0xFF;
            }

            return Color.FromArgb(a, r, g, b);
        }
        catch
        {
            return null;
        }
    }

    private static uint ToArgb(Color color)
    {
        return
            ((uint)color.A << 24) |
            ((uint)color.R << 16) |
            ((uint)color.G << 8) |
            color.B;
    }

    private static Color FromArgb(uint argb)
    {
        var a = (byte)((argb >> 24) & 0xFF);
        var r = (byte)((argb >> 16) & 0xFF);
        var g = (byte)((argb >> 8) & 0xFF);
        var b = (byte)(argb & 0xFF);
        return Color.FromArgb(a, r, g, b);
    }

    private static MonetPalette BuildPaletteCore(
        IReadOnlyList<Color> wallpaperCandidates,
        bool nightMode,
        Color? preferredSeed)
    {
        var recommendedColors = wallpaperCandidates.Count > 0
            ? wallpaperCandidates
            : BuildFallbackSeedCandidates();
        var seed = ResolveSeedColor(wallpaperCandidates, preferredSeed)
            ?? preferredSeed
            ?? TryGetSystemMonetSeedColor()
            ?? DefaultSeedColor;

        var corePalette = CorePalette.Of(ToArgb(seed), Style.TonalSpot);
        var primary = FromArgb(corePalette.Primary.Tone(nightMode ? 80u : 40u));
        var secondary = FromArgb(corePalette.Secondary.Tone(nightMode ? 80u : 40u));
        var tertiary = FromArgb(corePalette.Tertiary.Tone(nightMode ? 80u : 40u));
        var neutral = FromArgb(corePalette.Neutral.Tone(nightMode ? 20u : 94u));
        var neutralVariant = FromArgb(corePalette.NeutralVariant.Tone(nightMode ? 30u : 90u));

        return new MonetPalette(
            recommendedColors,
            seed,
            primary,
            secondary,
            tertiary,
            neutral,
            neutralVariant);
    }
}

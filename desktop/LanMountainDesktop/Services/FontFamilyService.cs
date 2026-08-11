using System;
using Avalonia.Media;

namespace LanMountainDesktop.Services;

public sealed class FontFamilyService
{
    private const string FontsBasePath = "avares://LanMountainDesktop/Assets/Fonts";

    public static readonly FontFamily DefaultFontFamily =
        new($"{FontsBasePath}#MiSans");

    public static readonly FontFamily JapaneseFontFamily =
        new($"{FontsBasePath}#MiSans");

    public static readonly FontFamily KoreanFontFamily =
        new($"Malgun Gothic, {FontsBasePath}#MiSans");

    public FontFamily GetFontFamilyForLanguage(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return DefaultFontFamily;
        }

        return languageCode.ToLowerInvariant() switch
        {
            "ja-jp" or "ja" => JapaneseFontFamily,
            "ko-kr" or "ko" => KoreanFontFamily,
            _ => DefaultFontFamily
        };
    }

    public string GetFontFamilyResourceKey(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return "AppFontFamily";
        }

        return languageCode.ToLowerInvariant() switch
        {
            "ja-jp" or "ja" => "AppFontFamilyJP",
            "ko-kr" or "ko" => "AppFontFamilyKR",
            _ => "AppFontFamily"
        };
    }
}

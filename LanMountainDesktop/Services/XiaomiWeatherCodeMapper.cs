using System;
using System.Collections.Generic;

namespace LanMountainDesktop.Services;

internal enum WeatherConditionBucket
{
    Unknown,
    Clear,
    PartlyCloudy,
    Cloudy,
    Haze,
    Fog,
    RainLight,
    RainHeavy,
    Storm,
    Sleet,
    Snow
}

internal static class XiaomiWeatherCodeMapper
{
    private readonly record struct WeatherCodeEntry(string Zh, string En, WeatherConditionBucket Bucket);

    private static readonly IReadOnlyDictionary<int, WeatherCodeEntry> Entries = new Dictionary<int, WeatherCodeEntry>
    {
        [0] = new("\u6674", "Clear", WeatherConditionBucket.Clear),
        [1] = new("\u591a\u4e91", "Partly Cloudy", WeatherConditionBucket.PartlyCloudy),
        [2] = new("\u9634", "Cloudy", WeatherConditionBucket.Cloudy),
        [3] = new("\u9635\u96e8", "Shower", WeatherConditionBucket.RainLight),
        [4] = new("\u96f7\u9635\u96e8", "Thunder Shower", WeatherConditionBucket.Storm),
        [5] = new("\u96f7\u9635\u96e8\u4f34\u6709\u51b0\u96f9", "Thunder Shower with Hail", WeatherConditionBucket.Storm),
        [6] = new("\u96e8\u5939\u96ea", "Sleet", WeatherConditionBucket.Sleet),
        [7] = new("\u5c0f\u96e8", "Light Rain", WeatherConditionBucket.RainLight),
        [8] = new("\u4e2d\u96e8", "Moderate Rain", WeatherConditionBucket.RainHeavy),
        [9] = new("\u5927\u96e8", "Heavy Rain", WeatherConditionBucket.RainHeavy),
        [10] = new("\u66b4\u96e8", "Storm", WeatherConditionBucket.RainHeavy),
        [11] = new("\u5927\u66b4\u96e8", "Heavy Storm", WeatherConditionBucket.RainHeavy),
        [12] = new("\u7279\u5927\u66b4\u96e8", "Severe Storm", WeatherConditionBucket.RainHeavy),
        [13] = new("\u9635\u96ea", "Snow Flurry", WeatherConditionBucket.Snow),
        [14] = new("\u5c0f\u96ea", "Light Snow", WeatherConditionBucket.Snow),
        [15] = new("\u4e2d\u96ea", "Moderate Snow", WeatherConditionBucket.Snow),
        [16] = new("\u5927\u96ea", "Heavy Snow", WeatherConditionBucket.Snow),
        [17] = new("\u66b4\u96ea", "Snowstorm", WeatherConditionBucket.Snow),
        [18] = new("\u96fe", "Fog", WeatherConditionBucket.Fog),
        [19] = new("\u51bb\u96e8", "Freezing Rain", WeatherConditionBucket.RainLight),
        [20] = new("\u6c99\u5c18\u66b4", "Duststorm", WeatherConditionBucket.Haze),
        [21] = new("\u5c0f\u5230\u4e2d\u96e8", "Light to Moderate Rain", WeatherConditionBucket.RainLight),
        [22] = new("\u4e2d\u5230\u5927\u96e8", "Moderate to Heavy Rain", WeatherConditionBucket.RainHeavy),
        [23] = new("\u5927\u5230\u66b4\u96e8", "Heavy Rain to Storm", WeatherConditionBucket.RainHeavy),
        [24] = new("\u66b4\u96e8\u5230\u5927\u66b4\u96e8", "Storm to Heavy Storm", WeatherConditionBucket.RainHeavy),
        [25] = new("\u5927\u66b4\u96e8\u5230\u7279\u5927\u66b4\u96e8", "Heavy to Severe Storm", WeatherConditionBucket.RainHeavy),
        [26] = new("\u5c0f\u5230\u4e2d\u96ea", "Light to Moderate Snow", WeatherConditionBucket.Snow),
        [27] = new("\u4e2d\u5230\u5927\u96ea", "Moderate to Heavy Snow", WeatherConditionBucket.Snow),
        [28] = new("\u5927\u5230\u66b4\u96ea", "Heavy Snow to Snowstorm", WeatherConditionBucket.Snow),
        [29] = new("\u6d6e\u5c18", "Dust", WeatherConditionBucket.Haze),
        [30] = new("\u626c\u6c99", "Sand", WeatherConditionBucket.Haze),
        [31] = new("\u5f3a\u6c99\u5c18\u66b4", "Sandstorm", WeatherConditionBucket.Haze),
        [32] = new("\u6d53\u96fe", "Dense Fog", WeatherConditionBucket.Fog),
        [49] = new("\u5f3a\u6d53\u96fe", "Strong Fog", WeatherConditionBucket.Fog),
        [53] = new("\u973e", "Haze", WeatherConditionBucket.Haze),
        [54] = new("\u4e2d\u5ea6\u973e", "Moderate Haze", WeatherConditionBucket.Haze),
        [55] = new("\u91cd\u5ea6\u973e", "Heavy Haze", WeatherConditionBucket.Haze),
        [56] = new("\u4e25\u91cd\u973e", "Severe Haze", WeatherConditionBucket.Haze),
        [57] = new("\u5927\u96fe", "Heavy Fog", WeatherConditionBucket.Fog),
        [58] = new("\u7279\u5f3a\u6d53\u96fe", "Extra Heavy Fog", WeatherConditionBucket.Fog),
        [301] = new("\u96e8", "Rain", WeatherConditionBucket.RainLight),
        [302] = new("\u96ea", "Snow", WeatherConditionBucket.Snow)
    };

    public static WeatherConditionBucket ResolveBucket(int? code)
    {
        if (!code.HasValue)
        {
            return WeatherConditionBucket.Unknown;
        }

        return Entries.TryGetValue(code.Value, out var entry)
            ? entry.Bucket
            : WeatherConditionBucket.Unknown;
    }

    public static string? ResolveDisplayText(int? code, string locale)
    {
        if (!code.HasValue)
        {
            return null;
        }

        if (Entries.TryGetValue(code.Value, out var entry))
        {
            return locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                ? entry.Zh
                : entry.En;
        }

        return locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? $"\u5929\u6c14\u7801 {code.Value}"
            : $"Weather {code.Value}";
    }
}

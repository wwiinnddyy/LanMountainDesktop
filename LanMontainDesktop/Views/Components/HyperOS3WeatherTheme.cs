using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FluentIcons.Common;
using LanMontainDesktop.Models;

namespace LanMontainDesktop.Views.Components;

public enum HyperOS3WeatherVisualKind
{
    ClearDay,
    ClearNight,
    CloudyDay,
    CloudyNight,
    RainLight,
    RainHeavy,
    Storm,
    Snow,
    Fog
}

public enum HyperOS3WeatherWidgetKind
{
    Realtime2x2,
    Hourly4x2,
    MultiDay4x2,
    WeatherClock2x1,
    Extended4x4
}

public readonly record struct HyperOS3WeatherPalette(
    string GradientFrom,
    string GradientTo,
    string Tint,
    string PrimaryText,
    string SecondaryText,
    string TertiaryText,
    string ParticleColor);

public readonly record struct HyperOS3WeatherMotion(
    double DriftX,
    double DriftY,
    double ZoomBase,
    double ZoomAmplitude,
    double MotionOpacityBase,
    double MotionOpacityPulse,
    double LightOpacityBase,
    double LightOpacityPulse,
    double ShadeOpacityBase,
    double ShadeOpacityPulse,
    double PhaseStep,
    int ParticleCount,
    double ParticleSpeedMin,
    double ParticleSpeedMax,
    double ParticleLengthMin,
    double ParticleLengthMax,
    double ParticleDriftPerTick);

public readonly record struct HyperOS3WeatherMetrics(
    double CornerRadiusScale,
    double HorizontalPaddingScale,
    double VerticalPaddingScale,
    double PrimaryTemperatureFont,
    double PrimaryTextFont,
    double SecondaryTextFont,
    double CaptionFont,
    double IconFont,
    double MainGap,
    double SectionGap);

public static class HyperOS3WeatherTheme
{
    private static readonly HyperOS3WeatherPalette FallbackPalette = new(
        GradientFrom: "#7187A8",
        GradientTo: "#92A5C2",
        Tint: "#3C4E66",
        PrimaryText: "#FFFFFFFF",
        SecondaryText: "#E4ECF7",
        TertiaryText: "#C9D4E4",
        ParticleColor: "#66EAF2FF");

    private static readonly HyperOS3WeatherMotion FallbackMotion = new(
        DriftX: 8.0, DriftY: 6.0, ZoomBase: 1.050, ZoomAmplitude: 0.010,
        MotionOpacityBase: 0.28, MotionOpacityPulse: 0.05,
        LightOpacityBase: 0.62, LightOpacityPulse: 0.05,
        ShadeOpacityBase: 0.83, ShadeOpacityPulse: 0.03,
        PhaseStep: 0.018, ParticleCount: 10,
        ParticleSpeedMin: 0.25, ParticleSpeedMax: 0.70,
        ParticleLengthMin: 16, ParticleLengthMax: 34, ParticleDriftPerTick: 0.12);

    private static readonly IReadOnlyDictionary<HyperOS3WeatherVisualKind, string> BackgroundAssets =
        new Dictionary<HyperOS3WeatherVisualKind, string>
        {
            [HyperOS3WeatherVisualKind.ClearDay] = "avares://LanMontainDesktop/Assets/Weather/HyperOS3/hyper_cross_sky_day.png",
            [HyperOS3WeatherVisualKind.ClearNight] = "avares://LanMontainDesktop/Assets/Weather/HyperOS3/hyper_cross_sky_night.png",
            [HyperOS3WeatherVisualKind.CloudyDay] = "avares://LanMontainDesktop/Assets/Weather/HyperOS3/hyper_sky_front.png",
            [HyperOS3WeatherVisualKind.CloudyNight] = "avares://LanMontainDesktop/Assets/Weather/HyperOS3/hyper_sky_back.png",
            [HyperOS3WeatherVisualKind.RainLight] = "avares://LanMontainDesktop/Assets/Weather/HyperOS3/hyper_sky_front.png",
            [HyperOS3WeatherVisualKind.RainHeavy] = "avares://LanMontainDesktop/Assets/Weather/HyperOS3/hyper_sky_back.png",
            [HyperOS3WeatherVisualKind.Storm] = "avares://LanMontainDesktop/Assets/Weather/HyperOS3/hyper_cross_sky_night.png",
            [HyperOS3WeatherVisualKind.Snow] = "avares://LanMontainDesktop/Assets/Weather/HyperOS3/hyper_sky_top.png",
            [HyperOS3WeatherVisualKind.Fog] = "avares://LanMontainDesktop/Assets/Weather/HyperOS3/hyper_sky_back.png"
        };

    private static readonly IReadOnlyDictionary<HyperOS3WeatherVisualKind, HyperOS3WeatherPalette> Palettes =
        new Dictionary<HyperOS3WeatherVisualKind, HyperOS3WeatherPalette>
        {
            [HyperOS3WeatherVisualKind.ClearDay] = new(
                GradientFrom: "#2D87DA",
                GradientTo: "#79BAF2",
                Tint: "#2E6CB5",
                PrimaryText: "#F7FCFF",
                SecondaryText: "#E8F1FD",
                TertiaryText: "#D6E5F8",
                ParticleColor: "#00FFFFFF"),
            [HyperOS3WeatherVisualKind.ClearNight] = new(
                GradientFrom: "#5A6B85",
                GradientTo: "#9DADC2",
                Tint: "#495B78",
                PrimaryText: "#F9FBFF",
                SecondaryText: "#E2EAF6",
                TertiaryText: "#C6D2E3",
                ParticleColor: "#00FFFFFF"),
            [HyperOS3WeatherVisualKind.CloudyDay] = new(
                GradientFrom: "#5F88B6",
                GradientTo: "#8FB0D1",
                Tint: "#496F98",
                PrimaryText: "#F8FCFF",
                SecondaryText: "#E4EDF8",
                TertiaryText: "#CBD9EA",
                ParticleColor: "#26FFFFFF"),
            [HyperOS3WeatherVisualKind.CloudyNight] = new(
                GradientFrom: "#556A85",
                GradientTo: "#95A5BC",
                Tint: "#43566E",
                PrimaryText: "#F6FAFF",
                SecondaryText: "#DEE7F4",
                TertiaryText: "#C1CDDE",
                ParticleColor: "#30F0F5FF"),
            [HyperOS3WeatherVisualKind.RainLight] = new(
                GradientFrom: "#5A7DA7",
                GradientTo: "#8FAAC8",
                Tint: "#3F5F84",
                PrimaryText: "#F8FBFF",
                SecondaryText: "#E3EAF5",
                TertiaryText: "#C4D0E0",
                ParticleColor: "#88D7E8FF"),
            [HyperOS3WeatherVisualKind.RainHeavy] = new(
                GradientFrom: "#4C678A",
                GradientTo: "#7D95AF",
                Tint: "#354C69",
                PrimaryText: "#F9FCFF",
                SecondaryText: "#E0E8F4",
                TertiaryText: "#C0CBDA",
                ParticleColor: "#A2CDE1FF"),
            [HyperOS3WeatherVisualKind.Storm] = new(
                GradientFrom: "#435D7B",
                GradientTo: "#6F869F",
                Tint: "#2B3D53",
                PrimaryText: "#F9FCFF",
                SecondaryText: "#DBE5F2",
                TertiaryText: "#B9C5D7",
                ParticleColor: "#A8C2D6F2"),
            [HyperOS3WeatherVisualKind.Snow] = new(
                GradientFrom: "#9FB7D0",
                GradientTo: "#B7CAE0",
                Tint: "#6D839D",
                PrimaryText: "#F8FBFF",
                SecondaryText: "#E5EDF7",
                TertiaryText: "#CDD9E7",
                ParticleColor: "#CCFFFFFF"),
            [HyperOS3WeatherVisualKind.Fog] = new(
                GradientFrom: "#687E9A",
                GradientTo: "#9AACBE",
                Tint: "#4B6078",
                PrimaryText: "#F8FBFF",
                SecondaryText: "#E3EAF4",
                TertiaryText: "#C4D0DF",
                ParticleColor: "#88E4EDF7")
        };

    private static readonly IReadOnlyDictionary<HyperOS3WeatherVisualKind, HyperOS3WeatherMotion> Motions =
        new Dictionary<HyperOS3WeatherVisualKind, HyperOS3WeatherMotion>
        {
            [HyperOS3WeatherVisualKind.ClearDay] = new(
                DriftX: 8.0, DriftY: 4.0, ZoomBase: 1.055, ZoomAmplitude: 0.012,
                MotionOpacityBase: 0.22, MotionOpacityPulse: 0.05,
                LightOpacityBase: 0.68, LightOpacityPulse: 0.08,
                ShadeOpacityBase: 0.72, ShadeOpacityPulse: 0.03,
                PhaseStep: 0.015, ParticleCount: 0,
                ParticleSpeedMin: 0, ParticleSpeedMax: 0,
                ParticleLengthMin: 0, ParticleLengthMax: 0, ParticleDriftPerTick: 0),
            [HyperOS3WeatherVisualKind.ClearNight] = new(
                DriftX: 10.0, DriftY: 6.0, ZoomBase: 1.060, ZoomAmplitude: 0.014,
                MotionOpacityBase: 0.28, MotionOpacityPulse: 0.06,
                LightOpacityBase: 0.58, LightOpacityPulse: 0.07,
                ShadeOpacityBase: 0.82, ShadeOpacityPulse: 0.04,
                PhaseStep: 0.018, ParticleCount: 0,
                ParticleSpeedMin: 0, ParticleSpeedMax: 0,
                ParticleLengthMin: 0, ParticleLengthMax: 0, ParticleDriftPerTick: 0),
            [HyperOS3WeatherVisualKind.CloudyDay] = new(
                DriftX: 12.0, DriftY: 7.0, ZoomBase: 1.060, ZoomAmplitude: 0.013,
                MotionOpacityBase: 0.32, MotionOpacityPulse: 0.06,
                LightOpacityBase: 0.62, LightOpacityPulse: 0.07,
                ShadeOpacityBase: 0.80, ShadeOpacityPulse: 0.03,
                PhaseStep: 0.020, ParticleCount: 6,
                ParticleSpeedMin: 0.30, ParticleSpeedMax: 0.70,
                ParticleLengthMin: 14, ParticleLengthMax: 28, ParticleDriftPerTick: 0.10),
            [HyperOS3WeatherVisualKind.CloudyNight] = new(
                DriftX: 14.0, DriftY: 8.0, ZoomBase: 1.065, ZoomAmplitude: 0.013,
                MotionOpacityBase: 0.34, MotionOpacityPulse: 0.07,
                LightOpacityBase: 0.54, LightOpacityPulse: 0.06,
                ShadeOpacityBase: 0.85, ShadeOpacityPulse: 0.03,
                PhaseStep: 0.021, ParticleCount: 8,
                ParticleSpeedMin: 0.35, ParticleSpeedMax: 0.80,
                ParticleLengthMin: 16, ParticleLengthMax: 30, ParticleDriftPerTick: 0.12),
            [HyperOS3WeatherVisualKind.RainLight] = new(
                DriftX: 6.0, DriftY: 10.0, ZoomBase: 1.050, ZoomAmplitude: 0.010,
                MotionOpacityBase: 0.30, MotionOpacityPulse: 0.08,
                LightOpacityBase: 0.50, LightOpacityPulse: 0.04,
                ShadeOpacityBase: 0.84, ShadeOpacityPulse: 0.04,
                PhaseStep: 0.030, ParticleCount: 18,
                ParticleSpeedMin: 1.80, ParticleSpeedMax: 3.20,
                ParticleLengthMin: 14, ParticleLengthMax: 26, ParticleDriftPerTick: 0.70),
            [HyperOS3WeatherVisualKind.RainHeavy] = new(
                DriftX: 5.0, DriftY: 11.0, ZoomBase: 1.045, ZoomAmplitude: 0.010,
                MotionOpacityBase: 0.34, MotionOpacityPulse: 0.10,
                LightOpacityBase: 0.42, LightOpacityPulse: 0.03,
                ShadeOpacityBase: 0.88, ShadeOpacityPulse: 0.05,
                PhaseStep: 0.036, ParticleCount: 30,
                ParticleSpeedMin: 2.80, ParticleSpeedMax: 4.80,
                ParticleLengthMin: 18, ParticleLengthMax: 34, ParticleDriftPerTick: 0.92),
            [HyperOS3WeatherVisualKind.Storm] = new(
                DriftX: 4.0, DriftY: 12.0, ZoomBase: 1.042, ZoomAmplitude: 0.012,
                MotionOpacityBase: 0.38, MotionOpacityPulse: 0.12,
                LightOpacityBase: 0.36, LightOpacityPulse: 0.02,
                ShadeOpacityBase: 0.91, ShadeOpacityPulse: 0.04,
                PhaseStep: 0.042, ParticleCount: 34,
                ParticleSpeedMin: 3.60, ParticleSpeedMax: 5.80,
                ParticleLengthMin: 20, ParticleLengthMax: 36, ParticleDriftPerTick: 1.08),
            [HyperOS3WeatherVisualKind.Snow] = new(
                DriftX: 9.0, DriftY: 7.0, ZoomBase: 1.055, ZoomAmplitude: 0.012,
                MotionOpacityBase: 0.28, MotionOpacityPulse: 0.06,
                LightOpacityBase: 0.74, LightOpacityPulse: 0.08,
                ShadeOpacityBase: 0.68, ShadeOpacityPulse: 0.03,
                PhaseStep: 0.020, ParticleCount: 24,
                ParticleSpeedMin: 0.60, ParticleSpeedMax: 1.60,
                ParticleLengthMin: 3.0, ParticleLengthMax: 8.5, ParticleDriftPerTick: 0.24),
            [HyperOS3WeatherVisualKind.Fog] = new(
                DriftX: 7.0, DriftY: 5.0, ZoomBase: 1.050, ZoomAmplitude: 0.011,
                MotionOpacityBase: 0.30, MotionOpacityPulse: 0.05,
                LightOpacityBase: 0.58, LightOpacityPulse: 0.05,
                ShadeOpacityBase: 0.86, ShadeOpacityPulse: 0.03,
                PhaseStep: 0.018, ParticleCount: 10,
                ParticleSpeedMin: 0.25, ParticleSpeedMax: 0.70,
                ParticleLengthMin: 16, ParticleLengthMax: 34, ParticleDriftPerTick: 0.12)
        };

    private static readonly IReadOnlyDictionary<HyperOS3WeatherWidgetKind, HyperOS3WeatherMetrics> Metrics =
        new Dictionary<HyperOS3WeatherWidgetKind, HyperOS3WeatherMetrics>
        {
            [HyperOS3WeatherWidgetKind.Realtime2x2] = new(0.45, 0.38, 0.38, 108, 30, 30, 24, 40, 8, 4),
            [HyperOS3WeatherWidgetKind.Hourly4x2] = new(0.45, 0.32, 0.30, 96, 28, 24, 20, 30, 8, 4),
            [HyperOS3WeatherWidgetKind.MultiDay4x2] = new(0.45, 0.32, 0.30, 96, 28, 24, 20, 30, 8, 4),
            [HyperOS3WeatherWidgetKind.WeatherClock2x1] = new(0.40, 0.18, 0.14, 42, 18, 15, 12, 18, 4, 3),
            [HyperOS3WeatherWidgetKind.Extended4x4] = new(0.45, 0.28, 0.28, 88, 24, 20, 18, 24, 8, 6)
        };

    public static HyperOS3WeatherVisualKind ResolveVisualKind(int? weatherCode, bool isNight)
    {
        return weatherCode switch
        {
            0 => isNight ? HyperOS3WeatherVisualKind.ClearNight : HyperOS3WeatherVisualKind.ClearDay,
            1 or 2 => isNight ? HyperOS3WeatherVisualKind.CloudyNight : HyperOS3WeatherVisualKind.CloudyDay,
            3 or 7 => HyperOS3WeatherVisualKind.RainLight,
            8 or 9 => HyperOS3WeatherVisualKind.RainHeavy,
            4 => HyperOS3WeatherVisualKind.Storm,
            13 or 14 or 15 or 16 => HyperOS3WeatherVisualKind.Snow,
            18 or 32 => HyperOS3WeatherVisualKind.Fog,
            _ => isNight ? HyperOS3WeatherVisualKind.CloudyNight : HyperOS3WeatherVisualKind.CloudyDay
        };
    }

    public static HyperOS3WeatherPalette ResolvePalette(HyperOS3WeatherVisualKind kind)
    {
        return Palettes.TryGetValue(kind, out var palette) ? palette : FallbackPalette;
    }

    public static HyperOS3WeatherMotion ResolveMotion(HyperOS3WeatherVisualKind kind)
    {
        return Motions.TryGetValue(kind, out var motion) ? motion : FallbackMotion;
    }

    public static HyperOS3WeatherMetrics ResolveMetrics(HyperOS3WeatherWidgetKind kind)
    {
        return Metrics.TryGetValue(kind, out var metrics)
            ? metrics
            : Metrics[HyperOS3WeatherWidgetKind.Realtime2x2];
    }

    public static string? ResolveBackgroundAsset(HyperOS3WeatherVisualKind kind)
    {
        return BackgroundAssets.TryGetValue(kind, out var asset) ? asset : null;
    }

    public static string ResolveSunCoreAsset()
    {
        return "avares://LanMontainDesktop/Assets/Weather/HyperOS3/hyper_sun_core.png";
    }

    public static string ResolveSunRingAsset()
    {
        return "avares://LanMontainDesktop/Assets/Weather/HyperOS3/hyper_sun_ring.png";
    }

    public static string? ResolveParticleAsset(HyperOS3WeatherVisualKind kind)
    {
        return kind switch
        {
            HyperOS3WeatherVisualKind.RainLight or HyperOS3WeatherVisualKind.RainHeavy or HyperOS3WeatherVisualKind.Storm
                => "avares://LanMontainDesktop/Assets/Weather/HyperOS3/hyper_rain_drop.png",
            HyperOS3WeatherVisualKind.Snow
                => "avares://LanMontainDesktop/Assets/Weather/HyperOS3/hyper_snow_flake.png",
            HyperOS3WeatherVisualKind.Fog
                => "avares://LanMontainDesktop/Assets/Weather/HyperOS3/hyper_fog.png",
            _ => null
        };
    }

    public static Symbol ResolveWeatherSymbol(HyperOS3WeatherVisualKind kind)
    {
        return kind switch
        {
            HyperOS3WeatherVisualKind.ClearDay => Symbol.WeatherSunny,
            HyperOS3WeatherVisualKind.ClearNight => Symbol.WeatherMoon,
            HyperOS3WeatherVisualKind.CloudyDay => Symbol.WeatherPartlyCloudyDay,
            HyperOS3WeatherVisualKind.CloudyNight => Symbol.WeatherPartlyCloudyNight,
            HyperOS3WeatherVisualKind.RainLight => Symbol.WeatherRainShowersDay,
            HyperOS3WeatherVisualKind.RainHeavy => Symbol.WeatherRain,
            HyperOS3WeatherVisualKind.Storm => Symbol.WeatherThunderstorm,
            HyperOS3WeatherVisualKind.Snow => Symbol.WeatherSnow,
            _ => Symbol.WeatherFog
        };
    }

    public static string ResolveIconAccent(HyperOS3WeatherVisualKind kind, Symbol symbol)
    {
        var isNight = kind is HyperOS3WeatherVisualKind.ClearNight or HyperOS3WeatherVisualKind.CloudyNight;
        return symbol switch
        {
            Symbol.WeatherSunny => isNight ? "#F0D18A" : "#F5C65C",
            Symbol.WeatherMoon => "#EED49A",
            Symbol.WeatherPartlyCloudyDay => "#F3D68E",
            Symbol.WeatherPartlyCloudyNight => "#CFDCFF",
            Symbol.WeatherRainShowersDay => "#C7DCF9",
            Symbol.WeatherRain => "#BCD4F4",
            Symbol.WeatherThunderstorm => "#F0D38B",
            Symbol.WeatherSnow => "#EBF5FF",
            Symbol.WeatherFog => "#E3EBF6",
            _ => isNight ? "#D2DDEE" : "#E5EEF9"
        };
    }

    public static bool ResolveIsNightPreferred(
        WeatherSnapshot snapshot,
        TimeZoneInfo? timeZone,
        DateTime fallbackLocalTime)
    {
        if (snapshot.Current.IsDaylight.HasValue)
        {
            return !snapshot.Current.IsDaylight.Value;
        }

        var referenceTime = snapshot.ObservationTime?.DateTime ?? fallbackLocalTime;
        if (snapshot.ObservationTime.HasValue && timeZone is not null)
        {
            referenceTime = TimeZoneInfo.ConvertTime(snapshot.ObservationTime.Value, timeZone).DateTime;
        }

        var date = DateOnly.FromDateTime(referenceTime);
        var todayForecast = snapshot.DailyForecasts.FirstOrDefault(item => item.Date == date);
        if (todayForecast is not null &&
            TryParseClockTime(todayForecast.SunriseTime, out var sunrise) &&
            TryParseClockTime(todayForecast.SunsetTime, out var sunset) &&
            sunrise < sunset)
        {
            var time = referenceTime.TimeOfDay;
            return time < sunrise || time >= sunset;
        }

        if (snapshot.ObservationTime.HasValue)
        {
            var observed = snapshot.ObservationTime.Value;
            if (timeZone is not null)
            {
                observed = TimeZoneInfo.ConvertTime(observed, timeZone);
            }

            return observed.Hour < 6 || observed.Hour >= 18;
        }

        return fallbackLocalTime.Hour < 6 || fallbackLocalTime.Hour >= 18;
    }

    private static bool TryParseClockTime(string? text, out TimeSpan value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = default;
            return false;
        }

        var candidate = text.Trim();
        if (TimeSpan.TryParse(candidate, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        if (DateTimeOffset.TryParse(candidate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dto))
        {
            value = dto.TimeOfDay;
            return true;
        }

        if (DateTime.TryParse(candidate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
        {
            value = dt.TimeOfDay;
            return true;
        }

        return false;
    }
}

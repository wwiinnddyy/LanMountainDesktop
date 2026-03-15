using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public enum HyperOS3WeatherVisualKind
{
    Unknown,
    ClearDay,
    ClearNight,
    PartlyCloudyDay,
    PartlyCloudyNight,
    CloudyDay,
    CloudyNight,
    Haze,
    Sleet,
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
        GradientFrom: "#607C9E",
        GradientTo: "#9DB3CB",
        Tint: "#55708D",
        PrimaryText: "#FFFFFFFF",
        SecondaryText: "#E4EDF7",
        TertiaryText: "#BFD0E1",
        ParticleColor: "#70D3E2F4");

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
            [HyperOS3WeatherVisualKind.Unknown] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/hyper_sky_back.png",
            [HyperOS3WeatherVisualKind.ClearDay] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/hyper_cross_sky_day.png",
            [HyperOS3WeatherVisualKind.ClearNight] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/hyper_cross_sky_night.png",
            [HyperOS3WeatherVisualKind.PartlyCloudyDay] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/hyper_cross_sky_day.png",
            [HyperOS3WeatherVisualKind.PartlyCloudyNight] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/hyper_cross_sky_night.png",
            [HyperOS3WeatherVisualKind.CloudyDay] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/hyper_sky_front.png",
            [HyperOS3WeatherVisualKind.CloudyNight] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/hyper_sky_back.png",
            [HyperOS3WeatherVisualKind.Haze] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/hyper_haze.png",
            [HyperOS3WeatherVisualKind.Sleet] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/hyper_sky_front.png",
            [HyperOS3WeatherVisualKind.RainLight] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/hyper_sky_front.png",
            [HyperOS3WeatherVisualKind.RainHeavy] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/hyper_sky_back.png",
            [HyperOS3WeatherVisualKind.Storm] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/hyper_cross_sky_night.png",
            [HyperOS3WeatherVisualKind.Snow] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/hyper_sky_top.png",
            [HyperOS3WeatherVisualKind.Fog] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/hyper_fog.png"
        };

    private static readonly IReadOnlyDictionary<HyperOS3WeatherVisualKind, string> HeroIconAssets =
        new Dictionary<HyperOS3WeatherVisualKind, string>
        {
            [HyperOS3WeatherVisualKind.Unknown] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/Icons/icon_cloudy.webp",
            [HyperOS3WeatherVisualKind.ClearDay] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/Icons/icon_hero_sun_soft.png",
            [HyperOS3WeatherVisualKind.ClearNight] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/Icons/icon_hero_moon_soft.png",
            [HyperOS3WeatherVisualKind.PartlyCloudyDay] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/Icons/icon_partly_cloudy_day.webp",
            [HyperOS3WeatherVisualKind.PartlyCloudyNight] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/Icons/icon_partly_cloudy_night.webp",
            [HyperOS3WeatherVisualKind.CloudyDay] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/Icons/icon_cloudy.webp",
            [HyperOS3WeatherVisualKind.CloudyNight] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/Icons/icon_cloudy.webp",
            [HyperOS3WeatherVisualKind.Haze] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/Icons/icon_haze.webp",
            [HyperOS3WeatherVisualKind.Sleet] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/Icons/icon_sleet.webp",
            [HyperOS3WeatherVisualKind.RainLight] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/Icons/icon_rain_light.webp",
            [HyperOS3WeatherVisualKind.RainHeavy] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/Icons/icon_rain_heavy.webp",
            [HyperOS3WeatherVisualKind.Storm] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/Icons/icon_thunder.webp",
            [HyperOS3WeatherVisualKind.Snow] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/Icons/icon_snow.webp",
            [HyperOS3WeatherVisualKind.Fog] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/Icons/icon_mini_fog_soft.png"
        };

    private static readonly IReadOnlyDictionary<HyperOS3WeatherVisualKind, string> MiniIconAssets =
        new Dictionary<HyperOS3WeatherVisualKind, string>
        {
            [HyperOS3WeatherVisualKind.Unknown] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/Icons/icon_mini_cloudy_soft.png",
            [HyperOS3WeatherVisualKind.ClearDay] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/Icons/icon_mini_partly_cloudy_day_soft.png",
            [HyperOS3WeatherVisualKind.ClearNight] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/Icons/icon_mini_partly_cloudy_night_soft.png",
            [HyperOS3WeatherVisualKind.PartlyCloudyDay] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/Icons/icon_mini_partly_cloudy_day_soft.png",
            [HyperOS3WeatherVisualKind.PartlyCloudyNight] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/Icons/icon_mini_partly_cloudy_night_soft.png",
            [HyperOS3WeatherVisualKind.CloudyDay] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/Icons/icon_mini_cloudy_soft.png",
            [HyperOS3WeatherVisualKind.CloudyNight] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/Icons/icon_mini_cloudy_soft.png",
            [HyperOS3WeatherVisualKind.Haze] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/Icons/icon_haze.webp",
            [HyperOS3WeatherVisualKind.Sleet] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/Icons/icon_sleet.webp",
            [HyperOS3WeatherVisualKind.RainLight] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/Icons/icon_mini_rain_light_soft.png",
            [HyperOS3WeatherVisualKind.RainHeavy] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/Icons/icon_mini_rain_heavy_soft.png",
            [HyperOS3WeatherVisualKind.Storm] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/Icons/icon_mini_storm_soft.png",
            [HyperOS3WeatherVisualKind.Snow] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/Icons/icon_mini_snow_soft.png",
            [HyperOS3WeatherVisualKind.Fog] = "avares://LanMountainDesktop/Assets/Weather/HyperOS3/Icons/icon_mini_fog_soft.png"
        };

    private static readonly IReadOnlyDictionary<HyperOS3WeatherVisualKind, HyperOS3WeatherPalette> Palettes =
        new Dictionary<HyperOS3WeatherVisualKind, HyperOS3WeatherPalette>
        {
            [HyperOS3WeatherVisualKind.Unknown] = new(
                GradientFrom: "#6B7785",
                GradientTo: "#98A4B3",
                Tint: "#55606E",
                PrimaryText: "#F8FBFF",
                SecondaryText: "#E1E8F0",
                TertiaryText: "#C2CCD8",
                ParticleColor: "#24FFFFFF"),
            [HyperOS3WeatherVisualKind.ClearDay] = new(
                GradientFrom: "#5F7FA3",
                GradientTo: "#9BB4CF",
                Tint: "#567495",
                PrimaryText: "#F8FCFF",
                SecondaryText: "#E5EEF8",
                TertiaryText: "#C3D3E4",
                ParticleColor: "#00FFFFFF"),
            [HyperOS3WeatherVisualKind.ClearNight] = new(
                GradientFrom: "#576B86",
                GradientTo: "#889CB6",
                Tint: "#495F79",
                PrimaryText: "#F9FBFF",
                SecondaryText: "#D9E4F0",
                TertiaryText: "#B4C3D6",
                ParticleColor: "#00FFFFFF"),
            [HyperOS3WeatherVisualKind.PartlyCloudyDay] = new(
                GradientFrom: "#607D9F",
                GradientTo: "#9BB2C8",
                Tint: "#55728F",
                PrimaryText: "#F8FCFF",
                SecondaryText: "#E4EDF7",
                TertiaryText: "#C4D4E4",
                ParticleColor: "#12FFFFFF"),
            [HyperOS3WeatherVisualKind.PartlyCloudyNight] = new(
                GradientFrom: "#5A6E87",
                GradientTo: "#8FA4BC",
                Tint: "#4D6178",
                PrimaryText: "#F8FBFF",
                SecondaryText: "#D9E5F0",
                TertiaryText: "#B6C5D7",
                ParticleColor: "#1FE8F2FF"),
            [HyperOS3WeatherVisualKind.CloudyDay] = new(
                GradientFrom: "#5D799A",
                GradientTo: "#95ADC6",
                Tint: "#526E8B",
                PrimaryText: "#F8FCFF",
                SecondaryText: "#E2ECF7",
                TertiaryText: "#C0D0E0",
                ParticleColor: "#26FFFFFF"),
            [HyperOS3WeatherVisualKind.CloudyNight] = new(
                GradientFrom: "#536882",
                GradientTo: "#869CB4",
                Tint: "#495E76",
                PrimaryText: "#F6FAFF",
                SecondaryText: "#D4E0ED",
                TertiaryText: "#B0BFD2",
                ParticleColor: "#30F0F5FF"),
            [HyperOS3WeatherVisualKind.Haze] = new(
                GradientFrom: "#6A7E95",
                GradientTo: "#A5B2BE",
                Tint: "#657789",
                PrimaryText: "#F7FBFF",
                SecondaryText: "#E3E8EE",
                TertiaryText: "#C1CBD6",
                ParticleColor: "#6FD6DEE8"),
            [HyperOS3WeatherVisualKind.Sleet] = new(
                GradientFrom: "#61788F",
                GradientTo: "#9AB0C4",
                Tint: "#587087",
                PrimaryText: "#F7FBFF",
                SecondaryText: "#DCE6F0",
                TertiaryText: "#B8C7D7",
                ParticleColor: "#98DCEBFF"),
            [HyperOS3WeatherVisualKind.RainLight] = new(
                GradientFrom: "#4F6786",
                GradientTo: "#7A92AF",
                Tint: "#425C7A",
                PrimaryText: "#F8FBFF",
                SecondaryText: "#D7E2EE",
                TertiaryText: "#AEBED0",
                ParticleColor: "#86CCDEFF"),
            [HyperOS3WeatherVisualKind.RainHeavy] = new(
                GradientFrom: "#435770",
                GradientTo: "#667F98",
                Tint: "#364961",
                PrimaryText: "#F9FCFF",
                SecondaryText: "#D3DEEB",
                TertiaryText: "#A9B8CB",
                ParticleColor: "#9FC4D8FF"),
            [HyperOS3WeatherVisualKind.Storm] = new(
                GradientFrom: "#3A4D63",
                GradientTo: "#5C7288",
                Tint: "#2F4055",
                PrimaryText: "#F9FCFF",
                SecondaryText: "#CEDAE8",
                TertiaryText: "#A6B6C8",
                ParticleColor: "#9EB8CCF2"),
            [HyperOS3WeatherVisualKind.Snow] = new(
                GradientFrom: "#8A9FBA",
                GradientTo: "#AEC1D6",
                Tint: "#6E829A",
                PrimaryText: "#F8FBFF",
                SecondaryText: "#D9E4EF",
                TertiaryText: "#B5C4D6",
                ParticleColor: "#CCFFFFFF"),
            [HyperOS3WeatherVisualKind.Fog] = new(
                GradientFrom: "#607793",
                GradientTo: "#90A7C2",
                Tint: "#4F6580",
                PrimaryText: "#F8FBFF",
                SecondaryText: "#DFEAF5",
                TertiaryText: "#B7C8DA",
                ParticleColor: "#88D9E5F1")
        };

    private static readonly IReadOnlyDictionary<HyperOS3WeatherVisualKind, HyperOS3WeatherMotion> Motions =
        new Dictionary<HyperOS3WeatherVisualKind, HyperOS3WeatherMotion>
        {
            [HyperOS3WeatherVisualKind.Unknown] = new(
                DriftX: 8.0, DriftY: 5.0, ZoomBase: 1.050, ZoomAmplitude: 0.010,
                MotionOpacityBase: 0.24, MotionOpacityPulse: 0.05,
                LightOpacityBase: 0.60, LightOpacityPulse: 0.05,
                ShadeOpacityBase: 0.80, ShadeOpacityPulse: 0.03,
                PhaseStep: 0.018, ParticleCount: 0,
                ParticleSpeedMin: 0, ParticleSpeedMax: 0,
                ParticleLengthMin: 0, ParticleLengthMax: 0, ParticleDriftPerTick: 0),
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
            [HyperOS3WeatherVisualKind.PartlyCloudyDay] = new(
                DriftX: 10.0, DriftY: 6.0, ZoomBase: 1.058, ZoomAmplitude: 0.013,
                MotionOpacityBase: 0.26, MotionOpacityPulse: 0.05,
                LightOpacityBase: 0.65, LightOpacityPulse: 0.06,
                ShadeOpacityBase: 0.76, ShadeOpacityPulse: 0.03,
                PhaseStep: 0.017, ParticleCount: 0,
                ParticleSpeedMin: 0, ParticleSpeedMax: 0,
                ParticleLengthMin: 0, ParticleLengthMax: 0, ParticleDriftPerTick: 0),
            [HyperOS3WeatherVisualKind.PartlyCloudyNight] = new(
                DriftX: 12.0, DriftY: 7.0, ZoomBase: 1.061, ZoomAmplitude: 0.013,
                MotionOpacityBase: 0.30, MotionOpacityPulse: 0.06,
                LightOpacityBase: 0.55, LightOpacityPulse: 0.05,
                ShadeOpacityBase: 0.82, ShadeOpacityPulse: 0.03,
                PhaseStep: 0.019, ParticleCount: 0,
                ParticleSpeedMin: 0, ParticleSpeedMax: 0,
                ParticleLengthMin: 0, ParticleLengthMax: 0, ParticleDriftPerTick: 0),
            [HyperOS3WeatherVisualKind.CloudyDay] = new(
                DriftX: 12.0, DriftY: 7.0, ZoomBase: 1.060, ZoomAmplitude: 0.013,
                MotionOpacityBase: 0.32, MotionOpacityPulse: 0.06,
                LightOpacityBase: 0.62, LightOpacityPulse: 0.07,
                ShadeOpacityBase: 0.80, ShadeOpacityPulse: 0.03,
                PhaseStep: 0.020, ParticleCount: 0,
                ParticleSpeedMin: 0.30, ParticleSpeedMax: 0.70,
                ParticleLengthMin: 14, ParticleLengthMax: 28, ParticleDriftPerTick: 0.10),
            [HyperOS3WeatherVisualKind.CloudyNight] = new(
                DriftX: 14.0, DriftY: 8.0, ZoomBase: 1.065, ZoomAmplitude: 0.013,
                MotionOpacityBase: 0.34, MotionOpacityPulse: 0.07,
                LightOpacityBase: 0.54, LightOpacityPulse: 0.06,
                ShadeOpacityBase: 0.85, ShadeOpacityPulse: 0.03,
                PhaseStep: 0.021, ParticleCount: 0,
                ParticleSpeedMin: 0.35, ParticleSpeedMax: 0.80,
                ParticleLengthMin: 16, ParticleLengthMax: 30, ParticleDriftPerTick: 0.12),
            [HyperOS3WeatherVisualKind.Haze] = new(
                DriftX: 9.0, DriftY: 5.0, ZoomBase: 1.052, ZoomAmplitude: 0.010,
                MotionOpacityBase: 0.30, MotionOpacityPulse: 0.04,
                LightOpacityBase: 0.54, LightOpacityPulse: 0.04,
                ShadeOpacityBase: 0.85, ShadeOpacityPulse: 0.03,
                PhaseStep: 0.018, ParticleCount: 0,
                ParticleSpeedMin: 0.20, ParticleSpeedMax: 0.45,
                ParticleLengthMin: 12, ParticleLengthMax: 28, ParticleDriftPerTick: 0.10),
            [HyperOS3WeatherVisualKind.Sleet] = new(
                DriftX: 7.0, DriftY: 9.0, ZoomBase: 1.048, ZoomAmplitude: 0.011,
                MotionOpacityBase: 0.31, MotionOpacityPulse: 0.06,
                LightOpacityBase: 0.52, LightOpacityPulse: 0.05,
                ShadeOpacityBase: 0.82, ShadeOpacityPulse: 0.04,
                PhaseStep: 0.026, ParticleCount: 20,
                ParticleSpeedMin: 1.20, ParticleSpeedMax: 2.40,
                ParticleLengthMin: 8, ParticleLengthMax: 18, ParticleDriftPerTick: 0.34),
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
                PhaseStep: 0.018, ParticleCount: 0,
                ParticleSpeedMin: 0.25, ParticleSpeedMax: 0.70,
                ParticleLengthMin: 16, ParticleLengthMax: 34, ParticleDriftPerTick: 0.12)
        };

    private static readonly IReadOnlyDictionary<HyperOS3WeatherWidgetKind, HyperOS3WeatherMetrics> Metrics =
        new Dictionary<HyperOS3WeatherWidgetKind, HyperOS3WeatherMetrics>
        {
            [HyperOS3WeatherWidgetKind.Realtime2x2] = new(0.47, 0.32, 0.30, 112, 28, 24, 20, 36, 8, 5),
            [HyperOS3WeatherWidgetKind.Hourly4x2] = new(0.47, 0.24, 0.22, 96, 24, 20, 16, 26, 7, 4),
            [HyperOS3WeatherWidgetKind.MultiDay4x2] = new(0.47, 0.24, 0.22, 96, 24, 20, 16, 26, 7, 4),
            [HyperOS3WeatherWidgetKind.WeatherClock2x1] = new(0.40, 0.18, 0.14, 42, 18, 15, 12, 18, 4, 3),
            [HyperOS3WeatherWidgetKind.Extended4x4] = new(0.47, 0.24, 0.22, 112, 26, 22, 18, 28, 9, 6)
        };

    public static HyperOS3WeatherVisualKind ResolveVisualKind(int? weatherCode, bool isNight)
    {
        return XiaomiWeatherCodeMapper.ResolveBucket(weatherCode) switch
        {
            WeatherConditionBucket.Unknown => HyperOS3WeatherVisualKind.Unknown,
            WeatherConditionBucket.Clear => isNight ? HyperOS3WeatherVisualKind.ClearNight : HyperOS3WeatherVisualKind.ClearDay,
            WeatherConditionBucket.PartlyCloudy => isNight ? HyperOS3WeatherVisualKind.PartlyCloudyNight : HyperOS3WeatherVisualKind.PartlyCloudyDay,
            WeatherConditionBucket.Cloudy => isNight ? HyperOS3WeatherVisualKind.CloudyNight : HyperOS3WeatherVisualKind.CloudyDay,
            WeatherConditionBucket.Haze => HyperOS3WeatherVisualKind.Haze,
            WeatherConditionBucket.Sleet => HyperOS3WeatherVisualKind.Sleet,
            WeatherConditionBucket.RainLight => HyperOS3WeatherVisualKind.RainLight,
            WeatherConditionBucket.RainHeavy => HyperOS3WeatherVisualKind.RainHeavy,
            WeatherConditionBucket.Storm => HyperOS3WeatherVisualKind.Storm,
            WeatherConditionBucket.Snow => HyperOS3WeatherVisualKind.Snow,
            WeatherConditionBucket.Fog => HyperOS3WeatherVisualKind.Fog,
            _ => HyperOS3WeatherVisualKind.Unknown
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

    public static string? ResolveIconAsset(HyperOS3WeatherVisualKind kind)
    {
        return ResolveMiniIconAsset(kind);
    }

    public static string? ResolveHeroIconAsset(HyperOS3WeatherVisualKind kind)
    {
        return HeroIconAssets.TryGetValue(kind, out var asset) ? asset : null;
    }

    public static string? ResolveMiniIconAsset(HyperOS3WeatherVisualKind kind)
    {
        return MiniIconAssets.TryGetValue(kind, out var asset) ? asset : null;
    }

    public static string ResolveSunCoreAsset()
    {
        return "avares://LanMountainDesktop/Assets/Weather/HyperOS3/hyper_sun_core.png";
    }

    public static string ResolveSunRingAsset()
    {
        return "avares://LanMountainDesktop/Assets/Weather/HyperOS3/hyper_sun_ring.png";
    }

    public static string? ResolveParticleAsset(HyperOS3WeatherVisualKind kind)
    {
        return kind switch
        {
            HyperOS3WeatherVisualKind.Sleet or HyperOS3WeatherVisualKind.RainLight or HyperOS3WeatherVisualKind.RainHeavy or HyperOS3WeatherVisualKind.Storm
                => "avares://LanMountainDesktop/Assets/Weather/HyperOS3/hyper_rain_drop.png",
            HyperOS3WeatherVisualKind.Haze
                => "avares://LanMountainDesktop/Assets/Weather/HyperOS3/hyper_haze.png",
            HyperOS3WeatherVisualKind.Fog
                => "avares://LanMountainDesktop/Assets/Weather/HyperOS3/hyper_fog.png",
            HyperOS3WeatherVisualKind.Snow
                => "avares://LanMountainDesktop/Assets/Weather/HyperOS3/hyper_snow_flake.png",
            _ => null
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

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

internal readonly record struct WeatherSceneProfile(
    string StyleId,
    MaterialWeatherCondition Condition,
    string RendererId,
    string WeatherLayerId,
    bool IsNight,
    bool IsLive)
{
    public string Signature => $"{RendererId}:{WeatherLayerId}:{(IsNight ? "night" : "day")}:{(IsLive ? "live" : "still")}";
}

internal static class WeatherSceneProfileResolver
{
    public static WeatherSceneProfile Resolve(string? styleId, MaterialWeatherCondition condition, bool isNight, bool isLive)
    {
        var normalized = WeatherVisualStyleCatalog.Normalize(styleId);
        var rendererId = normalized switch
        {
            WeatherVisualStyleId.Geometric => "geometric",
            WeatherVisualStyleId.Breezy => "breezy",
            WeatherVisualStyleId.LemonFlutter => "lemon",
            _ => "google"
        };

        var layerId = condition switch
        {
            MaterialWeatherCondition.Clear => "clear",
            MaterialWeatherCondition.PartlyCloudy => "partly-cloudy",
            MaterialWeatherCondition.Cloudy => "cloudy",
            MaterialWeatherCondition.Rain => "rain",
            MaterialWeatherCondition.Storm => "storm",
            MaterialWeatherCondition.Snow => "snow",
            MaterialWeatherCondition.Fog => "fog",
            MaterialWeatherCondition.Haze => "haze",
            _ => "ambient"
        };

        return new WeatherSceneProfile(normalized, condition, rendererId, layerId, isNight, isLive);
    }
}

public sealed class MaterialWeatherSceneControl : Control
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(66) };
    private MaterialWeatherPalette _palette = MaterialWeatherVisualTheme.ResolvePalette(MaterialWeatherCondition.Clear, false);
    private MaterialWeatherCondition _condition = MaterialWeatherCondition.Clear;
    private string _styleId = WeatherVisualStyleId.Default;
    private double _phase;
    private bool _isLive;
    private bool _isAttached;
    private bool _isNight;

    public MaterialWeatherSceneControl()
    {
        IsHitTestVisible = false;
        _timer.Tick += (_, _) =>
        {
            _phase = (_phase + 0.0065) % 1d;
            InvalidateVisual();
        };
    }

    public void Apply(string? styleId, MaterialWeatherCondition condition, MaterialWeatherPalette palette, bool isLive, bool isNight)
    {
        _styleId = WeatherVisualStyleCatalog.Normalize(styleId);
        _condition = condition;
        _palette = palette;
        _isLive = isLive;
        _isNight = isNight;
        UpdateTimer();
        InvalidateVisual();
    }

    public void Apply(string? styleId, MaterialWeatherCondition condition, MaterialWeatherPalette palette, bool isLive)
    {
        Apply(styleId, condition, palette, isLive, EstimateNightFromPalette(palette));
    }

    public void Apply(MaterialWeatherCondition condition, MaterialWeatherPalette palette, bool isLive)
    {
        Apply(_styleId, condition, palette, isLive, EstimateNightFromPalette(palette));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        UpdateTimer();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var rect = new Rect(Bounds.Size);
        if (rect.Width <= 1 || rect.Height <= 1)
        {
            return;
        }

        var profile = WeatherSceneProfileResolver.Resolve(_styleId, _condition, _isNight, _isLive);
        context.DrawRectangle(CreateLinearBrush(_palette.BackgroundTop, _palette.BackgroundBottom, 0, 0, 1, 1), null, rect);

        using (context.PushClip(rect))
        {
            switch (profile.RendererId)
            {
                case "geometric":
                    RenderGeometricScene(context, rect, profile);
                    break;
                case "breezy":
                    RenderBreezyScene(context, rect, profile);
                    break;
                case "lemon":
                    RenderLemonScene(context, rect, profile);
                    break;
                default:
                    RenderGoogleScene(context, rect, profile);
                    break;
            }
        }
    }

    private void UpdateTimer()
    {
        if (_isLive && _isAttached)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    private void RenderGoogleScene(DrawingContext ctx, Rect r, WeatherSceneProfile profile)
    {
        var min = Math.Min(r.Width, r.Height);
        var t = Oscillate(0);

        DrawSoftBlob(ctx, r.Width * 0.78 + t * 8, r.Height * 0.18 + Oscillate(0.7) * 5, min * 0.52, _palette.PrimaryShape, 0.20);
        DrawSoftBlob(ctx, r.Width * 0.15 - t * 6, r.Height * 0.76, min * 0.36, _palette.SecondaryShape, 0.13);
        DrawSoftBlob(ctx, r.Width * 0.58, r.Height * 0.92 - t * 7, min * 0.46, _palette.AccentShape, 0.08);

        switch (profile.Condition)
        {
            case MaterialWeatherCondition.Clear:
            case MaterialWeatherCondition.Unknown:
                DrawSunDisk(ctx, r, 0.74, 0.24, 0.24, 0.32, rays: false);
                DrawArc(ctx, r.Width * 0.76, r.Height * 0.24, min * 0.28, 205, 110, _palette.AccentShape, 0.12, min * 0.012);
                break;
            case MaterialWeatherCondition.PartlyCloudy:
                DrawSunDisk(ctx, r, 0.76, 0.22, 0.21, 0.25, rays: false);
                DrawCloudCluster(ctx, r, 0.58 + t * 0.015, 0.38, 0.34, _palette.SurfaceTint, 0.34, filled: true);
                break;
            case MaterialWeatherCondition.Cloudy:
                DrawCloudCluster(ctx, r, 0.48 + t * 0.012, 0.32, 0.42, _palette.SurfaceTint, 0.36, filled: true);
                DrawCloudCluster(ctx, r, 0.70 - t * 0.010, 0.52, 0.32, _palette.SecondaryShape, 0.20, filled: true);
                break;
            case MaterialWeatherCondition.Rain:
                DrawCloudCluster(ctx, r, 0.54 + t * 0.010, 0.28, 0.38, _palette.SurfaceTint, 0.30, filled: true);
                DrawRainField(ctx, r, 0.34, 0.17, _palette.AccentShape, 0.55, storm: false);
                break;
            case MaterialWeatherCondition.Storm:
                DrawCloudCluster(ctx, r, 0.50 + t * 0.010, 0.26, 0.42, _palette.SecondaryShape, 0.34, filled: true);
                DrawRainField(ctx, r, 0.36, 0.21, _palette.SurfaceTint, 0.50, storm: true);
                DrawLightning(ctx, r, 0.67, 0.44, 0.22, _palette.AccentShape, LightningOpacity());
                break;
            case MaterialWeatherCondition.Snow:
                DrawCloudCluster(ctx, r, 0.52 + t * 0.008, 0.28, 0.36, _palette.SurfaceTint, 0.24, filled: true);
                DrawSnowField(ctx, r, _palette.AccentShape, 0.68, geometric: false);
                break;
            case MaterialWeatherCondition.Fog:
            case MaterialWeatherCondition.Haze:
                DrawFogBands(ctx, r, _palette.SurfaceTint, 0.23, curved: false);
                DrawSoftBlob(ctx, r.Width * 0.50, r.Height * 0.42, min * 0.44, _palette.SecondaryShape, 0.08);
                break;
        }
    }

    private void RenderGeometricScene(DrawingContext ctx, Rect r, WeatherSceneProfile profile)
    {
        var min = Math.Min(r.Width, r.Height);
        var t = Oscillate(0.2);

        DrawCircle(ctx, r.Width * 0.82 + t * 5, r.Height * 0.18, min * 0.33, _palette.PrimaryShape, 0.12);
        DrawArc(ctx, r.Width * 0.34, r.Height * 0.52 + t * 4, min * 0.42, 25, 135, _palette.SecondaryShape, 0.18, min * 0.018);
        DrawArc(ctx, r.Width * 0.72, r.Height * 0.76, min * 0.32, 198, 112, _palette.AccentShape, 0.16, min * 0.014);

        switch (profile.Condition)
        {
            case MaterialWeatherCondition.Clear:
            case MaterialWeatherCondition.Unknown:
                DrawCircle(ctx, r.Width * 0.72, r.Height * 0.28, min * 0.21, _palette.PrimaryShape, 0.34);
                DrawSunRays(ctx, r.Width * 0.72, r.Height * 0.28, min * 0.24, min * 0.38, 12, _palette.PrimaryShape, 0.18);
                DrawArc(ctx, r.Width * 0.72, r.Height * 0.28, min * 0.30, -20, 230, _palette.AccentShape, 0.22, min * 0.016);
                break;
            case MaterialWeatherCondition.PartlyCloudy:
                DrawCircle(ctx, r.Width * 0.72, r.Height * 0.24, min * 0.18, _palette.PrimaryShape, 0.25);
                DrawCloudCluster(ctx, r, 0.56 + t * 0.012, 0.40, 0.34, _palette.SecondaryShape, 0.28, filled: false);
                DrawCircle(ctx, r.Width * 0.49, r.Height * 0.42, min * 0.18, _palette.SurfaceTint, 0.12);
                break;
            case MaterialWeatherCondition.Cloudy:
                DrawCloudCluster(ctx, r, 0.44 + t * 0.010, 0.34, 0.40, _palette.SecondaryShape, 0.27, filled: false);
                DrawCloudCluster(ctx, r, 0.68 - t * 0.010, 0.52, 0.31, _palette.AccentShape, 0.16, filled: false);
                DrawArc(ctx, r.Width * 0.58, r.Height * 0.44, min * 0.36, 190, 135, _palette.SurfaceTint, 0.19, min * 0.012);
                break;
            case MaterialWeatherCondition.Rain:
                DrawCloudCluster(ctx, r, 0.50, 0.28, 0.38, _palette.SecondaryShape, 0.24, filled: false);
                DrawGeometricRainGrid(ctx, r, _palette.AccentShape, 0.60, storm: false);
                break;
            case MaterialWeatherCondition.Storm:
                DrawCloudCluster(ctx, r, 0.48, 0.26, 0.42, _palette.SecondaryShape, 0.24, filled: false);
                DrawGeometricRainGrid(ctx, r, _palette.SurfaceTint, 0.52, storm: true);
                DrawLightning(ctx, r, 0.65, 0.43, 0.26, _palette.AccentShape, LightningOpacity());
                DrawTriangle(ctx, r.Width * 0.33, r.Height * 0.68, min * 0.18, _palette.PrimaryShape, 0.12, rotate: 0.35);
                break;
            case MaterialWeatherCondition.Snow:
                DrawCloudCluster(ctx, r, 0.50, 0.28, 0.36, _palette.SecondaryShape, 0.18, filled: false);
                DrawSnowField(ctx, r, _palette.AccentShape, 0.72, geometric: true);
                break;
            case MaterialWeatherCondition.Fog:
            case MaterialWeatherCondition.Haze:
                DrawFogBands(ctx, r, _palette.SurfaceTint, 0.25, curved: false);
                DrawArc(ctx, r.Width * 0.44, r.Height * 0.50, min * 0.36, 0, 180, _palette.SecondaryShape, 0.16, min * 0.016);
                DrawArc(ctx, r.Width * 0.64, r.Height * 0.62, min * 0.30, 180, 170, _palette.AccentShape, 0.12, min * 0.012);
                break;
        }
    }

    private void RenderBreezyScene(DrawingContext ctx, Rect r, WeatherSceneProfile profile)
    {
        var min = Math.Min(r.Width, r.Height);
        var t = Oscillate(0.4);

        DrawSoftBlob(ctx, r.Width * 0.76 + t * 7, r.Height * 0.18, min * 0.48, _palette.PrimaryShape, 0.18);
        DrawSoftBlob(ctx, r.Width * 0.18 - t * 5, r.Height * 0.62, min * 0.42, _palette.SecondaryShape, 0.12);
        DrawWaveField(ctx, r, _palette.SurfaceTint, 0.11, 4, amplitudeScale: 1.0);

        switch (profile.Condition)
        {
            case MaterialWeatherCondition.Clear:
            case MaterialWeatherCondition.Unknown:
                DrawSunDisk(ctx, r, 0.72, 0.28, 0.23, 0.24, rays: false);
                DrawWaveField(ctx, r, _palette.AccentShape, 0.12, 3, amplitudeScale: 0.75);
                DrawArc(ctx, r.Width * 0.76, r.Height * 0.28, min * 0.30, 205, 145, _palette.PrimaryShape, 0.16, min * 0.012);
                break;
            case MaterialWeatherCondition.PartlyCloudy:
                DrawSunDisk(ctx, r, 0.73, 0.24, 0.18, 0.18, rays: false);
                DrawBreezyCloudBands(ctx, r, yBase: 0.42, density: 3, alpha: 0.24);
                DrawWaveField(ctx, r, _palette.AccentShape, 0.10, 3, amplitudeScale: 0.65);
                break;
            case MaterialWeatherCondition.Cloudy:
                DrawBreezyCloudBands(ctx, r, yBase: 0.30, density: 5, alpha: 0.26);
                DrawSoftBlob(ctx, r.Width * 0.58, r.Height * 0.44, min * 0.35, _palette.SurfaceTint, 0.14);
                break;
            case MaterialWeatherCondition.Rain:
                DrawBreezyCloudBands(ctx, r, yBase: 0.26, density: 4, alpha: 0.26);
                DrawRainBands(ctx, r, _palette.AccentShape, 0.48, storm: false);
                DrawWaveField(ctx, r, _palette.SecondaryShape, 0.14, 4, amplitudeScale: 1.25);
                break;
            case MaterialWeatherCondition.Storm:
                DrawBreezyCloudBands(ctx, r, yBase: 0.24, density: 5, alpha: 0.30);
                DrawRainBands(ctx, r, _palette.SurfaceTint, 0.48, storm: true);
                DrawLightning(ctx, r, 0.64, 0.42, 0.23, _palette.AccentShape, LightningOpacity());
                DrawWaveField(ctx, r, _palette.AccentShape, 0.16, 5, amplitudeScale: 1.35);
                break;
            case MaterialWeatherCondition.Snow:
                DrawBreezyCloudBands(ctx, r, yBase: 0.28, density: 3, alpha: 0.20);
                DrawSnowField(ctx, r, _palette.AccentShape, 0.68, geometric: true);
                DrawWaveField(ctx, r, Colors.White, 0.13, 3, amplitudeScale: 0.85);
                break;
            case MaterialWeatherCondition.Fog:
            case MaterialWeatherCondition.Haze:
                DrawFogBands(ctx, r, _palette.SurfaceTint, 0.28, curved: true);
                DrawWaveField(ctx, r, _palette.SecondaryShape, 0.18, 5, amplitudeScale: 0.55);
                break;
        }
    }

    private void RenderLemonScene(DrawingContext ctx, Rect r, WeatherSceneProfile profile)
    {
        var min = Math.Min(r.Width, r.Height);
        var t = Oscillate(0.6);

        DrawSoftBlob(ctx, r.Width * 0.78 + t * 6, r.Height * 0.20, min * 0.45, _palette.PrimaryShape, 0.18);
        DrawCircle(ctx, r.Width * 0.18, r.Height * 0.78 - t * 5, min * 0.20, _palette.SecondaryShape, 0.13);
        DrawCircle(ctx, r.Width * 0.88, r.Height * 0.64, min * 0.16, _palette.AccentShape, 0.10);

        switch (profile.Condition)
        {
            case MaterialWeatherCondition.Clear:
            case MaterialWeatherCondition.Unknown:
                DrawSunDisk(ctx, r, 0.70, 0.30, 0.23, 0.30, rays: true);
                DrawCircle(ctx, r.Width * 0.36, r.Height * 0.30, min * 0.07, _palette.SecondaryShape, 0.16);
                break;
            case MaterialWeatherCondition.PartlyCloudy:
                DrawSunDisk(ctx, r, 0.73, 0.24, 0.20, 0.24, rays: true);
                DrawCloudCluster(ctx, r, 0.56 + t * 0.012, 0.40, 0.34, _palette.SurfaceTint, 0.30, filled: true);
                break;
            case MaterialWeatherCondition.Cloudy:
                DrawCloudCluster(ctx, r, 0.48 + t * 0.012, 0.34, 0.42, _palette.SurfaceTint, 0.31, filled: true);
                DrawCloudCluster(ctx, r, 0.70 - t * 0.010, 0.53, 0.28, _palette.SecondaryShape, 0.18, filled: true);
                DrawCircle(ctx, r.Width * 0.28, r.Height * 0.44, min * 0.08, _palette.AccentShape, 0.12);
                break;
            case MaterialWeatherCondition.Rain:
                DrawCloudCluster(ctx, r, 0.52, 0.28, 0.40, _palette.SurfaceTint, 0.28, filled: true);
                DrawRainField(ctx, r, 0.36, 0.18, _palette.AccentShape, 0.55, storm: false);
                DrawCircle(ctx, r.Width * 0.23, r.Height * 0.72, min * 0.09, _palette.PrimaryShape, 0.12);
                break;
            case MaterialWeatherCondition.Storm:
                DrawCloudCluster(ctx, r, 0.50, 0.26, 0.42, _palette.SurfaceTint, 0.30, filled: true);
                DrawRainField(ctx, r, 0.36, 0.22, _palette.SecondaryShape, 0.52, storm: true);
                DrawLightning(ctx, r, 0.66, 0.42, 0.24, _palette.AccentShape, LightningOpacity());
                break;
            case MaterialWeatherCondition.Snow:
                DrawCloudCluster(ctx, r, 0.52, 0.30, 0.38, _palette.SurfaceTint, 0.22, filled: true);
                DrawSnowField(ctx, r, _palette.AccentShape, 0.72, geometric: true);
                break;
            case MaterialWeatherCondition.Fog:
            case MaterialWeatherCondition.Haze:
                DrawFogBands(ctx, r, _palette.SurfaceTint, 0.26, curved: true);
                DrawCircle(ctx, r.Width * 0.70, r.Height * 0.28, min * 0.16, _palette.SecondaryShape, 0.10);
                break;
        }
    }

    private void DrawSunDisk(DrawingContext ctx, Rect r, double nx, double ny, double radiusScale, double alpha, bool rays)
    {
        var min = Math.Min(r.Width, r.Height);
        var cx = r.Width * nx + Oscillate(0.1) * min * 0.015;
        var cy = r.Height * ny + Oscillate(0.9) * min * 0.012;
        var radius = min * radiusScale;

        DrawSoftBlob(ctx, cx, cy, radius * 1.85, _palette.PrimaryShape, alpha * 0.55);
        DrawCircle(ctx, cx, cy, radius, _palette.PrimaryShape, alpha);
        DrawCircle(ctx, cx - radius * 0.25, cy - radius * 0.28, radius * 0.36, _palette.AccentShape, alpha * 0.32);
        if (rays)
        {
            DrawSunRays(ctx, cx, cy, radius * 1.05, radius * 1.78, 14, _palette.PrimaryShape, alpha * 0.38);
        }
    }

    private void DrawCloudCluster(DrawingContext ctx, Rect r, double nx, double ny, double scale, Color color, double alpha, bool filled)
    {
        var min = Math.Min(r.Width, r.Height);
        var cx = r.Width * nx;
        var cy = r.Height * ny;
        var brush = filled ? new SolidColorBrush(color, alpha) : null;
        var pen = filled ? null : new Pen(new SolidColorBrush(color, alpha), Math.Max(1.4, min * 0.012), lineCap: PenLineCap.Round);
        var radius = min * scale;

        DrawEllipse(ctx, brush, pen, cx - radius * 0.34, cy + radius * 0.04, radius * 0.34, radius * 0.18);
        DrawEllipse(ctx, brush, pen, cx, cy - radius * 0.06, radius * 0.42, radius * 0.24);
        DrawEllipse(ctx, brush, pen, cx + radius * 0.34, cy + radius * 0.08, radius * 0.30, radius * 0.17);

        if (filled)
        {
            var baseRect = new Rect(cx - radius * 0.66, cy + radius * 0.04, radius * 1.24, radius * 0.25);
            ctx.DrawRectangle(new SolidColorBrush(color, alpha * 0.78), null, baseRect, radius * 0.12, radius * 0.12);
        }
    }

    private void DrawBreezyCloudBands(DrawingContext ctx, Rect r, double yBase, int density, double alpha)
    {
        var min = Math.Min(r.Width, r.Height);
        for (var i = 0; i < density; i++)
        {
            var y = r.Height * (yBase + i * 0.085);
            var shift = Oscillate(i * 0.32) * r.Width * 0.035;
            var thickness = Math.Max(8, min * (0.075 - i * 0.006));
            var brush = new SolidColorBrush(i % 2 == 0 ? _palette.SurfaceTint : _palette.SecondaryShape, alpha * (1 - i * 0.10));
            ctx.DrawRectangle(
                brush,
                null,
                new Rect(r.Width * (0.06 + i * 0.025) + shift, y, r.Width * (0.84 - i * 0.055), thickness),
                thickness * 0.5,
                thickness * 0.5);
        }
    }

    private void DrawRainField(DrawingContext ctx, Rect r, double startY, double densityScale, Color color, double alpha, bool storm)
    {
        var count = Math.Clamp((int)(r.Width * densityScale), 8, storm ? 32 : 24);
        var pen = new Pen(new SolidColorBrush(color, alpha), Math.Max(1.2, r.Width / 160), lineCap: PenLineCap.Round);
        for (var i = 0; i < count; i++)
        {
            var p = (_phase * (storm ? 1.4 : 0.95) + i * 0.137) % 1d;
            var lane = (i + 0.37 * (i % 3)) / count;
            var x = r.Width * (0.08 + lane * 0.84);
            var y = r.Height * (startY + p * 0.74);
            var dx = -r.Width * (storm ? 0.040 : 0.026);
            var dy = r.Height * (storm ? 0.13 : 0.095);
            ctx.DrawLine(pen, new Point(x, y), new Point(x + dx, y + dy));
        }
    }

    private void DrawGeometricRainGrid(DrawingContext ctx, Rect r, Color color, double alpha, bool storm)
    {
        var min = Math.Min(r.Width, r.Height);
        var count = Math.Clamp((int)(r.Width / 18), 9, storm ? 28 : 22);
        var pen = new Pen(new SolidColorBrush(color, alpha), Math.Max(1.3, min * 0.009), lineCap: PenLineCap.Square);
        for (var i = 0; i < count; i++)
        {
            var p = (_phase * (storm ? 1.15 : 0.75) + i * 0.091) % 1d;
            var x = r.Width * (0.12 + (i / (double)count) * 0.78);
            var y = r.Height * (0.36 + p * 0.58);
            ctx.DrawLine(pen, new Point(x, y), new Point(x - min * 0.075, y + min * 0.145));
        }
    }

    private void DrawRainBands(DrawingContext ctx, Rect r, Color color, double alpha, bool storm)
    {
        var min = Math.Min(r.Width, r.Height);
        var count = Math.Clamp((int)(r.Width / 22), 8, storm ? 26 : 20);
        var pen = new Pen(new SolidColorBrush(color, alpha), Math.Max(2.2, min * 0.014), lineCap: PenLineCap.Round);
        for (var i = 0; i < count; i++)
        {
            var p = (_phase * (storm ? 1.35 : 0.85) + i * 0.118) % 1d;
            var x = r.Width * (0.10 + (i / (double)count) * 0.86);
            var y = r.Height * (0.34 + p * 0.62);
            ctx.DrawLine(pen, new Point(x, y), new Point(x - min * 0.09, y + min * 0.16));
        }
    }

    private void DrawSnowField(DrawingContext ctx, Rect r, Color color, double alpha, bool geometric)
    {
        var min = Math.Min(r.Width, r.Height);
        var count = Math.Clamp((int)(r.Width / 22), 8, 24);
        var brush = new SolidColorBrush(color, alpha);
        var pen = new Pen(brush, Math.Max(1.1, min * 0.007), lineCap: PenLineCap.Round);
        for (var i = 0; i < count; i++)
        {
            var p = (_phase * 0.45 + i * 0.119) % 1d;
            var x = r.Width * (0.10 + (i / (double)count) * 0.82) + Math.Sin(p * Math.PI * 2 + i) * min * 0.025;
            var y = r.Height * (0.22 + p * 0.78);
            if (geometric && i % 3 == 0)
            {
                DrawSnowflake(ctx, x, y, min * 0.025, pen);
            }
            else
            {
                ctx.DrawEllipse(brush, null, new Point(x, y), Math.Max(1.8, min * 0.012), Math.Max(1.8, min * 0.012));
            }
        }
    }

    private void DrawFogBands(DrawingContext ctx, Rect r, Color color, double alpha, bool curved)
    {
        var min = Math.Min(r.Width, r.Height);
        var count = 5;
        for (var i = 0; i < count; i++)
        {
            var y = r.Height * (0.35 + i * 0.105);
            var shift = Oscillate(i * 0.25) * r.Width * 0.045;
            var pen = new Pen(new SolidColorBrush(color, alpha * (1 - i * 0.08)), Math.Max(2.2, min * 0.015), lineCap: PenLineCap.Round);
            if (curved)
            {
                DrawWavePath(ctx, r.Width * 0.10 + shift, y, r.Width * 0.82, min * 0.020, i, pen);
            }
            else
            {
                ctx.DrawLine(pen, new Point(r.Width * 0.12 + shift, y), new Point(r.Width * 0.88 + shift, y));
            }
        }
    }

    private void DrawWaveField(DrawingContext ctx, Rect r, Color color, double alpha, int lines, double amplitudeScale)
    {
        var min = Math.Min(r.Width, r.Height);
        for (var i = 0; i < lines; i++)
        {
            var y = r.Height * (0.22 + i * 0.16);
            var shift = Oscillate(i * 0.22) * r.Width * 0.06;
            var pen = new Pen(new SolidColorBrush(color, alpha * (1 - i * 0.06)), Math.Max(1.6, min * 0.010), lineCap: PenLineCap.Round);
            DrawWavePath(ctx, r.Width * 0.06 + shift, y, r.Width * 0.88, min * 0.030 * amplitudeScale, i, pen);
        }
    }

    private void DrawWavePath(DrawingContext ctx, double startX, double baseY, double width, double amplitude, int index, Pen pen)
    {
        var stream = new StreamGeometry();
        using (var g = stream.Open())
        {
            g.BeginFigure(new Point(startX, baseY), false);
            var step = Math.Max(3, width / 48);
            for (var x = 0d; x <= width; x += step)
            {
                var y = baseY + Math.Sin((x / width) * Math.PI * 3.2 + _phase * Math.PI * 2 + index * 0.85) * amplitude;
                g.LineTo(new Point(startX + x, y));
            }
            g.EndFigure(false);
        }

        ctx.DrawGeometry(null, pen, stream);
    }

    private void DrawLightning(DrawingContext ctx, Rect r, double nx, double ny, double scale, Color color, double alpha)
    {
        var min = Math.Min(r.Width, r.Height);
        var cx = r.Width * nx;
        var cy = r.Height * ny;
        var s = min * scale;
        var bolt = new StreamGeometry();
        using (var g = bolt.Open())
        {
            g.BeginFigure(new Point(cx, cy), true);
            g.LineTo(new Point(cx - s * 0.28, cy + s * 0.46));
            g.LineTo(new Point(cx - s * 0.03, cy + s * 0.40));
            g.LineTo(new Point(cx - s * 0.36, cy + s * 0.98));
            g.LineTo(new Point(cx + s * 0.18, cy + s * 0.25));
            g.LineTo(new Point(cx - s * 0.05, cy + s * 0.31));
            g.EndFigure(true);
        }

        ctx.DrawGeometry(new SolidColorBrush(color, alpha), null, bolt);
    }

    private void DrawSunRays(DrawingContext ctx, double cx, double cy, double inner, double outer, int count, Color color, double alpha)
    {
        var pen = new Pen(new SolidColorBrush(color, alpha), Math.Max(1.4, inner * 0.055), lineCap: PenLineCap.Round);
        for (var i = 0; i < count; i++)
        {
            var angle = (i / (double)count) * Math.PI * 2 + _phase * 0.45;
            var outRadius = outer + Math.Sin(angle * 2.4 + _phase * Math.PI * 2) * inner * 0.16;
            ctx.DrawLine(
                pen,
                new Point(cx + Math.Cos(angle) * inner, cy + Math.Sin(angle) * inner),
                new Point(cx + Math.Cos(angle) * outRadius, cy + Math.Sin(angle) * outRadius));
        }
    }

    private void DrawSnowflake(DrawingContext ctx, double cx, double cy, double radius, Pen pen)
    {
        for (var i = 0; i < 6; i++)
        {
            var a = (i / 6d) * Math.PI * 2 + _phase * 0.35;
            ctx.DrawLine(pen, new Point(cx - Math.Cos(a) * radius * 0.45, cy - Math.Sin(a) * radius * 0.45), new Point(cx + Math.Cos(a) * radius, cy + Math.Sin(a) * radius));
        }
    }

    private void DrawTriangle(DrawingContext ctx, double cx, double cy, double radius, Color color, double alpha, double rotate)
    {
        var triangle = new StreamGeometry();
        using (var g = triangle.Open())
        {
            for (var i = 0; i < 3; i++)
            {
                var a = rotate + (i / 3d) * Math.PI * 2;
                var p = new Point(cx + Math.Cos(a) * radius, cy + Math.Sin(a) * radius);
                if (i == 0)
                {
                    g.BeginFigure(p, true);
                }
                else
                {
                    g.LineTo(p);
                }
            }

            g.EndFigure(true);
        }

        ctx.DrawGeometry(new SolidColorBrush(color, alpha), null, triangle);
    }

    private void DrawCircle(DrawingContext ctx, double cx, double cy, double radius, Color color, double alpha)
    {
        if (radius <= 0)
        {
            return;
        }

        ctx.DrawEllipse(new SolidColorBrush(color, alpha), null, new Point(cx, cy), radius, radius);
    }

    private void DrawSoftBlob(DrawingContext ctx, double cx, double cy, double radius, Color color, double peakAlpha)
    {
        if (radius <= 0)
        {
            return;
        }

        var brush = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(WithAlpha(color, peakAlpha), 0),
                new GradientStop(WithAlpha(color, peakAlpha * 0.52), 0.42),
                new GradientStop(WithAlpha(color, 0), 1)
            }
        };

        ctx.DrawEllipse(brush, null, new Point(cx, cy), radius, radius);
    }

    private static void DrawEllipse(DrawingContext ctx, IBrush? brush, Pen? pen, double cx, double cy, double rx, double ry)
    {
        ctx.DrawEllipse(brush, pen, new Point(cx, cy), Math.Max(0.1, rx), Math.Max(0.1, ry));
    }

    private void DrawArc(DrawingContext ctx, double cx, double cy, double radius, double startDeg, double sweepDeg, Color color, double alpha, double thickness)
    {
        if (radius < 2)
        {
            return;
        }

        var stream = new StreamGeometry();
        using (var g = stream.Open())
        {
            var startRad = startDeg * Math.PI / 180d;
            var sweepRad = sweepDeg * Math.PI / 180d;
            var steps = Math.Max(10, (int)(Math.Abs(sweepDeg) / 4));
            g.BeginFigure(new Point(cx + Math.Cos(startRad) * radius, cy + Math.Sin(startRad) * radius), false);
            for (var i = 1; i <= steps; i++)
            {
                var a = startRad + sweepRad * (i / (double)steps);
                g.LineTo(new Point(cx + Math.Cos(a) * radius, cy + Math.Sin(a) * radius));
            }

            g.EndFigure(false);
        }

        ctx.DrawGeometry(null, new Pen(new SolidColorBrush(color, alpha), Math.Max(1, thickness), lineCap: PenLineCap.Round), stream);
    }

    private double Oscillate(double offset)
    {
        return Math.Sin((_phase + offset) * Math.PI * 2d);
    }

    private double LightningOpacity()
    {
        if (!_isLive)
        {
            return 0.58;
        }

        var pulse = Math.Pow(Math.Max(0, Math.Sin((_phase * 2.8 + 0.15) * Math.PI * 2)), 7);
        return 0.42 + pulse * 0.46;
    }

    private static bool EstimateNightFromPalette(MaterialWeatherPalette palette)
    {
        static double Luma(Color color) => (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255d;
        return (Luma(palette.BackgroundTop) + Luma(palette.BackgroundBottom)) * 0.5 < 0.36;
    }

    private static Color WithAlpha(Color color, double alpha)
    {
        return new Color((byte)Math.Clamp(alpha * 255, 0, 255), color.R, color.G, color.B);
    }

    private static IBrush CreateLinearBrush(Color top, Color bottom, double sx, double sy, double ex, double ey)
    {
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(sx, sy, RelativeUnit.Relative),
            EndPoint = new RelativePoint(ex, ey, RelativeUnit.Relative),
            GradientStops = { new GradientStop(top, 0), new GradientStop(bottom, 1) }
        };
    }
}

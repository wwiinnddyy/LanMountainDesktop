using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public sealed class MaterialWeatherSceneControl : Control
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(66) };
    private MaterialWeatherPalette _palette = MaterialWeatherVisualTheme.ResolvePalette(MaterialWeatherCondition.Clear, false);
    private MaterialWeatherCondition _condition = MaterialWeatherCondition.Clear;
    private string _styleId = WeatherVisualStyleId.Default;
    private double _phase;
    private bool _isLive;
    private bool _isAttached;

    private static readonly Random _rng = new(42);

    public MaterialWeatherSceneControl()
    {
        IsHitTestVisible = false;
        _timer.Tick += (_, _) =>
        {
            _phase = (_phase + 0.008) % 1d;
            InvalidateVisual();
        };
    }

    public void Apply(string? styleId, MaterialWeatherCondition condition, MaterialWeatherPalette palette, bool isLive)
    {
        _styleId = WeatherVisualStyleCatalog.Normalize(styleId);
        _condition = condition;
        _palette = palette;
        _isLive = isLive;
        UpdateTimer();
        InvalidateVisual();
    }

    public void Apply(MaterialWeatherCondition condition, MaterialWeatherPalette palette, bool isLive)
    {
        Apply(_styleId, condition, palette, isLive);
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
        if (rect.Width <= 1 || rect.Height <= 1) return;

        context.DrawRectangle(CreateLinearBrush(_palette.BackgroundTop, _palette.BackgroundBottom, 0, 0, 1, 1), null, rect);

        using (context.PushClip(rect))
        {
            DrawStyleDecoration(context, rect);

            switch (_condition)
            {
                case MaterialWeatherCondition.Rain:
                case MaterialWeatherCondition.Storm:
                    DrawRain(context, rect, _condition == MaterialWeatherCondition.Storm);
                    break;
                case MaterialWeatherCondition.Snow:
                    DrawSnow(context, rect);
                    break;
                case MaterialWeatherCondition.Fog:
                case MaterialWeatherCondition.Haze:
                    DrawFog(context, rect);
                    break;
            }
        }
    }

    private void UpdateTimer()
    {
        if (_isLive && _isAttached) _timer.Start();
        else _timer.Stop();
    }

    private void DrawStyleDecoration(DrawingContext ctx, Rect r)
    {
        var t = Math.Sin(_phase * Math.PI * 2d);
        switch (_styleId)
        {
            case WeatherVisualStyleId.Geometric:
                DrawGeometricDecoration(ctx, r, t);
                break;
            case WeatherVisualStyleId.Breezy:
                DrawBreezyDecoration(ctx, r, t);
                break;
            case WeatherVisualStyleId.LemonFlutter:
                DrawLemonDecoration(ctx, r, t);
                break;
        }
    }

    private void DrawGeometricDecoration(DrawingContext ctx, Rect r, double t)
    {
        var min = Math.Min(r.Width, r.Height);

        DrawRadialGlow(ctx, r.Width * 0.78 + t * 6, r.Height * 0.20 + t * 4, min * 0.55, _palette.PrimaryShape, 0.22, 0.0);
        DrawRadialGlow(ctx, r.Width * 0.12 - t * 4, r.Height * 0.68 + t * 3, min * 0.42, _palette.SecondaryShape, 0.18, 0.0);
        DrawRadialGlow(ctx, r.Width * 0.52, r.Height * 0.82 - t * 5, min * 0.32, _palette.AccentShape, 0.14, 0.0);

        DrawRadialGlow(ctx, r.Width * 0.35 + t * 3, r.Height * 0.12, min * 0.28, _palette.AccentShape, 0.08, 0.0);
        DrawRadialGlow(ctx, r.Width * 0.88 - t * 2, r.Height * 0.55, min * 0.22, _palette.PrimaryShape, 0.10, 0.0);

        DrawArcSegment(ctx, r.Width * 0.65 + t * 4, r.Height * 0.35, min * 0.38, -30, 120, _palette.SecondaryShape, 0.12, 2.5);
        DrawArcSegment(ctx, r.Width * 0.25 - t * 3, r.Height * 0.50, min * 0.30, 45, 90, _palette.AccentShape, 0.10, 2);
    }

    private void DrawBreezyDecoration(DrawingContext ctx, Rect r, double t)
    {
        var min = Math.Min(r.Width, r.Height);

        DrawRadialGlow(ctx, r.Width * 0.72 + t * 5, r.Height * 0.25 + t * 3, min * 0.48, _palette.PrimaryShape, 0.20, 0.0);
        DrawRadialGlow(ctx, r.Width * 0.20 - t * 4, r.Height * 0.60 + t * 4, min * 0.36, _palette.SecondaryShape, 0.16, 0.0);
        DrawRadialGlow(ctx, r.Width * 0.50, r.Height * 0.80 - t * 3, min * 0.28, _palette.AccentShape, 0.12, 0.0);

        for (var i = 0; i < 4; i++)
        {
            var y = r.Height * (0.25 + i * 0.18);
            var shift = Math.Sin(_phase * Math.PI * 2 + i * 1.1) * r.Width * 0.05;
            DrawWaveLine(ctx, r, y, shift, i, _palette.SurfaceTint, 0.10 + i * 0.02);
        }

        DrawArcSegment(ctx, r.Width * 0.80 + t * 3, r.Height * 0.15, min * 0.25, 0, 180, _palette.PrimaryShape, 0.08, 1.5);
        DrawArcSegment(ctx, r.Width * 0.15 - t * 2, r.Height * 0.75, min * 0.20, 90, 180, _palette.AccentShape, 0.08, 1.5);
    }

    private void DrawLemonDecoration(DrawingContext ctx, Rect r, double t)
    {
        var min = Math.Min(r.Width, r.Height);

        switch (_condition)
        {
            case MaterialWeatherCondition.Clear:
            case MaterialWeatherCondition.PartlyCloudy:
            case MaterialWeatherCondition.Unknown:
                DrawSunScene(ctx, r, min, t);
                break;
            case MaterialWeatherCondition.Cloudy:
                DrawCloudScene(ctx, r, min, t);
                break;
            case MaterialWeatherCondition.Rain:
            case MaterialWeatherCondition.Storm:
                DrawRainScene(ctx, r, min, t);
                break;
            case MaterialWeatherCondition.Snow:
                DrawSnowScene(ctx, r, min, t);
                break;
            default:
                DrawSunScene(ctx, r, min, t);
                break;
        }

        DrawRadialGlow(ctx, r.Width * 0.15 - t * 3, r.Height * 0.70 + t * 4, min * 0.30, _palette.SecondaryShape, 0.10, 0.0);
        DrawRadialGlow(ctx, r.Width * 0.85 + t * 2, r.Height * 0.55 - t * 3, min * 0.22, _palette.AccentShape, 0.08, 0.0);
    }

    private void DrawSunScene(DrawingContext ctx, Rect r, double min, double t)
    {
        var cx = r.Width * 0.70;
        var cy = r.Height * 0.25;

        DrawRadialGlow(ctx, cx, cy, min * 0.35, _palette.PrimaryShape, 0.28, 0.0);
        DrawRadialGlow(ctx, cx, cy, min * 0.18, _palette.PrimaryShape, 0.45, 0.10);

        var rayCount = 14;
        var pen = new Pen(new SolidColorBrush(_palette.PrimaryShape, 0.18), Math.Max(2, min * 0.012), lineCap: PenLineCap.Round);
        for (var i = 0; i < rayCount; i++)
        {
            var angle = (i / (double)rayCount) * Math.PI * 2 + t * 0.25;
            var innerR = min * 0.16;
            var outerR = min * 0.30 + Math.Sin(angle * 3 + t * 2) * min * 0.04;
            ctx.DrawLine(pen,
                new Point(cx + Math.Cos(angle) * innerR, cy + Math.Sin(angle) * innerR),
                new Point(cx + Math.Cos(angle) * outerR, cy + Math.Sin(angle) * outerR));
        }
    }

    private void DrawCloudScene(DrawingContext ctx, Rect r, double min, double t)
    {
        DrawRadialGlow(ctx, r.Width * 0.60 + t * 5, r.Height * 0.30, min * 0.40, _palette.PrimaryShape, 0.16, 0.0);
        DrawRadialGlow(ctx, r.Width * 0.35 - t * 3, r.Height * 0.55, min * 0.32, _palette.SecondaryShape, 0.12, 0.0);

        var pen = new Pen(new SolidColorBrush(_palette.PrimaryShape, 0.14), Math.Max(1.5, min * 0.010), lineCap: PenLineCap.Round);
        var drift = t * 6;

        DrawCloudOutline(ctx, r.Width * 0.42 + drift, r.Height * 0.32, min * 0.18, min * 0.12, pen);
        DrawCloudOutline(ctx, r.Width * 0.58 + drift * 0.7, r.Height * 0.26, min * 0.22, min * 0.15, pen);
        DrawCloudOutline(ctx, r.Width * 0.72 + drift * 0.5, r.Height * 0.35, min * 0.14, min * 0.10, pen);
    }

    private void DrawRainScene(DrawingContext ctx, Rect r, double min, double t)
    {
        DrawRadialGlow(ctx, r.Width * 0.65 + t * 4, r.Height * 0.25, min * 0.38, _palette.PrimaryShape, 0.14, 0.0);
        DrawRadialGlow(ctx, r.Width * 0.30 - t * 3, r.Height * 0.50, min * 0.30, _palette.SecondaryShape, 0.10, 0.0);

        var pen = new Pen(new SolidColorBrush(_palette.PrimaryShape, 0.10), Math.Max(1, r.Width / 200), lineCap: PenLineCap.Round);
        var streaks = Math.Clamp((int)(r.Width / 28), 6, 16);
        for (var i = 0; i < streaks; i++)
        {
            var progress = (_phase * 0.5 + i * 0.12) % 1d;
            var x = r.Width * (0.12 + (i % streaks) / (double)streaks * 0.78);
            var y = r.Height * (0.15 + progress * 0.75);
            var len = r.Height * 0.08;
            ctx.DrawLine(pen, new Point(x, y), new Point(x - r.Width * 0.018, y + len));
        }
    }

    private void DrawSnowScene(DrawingContext ctx, Rect r, double min, double t)
    {
        DrawRadialGlow(ctx, r.Width * 0.68 + t * 3, r.Height * 0.22, min * 0.35, _palette.PrimaryShape, 0.16, 0.0);
        DrawRadialGlow(ctx, r.Width * 0.25 - t * 2, r.Height * 0.55, min * 0.28, _palette.AccentShape, 0.10, 0.0);

        var cx = r.Width * 0.72;
        var cy = r.Height * 0.28;
        var sr = min * 0.12;
        var pen = new Pen(new SolidColorBrush(_palette.PrimaryShape, 0.16), Math.Max(1.2, min * 0.008), lineCap: PenLineCap.Round);
        for (var i = 0; i < 6; i++)
        {
            var a = (i / 6d) * Math.PI * 2 + t * 0.15;
            var ex = cx + Math.Cos(a) * sr;
            var ey = cy + Math.Sin(a) * sr;
            ctx.DrawLine(pen, new Point(cx, cy), new Point(ex, ey));
            var br = sr * 0.35;
            var mx = cx + Math.Cos(a) * sr * 0.6;
            var my = cy + Math.Sin(a) * sr * 0.6;
            ctx.DrawLine(pen, new Point(mx, my), new Point(mx + Math.Cos(a + 0.5) * br, my + Math.Sin(a + 0.5) * br));
            ctx.DrawLine(pen, new Point(mx, my), new Point(mx + Math.Cos(a - 0.5) * br, my + Math.Sin(a - 0.5) * br));
        }
    }

    private void DrawRadialGlow(DrawingContext ctx, double cx, double cy, double radius, Color baseColor, double peakAlpha, double centerBoost)
    {
        if (radius < 1) return;

        var peak = (byte)Math.Clamp(peakAlpha * 255, 0, 255);
        var edge = (byte)0;
        var center = (byte)Math.Clamp(centerBoost * 255, 0, 255);

        var brush = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(new Color(Math.Clamp((byte)(peak + center), (byte)0, (byte)255), baseColor.R, baseColor.G, baseColor.B), 0),
                new GradientStop(new Color((byte)(peak * 0.6), baseColor.R, baseColor.G, baseColor.B), 0.4),
                new GradientStop(new Color(edge, baseColor.R, baseColor.G, baseColor.B), 1)
            }
        };

        ctx.DrawEllipse(brush, null, new Point(cx, cy), radius, radius);
    }

    private void DrawArcSegment(DrawingContext ctx, double cx, double cy, double radius, double startDeg, double sweepDeg, Color color, double alpha, double thickness)
    {
        if (radius < 2) return;

        var pen = new Pen(new SolidColorBrush(color, (float)alpha), thickness, lineCap: PenLineCap.Round);

        var stream = new StreamGeometry();
        var g = stream.Open();

        var startRad = startDeg * Math.PI / 180d;
        var sweepRad = sweepDeg * Math.PI / 180d;
        var steps = Math.Max(8, (int)(sweepDeg / 5));

        g.BeginFigure(new Point(cx + Math.Cos(startRad) * radius, cy + Math.Sin(startRad) * radius), false);
        for (var i = 1; i <= steps; i++)
        {
            var a = startRad + sweepRad * (i / (double)steps);
            g.LineTo(new Point(cx + Math.Cos(a) * radius, cy + Math.Sin(a) * radius));
        }
        g.EndFigure(false);

        ctx.DrawGeometry(null, pen, stream);
    }

    private void DrawWaveLine(DrawingContext ctx, Rect r, double baseY, double shift, int index, Color color, double alpha)
    {
        var pen = new Pen(new SolidColorBrush(color, (float)alpha), Math.Max(1.5, r.Width / 100), lineCap: PenLineCap.Round);
        var startX = r.Width * 0.05 + shift;
        var endX = r.Width * 0.95 + shift;

        var stream = new StreamGeometry();
        var g = stream.Open();
        g.BeginFigure(new Point(startX, baseY), false);
        for (var x = startX; x <= endX; x += 3)
        {
            var waveY = baseY + Math.Sin((x - startX) / (endX - startX) * Math.PI * 3 + _phase * Math.PI * 2 + index * 1.3) * (5 + index * 2.5);
            g.LineTo(new Point(x, waveY));
        }
        g.EndFigure(false);
        ctx.DrawGeometry(null, pen, stream);
    }

    private void DrawCloudOutline(DrawingContext ctx, double cx, double cy, double rx, double ry, Pen pen)
    {
        ctx.DrawEllipse(null, pen, new Point(cx, cy), rx, ry);
        ctx.DrawEllipse(null, pen, new Point(cx + rx * 0.6, cy - ry * 0.3), rx * 0.7, ry * 0.7);
        ctx.DrawEllipse(null, pen, new Point(cx - rx * 0.4, cy + ry * 0.2), rx * 0.5, ry * 0.5);
    }

    private void DrawRain(DrawingContext ctx, Rect rect, bool storm)
    {
        var drops = Math.Clamp((int)(rect.Width / 22), 8, 22);
        var brush = new SolidColorBrush(_palette.AccentShape, storm ? 0.72 : 0.52);
        var pen = new Pen(brush, Math.Max(1.4, rect.Width / 150), lineCap: PenLineCap.Round);
        for (var i = 0; i < drops; i++)
        {
            var t = (_phase + i * 0.137) % 1d;
            var x = rect.Width * (0.18 + (i % drops) / (double)drops * 0.72);
            var y = rect.Height * (0.36 + t * 0.66);
            ctx.DrawLine(pen, new Point(x, y), new Point(x - rect.Width * 0.025, y + rect.Height * 0.09));
        }

        if (storm)
        {
            var bolt = new StreamGeometry();
            var g = bolt.Open();
            g.BeginFigure(new Point(rect.Width * 0.70, rect.Height * 0.42), true);
            g.LineTo(new Point(rect.Width * 0.61, rect.Height * 0.64));
            g.LineTo(new Point(rect.Width * 0.69, rect.Height * 0.61));
            g.LineTo(new Point(rect.Width * 0.58, rect.Height * 0.86));
            g.EndFigure(true);
            ctx.DrawGeometry(new SolidColorBrush(_palette.AccentShape, 0.86), null, bolt);
        }
    }

    private void DrawSnow(DrawingContext ctx, Rect rect)
    {
        var flakes = Math.Clamp((int)(rect.Width / 24), 7, 20);
        var brush = new SolidColorBrush(Colors.White, 0.72);
        for (var i = 0; i < flakes; i++)
        {
            var t = (_phase * 0.45 + i * 0.113) % 1d;
            var x = rect.Width * (0.12 + (i % flakes) / (double)flakes * 0.78) + Math.Sin(t * Math.PI * 2) * 8;
            var y = rect.Height * (0.20 + t * 0.82);
            ctx.DrawEllipse(brush, null, new Point(x, y), 2.2, 2.2);
        }
    }

    private void DrawFog(DrawingContext ctx, Rect rect)
    {
        var pen = new Pen(new SolidColorBrush(_palette.TextSecondary, 0.28), Math.Max(2, rect.Height / 56), lineCap: PenLineCap.Round);
        for (var i = 0; i < 4; i++)
        {
            var y = rect.Height * (0.48 + i * 0.11);
            var shift = Math.Sin(_phase * Math.PI * 2 + i) * rect.Width * 0.04;
            ctx.DrawLine(pen, new Point(rect.Width * 0.18 + shift, y), new Point(rect.Width * 0.82 + shift, y));
        }
    }

    private IBrush CreateLinearBrush(Color top, Color bottom, double sx, double sy, double ex, double ey)
    {
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(sx, sy, RelativeUnit.Relative),
            EndPoint = new RelativePoint(ex, ey, RelativeUnit.Relative),
            GradientStops = { new GradientStop(top, 0), new GradientStop(bottom, 1) }
        };
    }
}

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;

namespace LanMountainDesktop.Controls;

/// <summary>
/// A Decorator that renders a border with continuous "Squircle" corners (super-ellipse).
/// Ported and adapted from SeiWoLauncherPro for Avalonia 11.
/// </summary>
public class SmoothBorder : Decorator
{
    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        Border.BackgroundProperty.AddOwner<SmoothBorder>();

    public static readonly StyledProperty<IBrush?> BorderBrushProperty =
        Border.BorderBrushProperty.AddOwner<SmoothBorder>();

    public static readonly StyledProperty<Thickness> BorderThicknessProperty =
        Border.BorderThicknessProperty.AddOwner<SmoothBorder>();

    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        Border.CornerRadiusProperty.AddOwner<SmoothBorder>();

    public static readonly StyledProperty<double> SmoothnessProperty =
        AvaloniaProperty.Register<SmoothBorder, double>(nameof(Smoothness), 0.6);

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public IBrush? BorderBrush
    {
        get => GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    public Thickness BorderThickness
    {
        get => GetValue(BorderThicknessProperty);
        set => SetValue(BorderThicknessProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public double Smoothness
    {
        get => GetValue(SmoothnessProperty);
        set => SetValue(SmoothnessProperty, value);
    }

    static SmoothBorder()
    {
        AffectsRender<SmoothBorder>(BackgroundProperty, BorderBrushProperty, BorderThicknessProperty, CornerRadiusProperty, SmoothnessProperty);
        AffectsMeasure<SmoothBorder>(BorderThicknessProperty);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var padding = BorderThickness;
        if (Child != null)
        {
            Child.Measure(constraint.Deflate(padding));
            return Child.DesiredSize.Inflate(padding);
        }
        return new Size(padding.Left + padding.Right, padding.Top + padding.Bottom);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child != null)
        {
            var padding = BorderThickness;
            Child.Arrange(new Rect(finalSize).Deflate(padding));
            Child.Clip = CreateSquircle(new Rect(0, 0, finalSize.Width - padding.Left - padding.Right, finalSize.Height - padding.Top - padding.Bottom), CornerRadius, Smoothness);
        }
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        var rect = new Rect(Bounds.Size);
        if (rect.Width <= 0 || rect.Height <= 0) return;

        var geometry = CreateSquircle(rect, CornerRadius, Smoothness);
        
        if (Background != null)
        {
            context.DrawGeometry(Background, null, geometry);
        }

        if (BorderBrush != null && BorderThickness != default)
        {
            // Simple implementation for uniform thickness
            var pen = new Pen(BorderBrush, BorderThickness.Left);
            context.DrawGeometry(null, pen, geometry);
        }
        
        // Apply clipping to children if needed
        // Note: In Avalonia 11, we usually set Clip property on the child or use a Clip content property.
    }

    private static Geometry CreateSquircle(Rect rect, CornerRadius radius, double smoothness)
    {
        smoothness = Math.Clamp(smoothness, 0, 1);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            // Top-left starting point
            double pTL = radius.TopLeft * (1 + smoothness);
            ctx.BeginFigure(new Point(rect.Left + pTL, rect.Top), true);

            // Top-right corner
            DrawCorner(ctx, rect, radius.TopRight, smoothness, Corner.TopRight);
            // Bottom-right corner
            DrawCorner(ctx, rect, radius.BottomRight, smoothness, Corner.BottomRight);
            // Bottom-left corner
            DrawCorner(ctx, rect, radius.BottomLeft, smoothness, Corner.BottomLeft);
            // Top-left corner (closing)
            DrawCorner(ctx, rect, radius.TopLeft, smoothness, Corner.TopLeft);

            ctx.EndFigure(true);
        }
        return geometry;
    }

    private enum Corner { TopRight, BottomRight, BottomLeft, TopLeft }

    private static void DrawCorner(StreamGeometryContext ctx, Rect rect, double radius, double smoothness, Corner corner)
    {
        if (radius <= 0)
        {
            Point pt = corner switch {
                Corner.TopRight => rect.TopRight,
                Corner.BottomRight => rect.BottomRight,
                Corner.BottomLeft => rect.BottomLeft,
                Corner.TopLeft => rect.TopLeft,
                _ => default
            };
            ctx.LineTo(pt);
            return;
        }

        double p = radius * (1 + smoothness);
        double theta = 45 * smoothness;
        double radTheta = theta * (Math.PI / 180.0);
        double radBeta = (90 * (1 - smoothness)) * (Math.PI / 180.0);

        double c = radius * Math.Tan(radTheta / 2) * Math.Cos(radTheta);
        double d = radius * Math.Tan(radTheta / 2) * Math.Sin(radTheta);
        double arcSeg = Math.Sin(radBeta / 2) * radius * Math.Sqrt(2);

        double b = (p - arcSeg - c - d) / 3;
        double a = 2 * b;

        // Points relative to corner
        Point[] points = corner switch
        {
            Corner.TopRight => new[] {
                new Point(rect.Right - (p - a - b - c), rect.Top + d), 
                new Point(rect.Right - (p - a), rect.Top), 
                new Point(rect.Right - (p - a - b), rect.Top),
                new Point(rect.Right, rect.Top + p), 
                new Point(rect.Right, rect.Top + p - a - b), 
                new Point(rect.Right, rect.Top + p - a)
            },
            Corner.BottomRight => new[] {
                new Point(rect.Right - d, rect.Bottom - (p - a - b - c)), 
                new Point(rect.Right, rect.Bottom - (p - a)), 
                new Point(rect.Right, rect.Bottom - (p - a - b)),
                new Point(rect.Right - p, rect.Bottom), 
                new Point(rect.Right - (p - a - b), rect.Bottom), 
                new Point(rect.Right - (p - a), rect.Bottom)
            },
            Corner.BottomLeft => new[] {
                new Point(rect.Left + (p - a - b - c), rect.Bottom - d), 
                new Point(rect.Left + (p - a), rect.Bottom), 
                new Point(rect.Left + (p - a - b), rect.Bottom),
                new Point(rect.Left, rect.Bottom - p), 
                new Point(rect.Left, rect.Bottom - (p - a - b)), 
                new Point(rect.Left, rect.Bottom - (p - a))
            },
            Corner.TopLeft => new[] {
                new Point(rect.Left + d, rect.Top + (p - a - b - c)), 
                new Point(rect.Left, rect.Top + (p - a)), 
                new Point(rect.Left, rect.Top + (p - a - b)),
                new Point(rect.Left + p, rect.Top), 
                new Point(rect.Left + (p - a - b), rect.Top), 
                new Point(rect.Left + (p - a), rect.Top)
            },
            _ => throw new ArgumentOutOfRangeException()
        };

        // 1. Line to start of segment
        ctx.LineTo(corner switch {
            Corner.TopRight => new Point(rect.Right - p, rect.Top),
            Corner.BottomRight => new Point(rect.Right, rect.Bottom - p),
            Corner.BottomLeft => new Point(rect.Left + p, rect.Bottom),
            Corner.TopLeft => new Point(rect.Left, rect.Top + p),
            _ => default
        });

        // 2. First Bezier
        ctx.CubicBezierTo(points[1], points[2], points[0]);

        // 3. Arc
        double startAngle = corner switch {
            Corner.TopRight => 270, Corner.BottomRight => 0, Corner.BottomLeft => 90, Corner.TopLeft => 180, _ => 0
        };
        double arcEndAngle = startAngle + 90 - theta;
        double endRad = arcEndAngle * (Math.PI / 180.0);
        Point center = corner switch {
            Corner.TopRight => new Point(rect.Right - radius, rect.Top + radius),
            Corner.BottomRight => new Point(rect.Right - radius, rect.Bottom - radius),
            Corner.BottomLeft => new Point(rect.Left + radius, rect.Bottom - radius),
            Corner.TopLeft => new Point(rect.Left + radius, rect.Top + radius),
            _ => default
        };
        Point arcEnd = new Point(center.X + radius * Math.Cos(endRad), center.Y + radius * Math.Sin(endRad));
        
        ctx.ArcTo(arcEnd, new Size(radius, radius), 0, false, SweepDirection.Clockwise);

        // 4. Second Bezier
        ctx.CubicBezierTo(points[4], points[5], points[3]);
    }
}

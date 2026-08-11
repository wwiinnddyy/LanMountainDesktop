using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace LanMountainDesktop.Controls;

public class CornerRadiusPreviewControl : Control
{
    public static readonly StyledProperty<double> RadiusProperty =
        AvaloniaProperty.Register<CornerRadiusPreviewControl, double>(nameof(Radius), 24);

    public static readonly StyledProperty<IBrush?> ShapeBrushProperty =
        AvaloniaProperty.Register<CornerRadiusPreviewControl, IBrush?>(nameof(ShapeBrush));

    public static readonly StyledProperty<IBrush?> GuideBrushProperty =
        AvaloniaProperty.Register<CornerRadiusPreviewControl, IBrush?>(nameof(GuideBrush));

    public static readonly StyledProperty<IBrush?> FillBrushProperty =
        AvaloniaProperty.Register<CornerRadiusPreviewControl, IBrush?>(nameof(FillBrush));

    public double Radius
    {
        get => GetValue(RadiusProperty);
        set => SetValue(RadiusProperty, value);
    }

    public IBrush? ShapeBrush
    {
        get => GetValue(ShapeBrushProperty);
        set => SetValue(ShapeBrushProperty, value);
    }

    public IBrush? GuideBrush
    {
        get => GetValue(GuideBrushProperty);
        set => SetValue(GuideBrushProperty, value);
    }

    public IBrush? FillBrush
    {
        get => GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    static CornerRadiusPreviewControl()
    {
        AffectsRender<CornerRadiusPreviewControl>(
            RadiusProperty,
            ShapeBrushProperty,
            GuideBrushProperty,
            FillBrushProperty);
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        var shapeBrush = ShapeBrush ?? Brushes.Gray;
        var guideBrush = GuideBrush ?? Brushes.Gray;
        var fillBrush = FillBrush ?? new SolidColorBrush(Colors.Gray, 0.08);

        var padding = 24.0;
        var maxShapeW = w - padding * 2;
        var maxShapeH = h - padding * 2;
        var shapeSize = Math.Min(maxShapeW, maxShapeH);

        if (shapeSize < 20) return;

        var ox = (w - shapeSize) / 2;
        var oy = (h - shapeSize) / 2;

        var r = Math.Min(Radius, shapeSize * 0.45);
        r = Math.Max(0, r);

        var shapeRect = new Rect(ox, oy, shapeSize, shapeSize);
        var shapePen = new Pen(shapeBrush, 1.5);
        var dashPen = new Pen(guideBrush, 0.75, new DashStyle([4, 3], 0));

        context.DrawRectangle(fillBrush, shapePen, shapeRect, r, r);

        if (r > 4)
        {
            var arcCenterX = ox + r;
            var arcCenterY = oy + r;

            context.DrawLine(
                dashPen,
                new Point(arcCenterX, oy + r * 0.2),
                new Point(arcCenterX, oy + r * 0.9));

            context.DrawLine(
                dashPen,
                new Point(ox + r * 0.2, arcCenterY),
                new Point(ox + r * 0.9, arcCenterY));

            context.DrawEllipse(null, new Pen(guideBrush, 0.75), new Point(arcCenterX, arcCenterY), 2, 2);
        }
    }
}

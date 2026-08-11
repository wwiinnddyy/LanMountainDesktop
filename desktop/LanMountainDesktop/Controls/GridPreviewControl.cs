using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace LanMountainDesktop.Controls;

public class GridPreviewControl : Control
{
    public static readonly StyledProperty<int> CellsProperty =
        AvaloniaProperty.Register<GridPreviewControl, int>(nameof(Cells), 12);

    public static readonly StyledProperty<double> AspectRatioProperty =
        AvaloniaProperty.Register<GridPreviewControl, double>(nameof(AspectRatio), 16.0 / 9.0);

    public static readonly StyledProperty<int> EdgeInsetPercentProperty =
        AvaloniaProperty.Register<GridPreviewControl, int>(nameof(EdgeInsetPercent), 0);

    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<GridPreviewControl, IBrush?>(nameof(GridBrush));

    public static readonly StyledProperty<IBrush?> ScreenBorderBrushProperty =
        AvaloniaProperty.Register<GridPreviewControl, IBrush?>(nameof(ScreenBorderBrush));

    public static readonly StyledProperty<IBrush?> InsetBrushProperty =
        AvaloniaProperty.Register<GridPreviewControl, IBrush?>(nameof(InsetBrush));

    public int Cells
    {
        get => GetValue(CellsProperty);
        set => SetValue(CellsProperty, value);
    }

    public double AspectRatio
    {
        get => GetValue(AspectRatioProperty);
        set => SetValue(AspectRatioProperty, value);
    }

    public int EdgeInsetPercent
    {
        get => GetValue(EdgeInsetPercentProperty);
        set => SetValue(EdgeInsetPercentProperty, value);
    }

    public IBrush? GridBrush
    {
        get => GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public IBrush? ScreenBorderBrush
    {
        get => GetValue(ScreenBorderBrushProperty);
        set => SetValue(ScreenBorderBrushProperty, value);
    }

    public IBrush? InsetBrush
    {
        get => GetValue(InsetBrushProperty);
        set => SetValue(InsetBrushProperty, value);
    }

    static GridPreviewControl()
    {
        AffectsRender<GridPreviewControl>(
            CellsProperty,
            AspectRatioProperty,
            EdgeInsetPercentProperty,
            GridBrushProperty,
            ScreenBorderBrushProperty,
            InsetBrushProperty);
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        var ratio = AspectRatio > 0 ? AspectRatio : 16.0 / 9.0;
        double screenW, screenH;
        if (w / h > ratio)
        {
            screenH = h;
            screenW = h * ratio;
        }
        else
        {
            screenW = w;
            screenH = w / ratio;
        }

        var offsetX = (w - screenW) / 2;
        var offsetY = (h - screenH) / 2;

        var borderBrush = ScreenBorderBrush ?? Brushes.Gray;
        var gridBrush = GridBrush ?? Brushes.Gray;
        var insetBrush = InsetBrush ?? new SolidColorBrush(Colors.Gray, 0.12);

        var borderThickness = 1.5;
        var borderPen = new Pen(borderBrush, borderThickness);

        var cells = Math.Max(1, Cells);
        var cellSize = screenW / cells;
        var rows = (int)Math.Floor(screenH / cellSize);

        var insetPercent = Math.Clamp(EdgeInsetPercent, 0, 30);
        var insetRatio = insetPercent / 100d;
        var insetPx = Math.Min(cellSize * insetRatio, screenH * 0.15);

        var contentX = offsetX + insetPx;
        var contentY = offsetY + insetPx;
        var contentW = Math.Max(1, screenW - insetPx * 2);
        var contentH = Math.Max(1, screenH - insetPx * 2);

        var screenRect = new Rect(offsetX, offsetY, screenW, screenH);
        var contentRect = new Rect(contentX, contentY, contentW, contentH);

        var cornerRadius = Math.Min(6, Math.Min(screenW, screenH) * 0.03);

        using (context.PushClip(screenRect))
        {
            if (insetPx > 0.5)
            {
                context.DrawRectangle(insetBrush, null, screenRect, cornerRadius, cornerRadius);
                context.DrawRectangle(
                    new SolidColorBrush(Colors.Transparent),
                    new Pen(borderBrush, 0.75, new DashStyle([3, 3], 0)),
                    contentRect,
                    cornerRadius * 0.6,
                    cornerRadius * 0.6);
            }

            var dashSegment = Math.Max(2, cellSize * 0.25);
            var dashPen = new Pen(gridBrush, 1.0, new DashStyle([dashSegment, dashSegment], 0));

            for (var col = 1; col < cells; col++)
            {
                var x = contentX + col * (contentW / cells);
                if (x < contentX + contentW)
                {
                    context.DrawLine(dashPen, new Point(x, contentY), new Point(x, contentY + contentH));
                }
            }

            for (var row = 1; row < rows; row++)
            {
                var y = contentY + row * (contentH / rows);
                if (y < contentY + contentH)
                {
                    context.DrawLine(dashPen, new Point(contentX, y), new Point(contentX + contentW, y));
                }
            }
        }

        var adjustedRect = screenRect.Deflate(borderThickness / 2);
        context.DrawRectangle(null, borderPen, adjustedRect, cornerRadius, cornerRadius);
    }
}

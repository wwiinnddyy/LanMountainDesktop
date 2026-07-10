using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

using DotNetCampus.Inking.Contexts;
using DotNetCampus.Inking.Erasing;
using DotNetCampus.Inking.Interactives;
using DotNetCampus.Inking.Primitive;
using DotNetCampus.Inking.Utils;

namespace DotNetCampus.Inking;

public class InkCanvas : Control
{
    public InkCanvas()
    {
        var avaloniaSkiaInkCanvas = new AvaloniaSkiaInkCanvas()
        {
            IsHitTestVisible = false
        };
        AddChild(avaloniaSkiaInkCanvas);
        AvaloniaSkiaInkCanvas = avaloniaSkiaInkCanvas;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
    }

    public InkCanvasEditingMode EditingMode
    {
        get => _editingMode;
        set
        {
            if (IsDuringInput)
            {
                throw new InvalidOperationException($"EditingMode should not be switched during the input process.");
            }

            _editingMode = value;
            EditingModeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? EditingModeChanged;

    public IReadOnlyList<SkiaStroke> Strokes => AvaloniaSkiaInkCanvas.StaticStrokeList;

    private InkCanvasEditingMode _editingMode = InkCanvasEditingMode.Ink;

    /// <summary>
    /// 为 Avalonia 实现的基于 Skia 的 InkCanvas 笔迹画布
    /// </summary>
    public AvaloniaSkiaInkCanvas AvaloniaSkiaInkCanvas { get; }

    private AvaloniaSkiaInkCanvasEraserMode EraserMode => AvaloniaSkiaInkCanvas.EraserMode;

    /// <inheritdoc cref="AvaloniaSkiaInkCanvas.StrokeCollected"/>
    public event EventHandler<AvaloniaSkiaInkCanvasStrokeCollectedEventArgs>? StrokeCollected
    {
        add => AvaloniaSkiaInkCanvas.StrokeCollected += value;
        remove => AvaloniaSkiaInkCanvas.StrokeCollected -= value;
    }

    public event EventHandler<ErasingCompletedEventArgs>? StrokeErased
    {
        add => EraserMode.ErasingCompleted += value;
        remove => EraserMode.ErasingCompleted -= value;
    }

    #region Input

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (EditingMode == InkCanvasEditingMode.None)
        {
            return;
        }

        var args = ToArgs(e);
        var wasDuringInput = IsDuringInput;
        _inputDictionary[e.Pointer.Id] = new InputInfo(args.StylusPoint);
        e.Pointer.Capture(this);

        if (EditingMode == InkCanvasEditingMode.Ink)
        {
            if (!wasDuringInput)
            {
                AvaloniaSkiaInkCanvas.WritingStart();
            }

            AvaloniaSkiaInkCanvas.WritingDown(in args);
        }
        else if (EditingMode == InkCanvasEditingMode.EraseByPoint)
        {
            EraserMode.EraserDown(in args);
        }

        base.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (EditingMode == InkCanvasEditingMode.None)
        {
            return;
        }

        if (!_inputDictionary.TryGetValue(e.Pointer.Id, out var inputInfo))
        {
            // Mouse? Not pressed yet.
            return;
        }

        var args = ToArgs(e, inputInfo);

        if (EditingMode == InkCanvasEditingMode.Ink)
        {
            AvaloniaSkiaInkCanvas.WritingMove(in args);
        }
        else if (EditingMode == InkCanvasEditingMode.EraseByPoint)
        {
            EraserMode.EraserMove(in args);
        }

        inputInfo.LastStylusPoint = args.StylusPoint;
        base.OnPointerMoved(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (EditingMode == InkCanvasEditingMode.None)
        {
            return;
        }

        if (!_inputDictionary.Remove(e.Pointer.Id, out var inputInfo))
        {
            // Mouse? Not pressed yet.
            return;
        }

        var args = ToArgs(e, inputInfo);
        inputInfo.LastStylusPoint = args.StylusPoint;

        if (EditingMode == InkCanvasEditingMode.Ink)
        {
            AvaloniaSkiaInkCanvas.WritingUp(in args);

            if (!IsDuringInput)
            {
                AvaloniaSkiaInkCanvas.WritingCompleted();
            }
        }
        else if (EditingMode == InkCanvasEditingMode.EraseByPoint)
        {
            EraserMode.EraserUp(in args);
        }

        e.Pointer.Capture(null);
        base.OnPointerReleased(e);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        if (_inputDictionary.Remove(e.Pointer.Id, out var inputInfo))
        {
            var args = new InkingModeInputArgs(e.Pointer.Id, inputInfo.LastStylusPoint, 0);
            if (EditingMode == InkCanvasEditingMode.Ink)
            {
                AvaloniaSkiaInkCanvas.WritingUp(in args);

                if (!IsDuringInput)
                {
                    AvaloniaSkiaInkCanvas.WritingCompleted();
                }
            }
            else if (EditingMode == InkCanvasEditingMode.EraseByPoint)
            {
                EraserMode.EraserUp(in args);
            }
        }

        base.OnPointerCaptureLost(e);
    }

    class InputInfo
    {
        public InputInfo(InkStylusPoint lastStylusPoint)
        {
            LastStylusPoint = lastStylusPoint;
        }

        public InkStylusPoint LastStylusPoint { get; set; }
    }

    private readonly Dictionary<int /*Id*/, InputInfo> _inputDictionary = [];
    private bool IsDuringInput => _inputDictionary.Count != 0;

    private InkingModeInputArgs ToArgs(PointerEventArgs args, InputInfo? inputInfo = null)
    {
        PointerPoint currentPoint = args.GetCurrentPoint(AvaloniaSkiaInkCanvas);
        InkStylusPoint? lastStylusPoint = inputInfo?.LastStylusPoint;

        List<InkStylusPoint>? stylusPointList = null;
        var intermediatePointList = args.GetIntermediatePoints(AvaloniaSkiaInkCanvas);
        if (intermediatePointList.Count > 0)
        {
            var orderedIntermediatePointList = OrderIntermediatePoints(intermediatePointList, lastStylusPoint);
            stylusPointList = new List<InkStylusPoint>(orderedIntermediatePointList.Count + 1);

            foreach (var intermediatePoint in orderedIntermediatePointList)
            {
                var stylusPoint = ToInkStylusPoint(intermediatePoint, lastStylusPoint);
                AddDistinctPoint(stylusPointList, stylusPoint);
                lastStylusPoint = stylusPoint;
            }
        }

        var inkStylusPoint = ToInkStylusPoint(currentPoint, lastStylusPoint);
        if (stylusPointList is not null)
        {
            AddDistinctPoint(stylusPointList, inkStylusPoint);
        }

        return new InkingModeInputArgs(args.Pointer.Id, inkStylusPoint, args.Timestamp)
        {
            StylusPointList = stylusPointList is { Count: > 0 } ? stylusPointList : null,
        };

        InkStylusPoint ToInkStylusPoint(PointerPoint point, InkStylusPoint? previousPoint)
        {
            var pressure = EnsurePressure(point.Properties.Pressure, previousPoint);
            var contactRect = point.Properties.ContactRect;
            var width = contactRect.Width;
            var height = contactRect.Height;

            if (previousPoint is not null)
            {
                if (width == 0 && previousPoint.Value.Width is { } lastWidth)
                {
                    width = lastWidth;
                }

                if (height == 0 && previousPoint.Value.Height is { } lastHeight)
                {
                    height = lastHeight;
                }
            }

            var stylusPoint = new InkStylusPoint(point.Position.ToPoint2D(), pressure)
            {
                Width = width != 0 ? width : null,
                Height = height != 0 ? height : null,
            };

            return stylusPoint;
        }

        float EnsurePressure(float pressure, InkStylusPoint? previousPoint)
        {
            // 这是一个修复补丁。在 Linux X11 上，如果前后两个点的压力是相同的，则后点将不会报告压力，此时 Avalonia 上将使用默认压力值 0.5 来填充压力值
            // 为了避免压力值抖动，将压力值修正为上一个点的压力值
            const float defaultPressure = InkStylusPoint.DefaultPressure;
            if (previousPoint is not null && (pressure == 0 || Math.Abs(pressure - defaultPressure) < 0.00001))
            {
                return previousPoint.Value.Pressure;
            }

            return pressure;
        }
    }

    private static IReadOnlyList<PointerPoint> OrderIntermediatePoints(
        IReadOnlyList<PointerPoint> intermediatePointList,
        InkStylusPoint? previousPoint)
    {
        if (previousPoint is null || intermediatePointList.Count <= 1)
        {
            return intermediatePointList;
        }

        var firstDistance = GetDistanceSquared(previousPoint.Value, intermediatePointList[0]);
        var lastDistance = GetDistanceSquared(previousPoint.Value, intermediatePointList[^1]);
        if (lastDistance >= firstDistance)
        {
            return intermediatePointList;
        }

        var orderedList = intermediatePointList.ToList();
        orderedList.Reverse();
        return orderedList;
    }

    private static double GetDistanceSquared(InkStylusPoint point, PointerPoint pointerPoint)
    {
        var dx = point.X - pointerPoint.Position.X;
        var dy = point.Y - pointerPoint.Position.Y;
        return dx * dx + dy * dy;
    }

    private static void AddDistinctPoint(List<InkStylusPoint> pointList, InkStylusPoint point)
    {
        if (pointList.Count > 0 && AreSamePosition(pointList[^1], point))
        {
            return;
        }

        pointList.Add(point);
    }

    private static bool AreSamePosition(InkStylusPoint first, InkStylusPoint second)
    {
        const double tolerance = 0.001d;
        return Math.Abs(first.X - second.X) < tolerance &&
               Math.Abs(first.Y - second.Y) < tolerance;
    }

    #endregion

    internal void AddChild(Control childControl)
    {
        LogicalChildren.Add(childControl);
        VisualChildren.Add(childControl);
    }

    internal void RemoveChild(Control childControl)
    {
        LogicalChildren.Remove(childControl);
        VisualChildren.Remove(childControl);
    }

    protected override Size MeasureCore(Size availableSize)
    {
        var width = availableSize.Width;
        var height = availableSize.Height;

        if (double.IsInfinity(width))
        {
            width = 0;
        }

        if (double.IsInfinity(height))
        {
            height = 0;
        }

        base.MeasureCore(availableSize);
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);

        return size;
    }

    public override void Render(DrawingContext context)
    {
        // to enable hit testing
        context.DrawRectangle(Brushes.Transparent, null, new Rect(new Point(), Bounds.Size));
    }


}

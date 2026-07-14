using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;

namespace LanMountainDesktop.Views;

internal enum ResizeHandlePosition
{
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left
}

internal sealed class DesktopWidgetResizeHandle : Control
{
    public static readonly StyledProperty<ResizeHandlePosition> PositionProperty =
        AvaloniaProperty.Register<DesktopWidgetResizeHandle, ResizeHandlePosition>(
            nameof(Position),
            ResizeHandlePosition.BottomRight);

    public ResizeHandlePosition Position
    {
        get => GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    private const double HandleSize = 12d;
    private const double CornerHandleSize = 16d;
    private const double EdgeHandleThickness = 4d;

    public DesktopWidgetResizeHandle()
    {
        Width = HandleSize;
        Height = HandleSize;
        Cursor = GetCursorForPosition(Position);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == PositionProperty)
        {
            Cursor = GetCursorForPosition(Position);
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var fillBrush = new SolidColorBrush(Colors.White, 0.9);
        var borderBrush = new SolidColorBrush(Color.Parse("#0078D4"), 0.8);
        var pen = new Pen(borderBrush, 1.5);

        var isCorner = IsCornerHandle(Position);
        if (isCorner)
        {
            var rect = new Rect(0, 0, Bounds.Width, Bounds.Height);
            context.DrawRectangle(fillBrush, pen, rect, 2, 2);
        }
        else
        {
            var rect = new Rect(0, 0, Bounds.Width, Bounds.Height);
            context.DrawRectangle(fillBrush, pen, rect, 1, 1);
        }
    }

    private static bool IsCornerHandle(ResizeHandlePosition position)
    {
        return position is ResizeHandlePosition.TopLeft or ResizeHandlePosition.TopRight or
               ResizeHandlePosition.BottomLeft or ResizeHandlePosition.BottomRight;
    }

    private static Cursor GetCursorForPosition(ResizeHandlePosition position)
    {
        return position switch
        {
            ResizeHandlePosition.TopLeft => new Cursor(StandardCursorType.TopLeftCorner),
            ResizeHandlePosition.Top => new Cursor(StandardCursorType.TopSide),
            ResizeHandlePosition.TopRight => new Cursor(StandardCursorType.TopRightCorner),
            ResizeHandlePosition.Right => new Cursor(StandardCursorType.RightSide),
            ResizeHandlePosition.BottomRight => new Cursor(StandardCursorType.BottomRightCorner),
            ResizeHandlePosition.Bottom => new Cursor(StandardCursorType.BottomSide),
            ResizeHandlePosition.BottomLeft => new Cursor(StandardCursorType.BottomLeftCorner),
            ResizeHandlePosition.Left => new Cursor(StandardCursorType.LeftSide),
            _ => new Cursor(StandardCursorType.Arrow)
        };
    }

    public Size GetHandleSize(ResizeHandlePosition position)
    {
        return IsCornerHandle(position)
            ? new Size(CornerHandleSize, CornerHandleSize)
            : position is ResizeHandlePosition.Left or ResizeHandlePosition.Right
                ? new Size(EdgeHandleThickness, HandleSize)
                : new Size(HandleSize, EdgeHandleThickness);
    }
}

internal sealed class DesktopWidgetResizeAdorner : Canvas
{
    private readonly DesktopWidgetResizeHandle[] _handles;
    private bool _isVisible;

    public event EventHandler<ResizeStartedEventArgs>? ResizeStarted;
    public event EventHandler<ResizeEventArgs>? Resizing;
    public event EventHandler<ResizeCompletedEventArgs>? ResizeCompleted;

    private ResizeHandlePosition _activeHandle;
    private bool _isResizing;
    private PixelPoint _resizeStartScreenPoint;
    private Rect _resizeStartBounds;

    public DesktopWidgetResizeAdorner()
    {
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        Background = new SolidColorBrush(Colors.Transparent);

        _handles = new DesktopWidgetResizeHandle[8];
        for (var i = 0; i < 8; i++)
        {
            var handle = new DesktopWidgetResizeHandle
            {
                Position = (ResizeHandlePosition)i,
                IsVisible = false
            };
            handle.PointerPressed += OnHandlePointerPressed;
            handle.PointerMoved += OnHandlePointerMoved;
            handle.PointerReleased += OnHandlePointerReleased;
            _handles[i] = handle;
            Children.Add(handle);
        }
    }

    public void Show()
    {
        if (_isVisible) return;
        _isVisible = true;
        IsVisible = true;
        foreach (var handle in _handles)
        {
            handle.IsVisible = true;
        }
        UpdateHandlePositions();
    }

    public void Hide()
    {
        if (!_isVisible) return;
        _isVisible = false;
        IsVisible = false;
        foreach (var handle in _handles)
        {
            handle.IsVisible = false;
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_isVisible && !_isResizing)
        {
            UpdateHandlePositions();
        }
    }

    private void UpdateHandlePositions()
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;

        var width = Bounds.Width;
        var height = Bounds.Height;

        foreach (var handle in _handles)
        {
            var size = handle.GetHandleSize(handle.Position);
            handle.Width = size.Width;
            handle.Height = size.Height;

            var (left, top) = handle.Position switch
            {
                ResizeHandlePosition.TopLeft => (0d, 0d),
                ResizeHandlePosition.Top => ((width - size.Width) / 2d, 0d),
                ResizeHandlePosition.TopRight => (width - size.Width, 0d),
                ResizeHandlePosition.Right => (width - size.Width, (height - size.Height) / 2d),
                ResizeHandlePosition.BottomRight => (width - size.Width, height - size.Height),
                ResizeHandlePosition.Bottom => ((width - size.Width) / 2d, height - size.Height),
                ResizeHandlePosition.BottomLeft => (0d, height - size.Height),
                ResizeHandlePosition.Left => (0d, (height - size.Height) / 2d),
                _ => (0d, 0d)
            };

            // A resize affordance must never extend beyond the native window. Transparent
            // pixels outside an HWND cannot be composed or hit-tested reliably after the
            // window is attached to Explorer's desktop host.
            SetLeft(handle, Math.Clamp(left, 0d, Math.Max(0d, width - size.Width)));
            SetTop(handle, Math.Clamp(top, 0d, Math.Max(0d, height - size.Height)));
        }
    }

    private void OnHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not DesktopWidgetResizeHandle handle) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        _isResizing = true;
        _activeHandle = handle.Position;
        _resizeStartScreenPoint = this.PointToScreen(e.GetPosition(this));
        _resizeStartBounds = Bounds;
        e.Pointer.Capture(handle);

        ResizeStarted?.Invoke(this, new ResizeStartedEventArgs(_activeHandle, _resizeStartBounds));
        e.Handled = true;
    }

    private void OnHandlePointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isResizing) return;

        var delta = GetScreenDelta(e);

        Resizing?.Invoke(this, new ResizeEventArgs(_activeHandle, delta, _resizeStartBounds));
        e.Handled = true;
    }

    private void OnHandlePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isResizing) return;

        var delta = GetScreenDelta(e);

        ResizeCompleted?.Invoke(this, new ResizeCompletedEventArgs(_activeHandle, delta, _resizeStartBounds));

        _isResizing = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private Point GetScreenDelta(PointerEventArgs e)
    {
        var currentScreenPoint = this.PointToScreen(e.GetPosition(this));
        return new Point(
            currentScreenPoint.X - _resizeStartScreenPoint.X,
            currentScreenPoint.Y - _resizeStartScreenPoint.Y);
    }
}

internal sealed class ResizeStartedEventArgs : EventArgs
{
    public ResizeHandlePosition Handle { get; }
    public Rect OriginalBounds { get; }

    public ResizeStartedEventArgs(ResizeHandlePosition handle, Rect originalBounds)
    {
        Handle = handle;
        OriginalBounds = originalBounds;
    }
}

internal sealed class ResizeEventArgs : EventArgs
{
    public ResizeHandlePosition Handle { get; }
    public Point Delta { get; }
    public Rect OriginalBounds { get; }

    public ResizeEventArgs(ResizeHandlePosition handle, Point delta, Rect originalBounds)
    {
        Handle = handle;
        Delta = delta;
        OriginalBounds = originalBounds;
    }
}

internal sealed class ResizeCompletedEventArgs : EventArgs
{
    public ResizeHandlePosition Handle { get; }
    public Point Delta { get; }
    public Rect OriginalBounds { get; }

    public ResizeCompletedEventArgs(ResizeHandlePosition handle, Point delta, Rect originalBounds)
    {
        Handle = handle;
        Delta = delta;
        OriginalBounds = originalBounds;
    }
}

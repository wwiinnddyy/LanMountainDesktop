using System;
using Avalonia;

namespace LanMountainDesktop.DesktopEditing;

internal enum DesktopEditSessionMode
{
    None = 0,
    PendingNew,
    DraggingNew,
    DraggingExisting,
    ResizingExisting
}

internal readonly record struct DesktopEditSession
{
    public DesktopEditSessionMode Mode { get; init; }
    public string? ComponentId { get; init; }
    public string? PlacementId { get; init; }
    public int PageIndex { get; init; }
    public int WidthCells { get; init; }
    public int HeightCells { get; init; }
    public Point StartPointerInViewport { get; init; }
    public Point CurrentPointerInViewport { get; init; }
    public Point PointerOffsetInViewport { get; init; }
    public Rect? ComponentLibraryBounds { get; init; }
    public int TargetRow { get; init; }
    public int TargetColumn { get; init; }

    public bool IsActive => Mode != DesktopEditSessionMode.None;
    public bool IsPendingNew => Mode == DesktopEditSessionMode.PendingNew;
    public bool IsDraggingNew => Mode == DesktopEditSessionMode.DraggingNew;
    public bool IsDraggingExisting => Mode == DesktopEditSessionMode.DraggingExisting;
    public bool IsResizingExisting => Mode == DesktopEditSessionMode.ResizingExisting;
    public bool HasTargetCell => TargetRow >= 0 && TargetColumn >= 0;

    public double PointerTravelDistance => DesktopPlacementMath.Distance(StartPointerInViewport, CurrentPointerInViewport);

    public bool HasExceededThreshold(double threshold)
    {
        return DesktopPlacementMath.HasExceededThreshold(StartPointerInViewport, CurrentPointerInViewport, threshold);
    }

    public bool IsPointerInsideComponentLibrary()
    {
        return DesktopPlacementMath.IsOccludedByComponentLibrary(CurrentPointerInViewport, ComponentLibraryBounds);
    }

    public bool IsPreviewOccludedByComponentLibrary(Rect previewRect)
    {
        return DesktopPlacementMath.IsOccludedByComponentLibrary(previewRect, ComponentLibraryBounds);
    }

    public bool CanCommit => IsActive && HasTargetCell;

    public Rect GetPreviewRect(DesktopGridGeometry grid)
    {
        if (HasTargetCell)
        {
            return DesktopPlacementMath.GetCellRect(
                grid,
                TargetColumn,
                TargetRow,
                Math.Max(1, WidthCells),
                Math.Max(1, HeightCells));
        }

        var freePreviewOrigin = DesktopPlacementMath.Subtract(CurrentPointerInViewport, PointerOffsetInViewport);
        return new Rect(
            freePreviewOrigin,
            new Size(
                Math.Max(1, WidthCells) * grid.CellSize + Math.Max(0, Math.Max(1, WidthCells) - 1) * grid.CellGap,
                Math.Max(1, HeightCells) * grid.CellSize + Math.Max(0, Math.Max(1, HeightCells) - 1) * grid.CellGap));
    }

    public DesktopEditSession WithCurrentPointer(Point pointerInViewport)
    {
        return this with { CurrentPointerInViewport = pointerInViewport };
    }

    public DesktopEditSession WithComponentLibraryBounds(Rect? componentLibraryBounds)
    {
        return this with { ComponentLibraryBounds = componentLibraryBounds };
    }

    public DesktopEditSession WithTargetCell(int row, int column)
    {
        return this with { TargetRow = row, TargetColumn = column };
    }

    public DesktopEditSession PromoteToDraggingNew()
    {
        return this with { Mode = DesktopEditSessionMode.DraggingNew };
    }

    public DesktopEditSession PromoteToDraggingExisting()
    {
        return this with { Mode = DesktopEditSessionMode.DraggingExisting };
    }

    public DesktopEditSession PromoteToResizingExisting()
    {
        return this with { Mode = DesktopEditSessionMode.ResizingExisting };
    }

    public static DesktopEditSession CreatePendingNew(
        string componentId,
        int pageIndex,
        int widthCells,
        int heightCells,
        Point startPointerInViewport,
        Point pointerOffsetInViewport,
        Rect? componentLibraryBounds)
    {
        return new DesktopEditSession
        {
            Mode = DesktopEditSessionMode.PendingNew,
            ComponentId = componentId,
            PageIndex = pageIndex,
            WidthCells = Math.Max(1, widthCells),
            HeightCells = Math.Max(1, heightCells),
            StartPointerInViewport = startPointerInViewport,
            CurrentPointerInViewport = startPointerInViewport,
            PointerOffsetInViewport = pointerOffsetInViewport,
            ComponentLibraryBounds = componentLibraryBounds,
            TargetRow = -1,
            TargetColumn = -1
        };
    }

    public static DesktopEditSession CreateDraggingNew(
        string componentId,
        int pageIndex,
        int widthCells,
        int heightCells,
        Point startPointerInViewport,
        Point pointerOffsetInViewport,
        Rect? componentLibraryBounds)
    {
        return CreatePendingNew(
            componentId,
            pageIndex,
            widthCells,
            heightCells,
            startPointerInViewport,
            pointerOffsetInViewport,
            componentLibraryBounds) with
        {
            Mode = DesktopEditSessionMode.DraggingNew
        };
    }

    public static DesktopEditSession CreateDraggingExisting(
        string componentId,
        string placementId,
        int pageIndex,
        int widthCells,
        int heightCells,
        Point startPointerInViewport,
        Point pointerOffsetInViewport,
        Rect? componentLibraryBounds)
    {
        return new DesktopEditSession
        {
            Mode = DesktopEditSessionMode.DraggingExisting,
            ComponentId = componentId,
            PlacementId = placementId,
            PageIndex = pageIndex,
            WidthCells = Math.Max(1, widthCells),
            HeightCells = Math.Max(1, heightCells),
            StartPointerInViewport = startPointerInViewport,
            CurrentPointerInViewport = startPointerInViewport,
            PointerOffsetInViewport = pointerOffsetInViewport,
            ComponentLibraryBounds = componentLibraryBounds,
            TargetRow = -1,
            TargetColumn = -1
        };
    }

    public static DesktopEditSession CreateResizingExisting(
        string componentId,
        string placementId,
        int pageIndex,
        int widthCells,
        int heightCells,
        Point startPointerInViewport,
        Rect? componentLibraryBounds)
    {
        return new DesktopEditSession
        {
            Mode = DesktopEditSessionMode.ResizingExisting,
            ComponentId = componentId,
            PlacementId = placementId,
            PageIndex = pageIndex,
            WidthCells = Math.Max(1, widthCells),
            HeightCells = Math.Max(1, heightCells),
            StartPointerInViewport = startPointerInViewport,
            CurrentPointerInViewport = startPointerInViewport,
            PointerOffsetInViewport = default,
            ComponentLibraryBounds = componentLibraryBounds,
            TargetRow = -1,
            TargetColumn = -1
        };
    }
}

using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.DesktopEditing;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using LanMountainDesktop.Theme;

namespace LanMountainDesktop.Views;

public partial class MainWindow : Window
{
    private static readonly TimeSpan DesktopEditCommitAnimationDuration = FluttermotionToken.Standard;
    private static readonly TimeSpan DesktopEditCancelAnimationDuration = FluttermotionToken.Fast;

    private DesktopEditSession _desktopEditSession;
    private DesktopEditOverlayPresenter? _desktopEditOverlayPresenter;
    private Border? _desktopEditSourceHost;
    private Rect _desktopEditOriginalRect;
    private int _desktopEditStartRow;
    private int _desktopEditStartColumn;
    private int _desktopEditStartWidthCells;
    private int _desktopEditStartHeightCells;
    private int _desktopEditMinWidthCells;
    private int _desktopEditMinHeightCells;
    private int _desktopEditMaxWidthCells;
    private int _desktopEditMaxHeightCells;
    private DesktopComponentResizeMode _desktopEditResizeMode;
    private int _desktopEditOverlayVersion;
    private int _desktopEditCommitVersion;
    private bool _isDesktopEditCommitPending;
    private ComponentLibraryCollapsePresenter? _componentLibraryCollapsePresenter;

    private bool HasActiveDesktopEditSession => _desktopEditSession.IsActive || _isDesktopEditCommitPending;

    private bool IsDesktopEditDragMode =>
        _desktopEditSession.Mode is DesktopEditSessionMode.PendingNew or DesktopEditSessionMode.DraggingNew or DesktopEditSessionMode.DraggingExisting;

    private bool IsDesktopEditResizeMode =>
        _desktopEditSession.Mode == DesktopEditSessionMode.ResizingExisting;

    private void EnsureDesktopEditOverlayPresenter()
    {
        if (DesktopEditDragLayer is null)
        {
            return;
        }

        _desktopEditOverlayPresenter ??= new DesktopEditOverlayPresenter();
        if (!DesktopEditDragLayer.Children.Contains(_desktopEditOverlayPresenter.Root))
        {
            DesktopEditDragLayer.Children.Clear();
            DesktopEditDragLayer.Children.Add(_desktopEditOverlayPresenter.Root);
        }

        UpdateDesktopEditOverlayViewportSize();
    }

    private void UpdateDesktopEditOverlayViewportSize()
    {
        if (_desktopEditOverlayPresenter is null)
        {
            return;
        }

        var width = Math.Max(
            DesktopPagesViewport?.Bounds.Width ?? 0,
            DesktopEditDragLayer?.Bounds.Width ?? 0);
        var height = Math.Max(
            DesktopPagesViewport?.Bounds.Height ?? 0,
            DesktopEditDragLayer?.Bounds.Height ?? 0);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        _desktopEditOverlayPresenter.SetViewportSize(new Size(width, height));
    }

    private void EnsureComponentLibraryCollapsePresenter()
    {
        if (_componentLibraryCollapsePresenter is not null || ComponentLibraryWindow is null)
        {
            return;
        }

        var collapsedChipHost = this.FindControl<Border>("ComponentLibraryCollapsedChipHost");
        var collapsedChipTextBlock = this.FindControl<TextBlock>("ComponentLibraryCollapsedChipTextBlock");
        var collapsedChipIcon = this.FindControl<Control>("ComponentLibraryCollapsedChipIcon");
        if (collapsedChipHost is null || collapsedChipTextBlock is null)
        {
            return;
        }

        _componentLibraryCollapsePresenter = new ComponentLibraryCollapsePresenter(
            ComponentLibraryWindow,
            collapsedChipHost,
            collapsedChipTextBlock,
            collapsedChipIcon);
    }

    private bool IsComponentLibraryTemporarilyCollapsedForDesktopEdit()
    {
        EnsureComponentLibraryCollapsePresenter();
        return _componentLibraryCollapsePresenter is not null &&
               _componentLibraryCollapsePresenter.VisualState != ComponentLibraryCollapseVisualState.Expanded;
    }

    private void SyncComponentLibraryCollapseExpandedState()
    {
        if (!_isComponentLibraryOpen || ComponentLibraryWindow is null)
        {
            return;
        }

        EnsureComponentLibraryCollapsePresenter();
        if (_componentLibraryCollapsePresenter is null)
        {
            return;
        }

        _componentLibraryCollapsePresenter.SyncExpandedState(ComponentLibraryWindow.Margin, ComponentLibraryWindow.Opacity);
    }

    private void CollapseComponentLibraryForDesktopEdit(string? title)
    {
        if (!_isComponentLibraryOpen)
        {
            return;
        }

        EnsureComponentLibraryCollapsePresenter();
        if (_componentLibraryCollapsePresenter is null)
        {
            return;
        }

        SyncComponentLibraryCollapseExpandedState();
        _componentLibraryCollapsePresenter.Collapse(ResolveComponentLibraryCollapsedChipTitle());
    }

    private string ResolveComponentLibraryCollapsedChipTitle()
    {
        if (!string.IsNullOrWhiteSpace(ComponentLibraryTitleTextBlock?.Text))
        {
            return ComponentLibraryTitleTextBlock.Text;
        }

        return L("button.component_library", "Widgets");
    }

    private void RestoreComponentLibraryAfterDesktopEdit()
    {
        EnsureComponentLibraryCollapsePresenter();
        if (_componentLibraryCollapsePresenter is null)
        {
            return;
        }

        _componentLibraryCollapsePresenter.Restore();
    }

    private bool TryGetCurrentDesktopGridGeometry(out DesktopGridGeometry geometry)
    {
        geometry = default;
        if (_currentDesktopCellSize <= 0 ||
            _currentDesktopSurfaceIndex < 0 ||
            _currentDesktopSurfaceIndex >= _desktopPageCount ||
            !_desktopPageComponentGrids.TryGetValue(_currentDesktopSurfaceIndex, out var pageGrid))
        {
            return false;
        }

        var columnCount = pageGrid.ColumnDefinitions.Count;
        var rowCount = pageGrid.RowDefinitions.Count;
        if (columnCount <= 0 || rowCount <= 0)
        {
            return false;
        }

        geometry = new DesktopGridGeometry(
            Origin: default,
            CellSize: _currentDesktopCellSize,
            CellGap: _currentDesktopCellGap,
            ColumnCount: columnCount,
            RowCount: rowCount);
        return geometry.IsValid;
    }

    private Rect? GetComponentLibraryBoundsInViewport()
    {
        if (!_isComponentLibraryOpen ||
            IsComponentLibraryTemporarilyCollapsedForDesktopEdit() ||
            ComponentLibraryWindow is null ||
            DesktopPagesViewport is null ||
            !ComponentLibraryWindow.IsVisible ||
            ComponentLibraryWindow.Bounds.Width <= 0 ||
            ComponentLibraryWindow.Bounds.Height <= 0)
        {
            return null;
        }

        var origin = ComponentLibraryWindow.TranslatePoint(default, DesktopPagesViewport);
        return origin.HasValue
            ? new Rect(origin.Value, ComponentLibraryWindow.Bounds.Size)
            : null;
    }

    private static Size GetComponentPixelSize(int widthCells, int heightCells, double cellSize, double cellGap)
    {
        var safeWidthCells = Math.Max(1, widthCells);
        var safeHeightCells = Math.Max(1, heightCells);
        return new Size(
            safeWidthCells * cellSize + Math.Max(0, safeWidthCells - 1) * cellGap,
            safeHeightCells * cellSize + Math.Max(0, safeHeightCells - 1) * cellGap);
    }

    private string ResolveDesktopEditTitle(string componentId)
    {
        return _componentRuntimeRegistry.TryGetDescriptor(componentId, out var descriptor)
            ? descriptor.Definition.DisplayName
            : componentId;
    }

    private void UpdateDesktopEditOverlayMetadata(string componentId, int widthCells, int heightCells, string? detail)
    {
        EnsureDesktopEditOverlayPresenter();
        _desktopEditOverlayPresenter?.UpdateGhostContent(
            ResolveDesktopEditTitle(componentId),
            detail,
            $"{Math.Max(1, widthCells)}x{Math.Max(1, heightCells)}");
    }

    private bool TryGetDesktopPlacementById(string? placementId, out DesktopComponentPlacementSnapshot placement)
    {
        placement = null!;
        if (string.IsNullOrWhiteSpace(placementId))
        {
            return false;
        }

        var matched = _desktopComponentPlacements.FirstOrDefault(candidate =>
            string.Equals(candidate.PlacementId, placementId, StringComparison.OrdinalIgnoreCase));
        if (matched is null)
        {
            return false;
        }

        placement = matched;
        return true;
    }

    private void SetDesktopEditSourceHost(Border? host, double opacity)
    {
        _desktopEditSourceHost = host;
        if (_desktopEditSourceHost is not null)
        {
            _desktopEditSourceHost.Opacity = opacity;
        }
    }

    private void RestoreDesktopEditSourceHost()
    {
        if (_desktopEditSourceHost is null)
        {
            return;
        }

        _desktopEditSourceHost.Opacity = 1;
        ApplyDesktopEditStateToHost(_desktopEditSourceHost, _isComponentLibraryOpen);
        _desktopEditSourceHost = null;
    }

    private void ResetDesktopEditState()
    {
        RestoreDesktopEditSourceHost();
        _desktopEditSession = default;
        _desktopEditOriginalRect = default;
        _desktopEditStartRow = 0;
        _desktopEditStartColumn = 0;
        _desktopEditStartWidthCells = 0;
        _desktopEditStartHeightCells = 0;
        _desktopEditMinWidthCells = 0;
        _desktopEditMinHeightCells = 0;
        _desktopEditMaxWidthCells = 0;
        _desktopEditMaxHeightCells = 0;
        _desktopEditResizeMode = DesktopComponentResizeMode.Proportional;
        _isDesktopEditCommitPending = false;

        if (_desktopEditOverlayPresenter is not null)
        {
            _desktopEditOverlayPresenter.SetCandidateRect(null);
            _desktopEditOverlayPresenter.Hide();
        }
    }

    private void CancelDesktopEditSession(bool animate)
    {
        RestoreComponentLibraryAfterDesktopEdit();

        if (_isDesktopEditCommitPending)
        {
            _desktopEditCommitVersion++;
            ResetDesktopEditState();
            return;
        }

        if (!_desktopEditSession.IsActive)
        {
            ResetDesktopEditState();
            return;
        }

        var version = ++_desktopEditOverlayVersion;
        if (animate && _desktopEditOverlayPresenter is not null)
        {
            _desktopEditOverlayPresenter.Cancel();
            DispatcherTimer.RunOnce(
                () =>
                {
                    if (version != _desktopEditOverlayVersion)
                    {
                        return;
                    }

                    ResetDesktopEditState();
                },
                DesktopEditCancelAnimationDuration);
            return;
        }

        ResetDesktopEditState();
    }

    private bool CanCommitDesktopEditAtRect(Rect finalRect)
    {
        return DesktopPlacementMath.CanCommitPlacement(finalRect, GetComponentLibraryBoundsInViewport());
    }

    private void RunDesktopEditCommit(Rect finalRect, Action commitAction)
    {
        _isDesktopEditCommitPending = true;
        var overlayVersion = ++_desktopEditOverlayVersion;
        var scheduledCommitVersion = ++_desktopEditCommitVersion;
        _desktopEditOverlayPresenter?.Commit();
        DispatcherTimer.RunOnce(
            () =>
            {
                if (overlayVersion != _desktopEditOverlayVersion ||
                    !DesktopEditCommitMath.IsPendingCommitValid(
                        _isDesktopEditCommitPending,
                        scheduledCommitVersion,
                        _desktopEditCommitVersion))
                {
                    return;
                }

                if (!CanCommitDesktopEditAtRect(finalRect))
                {
                    RestoreComponentLibraryAfterDesktopEdit();
                    ResetDesktopEditState();
                    return;
                }

                commitAction();
                RestoreComponentLibraryAfterDesktopEdit();
                ResetDesktopEditState();
            },
            DesktopEditCommitAnimationDuration);
    }

    private void UpdateDesktopEditSession(Point pointerInViewport)
    {
        if (_isDesktopEditCommitPending || !_desktopEditSession.IsActive)
        {
            return;
        }

        _desktopEditSession = _desktopEditSession
            .WithCurrentPointer(pointerInViewport)
            .WithComponentLibraryBounds(GetComponentLibraryBoundsInViewport());

        switch (_desktopEditSession.Mode)
        {
            case DesktopEditSessionMode.PendingNew:
                PromotePendingNewDesktopEditIfNeeded();
                break;
            case DesktopEditSessionMode.DraggingNew:
            case DesktopEditSessionMode.DraggingExisting:
                UpdateActiveDesktopDragPreview();
                break;
            case DesktopEditSessionMode.ResizingExisting:
                UpdateActiveDesktopResizePreview();
                break;
        }
    }

    private void PromotePendingNewDesktopEditIfNeeded()
    {
        var threshold = DesktopPlacementMath.ComputeDragStartThreshold(_currentDesktopCellSize);
        if (!_desktopEditSession.HasExceededThreshold(threshold) ||
            _desktopEditSession.IsPointerInsideComponentLibrary())
        {
            return;
        }

        _desktopEditSession = _desktopEditSession.PromoteToDraggingNew();
        CollapseComponentLibraryForDesktopEdit(ResolveDesktopEditTitle(_desktopEditSession.ComponentId ?? string.Empty));
        _desktopEditSession = _desktopEditSession.WithComponentLibraryBounds(GetComponentLibraryBoundsInViewport());
        EnsureDesktopEditOverlayPresenter();
        _desktopEditOverlayPresenter?.Show(DesktopEditGhostVisualStyle.ElevatedFromLibrary);
        UpdateActiveDesktopDragPreview();
    }

    private void UpdateActiveDesktopDragPreview()
    {
        if (_desktopEditSession.Mode is not (DesktopEditSessionMode.DraggingNew or DesktopEditSessionMode.DraggingExisting) ||
            !TryGetCurrentDesktopGridGeometry(out var grid) ||
            DesktopPagesViewport is null)
        {
            return;
        }

        EnsureDesktopEditOverlayPresenter();

        var previewSize = GetComponentPixelSize(
            _desktopEditSession.WidthCells,
            _desktopEditSession.HeightCells,
            _currentDesktopCellSize,
            _currentDesktopCellGap);
        var previewOrigin = DesktopPlacementMath.Subtract(
            _desktopEditSession.CurrentPointerInViewport,
            _desktopEditSession.PointerOffsetInViewport);
        var previewRect = new Rect(previewOrigin, previewSize);
        var hasSnap = DesktopPlacementMath.TryGetSnappedCell(
            grid,
            _desktopEditSession.CurrentPointerInViewport,
            _desktopEditSession.PointerOffsetInViewport,
            _desktopEditSession.WidthCells,
            _desktopEditSession.HeightCells,
            out var column,
            out var row);
        var snappedRect = hasSnap
            ? DesktopPlacementMath.GetCellRect(grid, column, row, _desktopEditSession.WidthCells, _desktopEditSession.HeightCells)
            : default;
        var withinViewport =
            _desktopEditSession.CurrentPointerInViewport.X >= 0 &&
            _desktopEditSession.CurrentPointerInViewport.Y >= 0 &&
            _desktopEditSession.CurrentPointerInViewport.X <= DesktopPagesViewport.Bounds.Width &&
            _desktopEditSession.CurrentPointerInViewport.Y <= DesktopPagesViewport.Bounds.Height;
        var occludedByLibrary =
            _desktopEditSession.IsPointerInsideComponentLibrary() ||
            _desktopEditSession.IsPreviewOccludedByComponentLibrary(previewRect);
        var canDrop = withinViewport && hasSnap && !occludedByLibrary;

        _desktopEditSession = canDrop
            ? _desktopEditSession.WithTargetCell(row, column)
            : _desktopEditSession with { TargetRow = -1, TargetColumn = -1 };

        _desktopEditOverlayPresenter?.SetPreviewRect(previewRect);
        _desktopEditOverlayPresenter?.SetCandidateRect(canDrop ? snappedRect : null);
        _desktopEditOverlayPresenter?.SetInvalid(!canDrop);
    }

    private void UpdateActiveDesktopResizePreview()
    {
        if (_desktopEditSession.Mode != DesktopEditSessionMode.ResizingExisting ||
            !TryGetCurrentDesktopGridGeometry(out var grid) ||
            !TryGetDesktopPlacementById(_desktopEditSession.PlacementId, out var placement))
        {
            return;
        }

        EnsureDesktopEditOverlayPresenter();

        var deltaX = _desktopEditSession.CurrentPointerInViewport.X - _desktopEditSession.StartPointerInViewport.X;
        var deltaY = _desktopEditSession.CurrentPointerInViewport.Y - _desktopEditSession.StartPointerInViewport.Y;

        var minSize = GetComponentPixelSize(
            _desktopEditMinWidthCells,
            _desktopEditMinHeightCells,
            _currentDesktopCellSize,
            _currentDesktopCellGap);
        var maxSize = GetComponentPixelSize(
            _desktopEditMaxWidthCells,
            _desktopEditMaxHeightCells,
            _currentDesktopCellSize,
            _currentDesktopCellGap);

        double previewWidth;
        double previewHeight;
        int widthCells;
        int heightCells;

        if (_desktopEditResizeMode == DesktopComponentResizeMode.Free)
        {
            previewWidth = Math.Clamp(_desktopEditOriginalRect.Width + deltaX, minSize.Width, maxSize.Width);
            previewHeight = Math.Clamp(_desktopEditOriginalRect.Height + deltaY, minSize.Height, maxSize.Height);
            widthCells = Math.Clamp(
                (int)Math.Round(_desktopEditStartWidthCells + deltaX / CurrentDesktopPitch),
                _desktopEditMinWidthCells,
                _desktopEditMaxWidthCells);
            heightCells = Math.Clamp(
                (int)Math.Round(_desktopEditStartHeightCells + deltaY / CurrentDesktopPitch),
                _desktopEditMinHeightCells,
                _desktopEditMaxHeightCells);
        }
        else
        {
            var widthScale = (_desktopEditOriginalRect.Width + deltaX) / Math.Max(1, _desktopEditOriginalRect.Width);
            var heightScale = (_desktopEditOriginalRect.Height + deltaY) / Math.Max(1, _desktopEditOriginalRect.Height);
            var proposedScale = Math.Max(widthScale, heightScale);
            var minScale = Math.Max(
                (double)_desktopEditMinWidthCells / Math.Max(1, _desktopEditStartWidthCells),
                (double)_desktopEditMinHeightCells / Math.Max(1, _desktopEditStartHeightCells));
            var maxScale = Math.Min(
                (double)_desktopEditMaxWidthCells / Math.Max(1, _desktopEditStartWidthCells),
                (double)_desktopEditMaxHeightCells / Math.Max(1, _desktopEditStartHeightCells));

            if (double.IsNaN(proposedScale) || double.IsInfinity(proposedScale))
            {
                proposedScale = minScale;
            }

            if (maxScale < minScale)
            {
                maxScale = minScale;
            }

            var scale = Math.Clamp(proposedScale, minScale, maxScale);
            previewWidth = Math.Clamp(_desktopEditOriginalRect.Width * scale, minSize.Width, maxSize.Width);
            previewHeight = Math.Clamp(_desktopEditOriginalRect.Height * scale, minSize.Height, maxSize.Height);
            widthCells = Math.Clamp(
                (int)Math.Round(_desktopEditStartWidthCells * scale),
                _desktopEditMinWidthCells,
                _desktopEditMaxWidthCells);
            heightCells = Math.Clamp(
                (int)Math.Round(_desktopEditStartHeightCells * scale),
                _desktopEditMinHeightCells,
                _desktopEditMaxHeightCells);
        }

        var normalized = NormalizeComponentCellSpan(_desktopEditSession.ComponentId ?? string.Empty, (widthCells, heightCells));
        widthCells = Math.Clamp(normalized.WidthCells, _desktopEditMinWidthCells, _desktopEditMaxWidthCells);
        heightCells = Math.Clamp(normalized.HeightCells, _desktopEditMinHeightCells, _desktopEditMaxHeightCells);

        var previewRect = new Rect(_desktopEditOriginalRect.X, _desktopEditOriginalRect.Y, previewWidth, previewHeight);
        var snappedRect = DesktopPlacementMath.GetCellRect(grid, placement.Column, placement.Row, widthCells, heightCells);
        var occludedByLibrary =
            _desktopEditSession.IsPointerInsideComponentLibrary() ||
            DesktopPlacementMath.IsOccludedByComponentLibrary(previewRect, _desktopEditSession.ComponentLibraryBounds);
        var canCommit = !occludedByLibrary;

        _desktopEditSession = (_desktopEditSession with
        {
            WidthCells = widthCells,
            HeightCells = heightCells,
            TargetRow = canCommit ? placement.Row : -1,
            TargetColumn = canCommit ? placement.Column : -1
        }).WithComponentLibraryBounds(GetComponentLibraryBoundsInViewport());

        UpdateDesktopEditOverlayMetadata(
            _desktopEditSession.ComponentId ?? placement.ComponentId,
            widthCells,
            heightCells,
            L("component.resize", "Resize"));
        _desktopEditOverlayPresenter?.SetPreviewRect(previewRect);
        _desktopEditOverlayPresenter?.SetCandidateRect(canCommit ? snappedRect : null);
        _desktopEditOverlayPresenter?.SetInvalid(!canCommit);
    }

    private bool CompleteDesktopEditSession(Point pointerInViewport)
    {
        if (_isDesktopEditCommitPending || !_desktopEditSession.IsActive)
        {
            return false;
        }

        UpdateDesktopEditSession(pointerInViewport);

        switch (_desktopEditSession.Mode)
        {
            case DesktopEditSessionMode.DraggingNew:
                return CompleteNewDesktopComponentDrag();
            case DesktopEditSessionMode.DraggingExisting:
                return CompleteExistingDesktopComponentMove();
            case DesktopEditSessionMode.ResizingExisting:
                return CompleteExistingDesktopComponentResize();
            default:
                return false;
        }
    }

    private bool CompleteNewDesktopComponentDrag()
    {
        if (!_desktopEditSession.HasTargetCell ||
            string.IsNullOrWhiteSpace(_desktopEditSession.ComponentId) ||
            !TryGetCurrentDesktopGridGeometry(out var grid))
        {
            return false;
        }

        var finalRect = _desktopEditSession.GetPreviewRect(grid);
        if (!CanCommitDesktopEditAtRect(finalRect))
        {
            return false;
        }

        _desktopEditOverlayPresenter?.SetPreviewRect(finalRect);
        _desktopEditOverlayPresenter?.SetCandidateRect(finalRect);
        _desktopEditOverlayPresenter?.SetInvalid(false);

        var componentId = _desktopEditSession.ComponentId;
        var pageIndex = _desktopEditSession.PageIndex;
        var row = _desktopEditSession.TargetRow;
        var column = _desktopEditSession.TargetColumn;
        RunDesktopEditCommit(finalRect, () => PlaceDesktopComponentOnPage(componentId, pageIndex, row, column));
        return true;
    }

    private bool CompleteExistingDesktopComponentMove()
    {
        if (!_desktopEditSession.HasTargetCell ||
            string.IsNullOrWhiteSpace(_desktopEditSession.PlacementId) ||
            !TryGetCurrentDesktopGridGeometry(out var grid))
        {
            return false;
        }

        var finalRect = _desktopEditSession.GetPreviewRect(grid);
        if (!CanCommitDesktopEditAtRect(finalRect))
        {
            return false;
        }

        _desktopEditOverlayPresenter?.SetPreviewRect(finalRect);
        _desktopEditOverlayPresenter?.SetCandidateRect(finalRect);
        _desktopEditOverlayPresenter?.SetInvalid(false);

        var placementId = _desktopEditSession.PlacementId;
        var row = _desktopEditSession.TargetRow;
        var column = _desktopEditSession.TargetColumn;
        if (!DesktopPlacementMath.HasCellPositionChanged(_desktopEditStartRow, _desktopEditStartColumn, row, column))
        {
            return false;
        }

        RunDesktopEditCommit(finalRect, () => TryMoveExistingDesktopComponent(placementId, row, column));
        return true;
    }

    private bool CompleteExistingDesktopComponentResize()
    {
        if (!_desktopEditSession.HasTargetCell ||
            string.IsNullOrWhiteSpace(_desktopEditSession.PlacementId) ||
            !TryGetCurrentDesktopGridGeometry(out var grid))
        {
            return false;
        }

        var finalRect = _desktopEditSession.GetPreviewRect(grid);
        if (!CanCommitDesktopEditAtRect(finalRect))
        {
            return false;
        }

        _desktopEditOverlayPresenter?.SetPreviewRect(finalRect);
        _desktopEditOverlayPresenter?.SetCandidateRect(finalRect);
        _desktopEditOverlayPresenter?.SetInvalid(false);

        var placementId = _desktopEditSession.PlacementId;
        var widthCells = Math.Max(1, _desktopEditSession.WidthCells);
        var heightCells = Math.Max(1, _desktopEditSession.HeightCells);
        if (!DesktopPlacementMath.HasCellSpanChanged(_desktopEditStartWidthCells, _desktopEditStartHeightCells, widthCells, heightCells))
        {
            return false;
        }

        RunDesktopEditCommit(finalRect, () => ApplyExistingDesktopComponentResize(placementId, widthCells, heightCells));
        return true;
    }

    private void ApplyExistingDesktopComponentResize(string placementId, int widthCells, int heightCells)
    {
        if (!TryGetDesktopPlacementById(placementId, out var placement))
        {
            return;
        }

        var before = ClonePlacementSnapshot(placement);
        var changed = placement.WidthCells != widthCells || placement.HeightCells != heightCells;
        placement.WidthCells = widthCells;
        placement.HeightCells = heightCells;

        if (_desktopEditSourceHost is not null)
        {
            Grid.SetColumnSpan(_desktopEditSourceHost, widthCells);
            Grid.SetRowSpan(_desktopEditSourceHost, heightCells);
            ApplyDesktopEditStateToHost(_desktopEditSourceHost, _isComponentLibraryOpen);
        }

        if (!changed)
        {
            return;
        }

        QueuePlacementPreviewRefresh(placement);
        PersistSettings();
        TelemetryServices.Usage?.TrackDesktopComponentResized(before, ClonePlacementSnapshot(placement), "component.resize");
    }
}

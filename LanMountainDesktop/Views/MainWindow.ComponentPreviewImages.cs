using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views;

public partial class MainWindow
{
    private const double PreviewRenderCellSizeMin = 42;
    private const double PreviewRenderCellSizeMax = 112;

    private readonly IComponentPreviewImageService _componentPreviewImageService = new ComponentPreviewImageService();
    private readonly Dictionary<ComponentPreviewKey, List<ComponentLibraryPreviewVisualTarget>> _componentLibraryPreviewVisualTargets = new(ComponentPreviewKeyComparer.Instance);
    private bool _componentLibraryPreviewWarmupStarted;
    private FusedDesktopComponentLibraryWindow? _fusedLibraryWindow;

    private sealed record ComponentLibraryPreviewVisualTarget(Image Image, Control Fallback);

    private void EnsureComponentLibraryPreviewWarmup()
    {
        if (_componentLibraryCategories.Count == 0)
        {
            return;
        }

        var activeCategoryId = _componentLibraryActiveCategoryId ??
            _componentLibraryCategories[Math.Clamp(_componentLibraryCategoryIndex, 0, _componentLibraryCategories.Count - 1)].Id;
        if (!_componentLibraryPreviewWarmupStarted)
        {
            _componentLibraryPreviewWarmupStarted = true;
            _ = WarmComponentLibraryPreviewsSeriallyAsync(activeCategoryId);
            return;
        }

        var activeCategory = _componentLibraryCategories.FirstOrDefault(category =>
            string.Equals(category.Id, activeCategoryId, StringComparison.OrdinalIgnoreCase));
        if (activeCategory is not null)
        {
            _ = WarmComponentLibraryCategoryPreviewsAsync(activeCategory);
        }
    }

    private async Task WarmComponentLibraryPreviewsSeriallyAsync(string activeCategoryId)
    {
        var prioritized = _componentLibraryCategories
            .OrderBy(category => string.Equals(category.Id, activeCategoryId, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ToList();

        foreach (var category in prioritized)
        {
            await WarmComponentLibraryCategoryPreviewsAsync(category);
        }
    }

    private async Task WarmComponentLibraryCategoryPreviewsAsync(ComponentLibraryCategory category)
    {
        foreach (var component in category.Components)
        {
            var span = NormalizeComponentCellSpan(
                component.ComponentId,
                (component.MinWidthCells, component.MinHeightCells));
            await EnsureComponentTypePreviewImageAsync(component.ComponentId, span.WidthCells, span.HeightCells);
        }
    }

    private async Task<IImage?> EnsureComponentTypePreviewImageAsync(string componentId, int widthCells, int heightCells)
    {
        if (string.IsNullOrWhiteSpace(componentId))
        {
            return null;
        }

        var key = CreateComponentTypePreviewKey(componentId, widthCells, heightCells);
        var cached = ResolvePreviewImageFromService(key);
        if (cached is not null)
        {
            ApplyPreviewEntryToEmbeddedVisuals(key);
            return cached;
        }

        var entry = await QueuePreviewGenerationAsync(
            key,
            pageIndex: null,
            action: "ComponentTypePreview",
            forceRefresh: false);
        return entry.Bitmap;
    }

    private async Task<IImage?> RefreshPlacementPreviewImageAsync(DesktopComponentPlacementSnapshot? placement, bool forceRefresh)
    {
        if (placement is null ||
            string.IsNullOrWhiteSpace(placement.ComponentId) ||
            string.IsNullOrWhiteSpace(placement.PlacementId))
        {
            return null;
        }

        if (!IsPlacementPresent(placement.PlacementId))
        {
            return null;
        }

        var snapshot = ClonePlacementSnapshot(placement);
        var key = CreatePlacementPreviewKey(
            snapshot.ComponentId,
            snapshot.PlacementId,
            snapshot.WidthCells,
            snapshot.HeightCells);
        if (!forceRefresh)
        {
            var cached = ResolvePreviewImageFromService(key);
            if (cached is not null)
            {
                return cached;
            }
        }
        else
        {
            _componentPreviewImageService.RemovePlacementPreviews(snapshot.PlacementId);
        }

        var entry = await QueuePreviewGenerationAsync(
            key,
            snapshot.PageIndex,
            action: "PlacementPreview",
            forceRefresh: false);
        if (!IsPlacementPresent(snapshot.PlacementId))
        {
            RemovePlacementPreviewImage(snapshot.PlacementId);
            return null;
        }

        return entry.Bitmap;
    }

    private async Task<ComponentPreviewImageEntry> QueuePreviewGenerationAsync(
        ComponentPreviewKey key,
        int? pageIndex,
        string action,
        bool forceRefresh,
        CancellationToken cancellationToken = default)
    {
        var renderCellSize = ResolvePreviewRenderCellSize(key.WidthCells, key.HeightCells);
        var visualSignature = BuildPreviewVisualSignature(key, renderCellSize);
        if (forceRefresh)
        {
            _componentPreviewImageService.Invalidate(key, visualSignature);
        }

        var entry = await _componentPreviewImageService.QueueGenerationAsync(
            key,
            visualSignature,
            async ct =>
            {
                _ = ct;
                if (key.Kind == ComponentPreviewKeyKind.PlacementInstance &&
                    !IsPlacementPresent(key.PlacementId))
                {
                    return null;
                }

                var bitmap = await CapturePreviewImageAsync(
                    key.ComponentTypeId,
                    key.PlacementId,
                    pageIndex,
                    key.WidthCells,
                    key.HeightCells,
                    renderCellSize,
                    action);
                if (key.Kind == ComponentPreviewKeyKind.PlacementInstance &&
                    !IsPlacementPresent(key.PlacementId))
                {
                    DisposeImageIfNeeded(bitmap);
                    return null;
                }

                return bitmap;
            },
            cancellationToken);
        NotifyPreviewEntryUpdated(entry);
        return entry;
    }

    private async Task<IImage?> CapturePreviewImageAsync(
        string componentId,
        string? placementId,
        int? pageIndex,
        int widthCells,
        int heightCells,
        double renderCellSize,
        string action)
    {
        if (ComponentPreviewStagingHost is null)
        {
            return null;
        }

        var safeWidthCells = Math.Max(1, widthCells);
        var safeHeightCells = Math.Max(1, heightCells);
        var safeCellSize = Math.Clamp(renderCellSize, PreviewRenderCellSizeMin, PreviewRenderCellSizeMax);
        var previewWidth = safeWidthCells * safeCellSize;
        var previewHeight = safeHeightCells * safeCellSize;

        var previewControl = CreateDesktopComponentControl(
            componentId,
            safeCellSize,
            placementId,
            pageIndex,
            action);
        if (previewControl is null)
        {
            return null;
        }

        previewControl.IsHitTestVisible = false;
        previewControl.Focusable = false;

        var stage = new Border
        {
            Width = previewWidth,
            Height = previewHeight,
            Background = Brushes.Transparent,
            ClipToBounds = true,
            Child = previewControl
        };

        Canvas.SetLeft(stage, -20000);
        Canvas.SetTop(stage, -20000);
        ComponentPreviewStagingHost.Children.Add(stage);

        try
        {
            stage.Measure(new Size(previewWidth, previewHeight));
            stage.Arrange(new Rect(0, 0, previewWidth, previewHeight));
            stage.UpdateLayout();
            await WaitForPreviewRenderPassAsync();

            var renderScale = RenderScaling > 0 ? RenderScaling : 1d;
            var pixelSize = new PixelSize(
                Math.Max(1, (int)Math.Ceiling(previewWidth * renderScale)),
                Math.Max(1, (int)Math.Ceiling(previewHeight * renderScale)));
            var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96 * renderScale, 96 * renderScale));
            bitmap.Render(stage);
            return bitmap;
        }
        catch (Exception ex) when (!UiExceptionGuard.IsFatalException(ex))
        {
            AppLogger.Warn(
                "ComponentPreview",
                $"Action={action}; ComponentId={componentId}; PlacementId={placementId ?? string.Empty}; ExceptionType={ex.GetType().FullName}; IsFatal=false",
                ex);
            return null;
        }
        finally
        {
            ComponentPreviewStagingHost.Children.Remove(stage);
            ClearTimeZoneServiceBindings(stage);
            if (previewControl is IDisposable disposableControl)
            {
                disposableControl.Dispose();
            }
        }
    }

    private static async Task WaitForPreviewRenderPassAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Render);
    }

    private double ResolvePreviewRenderCellSize(int widthCells, int heightCells)
    {
        var baseCellSize = _currentDesktopCellSize > 0
            ? _currentDesktopCellSize * 1.10
            : 74;
        var densityBoost = Math.Max(widthCells, heightCells) >= 4 ? 8 : 0;
        return Math.Clamp(baseCellSize + densityBoost, PreviewRenderCellSizeMin, PreviewRenderCellSizeMax);
    }

    private string BuildPreviewVisualSignature(ComponentPreviewKey key, double renderCellSize)
    {
        var appearance = _appearanceThemeService.GetCurrent();
        var renderScale = RenderScaling > 0 ? RenderScaling : 1d;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{key}|Cell={renderCellSize:F2}|Scale={renderScale:F2}|Night={(appearance.IsNightMode ? 1 : 0)}|Corner={appearance.CornerRadiusStyle}|Accent={FormatSignatureColor(appearance.AccentColor)}");
    }

    private ComponentPreviewKey CreateComponentTypePreviewKey(string componentId, int widthCells, int heightCells)
    {
        var span = NormalizeComponentCellSpan(componentId, (widthCells, heightCells));
        return ComponentPreviewKey.ForComponentType(componentId, span.WidthCells, span.HeightCells);
    }

    private ComponentPreviewKey CreatePlacementPreviewKey(string componentId, string placementId, int widthCells, int heightCells)
    {
        var span = NormalizeComponentCellSpan(componentId, (widthCells, heightCells));
        return ComponentPreviewKey.ForPlacementInstance(componentId, placementId, span.WidthCells, span.HeightCells);
    }

    private bool IsPlacementPresent(string? placementId)
    {
        return !string.IsNullOrWhiteSpace(placementId) &&
               _desktopComponentPlacements.Any(candidate =>
                   string.Equals(candidate.PlacementId, placementId, StringComparison.OrdinalIgnoreCase));
    }

    private string BuildCurrentVisualSignature(ComponentPreviewKey key)
    {
        var renderCellSize = ResolvePreviewRenderCellSize(key.WidthCells, key.HeightCells);
        return BuildPreviewVisualSignature(key, renderCellSize);
    }

    private bool TryGetReusablePreviewEntry(ComponentPreviewKey key, out ComponentPreviewImageEntry? entry)
    {
        if (!_componentPreviewImageService.TryGetEntry(key, out entry) ||
            entry is null ||
            entry.State != ComponentPreviewImageState.Ready ||
            entry.Bitmap is null)
        {
            entry = null;
            return false;
        }

        var expectedSignature = BuildCurrentVisualSignature(key);
        if (!string.Equals(entry.VisualSignature, expectedSignature, StringComparison.Ordinal))
        {
            entry = null;
            return false;
        }

        return true;
    }

    private IImage? ResolvePreviewImageFromService(ComponentPreviewKey key)
    {
        if (!TryGetReusablePreviewEntry(key, out var entry) || entry is null)
        {
            return null;
        }

        return entry.Bitmap;
    }

    private ComponentPreviewImageEntry? ResolvePreviewEntry(ComponentPreviewKey key)
    {
        if (!_componentPreviewImageService.TryGetEntry(key, out var entry) || entry is null)
        {
            return null;
        }

        if (entry.State != ComponentPreviewImageState.Ready)
        {
            return entry;
        }

        return TryGetReusablePreviewEntry(key, out var reusable) ? reusable : null;
    }

    private IImage? ResolveComponentTypePreviewImage(string componentId, int widthCells, int heightCells)
    {
        var key = CreateComponentTypePreviewKey(componentId, widthCells, heightCells);
        return ResolvePreviewImageFromService(key);
    }

    private IImage? ResolveDesktopEditPreviewImage(string componentId, string? placementId, int widthCells, int heightCells)
    {
        if (!string.IsNullOrWhiteSpace(placementId))
        {
            var placementKey = CreatePlacementPreviewKey(componentId, placementId, widthCells, heightCells);
            var placementImage = ResolvePreviewImageFromService(placementKey);
            if (placementImage is not null)
            {
                return placementImage;
            }
        }

        var componentTypeKey = CreateComponentTypePreviewKey(componentId, widthCells, heightCells);
        return ResolvePreviewImageFromService(componentTypeKey);
    }

    private (int WidthCells, int HeightCells) ResolveOverlayPreviewSpan(
        string componentId,
        string? placementId,
        int? widthCells,
        int? heightCells)
    {
        if (widthCells is > 0 && heightCells is > 0)
        {
            return NormalizeComponentCellSpan(componentId, (widthCells.Value, heightCells.Value));
        }

        if (!string.IsNullOrWhiteSpace(placementId) &&
            TryGetDesktopPlacementById(placementId, out var placement))
        {
            return NormalizeComponentCellSpan(componentId, (placement.WidthCells, placement.HeightCells));
        }

        if (!string.IsNullOrWhiteSpace(_desktopEditSession.ComponentId) &&
            string.Equals(_desktopEditSession.ComponentId, componentId, StringComparison.OrdinalIgnoreCase) &&
            _desktopEditSession.WidthCells > 0 &&
            _desktopEditSession.HeightCells > 0)
        {
            return NormalizeComponentCellSpan(componentId, (_desktopEditSession.WidthCells, _desktopEditSession.HeightCells));
        }

        if (_componentRuntimeRegistry.TryGetDescriptor(componentId, out var descriptor))
        {
            return NormalizeComponentCellSpan(
                componentId,
                (descriptor.Definition.MinWidthCells, descriptor.Definition.MinHeightCells));
        }

        return (1, 1);
    }

    private void ApplyDesktopEditOverlayPreviewImage(
        string componentId,
        string? placementId,
        int? widthCells = null,
        int? heightCells = null)
    {
        var span = ResolveOverlayPreviewSpan(componentId, placementId, widthCells, heightCells);
        EnsureDesktopEditOverlayPresenter();
        _desktopEditOverlayPresenter?.SetPreviewImage(ResolveDesktopEditPreviewImage(componentId, placementId, span.WidthCells, span.HeightCells));
    }

    private void PrimeDesktopEditPreviewImage(
        string componentId,
        string? placementId,
        int pageIndex,
        int widthCells,
        int heightCells)
    {
        _ = pageIndex;
        var normalized = NormalizeComponentCellSpan(componentId, (widthCells, heightCells));
        _ = EnsureComponentTypePreviewImageAsync(componentId, normalized.WidthCells, normalized.HeightCells);

        if (!string.IsNullOrWhiteSpace(placementId) &&
            TryGetDesktopPlacementById(placementId, out var placement))
        {
            _ = RefreshPlacementPreviewImageAsync(placement, forceRefresh: false);
        }
    }

    private void QueuePlacementPreviewRefresh(DesktopComponentPlacementSnapshot? placement)
    {
        _ = RefreshPlacementPreviewImageAsync(placement, forceRefresh: true);
    }

    private void RemovePlacementPreviewImage(string? placementId)
    {
        if (string.IsNullOrWhiteSpace(placementId))
        {
            return;
        }

        _componentPreviewImageService.RemovePlacementPreviews(placementId);
    }

    private void RemovePlacementPreviewImages(IEnumerable<DesktopComponentPlacementSnapshot> placements)
    {
        foreach (var placementId in placements
                     .Select(placement => placement.PlacementId)
                     .Where(static id => !string.IsNullOrWhiteSpace(id))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            RemovePlacementPreviewImage(placementId);
        }
    }

    private void RegisterComponentLibraryPreviewVisual(ComponentPreviewKey key, Image image, Control fallback)
    {
        if (!_componentLibraryPreviewVisualTargets.TryGetValue(key, out var visuals))
        {
            visuals = [];
            _componentLibraryPreviewVisualTargets[key] = visuals;
        }

        visuals.Add(new ComponentLibraryPreviewVisualTarget(image, fallback));
    }

    private void ClearComponentLibraryPreviewVisualTargets()
    {
        _componentLibraryPreviewVisualTargets.Clear();
    }

    private void ApplyPreviewEntryToEmbeddedVisuals(ComponentPreviewKey key)
    {
        if (!_componentLibraryPreviewVisualTargets.TryGetValue(key, out var targets))
        {
            return;
        }

        var previewImage = ResolvePreviewImageFromService(key);
        foreach (var target in targets)
        {
            target.Image.Source = previewImage;
            target.Image.IsVisible = previewImage is not null;
            target.Fallback.IsVisible = previewImage is null;
        }
    }

    private void NotifyPreviewEntryUpdated(ComponentPreviewImageEntry entry)
    {
        Dispatcher.UIThread.Post(
            () =>
            {
                ApplyPreviewEntryToEmbeddedVisuals(entry.Key);
                _detachedComponentLibraryWindow?.UpdatePreviewImage(entry);
                _fusedLibraryWindow?.UpdatePreviewImage(entry);

                if (entry.Key.Kind == ComponentPreviewKeyKind.PlacementInstance)
                {
                    RefreshDesktopEditOverlayPreviewIfActive(entry.Key.ComponentTypeId, entry.Key.PlacementId);
                }
                else
                {
                    RefreshDesktopEditOverlayPreviewIfActive(entry.Key.ComponentTypeId, placementId: null);
                }
            },
            DispatcherPriority.Background);
    }

    private static void DisposeImageIfNeeded(IImage? image)
    {
        if (image is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private static string FormatSignatureColor(Color color)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}");
    }

    private void RefreshDesktopEditOverlayPreviewIfActive(string componentId, string? placementId)
    {
        if (_desktopEditOverlayPresenter is null ||
            (!_desktopEditSession.IsActive && !_isDesktopEditCommitPending) ||
            string.IsNullOrWhiteSpace(_desktopEditSession.ComponentId) ||
            !string.Equals(_desktopEditSession.ComponentId, componentId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(placementId) &&
            !string.Equals(_desktopEditSession.PlacementId, placementId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ApplyDesktopEditOverlayPreviewImage(
            _desktopEditSession.ComponentId,
            _desktopEditSession.PlacementId,
            _desktopEditSession.WidthCells,
            _desktopEditSession.HeightCells);
    }

    private ComponentPreviewKey ResolveDetachedLibraryPreviewKey(ComponentLibraryComponentEntry entry)
    {
        return CreateComponentTypePreviewKey(entry.ComponentId, entry.MinWidthCells, entry.MinHeightCells);
    }

    private ComponentPreviewImageEntry? ResolveDetachedLibraryPreviewEntry(ComponentPreviewKey key)
    {
        return ResolvePreviewEntry(key);
    }

    private void RequestDetachedLibraryPreviewWarm(ComponentPreviewKey key)
    {
        _ = QueuePreviewGenerationAsync(
            key,
            pageIndex: null,
            action: "DetachedLibraryWarm",
            forceRefresh: false);
    }

    private void RequestDetachedLibraryPreviewRender(ComponentPreviewKey key)
    {
        _ = QueuePreviewGenerationAsync(
            key,
            pageIndex: null,
            action: "DetachedLibraryRender",
            forceRefresh: false);
    }
    
    // FusedDesktop 支持

    public void RegisterFusedLibraryWindow(FusedDesktopComponentLibraryWindow window)
    {
        _fusedLibraryWindow = window;
    }

    public void UnregisterFusedLibraryWindow(FusedDesktopComponentLibraryWindow window)
    {
        if (ReferenceEquals(_fusedLibraryWindow, window))
        {
            _fusedLibraryWindow = null;
        }
    }

    public ComponentPreviewImageEntry? GetPreviewEntry(ComponentPreviewKey key)
    {
        return ResolvePreviewEntry(key);
    }

    public void RequestDetachedLibraryPreview(ComponentPreviewKey key)
    {
        RequestDetachedLibraryPreviewWarm(key);
        RequestDetachedLibraryPreviewRender(key);
    }
}

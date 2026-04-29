using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views;

public partial class MainWindow : Window
{
    private FusedDesktopComponentLibraryWindow? _fusedLibraryWindow;

    private void EnsureComponentLibraryPreviewWarmup()
    {
    }

    private Control CreateStaticComponentLibraryPreview(
        string componentId,
        double cellSize,
        double previewWidth,
        double previewHeight)
    {
        if (string.IsNullOrWhiteSpace(componentId))
        {
            return CreateStaticComponentPreviewFallback(previewWidth, previewHeight);
        }

        var context = new ComponentLibraryCreateContext(
            cellSize,
            _timeZoneService,
            _weatherDataService,
            _recommendationInfoService,
            _calculatorDataService,
            _settingsFacade,
            PlacementId: null,
            RenderMode: DesktopComponentRenderMode.LibraryPreview);

        if (!_componentLibraryService.TryCreateControl(componentId, context, out var control, out var exception) ||
            control is null)
        {
            if (exception is not null)
            {
                AppLogger.Warn(
                    "ComponentLibrary",
                    $"Failed to create static preview for component '{componentId}'.",
                    exception);
            }

            return CreateStaticComponentPreviewFallback(previewWidth, previewHeight);
        }

        control.Width = previewWidth;
        control.Height = previewHeight;
        ComponentPreviewRuntimeQuiescer.Attach(control);
        return control;
    }

    private Control CreateStaticComponentPreviewFallback(double previewWidth, double previewHeight)
    {
        return new Border
        {
            Width = previewWidth,
            Height = previewHeight,
            Background = GetThemeBrush("AdaptiveCardBackgroundBrush"),
            BorderBrush = GetThemeBrush("AdaptiveButtonBorderBrush"),
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(Math.Clamp(Math.Min(previewWidth, previewHeight) * 0.18, 12, 28)),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = L("component_library.preview_unavailable", "Preview unavailable"),
                FontSize = 11,
                Foreground = GetThemeBrush("AdaptiveTextSecondaryBrush"),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            }
        };
    }

    private static void DisposeStaticComponentLibraryPreviews(IEnumerable<Control> roots)
    {
        foreach (var control in roots.SelectMany(EnumerateControls))
        {
            ComponentPreviewRuntimeQuiescer.Detach(control);
            if (control is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private static IEnumerable<Control> EnumerateControls(Control root)
    {
        yield return root;

        if (root is Panel panel)
        {
            foreach (var child in panel.Children.OfType<Control>())
            {
                foreach (var descendant in EnumerateControls(child))
                {
                    yield return descendant;
                }
            }
        }

        if (root is ContentControl { Content: Control content })
        {
            foreach (var descendant in EnumerateControls(content))
            {
                yield return descendant;
            }
        }

        if (root is Decorator { Child: Control decoratorChild })
        {
            foreach (var descendant in EnumerateControls(decoratorChild))
            {
                yield return descendant;
            }
        }
    }

    private void ApplyDesktopEditOverlayPreviewImage(
        string componentId,
        string? placementId,
        int? widthCells = null,
        int? heightCells = null)
    {
        _ = componentId;
        _ = placementId;
        _ = widthCells;
        _ = heightCells;
        EnsureDesktopEditOverlayPresenter();
        _desktopEditOverlayPresenter?.SetPreviewImage(null);
    }

    private void PrimeDesktopEditPreviewImage(
        string componentId,
        string? placementId,
        int pageIndex,
        int widthCells,
        int heightCells)
    {
        _ = componentId;
        _ = placementId;
        _ = pageIndex;
        _ = widthCells;
        _ = heightCells;
    }

    private void QueuePlacementPreviewRefresh(DesktopComponentPlacementSnapshot? placement)
    {
        _ = placement;
    }

    private void RemovePlacementPreviewImage(string? placementId)
    {
        _ = placementId;
    }

    private void RemovePlacementPreviewImages(IEnumerable<DesktopComponentPlacementSnapshot> placements)
    {
        _ = placements;
    }

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
}

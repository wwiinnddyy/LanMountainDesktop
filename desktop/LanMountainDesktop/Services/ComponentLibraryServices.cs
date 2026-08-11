using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.Views;
using LanMountainDesktop.Views.Components;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.Services;

public interface IEmbeddedComponentLibraryService
{
    void Open(MainWindow window);

    void Close(MainWindow window);

    void Toggle(MainWindow window);
}

public interface IDetachedComponentLibraryWindowService
{
    void Open(MainWindow window);

    void Close(MainWindow window);

    void Toggle(MainWindow window);
}

internal sealed class ComponentLibraryService : IComponentLibraryService
{
    private readonly ComponentRegistry _registry;
    private readonly DesktopComponentRuntimeRegistry _runtimeRegistry;

    public ComponentLibraryService(ComponentRegistry registry, DesktopComponentRuntimeRegistry runtimeRegistry)
    {
        _registry = registry;
        _runtimeRegistry = runtimeRegistry;
    }

    public IReadOnlyList<DesktopComponentDefinition> GetDefinitions()
    {
        return _registry.GetAll().ToArray();
    }

    public IReadOnlyList<ComponentLibraryCategoryEntry> GetDesktopCategories()
    {
        return _runtimeRegistry
            .GetDesktopComponents()
            .GroupBy(
                descriptor => string.IsNullOrWhiteSpace(descriptor.Definition.Category)
                    ? "Other"
                    : descriptor.Definition.Category.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ComponentLibraryCategoryEntry(
                group.Key,
                group
                    .OrderBy(descriptor => descriptor.Definition.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Select(descriptor => new ComponentLibraryComponentEntry(
                        descriptor.Definition.Id,
                        descriptor.Definition.DisplayName,
                        descriptor.DisplayNameLocalizationKey,
                        group.Key,
                        descriptor.Definition.MinWidthCells,
                        descriptor.Definition.MinHeightCells))
                    .ToArray()))
            .ToArray();
    }

    public bool TryCreateControl(
        string componentId,
        ComponentLibraryCreateContext context,
        out Control? control,
        out Exception? exception)
    {
        control = null;
        exception = null;

        if (!_runtimeRegistry.TryGetDescriptor(componentId, out var descriptor))
        {
            return false;
        }

        try
        {
            control = descriptor.CreateControl(
                context.CellSize,
                context.TimeZoneService,
                context.WeatherInfoService,
                context.RecommendationInfoService,
                context.CalculatorDataService,
                context.SettingsFacade,
                context.PlacementId,
                context.RenderMode);
            return true;
        }
        catch (Exception ex)
        {
            exception = ex;
            return false;
        }
    }
}

internal sealed class EmbeddedComponentLibraryService : IEmbeddedComponentLibraryService
{
    public void Open(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.OpenComponentLibraryWindowFromService();
    }

    public void Close(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.CloseComponentLibraryWindowFromService();
    }

    public void Toggle(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.IsComponentLibraryOpenFromService)
        {
            window.CloseComponentLibraryWindowFromService();
            return;
        }

        window.OpenComponentLibraryWindowFromService();
    }
}

internal sealed class DetachedComponentLibraryWindowService : IDetachedComponentLibraryWindowService
{
    public void Open(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.OpenDetachedComponentLibraryWindowFromService();
    }

    public void Close(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.CloseDetachedComponentLibraryWindowFromService();
    }

    public void Toggle(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.IsDetachedComponentLibraryWindowOpenFromService)
        {
            window.CloseDetachedComponentLibraryWindowFromService();
            return;
        }

        window.OpenDetachedComponentLibraryWindowFromService();
    }
}

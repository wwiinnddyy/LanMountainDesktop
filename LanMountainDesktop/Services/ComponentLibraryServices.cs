using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.Views;
using LanMountainDesktop.Views.Components;

namespace LanMountainDesktop.Services;

public sealed record ComponentLibraryCreateContext(
    double CellSize,
    TimeZoneService TimeZoneService,
    IWeatherInfoService WeatherInfoService,
    IRecommendationInfoService RecommendationInfoService,
    ICalculatorDataService CalculatorDataService,
    string? PlacementId = null);

public interface IComponentLibraryService
{
    IReadOnlyList<DesktopComponentDefinition> GetDefinitions();

    bool TryCreateControl(
        string componentId,
        ComponentLibraryCreateContext context,
        out Control? control,
        out Exception? exception);
}

public interface IComponentLibraryWindowService
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
                context.PlacementId);
            return true;
        }
        catch (Exception ex)
        {
            exception = ex;
            return false;
        }
    }
}

internal sealed class ComponentLibraryWindowService : IComponentLibraryWindowService
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

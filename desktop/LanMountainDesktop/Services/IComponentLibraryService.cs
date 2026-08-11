using System;
using System.Collections.Generic;
using Avalonia.Controls;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.Services;

public sealed record ComponentLibraryComponentEntry(
    string ComponentId,
    string DisplayName,
    string? DisplayNameLocalizationKey,
    string CategoryId,
    int MinWidthCells,
    int MinHeightCells);

public sealed record ComponentLibraryCategoryEntry(
    string Id,
    IReadOnlyList<ComponentLibraryComponentEntry> Components);

public sealed record ComponentLibraryCreateContext(
    double CellSize,
    TimeZoneService TimeZoneService,
    IWeatherInfoService WeatherInfoService,
    IRecommendationInfoService RecommendationInfoService,
    ICalculatorDataService CalculatorDataService,
    ISettingsFacadeService SettingsFacade,
    string? PlacementId = null,
    DesktopComponentRenderMode RenderMode = DesktopComponentRenderMode.Live);

public interface IComponentLibraryService
{
    IReadOnlyList<DesktopComponentDefinition> GetDefinitions();

    IReadOnlyList<ComponentLibraryCategoryEntry> GetDesktopCategories();

    bool TryCreateControl(
        string componentId,
        ComponentLibraryCreateContext context,
        out Control? control,
        out Exception? exception);
}

using System;
using System.Linq;
using Avalonia.Controls;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Views.Components;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class DesktopComponentRenderModeTests
{
    private const string ComponentId = "RenderModeProbe";

    [Fact]
    public void DescriptorCreateControl_DefaultsToLiveRenderMode()
    {
        var descriptor = CreateDescriptor();
        var control = (ProbeControl)descriptor.CreateControl(
            cellSize: 64,
            CreateTimeZoneService(),
            CreateWeatherInfoService(),
            new RecommendationDataService(),
            new CalculatorDataService(),
            CreateSettingsFacade(),
            placementId: "desktop-placement");

        Assert.Equal(DesktopComponentRenderMode.Live, control.RuntimeContext?.RenderMode);
        Assert.Equal("desktop-placement", control.RuntimeContext?.PlacementId);
    }

    [Fact]
    public void DescriptorCreateControl_CanCreateLibraryPreviewRenderModeWithoutPlacement()
    {
        var descriptor = CreateDescriptor();
        var control = (ProbeControl)descriptor.CreateControl(
            cellSize: 64,
            CreateTimeZoneService(),
            CreateWeatherInfoService(),
            new RecommendationDataService(),
            new CalculatorDataService(),
            CreateSettingsFacade(),
            placementId: null,
            renderMode: DesktopComponentRenderMode.LibraryPreview);

        Assert.Equal(DesktopComponentRenderMode.LibraryPreview, control.RuntimeContext?.RenderMode);
        Assert.Null(control.RuntimeContext?.PlacementId);
    }

    [Fact]
    public void ComponentLibraryService_CreatesLibraryPreviewRenderMode()
    {
        var service = new ComponentLibraryService(
            CreateComponentRegistry(),
            CreateRuntimeRegistry());

        var created = service.TryCreateControl(
            ComponentId,
            new ComponentLibraryCreateContext(
                64,
                CreateTimeZoneService(),
                CreateWeatherInfoService(),
                new RecommendationDataService(),
                new CalculatorDataService(),
                CreateSettingsFacade(),
                PlacementId: null,
                RenderMode: DesktopComponentRenderMode.LibraryPreview),
            out var control,
            out var exception);

        Assert.True(created, exception?.ToString());
        var probe = Assert.IsType<ProbeControl>(control);
        Assert.Equal(DesktopComponentRenderMode.LibraryPreview, probe.RuntimeContext?.RenderMode);
        Assert.Null(probe.RuntimeContext?.PlacementId);
    }

    [Fact]
    public void DefaultRuntimeRegistrations_IncludeMaterialWeatherComponents()
    {
        var componentIds = DesktopComponentRuntimeRegistry.GetDefaultRegistrations()
            .Select(registration => registration.ComponentId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains(BuiltInComponentIds.DesktopWeatherClock, componentIds);
        Assert.Contains(BuiltInComponentIds.DesktopWeather, componentIds);
        Assert.Contains(BuiltInComponentIds.DesktopHourlyWeather, componentIds);
        Assert.Contains(BuiltInComponentIds.DesktopMultiDayWeather, componentIds);
        Assert.Contains(BuiltInComponentIds.DesktopExtendedWeather, componentIds);
    }

    [Fact]
    public void WeatherVisualStyleCatalog_NormalizesLegacyAndSupportedIds()
    {
        Assert.Equal(WeatherVisualStyleId.GoogleWeatherV4, WeatherVisualStyleCatalog.Normalize(null));
        Assert.Equal(WeatherVisualStyleId.GoogleWeatherV4, WeatherVisualStyleCatalog.Normalize("DefaultWeather"));
        Assert.Equal(WeatherVisualStyleId.GoogleWeatherV4, WeatherVisualStyleCatalog.Normalize("HyperOS3"));
        Assert.Equal(WeatherVisualStyleId.Geometric, WeatherVisualStyleCatalog.Normalize("Geometric"));
        Assert.Equal(WeatherVisualStyleId.Breezy, WeatherVisualStyleCatalog.Normalize("Breezy"));
        Assert.Equal(WeatherVisualStyleId.LemonFlutter, WeatherVisualStyleCatalog.Normalize("LemonFlutter"));
        Assert.Equal(WeatherVisualStyleId.GoogleWeatherV4, WeatherVisualStyleCatalog.Normalize("MissingPack"));
    }

    [Theory]
    [InlineData(WeatherVisualStyleId.GoogleWeatherV4)]
    [InlineData(WeatherVisualStyleId.Geometric)]
    [InlineData(WeatherVisualStyleId.Breezy)]
    [InlineData(WeatherVisualStyleId.LemonFlutter)]
    public void WeatherIconAssetResolver_ResolvesCoreWeatherStates(string styleId)
    {
        Assert.NotNull(WeatherIconAssetResolver.ResolveAssetUri(styleId, 0, "Clear", isDaylight: true));
        Assert.NotNull(WeatherIconAssetResolver.ResolveAssetUri(styleId, 1, "Partly cloudy", isDaylight: false));
        Assert.NotNull(WeatherIconAssetResolver.ResolveAssetUri(styleId, 7, "Rain", isDaylight: true));
        Assert.NotNull(WeatherIconAssetResolver.ResolveAssetUri(styleId, 4, "Thunderstorm", isDaylight: true));
        Assert.NotNull(WeatherIconAssetResolver.ResolveAssetUri(styleId, 13, "Snow", isDaylight: true));
        Assert.NotNull(WeatherIconAssetResolver.ResolveAssetUri(styleId, 18, "Fog", isDaylight: true));
        Assert.NotNull(WeatherIconAssetResolver.ResolveAssetUri(styleId, 999, "Unknown", isDaylight: true));
    }

    private static DesktopComponentRuntimeDescriptor CreateDescriptor()
    {
        Assert.True(CreateRuntimeRegistry().TryGetDescriptor(ComponentId, out var descriptor));
        return descriptor;
    }

    private static DesktopComponentRuntimeRegistry CreateRuntimeRegistry()
    {
        return new DesktopComponentRuntimeRegistry(
            CreateComponentRegistry(),
            [
                new DesktopComponentRuntimeRegistration(
                    ComponentId,
                    displayNameLocalizationKey: null,
                    _ => new ProbeControl(),
                    cornerRadiusResolver: (System.Func<double, double>?)null)
            ]);
    }

    private static ComponentRegistry CreateComponentRegistry()
    {
        return new ComponentRegistry(
            [
                new DesktopComponentDefinition(
                    ComponentId,
                    "Render Mode Probe",
                    "Apps",
                    "Test",
                    MinWidthCells: 1,
                    MinHeightCells: 1,
                    AllowStatusBarPlacement: false,
                    AllowDesktopPlacement: true)
            ]);
    }

    private static ISettingsFacadeService CreateSettingsFacade()
    {
        return HostSettingsFacadeProvider.GetOrCreate();
    }

    private static TimeZoneService CreateTimeZoneService()
    {
        return CreateSettingsFacade().Region.GetTimeZoneService();
    }

    private static IWeatherInfoService CreateWeatherInfoService()
    {
        return CreateSettingsFacade().Weather.GetWeatherInfoService();
    }

    private sealed class ProbeControl : Control, IComponentRuntimeContextAware
    {
        public DesktopComponentRuntimeContext? RuntimeContext { get; private set; }

        public void SetComponentRuntimeContext(DesktopComponentRuntimeContext context)
        {
            RuntimeContext = context;
        }
    }
}

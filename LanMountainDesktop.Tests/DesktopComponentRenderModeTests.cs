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

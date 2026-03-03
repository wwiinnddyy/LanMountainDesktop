using System;
using Avalonia.Controls;
using LanMontainDesktop.Services;

namespace LanMontainDesktop.Views.Components;

public partial class ExtendedWeatherWidget : UserControl, IDesktopComponentWidget, ITimeZoneAwareComponentWidget, IWeatherInfoAwareComponentWidget
{
    private TimeZoneService? _timeZoneService;
    private IWeatherInfoService? _weatherInfoService;
    private double _currentCellSize = 48;

    public ExtendedWeatherWidget()
    {
        InitializeComponent();
        ApplyCellSize(_currentCellSize);
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);
        var metrics = HyperOS3WeatherTheme.ResolveMetrics(HyperOS3WeatherWidgetKind.Extended4x4);
        ContainerGrid.RowSpacing = Math.Clamp(_currentCellSize * metrics.SectionGap * 0.22, 6, 18);
        HourlyHost.ApplyCellSize(_currentCellSize);
        MultiDayHost.ApplyCellSize(_currentCellSize);
    }

    public void SetTimeZoneService(TimeZoneService timeZoneService)
    {
        _timeZoneService = timeZoneService;
        HourlyHost.SetTimeZoneService(timeZoneService);
        MultiDayHost.SetTimeZoneService(timeZoneService);
    }

    public void ClearTimeZoneService()
    {
        HourlyHost.ClearTimeZoneService();
        MultiDayHost.ClearTimeZoneService();
        _timeZoneService = null;
    }

    public void SetWeatherInfoService(IWeatherInfoService weatherInfoService)
    {
        _weatherInfoService = weatherInfoService;
        HourlyHost.SetWeatherInfoService(weatherInfoService);
        MultiDayHost.SetWeatherInfoService(weatherInfoService);
    }
}

using LanMontainDesktop.Services;

namespace LanMontainDesktop.Views.Components;

public interface IDesktopComponentWidget
{
    void ApplyCellSize(double cellSize);
}

public interface ITimeZoneAwareComponentWidget
{
    void SetTimeZoneService(TimeZoneService timeZoneService);
}

public interface IWeatherInfoAwareComponentWidget
{
    void SetWeatherInfoService(IWeatherInfoService weatherInfoService);
}

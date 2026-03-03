using LanMontainDesktop.Services;

namespace LanMontainDesktop.Views.Components;

public interface IDesktopComponentWidget
{
    void ApplyCellSize(double cellSize);
}

public interface ITimeZoneAwareComponentWidget
{
    void SetTimeZoneService(TimeZoneService timeZoneService);
    void ClearTimeZoneService();
}

public interface IWeatherInfoAwareComponentWidget
{
    void SetWeatherInfoService(IWeatherInfoService weatherInfoService);
}

public interface IDesktopPageVisibilityAwareComponentWidget
{
    void SetDesktopPageContext(bool isOnActivePage, bool isEditMode);
}

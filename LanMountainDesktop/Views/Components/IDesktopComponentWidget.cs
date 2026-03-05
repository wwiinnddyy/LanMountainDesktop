using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

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

public interface IRecommendationInfoAwareComponentWidget
{
    void SetRecommendationInfoService(IRecommendationInfoService recommendationInfoService);
}

public interface ICalculatorInfoAwareComponentWidget
{
    void SetCalculatorDataService(ICalculatorDataService calculatorDataService);
}

public interface IDesktopPageVisibilityAwareComponentWidget
{
    void SetDesktopPageContext(bool isOnActivePage, bool isEditMode);
}

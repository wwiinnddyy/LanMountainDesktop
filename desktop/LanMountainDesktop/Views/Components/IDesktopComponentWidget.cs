using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public interface IDesktopComponentWidget
{
    void ApplyCellSize(double cellSize);
}

public interface IDesktopComponentLifecycleWidget
{
    void OnWidgetDestroyed();
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

/// <summary>
/// 组件"设置变了，重读一遍再画"的入口。宿主重载走的是"就地刷新"（不重建控件实例），
/// 所以这一步必须由宿主递进来：实测有 15 个组件自己写了 <c>RefreshFromSettings()</c> 却没有任何调用点，
/// 切语言后已经放在桌面上的组件要等到被重新添加或重启才换语言。
/// </summary>
public interface ISettingsAwareComponentWidget
{
    void RefreshFromSettings();
}

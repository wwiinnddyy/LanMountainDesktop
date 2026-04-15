* [x] AppSettingsSnapshot 包含 EnableSlideTransition 字段且默认为 false

* [x] DesktopPage 拥有名为 DesktopPageSlideTransform 的 TranslateTransform

* [x] DesktopPage.Transitions 包含 Opacity 和 TranslateTransform.X 两个 DoubleTransition

* [x] 点击"回到 Windows"时播放退场动画（Opacity 淡出 或 Opacity+滑动），动画完成后再最小化

* [x] 从最小化恢复时 DesktopPage 先以 Opacity=0 遮住 Normal 中间态，FullScreen 生效后播放入场动画

* [x] 动画期间 DesktopPage.IsHitTestVisible 为 false，动画完成后恢复

* [x] 动画期间 OnWindowPropertyChanged 不执行强制全屏纠正

* [x] 快速连续操作不会导致动画冲突

* [x] GeneralSettingsPage 在 Windows 平台显示"滑入滑出过渡效果"开关

* [x] GeneralSettingsPage 在非 Windows 平台不显示该开关

* [x] EnableSlideTransition 设置持久化到 AppSettingsSnapshot 且立即生效

* [x] dotnet build 无编译错误


# Tasks

- [x] Task 1: 在 `AppSettingsSnapshot` 中添加 `EnableSlideTransition` 字段
  - [x] 添加 `public bool EnableSlideTransition { get; set; } = false;`
  - [x] 在 `Clone()` 方法中无需特殊处理（bool 是值类型）

- [x] Task 2: 在 `MainWindow.axaml` 的 `DesktopPage` 上添加 `TranslateTransform` 和过渡动画
  - [x] 添加 `<TranslateTransform />`
  - [x] 在 `Grid.Transitions` 中添加 `TranslateTransform.X` 的 `DoubleTransition`，使用 `FluttermotionToken.Duration.Intro` 和 DecelerateBezier 缓动

- [x] Task 3: 在 `MainWindow.axaml.cs` 中实现退场动画逻辑
  - [x] 添加 `_isSlideAnimationActive` 标志位
  - [x] 修改 `OnMinimizeClick`，调用新的 `SlideOutAndMinimizeAsync` 方法
  - [x] 实现 `SlideOutAndMinimizeAsync`：读取设置 → 播放退场动画（Opacity + 可选滑动）→ 等动画完成 → 最小化 → 重置位置
  - [x] 动画期间设置 `DesktopPage.IsHitTestVisible = false`

- [x] Task 4: 在 `MainWindow.axaml.cs` 中实现入场动画逻辑
  - [x] 添加 `public void PrepareEnterAnimation()` 方法：禁用过渡 → 设置初始位置（Opacity=0, X=屏幕宽度或0）→ 重新启用过渡
  - [x] 添加 `public void PlayEnterAnimation()` 方法：触发入场动画（Opacity=1, X=0）
  - [x] 添加 `private bool IsSlideTransitionEnabled()` 方法，从设置中读取

- [x] Task 5: 修改 `App.axaml.cs` 的 `RestoreOrCreateMainWindow`
  - [x] 在窗口状态切换前调用 `mainWindow.PrepareEnterAnimation()`
  - [x] 在 FullScreen 状态生效后调用 `mainWindow.PlayEnterAnimation()`

- [x] Task 6: 修改 `MainWindow.axaml.cs` 的 `OnWindowPropertyChanged`
  - [x] 当 `_isSlideAnimationActive` 为 true 时跳过强制全屏逻辑

- [x] Task 7: 在 `GeneralSettingsPageViewModel` 中添加 `EnableSlideTransition` 属性
  - [x] 添加 `[ObservableProperty] private bool _enableSlideTransition;`
  - [x] 添加 `OnEnableSlideTransitionChanged` 持久化方法
  - [x] 在构造函数和 `OnSettingsChanged` 中加载/同步该设置
  - [x] 添加 `IsSlideTransitionAvailable` 平台检测属性

- [x] Task 8: 在 `GeneralSettingsPage.axaml` 中添加"滑入滑出过渡效果"开关
  - [x] 在"运行时设置"分组中添加 `SettingsExpander`
  - [x] 仅 Windows 平台显示（使用 `IsVisible` 绑定到 `IsSlideTransitionAvailable`）
  - [x] 图标使用 `ArrowRight`

- [x] Task 9: 构建验证
  - [x] 执行 `dotnet build` 确保无编译错误

# Task Dependencies

- [Task 2] depends on [Task 1]
- [Task 3] depends on [Task 1, Task 2]
- [Task 4] depends on [Task 1, Task 2]
- [Task 5] depends on [Task 4]
- [Task 6] depends on [Task 3]
- [Task 7] depends on [Task 1]
- [Task 8] depends on [Task 7]
- [Task 9] depends on [Task 3, Task 4, Task 5, Task 6, Task 7, Task 8]

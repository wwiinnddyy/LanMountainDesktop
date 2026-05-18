# StandBy Digital Clock 任务计划

- [x] Task 1: 注册组件定义与运行时
    - 1.1: 在 `BuiltInComponentIds.cs` 中新增 `DesktopStandbyDigitalClock` 常量
    - 1.2: 在 `ComponentRegistry.cs` 的 `CreateDefault()` 中新增 `DesktopComponentDefinition`（4×2, Clock 分类, Proportional）
    - 1.3: 在 `DesktopComponentRuntimeRegistry.cs` 的 `GetDefaultRegistrations()` 中新增运行时注册项
    - 1.4: 在 `MainWindow.ComponentSystem.cs` 的 `NormalizeAspectRatioForComponent()` 中为 StandbyDigitalClock 添加 2:1 缩放规则

- [x] Task 2: 创建 StandbyDigitalClockWidget AXAML 布局
    - 2.1: 创建 `StandbyDigitalClockWidget.axaml`，定义 RootBorder（DesignCornerRadiusComponent）、Viewbox、时间数字区域（4 个 ClipToBounds 数位容器 + 冒号）、日期文本
    - 2.2: 确保 Viewbox 内基准设计尺寸为 400×200，数字使用 FontWeight.Bold，冒号和日期布局合理

- [x] Task 3: 实现组件代码后置（核心逻辑与动画）
    - 3.1: 创建 `StandbyDigitalClockWidget.axaml.cs`，实现 `IDesktopComponentWidget`, `ITimeZoneAwareComponentWidget`, `IComponentPlacementContextAware`, `IComponentRuntimeContextAware` 接口
    - 3.2: 实现 DispatcherTimer 每秒更新逻辑，比较新旧时间数字，触发数位滚动动画
    - 3.3: 实现数字垂直滚动动画：每位数字使用 TranslateTransform.Y + DoubleTransition，旧数字上滑出新数字滑入，动画完成后清理
    - 3.4: 实现冒号呼吸动画：每秒切换透明度，配合 DoubleTransition 平滑过渡
    - 3.5: 实现日间/夜间模式切换：检测 ActualThemeVariant 和亮度，切换背景渐变和数字颜色；夜间暗光环境过渡到红色调
    - 3.6: 实现 ApplyCellSize 缩放逻辑，clamp 缩放因子，更新圆角和间距
    - 3.7: 实现时区设置加载（复用 AnalogClockWidget 逻辑），点击打开世界时钟 AirApp
    - 3.8: 实现日期文本更新逻辑，显示完整日期和星期

- [x] Task 4: 构建验证与调试
    - 4.1: 执行 `dotnet build` 确保编译通过，修复所有错误
    - 4.2: 检查圆角规范合规性（根容器使用 DesignCornerRadiusComponent）

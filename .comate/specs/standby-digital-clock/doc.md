# StandBy Digital Clock - iPhone 待机风格大数字时钟组件

## 1. 需求场景与处理逻辑

### 1.1 需求描述
新增一个 4×2 尺寸的数字时钟桌面组件，视觉风格参考 iPhone 横屏充电时的 StandBy 待机显示——大面积、粗体、圆润的数字显示当前时间（HH:MM），数字采用不规则的自由排版（有微妙的垂直偏移，不在一条直线上），颜色使用 Monet 主题色而非纯黑/白，伴随数字切换时的流畅垂直滚动/翻转动画，下方显示日期信息。

### 1.2 用户体验目标
- 大字号、圆润粗体的数字时间，远距离一目了然
- 数字采用不规则自由排版（微妙垂直偏移），营造 iPhone StandBy 那种有机、散漫的视觉节奏
- 数字使用 Monet 主题色（跟随壁纸/用户选色的强调色），而非死板的纯黑/白
- 数字变化时执行垂直滑动动画（旧数字向上滑出，新数字从下方滑入），类似翻页时钟效果
- 冒号（:）有呼吸闪烁效果
- 支持夜间/日间模式自动切换
- 点击组件可打开世界时钟 AirApp
- 支持时区配置（与现有桌面时钟共享设置体系）

### 1.3 处理逻辑
1. 组件加载时读取时区设置和秒针模式设置
2. `DispatcherTimer` 每秒触发一次更新
3. 当检测到分钟数变化时，触发分钟数字的垂直滑动动画
4. 当检测到小时数变化时，触发小时数字的垂直滑动动画
5. 冒号以 1 秒周期做透明度脉冲动画
6. 每 tick 检查是否需要切换日间/夜间视觉模式

## 2. 架构与技术方案

### 2.1 组件架构
遵循现有桌面组件架构模式：
- 继承 `UserControl`，实现 `IDesktopComponentWidget`, `ITimeZoneAwareComponentWidget`, `IComponentPlacementContextAware`, `IComponentRuntimeContextAware`
- AXAML 定义根布局结构，代码后置处理动画逻辑
- 通过 `DesktopComponentDefinition` 注册到组件系统

### 2.2 数字滚动动画技术方案
采用 Avalonia `RenderTransform` + `DoubleTransition` 实现数字滚动：

**核心思路**：每个数位（共 4 位：H1, H2, M1, M2）使用 `ClipToBounds` 的容器，内含一个垂直排列的 `StackPanel`，包含当前数字和下一个数字。切换时通过 `TranslateTransform.Y` 的 `DoubleTransition` 实现平滑滚动。

```
每位数字的结构：
┌─ DigitClip (ClipToBounds=true) ──────────┐
│  ┌─ DigitStack (TranslateTransform.Y) ──┐ │
│  │  [当前数字 TextBlock]                  │ │
│  │  [新数字 TextBlock]                    │ │
│  └───────────────────────────────────────┘ │
└───────────────────────────────────────────┘
```

当数字变化时：
1. 在 StackPanel 底部添加新数字的 TextBlock
2. 将 `TranslateTransform.Y` 从 0 动画过渡到 `-digitHeight`
3. 动画完成后移除旧数字，重置 Y 为 0

### 2.3 动画参数
- 使用项目 `FluttermotionToken` 体系：滚动动画时长 `FluttermotionToken.Standard`（200ms）
- 缓动函数：`CubicEaseOut`（与项目现有动画风格一致）
- 冒号呼吸动画：透明度 1.0 → 0.3 → 1.0，周期 2 秒，使用 `DoubleTransition`

### 2.4 尺寸与布局
- 组件定义：`MinWidthCells = 4, MinHeightCells = 2`
- 缩放规则：2:1 比例（与 WorldClock 一致）
- 内部布局采用 `Viewbox` 包裹，确保在不同 cellSize 下自适应缩放
- 数字字体大小：基准设计为 130px（在 Viewbox 内），实际显示由 Viewbox 缩放

### 2.5 布局风格——不规则自由排版（iPhone StandBy 风格）
iPhone StandBy 的数字不是规矩地排成一条直线，而是有微妙的垂直偏移和大小差异，营造出自由散漫、有机的视觉节奏：

```
  H1        H2     :     M1        M2
  ↗↘       ↘↗           ↗↘       ↘↗
  ↕+6      ↕+2     :     ↕+4      ↕+2
  ↖ -3°    ↗ +4°   :     ↖ -1°    ↗ +5°
  ←+6,↑-10 ←-2,↓+10      →+4,↑-3   ←-2,↓+12
```

每个数字有三个自由度：
- **垂直偏移 (Y)**：H1=-10, H2=+10, 冒号=+8, M1=-3, M2=+12
- **水平偏移 (X)**：H1=+6, H2=-2, 冒号=0, M1=+4, M2=-2  
- **旋转角度 (Z)**：H1=-4°, H2=+3°, 冒号=-1°, M1=-2°, M2=+5°

### 2.6 视觉风格——圆润粗体 + Monet 主题色
- **字体**：`FontWeight.Bold`，配合较大的字号，视觉上圆润饱满
- **颜色**：使用项目 Monet 主题色系统，数字颜色跟随 `AdaptiveAccentBrush` / `SystemAccentColor`，而非纯黑/白
  - 数字颜色通过 `ComponentColorSchemeHelper.ShouldUseMonetColor()` 判断：
    - 跟随系统：使用 `AdaptiveAccentBrush`（Monet 提取的强调色）
    - 原生模式：使用组件自带的特色色彩
  - 夜间模式：深色渐变背景 + 主题色数字（亮色调）
  - 日间模式：浅色渐变背景 + 主题色数字（深色调）
  - 夜间暗光环境：数字过渡到柔和的红色调（`#FF6B4A`），模拟 iPhone StandBy 夜间红色调
- **冒号颜色**：与数字同色，但有呼吸动画
- **日期行**：使用 `AdaptiveTextMutedBrush`（跟随主题的弱化文字色），字号约 14-16px 基准
- **根容器圆角**：`DesignCornerRadiusComponent`（遵循圆角规范）

## 3. 受影响文件

### 3.1 新增文件
| 文件 | 类型 | 说明 |
|------|------|------|
| `LanMountainDesktop/Views/Components/StandbyDigitalClockWidget.axaml` | 新增 | 组件 AXAML 布局 |
| `LanMountainDesktop/Views/Components/StandbyDigitalClockWidget.axaml.cs` | 新增 | 组件代码后置（动画逻辑、时间更新、模式切换） |

### 3.2 修改文件
| 文件 | 修改类型 | 受影响函数/区域 |
|------|----------|-----------------|
| `LanMountainDesktop/ComponentSystem/BuiltInComponentIds.cs` | 新增常量 | 新增 `DesktopStandbyDigitalClock` 常量 |
| `LanMountainDesktop/ComponentSystem/ComponentRegistry.cs` | 新增定义 | `CreateDefault()` 中新增组件定义 |
| `LanMountainDesktop/Views/Components/DesktopComponentRuntimeRegistry.cs` | 新增运行时注册 | `GetDefaultRegistrations()` 中新增运行时注册项 |
| `LanMountainDesktop/Views/MainWindow.ComponentSystem.cs` | 新增缩放规则 | `NormalizeAspectRatioForComponent()` 中为 StandbyDigitalClock 添加 2:1 缩放规则 |

## 4. 实现细节

### 4.1 BuiltInComponentIds 新增常量
```csharp
public const string DesktopStandbyDigitalClock = "DesktopStandbyDigitalClock";
```

### 4.2 ComponentRegistry 新增定义
```csharp
new DesktopComponentDefinition(
    BuiltInComponentIds.DesktopStandbyDigitalClock,
    "StandBy Clock",
    "Clock",
    "Clock",
    MinWidthCells: 4,
    MinHeightCells: 2,
    AllowStatusBarPlacement: false,
    AllowDesktopPlacement: true),
```

### 4.3 DesktopComponentRuntimeRegistry 新增注册
```csharp
new DesktopComponentRuntimeRegistration(
    BuiltInComponentIds.DesktopStandbyDigitalClock,
    "component.standby_digital_clock",
    () => new StandbyDigitalClockWidget()),
```

### 4.4 NormalizeAspectRatioForComponent 缩放规则
在 `case BuiltInComponentIds.DesktopWorldClock:` 的同一分支中添加 `BuiltInComponentIds.DesktopStandbyDigitalClock`，使用 2:1 比例规则。

### 4.5 AXAML 布局结构
```xml
<UserControl x:Class="LanMountainDesktop.Views.Components.StandbyDigitalClockWidget">
    <Border x:Name="RootBorder"
            CornerRadius="{DynamicResource DesignCornerRadiusComponent}"
            ClipToBounds="True"
            Padding="14">
        <!-- 背景在代码后置中设置（渐变，与AnalogClockWidget一致） -->
        <Viewbox Stretch="Uniform">
            <Grid Width="400" Height="200">
                <StackPanel VerticalAlignment="Center"
                            HorizontalAlignment="Center"
                            Orientation="Horizontal">
                    <!-- H1 数位 -->
                    <Border x:Name="H1Clip" ClipToBounds="True" ...>
                        <Panel x:Name="H1Stack" ...>
                            <TextBlock x:Name="H1Text" Text="0" ... />
                        </Panel>
                    </Border>
                    <!-- H2 数位 -->
                    <Border x:Name="H2Clip" ClipToBounds="True" ...>
                        <Panel x:Name="H2Stack" ...>
                            <TextBlock x:Name="H2Text" Text="0" ... />
                        </Panel>
                    </Border>
                    <!-- 冒号 -->
                    <TextBlock x:Name="ColonText" Text=":" ... />
                    <!-- M1 数位 -->
                    <Border x:Name="M1Clip" ClipToBounds="True" ...>
                        <Panel x:Name="M1Stack" ...>
                            <TextBlock x:Name="M1Text" Text="0" ... />
                        </Panel>
                    </Border>
                    <!-- M2 数位 -->
                    <Border x:Name="M2Clip" ClipToBounds="True" ...>
                        <Panel x:Name="M2Stack" ...>
                            <TextBlock x:Name="M2Text" Text="0" ... />
                        </Panel>
                    </Border>
                </StackPanel>
                <!-- 日期行 -->
                <TextBlock x:Name="DateTextBlock"
                           VerticalAlignment="Bottom"
                           HorizontalAlignment="Center" ... />
            </Grid>
        </Viewbox>
    </Border>
</UserControl>
```

### 4.6 数字滚动动画核心代码（伪代码）
```csharp
private void AnimateDigit(Border clip, Panel stack, TextBlock currentText, char newDigit, double digitHeight)
{
    var oldText = currentText;
    var newTextBlock = new TextBlock
    {
        Text = newDigit.ToString(),
        FontSize = oldText.FontSize,
        FontWeight = oldText.FontWeight,
        Foreground = oldText.Foreground,
        Width = oldText.Width,
        Height = digitHeight,
        // 复制旧文本的所有样式属性
    };
    stack.Children.Add(newTextBlock);

    // 应用 TranslateTransform 过渡动画
    var transform = new TranslateTransform { Y = 0 };
    stack.RenderTransform = transform;
    stack.Transitions = new Transitions
    {
        new DoubleTransition(TranslateTransform.YProperty, FluttermotionToken.Standard, new CubicEaseOut())
    };

    // 触发动画：从当前位置滑到 -digitHeight
    transform.Y = -digitHeight;

    // 动画完成后清理
    _ = DispatcherTimer.RunOnce(() =>
    {
        stack.Children.Remove(oldText);
        transform.Y = 0;
        stack.Transitions = null; // 移除过渡，避免重置时再次动画
        // 更新引用
        UpdateCurrentTextReference(newTextBlock);
    }, FluttermotionToken.Standard);
}
```

### 4.7 冒号呼吸动画
使用 `DispatcherTimer` 每秒切换冒号透明度：
```csharp
private void ToggleColonOpacity()
{
    _colonVisible = !_colonVisible;
    ColonText.Opacity = _colonVisible ? 1.0 : 0.3;
}
```
配合 `DoubleTransition` 使透明度变化平滑过渡。

### 4.8 日间/夜间模式
与 `AnalogClockWidget` 使用完全相同的判断逻辑：
- 检查 `ActualThemeVariant`
- 回退到 `AdaptiveSurfaceBaseBrush` 亮度计算
- 夜间模式：深色渐变背景 + 浅色数字
- 日间模式：浅色渐变背景 + 深色数字

### 4.9 时区与设置
- 复用 `AnalogClockWidget` 的时区解析和设置加载逻辑
- 使用 `ComponentSettingsSnapshot.DesktopClockTimeZoneId` 读取时区配置
- 点击打开世界时钟 AirApp

## 5. 边界条件与异常处理

| 场景 | 处理方式 |
|------|----------|
| 组件首次加载时数字尚未初始化 | 在构造函数中初始化所有数字为当前时间，不触发动画 |
| 快速连续触发数字变化（如时间同步导致跳变） | 在动画完成前忽略新的变化请求，或中断当前动画立即跳转到目标值 |
| cellSize 极小或极大 | `ApplyCellSize` 中 clamp 缩放因子（0.58-1.95，与 AnalogClockWidget 一致） |
| 时区切换 | 重新加载设置并更新所有数字（无动画，直接设置） |
| 主题切换 | 通过 `ApplyModeVisualIfNeeded()` 在下一个 tick 自动检测并切换 |
| 组件被销毁 | `DetachedFromVisualTree` 停止 timer，清理资源 |
| 冒号动画在组件不可见时 | timer 仍在运行但 Opacity 变化无性能开销；若需要可结合 `IDesktopPageVisibilityAwareComponentWidget` |

## 6. 数据流路径

```
DispatcherTimer (1s interval)
    → OnTimerTick
        → 计算当前时间 (TimeZoneInfo.ConvertTimeFromUtc)
        → 比较新旧时间数字
        → 若有变化: AnimateDigit() 执行滚动动画
        → ToggleColonOpacity() 切换冒号
        → ApplyModeVisualIfNeeded() 检查日/夜间切换
        → UpdateDateText() 更新日期文本

用户点击 → OnPointerReleased → AirAppLauncherServiceProvider.OpenWorldClock()

时区变更 → TimeZoneChanged event → RefreshFromSettings() → 无动画更新所有数字
```

## 7. 预期成果

- 在桌面组件选择器中新增 "StandBy Clock" 组件，位于 Clock 分类
- 拖放到桌面后显示 4×2 大数字时钟
- 数字切换时有流畅的垂直滑动动画
- 冒号有呼吸闪烁效果
- 支持日间/夜间自动切换
- 支持时区配置
- 支持组件缩放（2:1 比例规则）

# StandBy Digital Clock 实现总结

## 完成状态
全部任务已完成，构建通过（0 错误）。

## 变更清单

### 新增文件
| 文件 | 说明 |
|------|------|
| `LanMountainDesktop/Views/Components/StandbyDigitalClockWidget.axaml` | AXAML 布局：不规则自由排版数字 + 冒号 + 日期，Monet 主题色绑定 |
| `LanMountainDesktop/Views/Components/StandbyDigitalClockWidget.axaml.cs` | 代码后置：数字滚动动画、冒号呼吸、Monet 主题色、日/夜模式、时区支持 |

### 修改文件
| 文件 | 改动 |
|------|------|
| `LanMountainDesktop/ComponentSystem/BuiltInComponentIds.cs` | 新增 `DesktopStandbyDigitalClock` 常量 |
| `LanMountainDesktop/ComponentSystem/ComponentRegistry.cs` | 在 `CreateDefault()` 中新增 4×2 Clock 分类组件定义 |
| `LanMountainDesktop/Views/Components/DesktopComponentRuntimeRegistry.cs` | 新增 `StandbyDigitalClockWidget` 运行时注册 |
| `LanMountainDesktop/Views/MainWindow.ComponentSystem.cs` | `NormalizeAspectRatioForComponent` 将 StandbyDigitalClock 加入 2:1 缩放规则 |

## 核心设计要点

### 不规则自由排版（iPhone StandBy 风格）
- 每个数字有独立的垂直 Margin 偏移（H1 上移10, H2 下移8, M1 上移5, M2 下移10）
- 冒号比数字中心略低（下移6）
- 数字间距不等，营造自由散漫的视觉节奏

### Monet 主题色
- 数字和冒号使用 `AdaptiveAccentBrush` / `SystemAccentColor`，跟随壁纸/用户选色的强调色
- 通过 `ComponentColorSchemeHelper.ShouldUseMonetColor()` 判断：
  - 跟随系统：使用 Monet 提取的强调色
  - 原生模式：使用暖橙红色（`#E84530` 日间 / `#FF8A65` 夜间），灵感来自 iPhone StandBy
- 日期文本使用 `AdaptiveTextMutedBrush`

### 数字滚动动画
- `TranslateTransform.Y` + `DoubleTransition`（200ms CubicEaseOut）
- 动画完成后清理旧 TextBlock 并重置 transform

### 冒号呼吸
- 每秒切换 Opacity（1.0 ↔ 0.25），配合 400ms CubicEaseInOut 平滑过渡

### 日/夜模式
- 检测 `ActualThemeVariant` + `AdaptiveSurfaceBaseBrush` 亮度计算
- 夜间：深色渐变背景 + 亮调强调色数字
- 日间：浅色渐变背景 + 深调强调色数字

### 组件规格
- 尺寸：4×2 (MinWidthCells=4, MinHeightCells=2)
- 分类：Clock
- 缩放：2:1 比例 (Proportional)
- 字体：FontWeight.Bold, 120px 基准

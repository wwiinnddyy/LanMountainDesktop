# LanMontainDesktop 视觉规范（主题色 + 毛玻璃）

## 1. 主题色应用规范

### 1.1 颜色角色定义

- `Primary`（主色）：品牌主导色，用于主要操作、关键状态提示。
- `Secondary`（辅助色）：主色的低权重变体，用于次级强调、辅助信息。
- `Accent`（强调色）：可被用户替换的动态主题色，用于选中态、激活态、聚焦态。
- `OnAccent`：放在强调色背景上的文本/图标颜色。
- `SurfaceBase` / `SurfaceRaised` / `SurfaceOverlay`：基础背景、抬升层、遮罩层。
- `TextPrimary` / `TextSecondary` / `TextMuted` / `TextAccent`：文字语义层级。

### 1.2 UI 元素映射规则

- 主按钮、主导航选中态：`Accent` + `OnAccent`
- 次级按钮/输入控件：`AdaptiveButtonBackgroundBrush` + `TextPrimary`
- 页头标题：`TextPrimary`
- 说明/辅助文本：`TextSecondary` / `TextMuted`
- 设置页导航激活项：`AdaptiveNavItemSelectedBackgroundBrush` + `AdaptiveNavSelectedTextBrush`

### 1.3 统一资源键（单一真相源）

- 主题核心：
  - `AdaptivePrimaryBrush`
  - `AdaptiveSecondaryBrush`
  - `AdaptiveAccentBrush`
  - `AdaptiveOnAccentBrush`
- 文本：
  - `AdaptiveTextPrimaryBrush`
  - `AdaptiveTextSecondaryBrush`
  - `AdaptiveTextMutedBrush`
  - `AdaptiveTextAccentBrush`
- 表面：
  - `AdaptiveSurfaceBaseBrush`
  - `AdaptiveSurfaceRaisedBrush`
  - `AdaptiveSurfaceOverlayBrush`

## 2. 毛玻璃（Glassmorphism）统一实现方案

### 2.1 分层标准

- `glass-overlay`：最高层遮罩（设置页背板）
- `glass-strong`：主内容容器（设置页主体）
- `glass-panel`：子功能区、组件容器（网格卡片、按钮容器）

### 2.2 参数标准（模拟毛玻璃，跨平台稳定）

- 描边：统一去除（`BorderThickness = 0`）
- 模糊半径资源（供样式/扩展复用）：
  - `AdaptiveGlassPanelBlurRadius`（日 18 / 夜 22）
  - `AdaptiveGlassStrongBlurRadius`（日 24 / 夜 28）
- 透明度资源：
  - `AdaptiveGlassPanelOpacity`（日 0.88 / 夜 0.92）
  - `AdaptiveGlassStrongOpacity`（日 0.92 / 夜 0.95）
- 背景色：由 `GlassEffectService` 基于主题色动态混合，统一下发到：
  - `AdaptiveGlassPanelBackgroundBrush`
  - `AdaptiveGlassStrongBackgroundBrush`
  - `AdaptiveGlassOverlayBackgroundBrush`

## 3. 视觉一致性策略

- 全局样式入口：`Styles/GlassModule.axaml`
- 全局主题入口：`ThemeColorSystemService` + `GlassEffectService`
- 页面侧仅使用语义资源键和 `glass-*` 类，不写硬编码颜色
- `MainWindow` 只负责编排：切换模式、选择主题色、触发资源重算

## 4. 可访问性（WCAG）

### 4.1 对比度目标

- 正文文本：`>= 4.5:1`
- 大号文本 / 强调文本：`>= 3.0:1`

### 4.2 实现方式

- `Theme/ColorMath.cs` 提供：
  - 相对亮度计算
  - 对比度计算
  - `EnsureContrast(...)` 自动修正文本前景色
- `ThemeColorSystemService` 在生成 `TextPrimary/TextSecondary/TextMuted/NavText` 时强制走对比度校正

## 5. 跨尺寸与分辨率一致性

- 启用像素对齐：`UseLayoutRounding="True"` + `SnapsToDevicePixels="True"`
- 桌面网格布局通过统一计算函数输出 `row/col/cell`，主视图与预览共用算法
- 预览区域按窗口实际宽高比缩放，保持 Win11 风格比例一致性
- 关键尺寸自适应（字体、内边距、圆角）随 `cellSize` 动态计算

## 6. 实现代码示例

### 6.1 主题系统应用（C#）

```csharp
var context = new ThemeColorContext(
    selectedAccent,
    isLightBackground,
    isLightNavBackground,
    isNightMode);

ThemeColorSystemService.ApplyThemeResources(Resources, context);
GlassEffectService.ApplyGlassResources(Resources, context);
```

### 6.2 页面层使用语义资源（AXAML）

```xml
<Border Classes="glass-overlay" />

<Border Classes="glass-strong" CornerRadius="16">
    <Border Classes="glass-panel" CornerRadius="10" Padding="14">
        <TextBlock Foreground="{DynamicResource AdaptiveTextPrimaryBrush}" />
        <Button Background="{DynamicResource AdaptiveButtonBackgroundBrush}" />
    </Border>
</Border>
```

### 6.3 无描边层级区分原则（AXAML）

```xml
<Style Selector="Border.glass-panel">
    <Setter Property="BorderThickness" Value="0" />
    <Setter Property="Opacity" Value="{DynamicResource AdaptiveGlassPanelOpacity}" />
</Style>
```


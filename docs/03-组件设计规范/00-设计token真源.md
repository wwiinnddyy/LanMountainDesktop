# 设计 Token 与资源真源

本文是组件视觉与主题的**唯一真源清单**：列出代码里真实存在的资源键、Token 与其取值。
设计规范类文档（[01](01-设计系统概述.md)~[05](05-主题系统.md)）负责讲怎么用，**键名与数值一律以本文与代码为准**。

真源文件：

| 内容 | 文件 |
|---|---|
| 自适应画笔键名 | `desktop/LanMountainDesktop/Theme/ThemeResourceKeys.cs` |
| 主题色派生逻辑 | `desktop/LanMountainDesktop/Theme/AdaptiveBrushFactory.cs`、`ThemeColorContext.cs`、`ColorMath.cs` |
| 调色板模型 | `desktop/LanMountainDesktop/Theme/AppThemePalette.cs` |
| 圆角与表面样式 | `desktop/LanMountainDesktop/Styles/GlassModule.axaml` |
| 动效时长 | `desktop/LanMountainDesktop/Styles/FluttermotionToken.axaml` |
| 字体 | `desktop/LanMountainDesktop/App.axaml`（`AppFontFamily`） |

最后核对：2026-11-14。

---

## 1. 颜色：不存在"标准色值表"

**这是最容易被写错的一点。** 项目的颜色是**运行时派生**的，不是一组固定的十六进制值：

```
材质色 / 壁纸种子
  → ThemeColorContext + ColorMath 计算
  → AdaptiveBrushFactory 生成画笔
  → 注册为 27 个 Adaptive* 资源键
```

因此：

- **不要写死颜色**（`Color="#0078D4"` 这类）。
- **不要自造键名**（历史上文档里的 `AccentBrush`、`CardBackgroundBrush`、`SuccessBrush`、`DividerBrush` 等**都不存在**，写了只会静默失色）。
- 取用时用 `{DynamicResource <键名>}`。

### 1.1 真实键名（共 27 个）

| 常量 | 资源键 |
|---|---|
| `AccentBrush` | `AdaptiveAccentBrush` |
| `OnAccentBrush` | `AdaptiveOnAccentBrush` |
| `TextAccentBrush` | `AdaptiveTextAccentBrush` |
| `TextPrimaryBrush` | `AdaptiveTextPrimaryBrush` |
| `TextSecondaryBrush` | `AdaptiveTextSecondaryBrush` |
| `TextMutedBrush` | `AdaptiveTextMutedBrush` |
| `SurfaceBaseBrush` | `AdaptiveSurfaceBaseBrush` |
| `SurfaceOverlayBrush` | `AdaptiveSurfaceOverlayBrush` |
| `SurfaceRaisedBrush` | `AdaptiveSurfaceRaisedBrush` |
| `GlassPanelBackgroundBrush` / `GlassPanelBorderBrush` / `GlassPanelOpacity` | `AdaptiveGlassPanel*` |
| `GlassStrongBackgroundBrush` / `GlassStrongBorderBrush` / `GlassStrongOpacity` | `AdaptiveGlassStrong*` |
| `GlassOverlayBackgroundBrush` / `GlassOverlayOpacity` | `AdaptiveGlassOverlay*` |
| `DockGlassBackgroundBrush` / `DockGlassBorderBrush` | `AdaptiveDockGlass*` |
| `ButtonBackgroundBrush` / `ButtonBorderBrush` / `ButtonHoverBackgroundBrush` / `ButtonPressedBackgroundBrush` | `AdaptiveButton*` |
| `NavItemBackgroundBrush` / `NavItemSelectedBackgroundBrush` | `AdaptiveNavItem*` |
| `SettingsWindowBackgroundBrush` / `SettingsWindowTintBrush` | `AdaptiveSettingsWindow*` |

> 2026-10-25 的清理删掉了 28 个"只写不读"的键，键数从 55 降到 27。**新增键必须同时在注册方与消费方落地**，否则又会积累纯开销。

### 1.2 另一族可用键：FluentAvalonia 系统色

除了本仓库的 `Adaptive*`，**FluentAvalonia 主题**还提供一族系统色键，仓库里在用（全仓约 120 处引用），可以直接使用：

| 键 | 用途 |
|---|---|
| `TextFillColorPrimaryBrush` | 主要文本 |
| `TextFillColorSecondaryBrush` | 次要文本 |
| `TextFillColorTertiaryBrush` | 辅助文本 |
| `SystemFillColorSuccessBrush` / `SystemFillColorSuccessBackgroundBrush` | 成功 |
| `SystemFillColorCautionBrush` / `SystemFillColorCautionBackgroundBrush` | 警告 |
| `SystemFillColorCriticalBrush` / `SystemFillColorCriticalBackgroundBrush` | 错误 |
| `SystemFillColorAttentionBrush` / `SystemFillColorAttentionBackgroundBrush` | 信息 / 强调 |

**选择口径**：

- 需要**跟随材质色与壁纸**的表面、玻璃、按钮、导航类观感 → 用本仓库的 `Adaptive*`；
- 文本层级与语义色（成功/警告/错误/信息） → 用 FluentAvalonia 的 `TextFillColor*` / `SystemFillColor*`。

**本项目没有**自己的 `SuccessBrush` / `ErrorBrush` / `WarningBrush` / `InfoBrush`——要用语义色就用上面的 `SystemFillColor*`。

### 1.2 读取方式

C# 侧唯一口径是 `AdaptiveTokens`（`Theme/AdaptiveTokens.cs`）：

```csharp
// 沿资源作用域往上找；找不到返回 false，不猜也不兜底
if (AdaptiveTokens.TryGet<IBrush>(this, ThemeResourceKeys.TextPrimaryBrush, out var brush))
{
    // 用 brush
}

// 或带 fallback
var b = AdaptiveTokens.Brush(this, ThemeResourceKeys.SurfaceRaisedBrush, Brushes.Transparent);
```

不要在组件里自己 `TryFindResource` 或只看某一本字典——历史上宿主里曾有 5 份各自为政的实现，表现是"同一个键在 A 处取得到、B 处取不到，且静默失色"。

---

## 2. 圆角

Token 定义在 `Styles/GlassModule.axaml`，**同一个键在两套档位下取值不同**（玻璃档与纯色回退档）：

| Token | 玻璃档 | 回退档 |
|---|---|---|
| `DesignCornerRadiusMicro` | 6 | 2 |
| `DesignCornerRadiusXs` | 12 | 4 |
| `DesignCornerRadiusSm` | 14 | 4 |
| `DesignCornerRadiusMd` | 20 | 8 |
| `DesignCornerRadiusLg` | 28 | 8 |
| `DesignCornerRadiusXl` | 32 | 12 |
| `DesignCornerRadiusIsland` | 36 | 16 |
| **`DesignCornerRadiusComponent`** | **18** | **8** |

另有对控件友好的两个键：`OverlayCornerRadius`（8）、`ControlCornerRadius`（4）。

规则：

- 桌面组件根容器**必须且仅能**用 `{DynamicResource DesignCornerRadiusComponent}`。
- 内部元素按嵌套层级用 `Sm/Md/Lg` 等 Token，**不要硬编码像素值**。
- **严禁在圆角资源上乘 `scale`**——缩放由全局样式统一控制。
- 数值随档位变化，所以**任何文档里的"标准圆角 = N px"都只对某一档成立**；写文档时要么给两档，要么只给键名。

---

## 3. 动效

`Styles/FluttermotionToken.axaml`：

| 键 | 值 |
|---|---|
| `FluttermotionToken.Duration.Fast` | 0.12s（120ms） |
| `FluttermotionToken.Duration.Standard` | 0.20s（200ms） |
| `FluttermotionToken.Duration.Slow` | 0.28s（280ms） |
| `FluttermotionToken.Duration.Page` | 0.32s（320ms） |
| `FluttermotionToken.Duration.Intro` | 0.40s（400ms） |
| `FluttermotionToken.BackdropBlurRadiusStrong` | 30 |

**只有这六个键**。历史文档里的"150/300/500ms""说明性动画 100-150ms"等数值都没有对应实现。

缓动在代码里用 Avalonia 的 `Easing` 子类（如 `QuarticEaseOut`），不要引用不存在的自定义缓动名。

---

## 4. 字体

`App.axaml`：

| 键 | 值 |
|---|---|
| `AppFontFamily` | `MiSans VF`（内嵌字体资源） |
| `AppFontFamilyJP` | `MiSans JP` |
| `AppFontFamilyKR` | `MiSans KR` |

窗口级默认已绑定 `AppFontFamily`。**不是** Microsoft YaHei UI / Segoe UI——文档里这么写的都过期了。

字号没有 `DesignFontSize*` 这类 Token；直接用 Avalonia 的字号值或复用已有样式类。

---

## 5. 表面样式类

`Styles/GlassModule.axaml` 里只有这 5 个给 `Border` 用的表面类：

| 类名 | 用途 |
|---|---|
| `glass-panel` | 玻璃面板（默认档） |
| `surface-translucent-panel` | 半透明面板 |
| `surface-translucent-strong` | 强半透明 |
| `surface-translucent-island` | 岛式半透明 |
| `surface-solid-strong` | 纯色强表面 |

设置页作用域另有 `.settings-scope` 一族的控件样式。

`Styles/` 下的其余文件：`ComponentEditorThemeResources.axaml`、`FluttermotionToken.axaml`、`NavigationStyles.axaml`、`SettingsAnimations.axaml`、`SettingsCardStyles.axaml`。

---

## 6. 组件契约

桌面组件的真实契约是 **Avalonia `Control`（通常是 `UserControl`）+ `IDesktopComponentWidget`**（`Views/Components/IDesktopComponentWidget.cs`）。

**不存在 `ComponentBase`**，也没有 `OnSizeChanged(Size)`、`ShowCompactLayout()`、`InitializeAsync()` 这些方法。宿主的运行期信息通过这些接口交给控件：

| 接口 | 提供的上下文 |
|---|---|
| `IComponentPlacementContextAware` | `ComponentId` + `PlacementId` |
| `IComponentRuntimeContextAware` | 运行时上下文 |
| `IComponentSettingsContextAware` | 组件设置读写 |
| `IComponentChromeContextAware` | 外观/圆角解析 |

**不存在 `LayoutHelper`**（`AlignToGrid` / `SafeArea` / `IsCompact` 之类）。网格几何由宿主 `DesktopGridLayoutService` 负责，组件只需按拿到的单元尺寸排版。

---

## 相关文档

- [设计系统概述](01-设计系统概述.md) · [视觉规范](02-视觉规范.md) · [布局规范](03-布局规范.md) · [交互规范](04-交互规范.md) · [主题系统](05-主题系统.md)
- [组件系统（架构）](../04-架构与实现/05-组件系统.md)

# 骨架页（Skeleton Screen）实施计划

## 问题分析

当前首次启动时，用户看到的现象：

1. 全屏窗口立即显示，但状态栏组件全部 `IsVisible="False"`（空白）
2. 头像区域只有 fallback 文字 "U"，尺寸未计算（显得巨大）
3. 底部 Dock 任务栏已经渲染但内容为空
4. 壁纸加载完成前，桌面区域是透明/黑色的
5. 整体看起来就是一个"Dock 栏覆盖全屏 + 巨大头像"的半成品状态

**根本原因**：`OnOpened` 中有大量同步初始化操作（壁纸解码、组件布局、启动器扫描等），在它们完成之前，UI 元素要么不可见要么处于默认状态。

## 方案概述

在 `DesktopPage` 层添加一个**骨架遮罩层**，覆盖在真实内容之上，在初始化完成前显示骨架占位，初始化完成后淡出消失。

### 骨架页布局

```
┌──────────────────────────────────────────────────────────────┐
│  [状态栏骨架]                                                  │
│  ┌─────────┐   ┌──────────────────┐   ┌─────────┐           │
│  │ ○ 头像  │   │ ████ 时钟 ████   │   │  ○ ○    │           │
│  └─────────┘   └──────────────────┘   └─────────┘           │
│                                                              │
│                     (桌面区域 - 壁纸层)                        │
│                                                              │
│  ┌──────────────────────────────────────────────────────────┐ │
│  │  [Dock 任务栏骨架]                                        │ │
│  │  ██  ████████  ████████  ████████  ████████  ○          │ │
│  └──────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────┘
```

### 骨架元素

| 区域      | 骨架元素   | 形状                                  | 说明                            |
| ------- | ------ | ----------------------------------- | ----------------------------- |
| 状态栏-中间  | 时钟骨架   | 圆角矩形（`DesignCornerRadiusComponent`） | 模拟 ClockWidget 的胶囊形状          |
| 状态栏-中间  | 文本胶囊骨架 | 圆角矩形（较小）                            | 模拟 TextCapsuleWidget          |
| 底部 Dock | 头像骨架   | 圆形                                  | 模拟 TaskbarProfileAvatarBorder |
| 底部 Dock | 操作按钮骨架 | 圆角矩形                                | 模拟任务栏按钮                       |
| 底部 Dock | 分隔线骨架  | 细长矩形                                | 模拟按钮间分隔                       |

### 骨架样式

* **颜色**：使用 `AdaptiveGlassPanelBackgroundBrush` 作为基础色，叠加一个 **Shimmer 动画**（微光扫过效果）

* **圆角**：与真实组件一致，使用 `DesignCornerRadiusComponent`

* **动画**：Shimmer 微光从左到右扫过，周期 2s，使用 `FluttermotionToken` 缓动

## 实施步骤

### Step 1: 创建 Shimmer 动画画刷

在 `GlassModule.axaml` 或新建 `SkeletonStyles.axaml` 中定义：

* 创建 `ShimmerBrush`：一个 `LinearGradientBrush`，包含高光条带

* 创建 `ShimmerAnimation` storyboard：让高光条带从左到右移动

* 定义 `skeleton-shimmer` 样式类：应用 ShimmerBrush + 动画

### Step 2: 在 MainWindow\.axaml 中添加骨架遮罩层

在 `DesktopPage` Grid 内、所有真实内容之上添加一个 `Grid x:Name="SkeletonOverlay"`：

* 初始 `IsVisible="True"`，`ZIndex="999"`

* 包含状态栏骨架区域和底部 Dock 骨架区域

* 使用与真实布局相同的 Grid RowDefinitions，确保骨架元素对齐

### Step 3: 在 MainWindow\.axaml.cs 中控制骨架显示/隐藏

* 在 `OnOpened` 开始时，骨架层可见

* 在 `OnOpened` 末尾（所有初始化完成后），调用 `HideSkeletonOverlayAsync()`

* `HideSkeletonOverlayAsync()`：播放淡出动画 → 设置 `IsVisible="False"`

* 如果启用了滑入滑出过渡，骨架层在入场动画期间也应可见，入场动画完成后再淡出

### Step 4: 骨架元素尺寸适配

* 骨架元素需要在 `ApplyTaskbarSettings()` 后更新尺寸（因为 `taskbarCellHeight` 等值在 OnOpened 中才计算）

* 或者在 XAML 中使用相对尺寸（百分比/比例），避免依赖代码计算

### Step 5: 与窗口过渡动画的协调

* 入场动画（`PrepareEnterAnimation` / `PlayEnterAnimation`）期间，骨架层应保持可见

* 入场动画完成后，先短暂显示骨架（\~100ms），然后淡出骨架

* 退场动画时，无需特殊处理（骨架已隐藏）

## 涉及文件

| 文件                                | 改动类型                 |
| --------------------------------- | -------------------- |
| `Styles/SkeletonStyles.axaml`（新建） | Shimmer 画刷 + 骨架样式类   |
| `Views/MainWindow.axaml`          | 添加 SkeletonOverlay 层 |
| `Views/MainWindow.axaml.cs`       | 骨架显示/隐藏逻辑            |
| `App.axaml`                       | 引入 SkeletonStyles 资源 |

## 不涉及的文件

* 不修改组件代码（ClockWidget、TextCapsuleWidget 等）

* 不修改设置系统

* 不修改 App.axaml.cs 的启动流程


# 天气组件视觉重构 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 彻底重构阑山桌面天气系列组件的背景视觉和文字排版，为每种图标风格（Google Weather / Geometric / Breezy / Lemon）提供独立的背景配色和视觉质感，参考各天气 App 的 Material Design 风格，实现几何质感+柔和渐变+层次分明的排版。

**Architecture:** 保留现有数据层（WeatherWidgetBase、WeatherSnapshot、WeatherIconAssetResolver）和组件注册机制不变。核心改动：1) 将 `MaterialWeatherVisualTheme.ResolvePalette()` 扩展为按 styleId 分派不同配色方案；2) 重构 `MaterialWeatherSceneControl` 为按 styleId 渲染不同背景风格；3) 改进各天气 Widget 的文字排版层次。先创建 HTML Mock 验证视觉效果。

**Tech Stack:** Avalonia UI (XAML + C# code-behind)、HTML/CSS (Mock 预览)

---

## 当前状态分析

### 现有天气组件体系
5 个天气组件，全部继承自 `WeatherWidgetBase`：

| 组件 | 文件 | 功能 |
|------|------|------|
| WeatherWidget | `WeatherWidget.axaml(.cs)` | 基础天气：温度+状况+图标+位置 |
| WeatherClockWidget | `WeatherClockWidget.axaml(.cs)` | 天气+时钟 |
| ExtendedWeatherWidget | `ExtendedWeatherWidget.axaml(.cs)` | 扩展天气：含指标/小时/多日预报 |
| HourlyWeatherWidget | `HourlyWeatherWidget.axaml(.cs)` | 逐小时天气 |
| MultiDayWeatherWidget | `MultiDayWeatherWidget.axaml(.cs)` | 多日天气 |

### 核心问题

1. **背景与图标风格脱钩**: `MaterialWeatherVisualTheme.ResolvePalette()` 只返回一套配色，与 `WeatherVisualStyleId`（GoogleWeatherV4/Geometric/Breezy/LemonFlutter）完全无关。切换图标风格时背景不变。
2. **背景视觉单调**: `MaterialWeatherSceneControl` 只有一种手绘几何风格（椭圆+云+雨滴），质感差，缺乏各 App 的特色。
3. **文字排版粗糙**: 温度数字不够大，信息层次不分明，指标用纯文字堆叠，预报区域无卡片样式。
4. **半透明遮罩硬编码**: 所有组件都覆盖 `<Border Background="#30FFFFFF" />` 等硬编码遮罩，不随风格变化。

### 各天气 App 风格特征

**Google Weather (v4)**:
- 背景：大面积柔和蓝白渐变，晴天偏暖黄蓝，雨天偏深蓝灰
- 装饰：极简，几乎无几何装饰，纯靠渐变色彩表现天气氛围
- 排版：温度超大（72px+），天气状况中等，位置小字

**Geometric Weather (几何天气)**:
- 背景：深色系渐变（深蓝/深紫/深灰），搭配半透明几何圆形装饰
- 装饰：大面积半透明圆形叠加，营造深度感
- 排版：紧凑信息密度，指标用小标签

**Breezy Weather (微风天气)**:
- 背景：清新渐变（浅蓝/浅绿/浅紫），比 Geometric 更明亮
- 装饰：柔和波浪线条 + 少量几何装饰，Material Design 风格
- 排版：卡片式预报，圆角芯片

**Lemon Weather (柠檬天气)**:
- 背景：暖色系渐变（橙黄/粉紫/暖蓝），柠檬2偏扁平，柠檬3偏Material
- 装饰：天气场景装饰（太阳光芒/云朵轮廓/雨丝），更有场景感
- 排版：温度超大，天气图标突出

---

## 设计方案

### 视觉论文 (Visual Thesis)
每种图标风格拥有独特的背景渐变配色和几何装饰语言——Google 纯净渐变、Geometric 深色几何、Breezy 清新波浪、Lemon 暖色场景——配合超大温度数字和层次分明的排版，在桌面小组件空间内实现 Material Design 的几何质感。

### 配色方案设计

每种风格 × 每种天气条件 × 昼夜 = 独立配色。以下为关键配色定义：

#### Google Weather 风格
| 天气 | 白天 Top→Bottom | 夜晚 Top→Bottom |
|------|----------------|----------------|
| Clear | #4FC3F7 → #B3E5FC | #0D47A1 → #1A237E |
| PartlyCloudy | #81D4FA → #E1F5FE | #1565C0 → #283593 |
| Cloudy | #90A4AE → #CFD8DC | #37474F → #455A64 |
| Rain | #78909C → #B0BEC5 | #263238 → #37474F |
| Storm | #546E7A → #78909C | #1A1A2E → #263238 |
| Snow | #E1F5FE → #FFFFFF | #1A237E → #283593 |
| Fog/Haze | #B0BEC5 → #ECEFF1 | #455A64 → #546E7A |

#### Geometric 风格
| 天气 | 白天 Top→Bottom | 夜晚 Top→Bottom |
|------|----------------|----------------|
| Clear | #1A237E → #3949AB | #0A0E27 → #1A1A3E |
| PartlyCloudy | #283593 → #5C6BC0 | #0D1033 → #1E1E4A |
| Cloudy | #37474F → #607D8B | #1A1A2E → #2D2D44 |
| Rain | #1A237E → #3F51B5 | #0A0E27 → #1A1A3E |
| Storm | #1A1A2E → #3F51B5 | #050510 → #1A1A2E |
| Snow | #E8EAF6 → #C5CAE9 | #1A237E → #283593 |
| Fog/Haze | #455A64 → #78909C | #1A1A2E → #37474F |

#### Breezy 风格
| 天气 | 白天 Top→Bottom | 夜晚 Top→Bottom |
|------|----------------|----------------|
| Clear | #4DD0E1 → #80DEEA | #006064 → #00838F |
| PartlyCloudy | #4FC3F7 → #B2EBF2 | #00695C → #00897B |
| Cloudy | #80CBC4 → #B2DFDB | #37474F → #546E7A |
| Rain | #4DB6AC → #80CBC4 | #004D40 → #00695C |
| Storm | #26A69A → #4DB6AC | #1A1A2E → #004D40 |
| Snow | #E0F7FA → #FFFFFF | #006064 → #00838F |
| Fog/Haze | #80CBC4 → #E0F7FA | #37474F → #546E7A |

#### Lemon 风格
| 天气 | 白天 Top→Bottom | 夜晚 Top→Bottom |
|------|----------------|----------------|
| Clear | #FFB74D → #FFF176 | #1A237E → #311B92 |
| PartlyCloudy | #FF8A65 → #FFCC80 | #283593 → #4A148C |
| Cloudy | #BCAAA4 → #D7CCC8 | #37474F → #4E342E |
| Rain | #90A4AE → #B0BEC5 | #1A1A2E → #311B92 |
| Storm | #78909C → #90A4AE | #0D0D1A → #1A1A2E |
| Snow | #FFF9C4 → #FFFFFF | #1A237E → #311B92 |
| Fog/Haze | #D7CCC8 → #EFEBE9 | #4E342E → #5D4037 |

### 排版改进方案

1. **温度超大化**: 温度字号从 56-58px 提升到 64-72px（基础组件），形成视觉锚点
2. **层次分明**: 温度 → 天气状况 → 位置/指标，字号递减，透明度递减
3. **指标标签化**: 湿度/风速/AQI 用半透明圆角标签展示，而非纯文字
4. **预报芯片化**: 小时/每日预报用圆角半透明芯片卡片
5. **图标间距**: 天气图标与文字之间增加 8-12px 间距

---

## 文件变更清单

| 文件 | 操作 | 说明 |
|------|------|------|
| `Views/Components/MaterialWeatherVisualTheme.cs` | 修改 | 扩展 ResolvePalette 支持 styleId 分派，新增4套风格配色 |
| `Views/Components/MaterialWeatherSceneControl.cs` | 修改 | 按 styleId 渲染不同背景风格（纯渐变/深色几何/清新波浪/暖色场景） |
| `Views/Components/WeatherWidgetBase.cs` | 修改 | 传递 styleId 到 SceneControl.Apply()，移除硬编码遮罩 |
| `Views/Components/WeatherWidget.axaml` | 修改 | 改进排版层次，移除硬编码遮罩 |
| `Views/Components/WeatherWidget.axaml.cs` | 修改 | 适配新排版 |
| `Views/Components/WeatherClockWidget.axaml` | 修改 | 改进排版，移除硬编码遮罩 |
| `Views/Components/WeatherClockWidget.axaml.cs` | 修改 | 适配新排版 |
| `Views/Components/ExtendedWeatherWidget.axaml` | 修改 | 改进排版，指标标签化，预报芯片化 |
| `Views/Components/ExtendedWeatherWidget.axaml.cs` | 修改 | 适配新排版+标签+芯片 |
| `Views/Components/HourlyWeatherWidget.axaml` | 修改 | 改进排版，预报芯片化 |
| `Views/Components/HourlyWeatherWidget.axaml.cs` | 修改 | 适配新排版+芯片 |
| `Views/Components/MultiDayWeatherWidget.axaml` | 修改 | 改进排版 |
| `Views/Components/MultiDayWeatherWidget.axaml.cs` | 修改 | 适配新排版 |
| `mocks/weather-widget-mock.html` | 新建 | HTML Mock 预览（4种风格×2种天气×2种主题） |

---

## Task 分解

### Task 1: 创建 HTML Mock 预览

**Files:**
- Create: `mocks/weather-widget-mock.html`

- [ ] **Step 1: 创建 HTML Mock 文件**

创建完整的 HTML Mock，包含：
- 4 种风格（Google / Geometric / Breezy / Lemon）× 2 种天气（晴/雨）× 2 种主题（亮/暗）
- 每种风格展示基础天气组件（温度+状况+图标+位置）
- 改进后的排版：超大温度、层次分明、指标标签化
- 亮色/暗色主题切换按钮

- [ ] **Step 2: 在浏览器中打开 Mock 验证效果**

Run: `start mocks/weather-widget-mock.html`

---

### Task 2: 扩展 MaterialWeatherVisualTheme 支持多风格配色

**Files:**
- Modify: `LanMountainDesktop/Views/Components/MaterialWeatherVisualTheme.cs`

- [ ] **Step 1: 修改 ResolvePalette 方法签名**

将 `ResolvePalette(MaterialWeatherCondition condition, bool isNight)` 改为 `ResolvePalette(string? styleId, MaterialWeatherCondition condition, bool isNight)`，内部按 styleId 分派到不同配色方案。

- [ ] **Step 2: 新增 Google Weather 配色表**

为 GoogleWeatherV4 风格定义所有天气条件×昼夜的配色（参考上面配色方案设计章节）。

- [ ] **Step 3: 新增 Geometric 配色表**

为 Geometric 风格定义深色系配色。

- [ ] **Step 4: 新增 Breezy 配色表**

为 Breezy 风格定义清新渐变配色。

- [ ] **Step 5: 新增 Lemon 配色表**

为 LemonFlutter 风格定义暖色系配色。

- [ ] **Step 6: 更新所有调用点**

将所有 `ResolvePalette(condition, isNight)` 调用改为 `ResolvePalette(styleId, condition, isNight)`。

---

### Task 3: 重构 MaterialWeatherSceneControl 支持多风格背景

**Files:**
- Modify: `LanMountainDesktop/Views/Components/MaterialWeatherSceneControl.cs`

- [ ] **Step 1: 扩展 Apply 方法签名**

将 `Apply(MaterialWeatherCondition condition, MaterialWeatherPalette palette, bool isLive)` 改为 `Apply(string? styleId, MaterialWeatherCondition condition, MaterialWeatherPalette palette, bool isLive)`，存储 styleId。

- [ ] **Step 2: 实现 Google Weather 风格渲染**

纯渐变背景，无几何装饰。背景使用 palette 的 BackgroundTop→BackgroundBottom 渐变。仅保留天气特效（雨滴/雪花/雾线）。

- [ ] **Step 3: 实现 Geometric 风格渲染**

深色渐变 + 大面积半透明几何圆形叠加。在基础渐变上叠加 2-3 个大椭圆（使用 palette 的 PrimaryShape/SecondaryShape/AccentShape），营造深度感。保留天气特效。

- [ ] **Step 4: 实现 Breezy 风格渲染**

清新渐变 + 柔和波浪线条。在基础渐变上绘制 2-3 条正弦波浪线（使用 palette 的 SurfaceTint），营造微风感。保留天气特效。

- [ ] **Step 5: 实现 Lemon 风格渲染**

暖色渐变 + 天气场景装饰。晴天绘制太阳光芒（放射线），多云绘制云朵轮廓，雨天绘制雨丝。保留天气特效。

- [ ] **Step 6: 更新所有调用点**

将所有 `SceneControl.Apply(condition, palette, isLive)` 改为 `SceneControl.Apply(styleId, condition, palette, isLive)`。

---

### Task 4: 更新 WeatherWidgetBase 传递 styleId

**Files:**
- Modify: `LanMountainDesktop/Views/Components/WeatherWidgetBase.cs`

- [ ] **Step 1: 修改 ApplyCurrentScene 方法**

在 `ApplyCurrentScene()` 中将 `CurrentVisualStyleId` 传递给 `SceneControl.Apply()`。

- [ ] **Step 2: 修改 ApplySnapshot 中的 ResolvePalette 调用**

将 `MaterialWeatherVisualTheme.ResolvePalette(CurrentCondition, isNight)` 改为 `MaterialWeatherVisualTheme.ResolvePalette(CurrentVisualStyleId, CurrentCondition, isNight)`。

---

### Task 5: 改进各天气 Widget 的 XAML 排版

**Files:**
- Modify: `WeatherWidget.axaml` — 移除硬编码遮罩 `<Border Background="#30FFFFFF" />`，改用 palette 驱动的半透明遮罩
- Modify: `WeatherClockWidget.axaml` — 同上
- Modify: `ExtendedWeatherWidget.axaml` — 同上 + 指标区域改用标签样式
- Modify: `HourlyWeatherWidget.axaml` — 同上 + 预报区域改用芯片样式
- Modify: `MultiDayWeatherWidget.axaml` — 同上

- [ ] **Step 1: 移除所有硬编码遮罩**

将 `<Border Background="#30FFFFFF" />` / `#42FFFFFF` / `#34FFFFFF` / `#38FFFFFF` / `#3CFFFFFF` 替换为 `<Border x:Name="OverlayBorder" />`，在 code-behind 中根据 palette 设置遮罩颜色。

- [ ] **Step 2: 改进 WeatherWidget 排版**

增大温度字号（58→64），增加图标与文字间距，调整位置文字透明度。

- [ ] **Step 3: 改进 WeatherClockWidget 排版**

增大时钟字号，增加天气信息与时间间距。

- [ ] **Step 4: 改进 ExtendedWeatherWidget 排版**

指标用半透明圆角标签，小时/每日预报用圆角芯片卡片。

- [ ] **Step 5: 改进 HourlyWeatherWidget 排版**

预报区域用圆角芯片卡片样式。

- [ ] **Step 6: 改进 MultiDayWeatherWidget 排版**

每日预报行增加分隔线和更好的间距。

---

### Task 6: 更新各天气 Widget 的 code-behind

**Files:**
- Modify: 所有天气 Widget 的 `.axaml.cs` 文件

- [ ] **Step 1: 更新 WeatherWidget.axaml.cs**

- 设置 OverlayBorder 背景
- 增大温度字号
- 适配新排版参数

- [ ] **Step 2: 更新 WeatherClockWidget.axaml.cs**

- 设置 OverlayBorder 背景
- 适配新排版

- [ ] **Step 3: 更新 ExtendedWeatherWidget.axaml.cs**

- 设置 OverlayBorder 背景
- 指标标签化（CreateMetric 改为带圆角背景的标签）
- 预报芯片化

- [ ] **Step 4: 更新 HourlyWeatherWidget.axaml.cs**

- 设置 OverlayBorder 背景
- 预报芯片化（CreateChip 改为带圆角背景的芯片）

- [ ] **Step 5: 更新 MultiDayWeatherWidget.axaml.cs**

- 设置 OverlayBorder 背景
- 适配新排版

---

### Task 7: 验证与测试

- [ ] **Step 1: 运行项目查看效果**

Run: `dotnet run --project LanMountainDesktop/LanMountainDesktop.csproj`

- [ ] **Step 2: 运行相关测试**

Run: `dotnet test LanMountainDesktop.slnx -c Debug`

- [ ] **Step 3: 检查圆角规范合规**

确认所有组件 RootBorder 使用 `DesignCornerRadiusComponent`，新增的标签/芯片使用 `DesignCornerRadiusSm`/`DesignCornerRadiusMd`。

---

## 假设与决策

1. **4 套独立风格**: 每种图标风格对应独立的背景配色和装饰风格，切换图标风格时背景也跟着变
2. **配色表驱动**: 所有颜色定义在 `MaterialWeatherVisualTheme` 中，不硬编码到 SceneControl
3. **保留天气特效**: 雨滴/雪花/雾线/闪电等天气特效在所有风格中保留，但颜色跟随 palette
4. **遮罩动态化**: 半透明遮罩颜色从 palette 中派生，而非硬编码 `#30FFFFFF`
5. **排版渐进改进**: 不做大规模 XAML 重构，而是在现有结构上优化字号/间距/透明度
6. **数据层不变**: WeatherSnapshot、WeatherIconAssetResolver、WeatherWidgetBase 的数据逻辑不变
7. **接口兼容**: IDesktopComponentWidget 等接口实现不变

## 验证步骤

1. HTML Mock 在浏览器中展示 4 种风格效果满意
2. Avalonia 项目编译通过
3. 运行项目，切换图标风格时背景配色和装饰风格跟着变化
4. 亮色/暗色主题切换正常
5. 5 个天气组件排版层次分明
6. 指标标签化和预报芯片化正常显示
7. 测试通过

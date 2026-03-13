# 设置页面 Fluent 设计改造规格说明书

## Why

当前 LanMountainDesktop 设置页面存在以下问题：
1. 右侧详细设置区域被额外边框包裹，未能实现 Fluent Avalonia 控件的完整填充效果
2. 设置项未采用 Fluent 卡片设计风格，仍使用传统 Border + StackPanel 布局
3. 与 ClassIsland 项目的视觉风格差异较大

## What Changes

- 移除页面内容区域的额外 Border 包裹，直接使用 ScrollViewer + StackPanel
- 参考 ClassIsland 项目，引入 SettingsExpander 控件替代传统布局
- 统一设置项的间距、圆角、字体等视觉规范
- 修改窗口布局，移除内容区域的 glass-panel 样式

## Impact

### Affected specs
- 设置页面 UI 布局规范
- Fluent 设计风格适配

### Affected code
- `Views/SettingsPages/*.axaml` - 所有设置页面
- `Views/SettingsWindow.axaml` - 设置窗口布局
- `Styles/GlassModule.axaml` - 样式资源

---

## ADDED Requirements

### Requirement: 设置页面 Fluent 卡片设计

系统 SHALL 提供类似 ClassIsland 的 SettingsExpander 卡片式设置项。

#### Scenario: 设置页面布局
- **WHEN** 用户打开任意设置页面
- **THEN** 页面使用 ScrollViewer 直接包裹内容，无额外 Border 包裹
- **AND THEN** 设置项使用 SettingsExpander 或 Fluent 卡片样式

### Requirement: 移除内容区域额外边框

系统 SHALL 移除右侧内容区域的 glass-panel 边框包裹。

#### Scenario: 内容区域无额外边框
- **WHEN** 用户查看设置页面内容
- **THEN** 内容直接显示在透明背景上，无额外边框包裹

### Requirement: 设置项视觉规范

系统 SHALL 统一设置项的视觉样式。

#### Scenario: 设置项样式
- **WHEN** 开发者创建新的设置项
- **THEN** 使用统一的间距（Spacing）、圆角、字体大小
- **AND THEN** 参考 ClassIsland 的 SettingsExpander 样式

---

## MODIFIED Requirements

### Requirement: 设置页面布局结构

**当前**: Border → ScrollViewer → Border → StackPanel → 内容

**修改后**: ScrollViewer → StackPanel → 设置项（无额外 Border）

---

## REMOVED Requirements

### Requirement: 传统 Border 包裹布局

**Reason**: 实现 Fluent 设计风格，移除视觉噪音

**Migration**: 将现有 Border 包裹改为直接内容布局

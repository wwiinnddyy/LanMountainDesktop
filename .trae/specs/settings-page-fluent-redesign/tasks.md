# Tasks - 设置页面 Fluent 设计改造

## Phase 1: 分析与准备

- [ ] Task 1.1: 分析 ClassIsland SettingsExpander 控件实现
  - [ ] 查看 ClassIsland.Core 中的 SettingsExpander 定义
  - [ ] 分析样式模板和视觉效果
  - [ ] 确定是否需要自定义控件或使用现有替代方案

- [ ] Task 1.2: 分析当前设置页面布局问题
  - [ ] 定位右侧内容区域的 Border 包裹代码
  - [ ] 分析 glass-panel 样式对布局的影响

## Phase 2: 窗口布局调整

- [ ] Task 2.1: 修改 SettingsWindow.axaml 内容区域布局
  - [ ] 移除 Frame 外部的 glass-panel Border
  - [ ] 直接使用透明背景
  - [ ] 验证窗口整体视觉效果

## Phase 3: 设置页面改造

- [ ] Task 3.1: 改造 AppearanceSettingsPage 页面
  - [ ] 移除外部的 glass-panel Border
  - [ ] 调整内容布局为直接填充
  - [ ] 验证视觉效果

- [ ] Task 3.2: 改造 GeneralSettingsPage 页面
  - [ ] 移除外部的 glass-panel Border
  - [ ] 调整内容布局

- [ ] Task 3.3: 改造其他设置页面
  - [ ] ComponentsSettingsPage
  - [ ] PluginsSettingsPage
  - [ ] AboutSettingsPage

## Phase 4: 视觉规范统一

- [ ] Task 4.1: 统一设置项间距和圆角
  - [ ] 定义统一的 Spacing 值
  - [ ] 统一圆角大小

- [ ] Task 4.2: 优化页面标题区域样式
  - [ ] 调整 Page Header 字体大小
  - [ ] 优化 Description 样式

## Task Dependencies
- Task 1.2 依赖 Task 1.1
- Task 2.1 依赖 Task 1.2
- Task 3.x 依赖 Task 2.1
- Task 4.x 依赖 Task 3.x

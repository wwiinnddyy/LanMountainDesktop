# 融合桌面组件库窗口重设计规格

## Why
当前融合桌面组件库窗口（FusedDesktopComponentLibraryWindow）的UI设计较为基础，与Windows 11小组件编辑面板相比，缺乏现代化的交互体验和视觉层次。用户需要一个更直观、更美观的界面来浏览和添加组件到系统桌面（负一屏）。

参考Windows 11小组件编辑面板的设计特点：
- 左侧分类列表，右侧选中组件的详细预览
- 大型组件预览区域，让用户清楚看到组件效果
- 底部明显的"添加"操作按钮
- 简洁的关闭按钮（X）在右上角
- 深色主题配合毛玻璃效果

## What Changes
- **重新设计窗口布局**：从左右分栏（分类列表+组件网格）改为左侧面板+右侧预览区的布局
- **添加组件详情预览区**：选中组件后右侧显示大尺寸预览和组件信息
- **优化关闭按钮**：使用标准的X图标按钮，不使用圆形样式
- **添加底部操作栏**：包含"添加到桌面"主操作按钮和"查找更多组件"链接
- **复用阑山桌面组件库分类**：使用相同的分类ID、图标和本地化文本
- **移除搜索功能**：参考Windows 11设计，暂不提供搜索

## Impact
- 受影响文件：
  - `LanMountainDesktop/Views/FusedDesktopComponentLibraryWindow.axaml`
  - `LanMountainDesktop/Views/FusedDesktopComponentLibraryWindow.axaml.cs`
  - `LanMountainDesktop/Views/FusedDesktopComponentLibraryControl.axaml`
  - `LanMountainDesktop/Views/FusedDesktopComponentLibraryControl.axaml.cs`
  - `LanMountainDesktop/ViewModels/ComponentLibraryWindowViewModel.cs`（可能需要添加新属性）

## ADDED Requirements

### Requirement: 窗口布局重设计
系统应提供一个类似于Windows 11小组件编辑面板的组件库窗口。

#### Scenario: 窗口整体结构
- **GIVEN** 用户从托盘菜单打开融合桌面组件库
- **WHEN** 窗口显示时
- **THEN** 窗口应呈现：
  - 顶部标题栏：左侧显示"添加小组件"标题，右侧有关闭按钮（X）
  - 左侧面板：分类列表（复用阑山桌面组件库的分类和图标）
  - 右侧主区域：选中组件的大尺寸预览 + 组件信息 + 添加按钮
  - 底部："查找更多组件"链接

#### Scenario: 分类列表交互
- **GIVEN** 左侧显示组件分类列表
- **WHEN** 用户点击某个分类
- **THEN** 右侧应显示该分类下的第一个组件的预览
- **AND** 分类项应有选中状态视觉反馈
- **AND** 分类图标和名称应与阑山桌面组件库保持一致

#### Scenario: 组件预览区
- **GIVEN** 用户选中一个组件
- **WHEN** 预览区显示时
- **THEN** 应显示：
  - 组件标题（大字号）
  - 大尺寸组件预览图（接近实际尺寸）
  - 组件描述/功能说明
  - 底部"添加到桌面"按钮

#### Scenario: 添加组件操作
- **GIVEN** 用户查看组件预览
- **WHEN** 用户点击"添加到桌面"按钮
- **THEN** 组件应被添加到系统桌面（负一屏）中央
- **AND** 窗口应关闭

#### Scenario: 关闭按钮样式
- **GIVEN** 窗口标题栏有关闭按钮
- **THEN** 关闭按钮应使用标准的X图标
- **AND** 不使用圆形背景或特殊样式
- **AND** 使用 `DesignCornerRadiusSm` 动态资源

#### Scenario: 查找更多组件链接
- **GIVEN** 窗口底部显示"查找更多组件"链接
- **WHEN** 用户点击该链接
- **THEN** 应打开设置窗口的插件目录页面（后续将改为插件市场）

## MODIFIED Requirements

### Requirement: 组件列表展示
原实现使用网格展示所有组件，新实现改为：
- 左侧列表仅显示分类（复用阑山桌面组件库的分类ID和图标映射）
- 右侧预览区一次只显示一个组件的详细信息
- ~~移除搜索功能~~（根据Windows 11设计，暂不提供搜索）

### Requirement: 关闭按钮圆角规范
原实现关闭按钮使用硬编码 `CornerRadius="18"`，应改为使用动态资源 `DesignCornerRadiusSm`。

### Requirement: 分类图标复用
分类图标映射应与阑山桌面组件库保持一致：
- Clock -> Symbol.Clock
- Date -> Symbol.CalendarDate
- Weather -> Symbol.WeatherSunny
- Board -> Symbol.Edit
- Media -> Symbol.Play
- Info -> Symbol.Info
- Calculator -> Symbol.Calculator
- Study -> Symbol.Hourglass
- 其他 -> Symbol.Apps

## REMOVED Requirements
- ~~搜索功能~~：根据Windows 11小组件面板设计，暂不提供搜索功能

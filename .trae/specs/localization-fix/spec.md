# 本地化修复 Spec

## Why

- 项目在中文设置下，多处 UI 仍显示英文。
- 主要问题集中在：
  1. `MainWindow.axaml` 中任务栏头像弹窗、电源菜单、组件库等文本硬编码为英文，且未被 `ApplyLocalization()` 覆盖。
  2. `LanMountainDesktop.Launcher` 的所有视图完全没有接入本地化系统。
  3. 部分组件（BrowserWidget、WhiteboardWidget、HolidayCalendarWidget 等）存在未覆盖的硬编码英文。
  4. 少量设置页面 Tooltip 硬编码英文。

## What Changes

### 1. MainWindow.axaml 硬编码修复
将以下硬编码文本改为由 `ApplyLocalization()` 通过 `L()` 动态设置：
- 任务栏头像弹窗：`User` → `power.user` / `Settings` → `settings.title` / `Edit Desktop` → `button.component_library` / `Power` → `power.title`
- 电源菜单：`Back` → `common.back` / `Power` → `power.title` / `Shutdown` → `power.shutdown` / `Restart` → `power.restart` / `Log Out` → `power.logout` / `Sleep` → `power.sleep` / `Lock Screen` → `power.lock_screen`
- 组件库：`Widgets` → `component_library.title` / `Back` → `common.back` / `No components.` → `component_library.empty`
- 悬浮芯片：`Widgets` → `component_library.title`

### 2. Launcher 视图本地化
为 `LanMountainDesktop.Launcher/Views/` 下的窗口引入独立本地化机制（复用 `LocalizationService` 或内嵌资源字典）：
- `SplashWindow.axaml`：`LanMountain Desktop`、`Initializing...`
- `DataLocationPromptWindow.axaml`：全部文本
- `ErrorWindow.axaml`：全部文本
- `LoadingDetailsWindow.axaml`：全部文本
- `UpdateWindow.axaml`：`Update`

### 3. 组件硬编码修复
- `BrowserWidget.axaml`：`Browser runtime unavailable.` → 新增键 `browser.widget.unavailable`
- `WhiteboardWidget.axaml`：`Pen` / `Eraser` / `Clear` / `Export SVG` → 新增键 `whiteboard.tool.pen` 等
- `HolidayCalendarWidget.axaml`：`Holiday countdown` / `Days` → 新增键 `holiday.widget.title` / `holiday.widget.days`
- `BilibiliHotSearchWidget.axaml`：`Trending Topic` → 新增键 `bilihot.widget.trending_topic`
- `WallpaperSettingsPage.axaml`：`Custom color` Tooltip → 复用 `settings.wallpaper.custom_color_tooltip`

### 4. 本地化资源文件补充
在 `zh-CN.json` 和 `en-US.json` 中补充上述新增键值。

## Impact

- Affected code:
  - `LanMountainDesktop/Views/MainWindow.axaml`
  - `LanMountainDesktop/Views/MainWindow.SettingsHardCut.Stubs.cs`
  - `LanMountainDesktop.Launcher/Views/*.axaml`（多个文件）
  - `LanMountainDesktop/Views/Components/BrowserWidget.axaml`
  - `LanMountainDesktop/Views/Components/WhiteboardWidget.axaml`
  - `LanMountainDesktop/Views/Components/HolidayCalendarWidget.axaml`
  - `LanMountainDesktop/Views/Components/BilibiliHotSearchWidget.axaml`
  - `LanMountainDesktop/Views/SettingsPages/WallpaperSettingsPage.axaml`
  - `LanMountainDesktop/Localization/zh-CN.json`
  - `LanMountainDesktop/Localization/en-US.json`
- Affected behavior:
  - 中文设置下上述位置将正确显示中文。
  - Launcher 各窗口将支持中英文切换。

---

## Requirements

### Requirement: MainWindow 任务栏弹窗与电源菜单本地化
系统 SHALL 在 `ApplyLocalization()` 中覆盖任务栏头像弹窗和电源菜单的所有文本。

#### Scenario: 中文设置下打开任务栏弹窗
- **WHEN** 语言设置为中文
- **THEN** 弹窗中显示"设置"、"桌面编辑"、"电源"等中文文本
- **AND THEN** 电源菜单中显示"返回"、"关机"、"重启"、"注销"、"睡眠"、"锁定屏幕"等中文文本

### Requirement: Launcher 窗口本地化
系统 SHALL 让 Launcher 的所有窗口文本通过本地化服务获取。

#### Scenario: 中文设置下启动应用
- **WHEN** 语言设置为中文
- **THEN** SplashWindow 显示中文启动文本
- **AND THEN** 数据位置选择、错误页、加载详情页等显示中文

### Requirement: 组件与设置页硬编码修复
系统 SHALL 移除或覆盖所有组件和设置页中的英文硬编码文本。

#### Scenario: 中文设置下查看各组件
- **WHEN** 语言设置为中文
- **THEN** BrowserWidget 显示"浏览器运行时不可用"
- **AND THEN** WhiteboardWidget 工具提示显示"笔"、"橡皮擦"、"清空"、"导出 SVG"
- **AND THEN** HolidayCalendarWidget 显示"节假日倒计时"、"天"
- **AND THEN** BilibiliHotSearchWidget 显示"热门话题"
- **AND THEN** 壁纸设置页自定义颜色 Tooltip 显示"自定义颜色"

# 本地化修复 Tasks

## Task 1: MainWindow.axaml 硬编码文本移除与代码覆盖
- [ ] 1.1 在 `MainWindow.axaml` 中，将任务栏头像弹窗的 `User`、`Settings`、`Edit Desktop`、`Power` 的 `Text` 属性改为空或绑定（保留 x:Name）
- [ ] 1.2 在 `MainWindow.axaml` 中，将电源菜单的 `Back`、`Power`、`Shutdown`、`Restart`、`Log Out`、`Sleep`、`Lock Screen` 的 `Text` 属性改为空或绑定
- [ ] 1.3 在 `MainWindow.axaml` 中，将组件库的 `Widgets`、`Back`、`No components.` 的 `Text` 属性改为空或绑定
- [ ] 1.4 在 `MainWindow.axaml` 中，将悬浮芯片的 `Widgets` 的 `Text` 属性改为空或绑定
- [ ] 1.5 在 `MainWindow.SettingsHardCut.Stubs.cs` 的 `ApplyLocalization()` 中补充上述所有控件的 `L()` 赋值

## Task 2: Launcher 视图本地化
- [ ] 2.1 在 `LanMountainDesktop.Launcher` 中引入 `LocalizationService`（或共享主应用服务）
- [ ] 2.2 为 Launcher 创建独立的 `Localization/` 目录和 `zh-CN.json` / `en-US.json`
- [ ] 2.3 修改 `SplashWindow.axaml`：将 `LanMountain Desktop`、`Initializing...` 改为动态绑定
- [ ] 2.4 修改 `DataLocationPromptWindow.axaml`：将所有文本改为动态绑定
- [ ] 2.5 修改 `ErrorWindow.axaml`：将所有文本改为动态绑定
- [ ] 2.6 修改 `LoadingDetailsWindow.axaml`：将所有文本改为动态绑定
- [ ] 2.7 修改 `UpdateWindow.axaml`：将 `Update` 改为动态绑定
- [ ] 2.8 在 Launcher 启动流程中初始化语言设置

## Task 3: 组件硬编码修复
- [ ] 3.1 `BrowserWidget.axaml`：将 `Browser runtime unavailable.` 改为绑定，并在代码后置中通过 `L()` 设置
- [ ] 3.2 `WhiteboardWidget.axaml`：将 `Pen`、`Eraser`、`Clear`、`Export SVG` Tooltip 改为绑定，并在代码后置中通过 `L()` 设置
- [ ] 3.3 `HolidayCalendarWidget.axaml`：将 `Holiday countdown`、`Days` 改为绑定，并在代码后置中通过 `L()` 设置
- [ ] 3.4 `BilibiliHotSearchWidget.axaml`：将 `Trending Topic` 改为绑定，并在代码后置中通过 `L()` 设置
- [ ] 3.5 `WallpaperSettingsPage.axaml`：将 `Custom color` Tooltip 改为绑定到 `settings.wallpaper.custom_color_tooltip`

## Task 4: 本地化资源文件补充
- [ ] 4.1 在 `zh-CN.json` 中补充以下键值：
  - `browser.widget.unavailable`
  - `whiteboard.tool.pen`、`whiteboard.tool.eraser`、`whiteboard.tool.clear`、`whiteboard.tool.export_svg`
  - `holiday.widget.title`、`holiday.widget.days`
  - `bilihot.widget.trending_topic`
  - `power.user`（或复用现有键）
- [ ] 4.2 在 `en-US.json` 中补充上述键值的英文版本
- [ ] 4.3 为 Launcher 创建独立的本地化 JSON 文件并填充中英文

## Task 5: 验证
- [ ] 5.1 执行 `dotnet build LanMountainDesktop.slnx -c Debug` 确保编译通过
- [ ] 5.2 检查是否有遗漏的硬编码英文（通过正则搜索）

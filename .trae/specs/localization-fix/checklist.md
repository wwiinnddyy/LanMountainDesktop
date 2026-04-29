# 本地化修复 Checklist

## MainWindow 修复
- [ ] `TaskbarProfileDisplayNameTextBlock.Text` 在中文下显示"用户"（或保持动态）
- [ ] `TaskbarProfileSettingsActionTextBlock.Text` 在中文下显示"设置"
- [ ] `TaskbarProfileDesktopEditActionTextBlock.Text` 在中文下显示"桌面编辑"
- [ ] `TaskbarProfilePowerActionTextBlock.Text` 在中文下显示"电源"
- [ ] `TaskbarPowerBackTextBlock.Text` 在中文下显示"返回"
- [ ] `TaskbarPowerTitleTextBlock.Text` 在中文下显示"电源"
- [ ] `PowerShutdownTextBlock.Text` 在中文下显示"关机"
- [ ] `PowerRestartTextBlock.Text` 在中文下显示"重启"
- [ ] `PowerLogoutTextBlock.Text` 在中文下显示"注销"
- [ ] `PowerSleepTextBlock.Text` 在中文下显示"睡眠"
- [ ] `PowerLockTextBlock.Text` 在中文下显示"锁定屏幕"
- [ ] `ComponentLibraryTitleTextBlock.Text` 在中文下显示"桌面编辑"
- [ ] `ComponentLibraryEmptyTextBlock.Text` 在中文下显示"左右滑动选择类别，点击进入，然后拖动组件到桌面放置。"
- [ ] `ComponentLibraryBackTextBlock.Text` 在中文下显示"返回"
- [ ] `ComponentLibraryCollapsedChipTextBlock.Text` 在中文下显示"桌面编辑"

## Launcher 修复
- [ ] `SplashWindow` 在中文下显示中文启动文本
- [ ] `DataLocationPromptWindow` 在中文下全部显示中文
- [ ] `ErrorWindow` 在中文下全部显示中文
- [ ] `LoadingDetailsWindow` 在中文下全部显示中文
- [ ] `UpdateWindow` 在中文下显示中文标题

## 组件修复
- [ ] `BrowserWidget` 在中文下显示"浏览器运行时不可用"
- [ ] `WhiteboardWidget` 工具提示在中文下显示"笔"、"橡皮擦"、"清空"、"导出 SVG"
- [ ] `HolidayCalendarWidget` 在中文下显示"节假日倒计时"、"天"
- [ ] `BilibiliHotSearchWidget` 在中文下显示"热门话题"
- [ ] `WallpaperSettingsPage` 自定义颜色 Tooltip 在中文下显示"自定义颜色"

## 资源文件
- [ ] `zh-CN.json` 包含所有新增键值
- [ ] `en-US.json` 包含所有新增键值
- [ ] Launcher 本地化文件包含所有新增键值

## 构建与质量
- [ ] `dotnet build LanMountainDesktop.slnx -c Debug` 编译通过，无错误
- [ ] 无新增警告
- [ ] 无遗漏的硬编码英文（通过 `grep -r 'Text="[a-zA-Z]'` 等检查）

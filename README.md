# LanMountainDesktop

> 你的桌面，不止一面。

`LanMountainDesktop` 是一个基于 Avalonia 的桌面壳层项目，目标不是“做一个启动器”，而是把桌面变成可编排的信息与交互空间。

> ⚠️ **注意**：该项目使用 Vibe Coding，介意勿用。
## 项目定位
- 以网格化布局组织桌面组件，支持多页桌面与组件自由摆放。
- 提供顶部状态栏 + 底部任务栏的桌面框架，强调信息密度与可读性平衡。
- 通过主题色、日夜模式、玻璃视觉与动画系统，形成统一的视觉语言。
- 通过组件注册机制与 JSON 扩展入口，让桌面能力可持续扩展。

## 核心能力
- 桌面组件系统：天气、时钟、计时器、课程表、日历、白板、音乐控制、学习环境等组件可组合使用。
- 壁纸系统：支持图片与视频壁纸，并可在设置中实时预览。
- 主题系统：支持日夜模式、主题色与调色联动（Monet 风格色板）。
- 个性化设置：网格密度、状态栏间距、任务栏布局、语言与时区等可持久化配置。
- 本地化：内置 `zh-CN` 与 `en-US` 资源。

## 工程结构
- `LanMountainDesktop/`：桌面端主程序（Avalonia）。
- `LanMountainDesktop.RecommendationBackend/`：推荐内容后端服务（ASP.NET Core Minimal API）。
- `docs/`：视觉与圆角等规范文档。
- `LanMountainDesktop/ComponentSystem/`：组件定义、注册、放置规则与扩展入口。

## 技术栈
- .NET 10（`net10.0`）
- Avalonia 11
- FluentAvalonia + FluentIcons.Avalonia
- LibVLCSharp（用于视频相关能力）
- WebView.Avalonia（嵌入式网页组件能力）

## 扩展机制（摘要）
- 组件系统通过 `ComponentRegistry` 合并内置组件与扩展组件。
- 运行时会扫描 `Extensions/Components/*.json`（相对应用输出目录）加载第三方组件清单。
- 扩展契约与字段说明见组件系统文档：`LanMountainDesktop/ComponentSystem/README.md`。

## 当前状态
- 项目包含桌面端与推荐后端两个子项目，并在同一 solution 中维护。
- 配置默认写入本地：`%LOCALAPPDATA%\LanMountainDesktop\settings.json`。
- 启动台与桌面布局现已拆分到独立文件：`%LOCALAPPDATA%\LanMountainDesktop\launcher-settings.json`、`%LOCALAPPDATA%\LanMountainDesktop\desktop-layout-settings.json`。
- 当前体验以 Windows 为主要目标平台。

## 运行说明
运行与环境准备已拆分到独立文档：[`run.md`](./run.md)

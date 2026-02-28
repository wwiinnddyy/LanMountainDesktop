# LanMontainDesktop

## 项目简介 / Project Overview
`LanMontainDesktop` 是一个基于 Avalonia 的桌面壳层应用原型，聚焦于网格化桌面布局、毛玻璃视觉、主题色系统与可扩展组件体系。  
`LanMontainDesktop` is an Avalonia-based desktop shell prototype focused on grid layout, glass visuals, theme system, and extensible components.

## 主要功能 / Key Features
- 网格化桌面：顶部状态栏 + 底部任务栏（Dock 风格容器）。  
  Grid-based desktop with top status bar and bottom taskbar (dock-like container).
- 设置中心：壁纸、网格、颜色、状态栏、地区（语言）选项。  
  Settings center with wallpaper, grid, color, status bar, and region (language) tabs.
- 壁纸系统：支持图片与视频壁纸，并提供设置页预览。  
  Wallpaper system supporting image/video wallpapers with in-settings preview.
- 主题系统：日夜模式、主题色、Monet 调色联动。  
  Theme system with day/night mode, accent color, and Monet palette integration.
- 组件系统基础：内置组件注册 + 第三方扩展入口（JSON manifest）。  
  Component system foundation with built-in registry and third-party JSON extension entry.

## 技术栈 / Tech Stack
- .NET 10 (`net10.0`)
- Avalonia 11
- FluentAvalonia + FluentIcons.Avalonia
- LibVLCSharp + VideoLAN.LibVLC.Windows（视频壁纸）

## 环境要求 / Prerequisites
- .NET SDK `10.0`
- Windows（当前项目引用 `VideoLAN.LibVLC.Windows`，视频能力以 Windows 为主）  
  Windows is the primary platform for current video capability due to `VideoLAN.LibVLC.Windows`.

## 快速启动 / Quick Start
```bash
dotnet restore
dotnet build LanMontainDesktop/LanMontainDesktop.csproj
dotnet run --project LanMontainDesktop/LanMontainDesktop.csproj
```

## 配置与持久化 / Configuration & Persistence
应用设置通过 `AppSettingsSnapshot` 持久化到本地：  
App settings are persisted from `AppSettingsSnapshot` to local storage:

- 路径 / Path: `%LOCALAPPDATA%\LanMontainDesktop\settings.json`

核心字段（简表）/ Key fields (summary):
- `GridShortSideCells`: 网格短边格子数 / short-side grid cells
- `IsNightMode`: 日夜模式 / day-night mode
- `ThemeColor`: 主题色 / accent color
- `WallpaperPath` + `WallpaperPlacement`: 壁纸路径与显示模式 / wallpaper path and placement
- `SettingsTabIndex`: 设置页当前选项卡 / active settings tab index
- `LanguageCode`: 语言代码（`zh-CN` / `en-US`）
- `TopStatusComponentIds`: 顶部状态栏组件 ID 列表 / status bar component IDs
- `PinnedTaskbarActions`: 任务栏固定动作 / pinned taskbar actions

## 组件扩展入口 / Component Extension Entry
- 运行时会扫描：`Extensions/Components/*.json`（相对应用输出目录）  
  Runtime scan target: `Extensions/Components/*.json` (relative to app output).
- 扩展加载器：`JsonComponentExtensionProvider`
- 详细契约与 schema 见：`LanMontainDesktop/ComponentSystem/README.md`

## 国际化 / Localization
- 语言资源文件：  
  Localization files:
  - `LanMontainDesktop/Localization/zh-CN.json`
  - `LanMontainDesktop/Localization/en-US.json`
- 当前支持：简体中文、English

## 已知限制（快速版）/ Known Limitations
- 视频壁纸能力当前以 Windows 运行环境为主。  
  Video wallpaper support is currently Windows-first.
- `docs/VISUAL_SPEC.md` 存在历史编码问题，本次未纳入修复范围。  
  `docs/VISUAL_SPEC.md` has historical encoding issues and is not updated in this round.

## 许可证与贡献（占位）/ License & Contributing (Placeholder)
- License: TBD
- Contributing guide: TBD


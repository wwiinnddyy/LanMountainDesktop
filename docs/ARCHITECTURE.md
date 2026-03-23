# 架构文档 / Architecture

## 中文

### 仓库结构

| 路径 | 角色 |
| --- | --- |
| `LanMountainDesktop/` | 主桌面宿主应用，包含 UI、服务、组件系统、插件运行时接入 |
| `LanMountainDesktop.PluginSdk/` | 官方插件 SDK，定义插件可依赖的公开接口与打包行为 |
| `LanMountainDesktop.Shared.Contracts/` | 宿主与插件共享的稳定契约类型 |
| `LanMountainDesktop.Appearance/` | 主题、圆角、外观资源相关基础设施 |
| `LanMountainDesktop.Settings.Core/` | 设置域、持久化和设置基础抽象 |
| `LanMountainDesktop.DesktopHost/` | 桌面宿主流程与生命周期相关逻辑 |
| `LanMountainDesktop.DesktopComponents.Runtime/` | 组件运行时支撑能力 |
| `LanMountainDesktop.Host.Abstractions/` | 宿主侧抽象接口 |
| `LanMountainDesktop.PluginsInstallHelper/` | 插件安装辅助程序与发布输出配套 |
| `LanMountainDesktop.PluginTemplate/` | `dotnet new lmd-plugin` 官方模板 |
| `LanMountainDesktop.Tests/` | 宿主与 SDK 的测试项目 |

### 宿主启动主线

启动入口在 `LanMountainDesktop/Program.cs`：

1. 初始化日志、单实例锁和启动诊断
2. 初始化遥测身份、崩溃遥测与使用遥测
3. 构建 Avalonia `AppBuilder`
4. 进入 `LanMountainDesktop/App.axaml.cs`
5. 初始化主题、语言、设置窗口服务、天气定位刷新
6. 初始化桌面壳层、主窗口、托盘、插件运行时

### 运行时主数据流

- 设置流：`Settings.Core` 提供基础设置能力，宿主通过 facade 读取和监听设置变化
- 外观流：`Appearance` 提供主题和圆角资源，宿主在 `App.axaml.cs` 中应用到资源字典
- 组件流：`LanMountainDesktop/ComponentSystem/` 维护内置组件定义、注册和扩展接入
- 插件流：宿主侧 `plugins/` 负责 `.laapp` 的发现、安装、替换、激活与共享契约装配
- 设置页流：插件运行时可把自己的设置页注册进宿主设置窗口

### 关键目录落点

`LanMountainDesktop/` 内高频目录：

- `Views/`：窗口、页面、组件视图
- `ViewModels/`：视图模型
- `Services/`：业务服务、持久化、启动、遥测等
- `ComponentSystem/`：组件定义、注册、扩展加载
- `plugins/`：宿主侧插件运行时
- `Theme/` 与 `Styles/`：主题资源、样式、外观应用
- `DesktopEditing/`：桌面布局编辑相关逻辑
- `Localization/`：本地化资源

### 插件边界

- 插件 SDK 权威定义在 `LanMountainDesktop.PluginSdk/`
- 宿主与插件共享的稳定通信类型在 `LanMountainDesktop.Shared.Contracts/`
- 插件市场和开发者生态资料不在本仓库维护
- 本地 market 调试从兄弟仓库 `..\\LanAirApp` 读取数据

### 测试边界

`LanMountainDesktop.Tests/` 当前主要覆盖：

- 圆角与外观相关基线
- 组件放置与编辑数学
- 组件设置服务
- UI 异常防护
- 白板笔记持久化

涉及宿主行为、SDK 契约、布局计算或设置持久化的改动，应优先补对应测试。

## English

This repository is organized around a desktop host app plus a host-side plugin ecosystem. `LanMountainDesktop/` contains the application entry points, UI, services, component system, and plugin runtime integration. The surrounding projects provide the public SDK, shared contracts, appearance infrastructure, settings primitives, host abstractions, runtime support, and tests.

The runtime flow starts in `Program.cs`, proceeds into `App.axaml.cs`, initializes settings/theme/localization services, then boots the desktop shell, tray, windows, and plugin runtime. The most important behavior boundaries are component registration, plugin activation, appearance resources, and settings persistence.

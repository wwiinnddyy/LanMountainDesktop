# 阑山桌面（LanMountainDesktop）

## 中文

阑山桌面是一个基于 Avalonia 的桌面壳层项目。它不是单纯的启动器，而是一个可编排、可扩展、可长期演进的桌面信息空间。

### 核心目标

- 通过网格化布局管理桌面组件。
- 提供状态栏、任务栏和多页桌面的统一外壳。
- 通过主题、玻璃效果和动效塑造统一体验。
- 通过组件系统和插件系统持续扩展能力。

### 当前工程结构

- `LanMountainDesktop/`：桌面主程序。
- `LanMountainDesktop.RecommendationBackend/`：推荐内容后端。
- `LanMountainDesktop/ComponentSystem/`：组件定义与注册系统。
- `LanMountainDesktop/plugins/`：宿主侧插件加载、安装和设置集成。
- `docs/`：视觉与设计规范。
- `LanAirApp/`：插件开发资料镜像，权威版本以独立 `LanAirApp` 仓库为准。

### 生态关系

- 宿主程序只连接 `LanAirApp` 仓库中的官方市场索引。
- 官方市场索引返回插件列表以及各插件项目根目录链接。
- 插件项目根目录提供 `.laapp` 安装包和 `README.md`。

### 当前状态

- Windows 是当前主要目标平台。
- 已提供组件系统、插件系统、主题系统和设置系统。
- 中文为主语言，英文为附加扩展语言。
- 仓库主入口解决方案文件已切换为 `LanMountainDesktop.slnx`，SDK 版本由根目录 `global.json` 锁定。

### 运行说明

运行方法见 [run.md](./run.md)。

## English

LanMountainDesktop is an Avalonia-based desktop shell. It is designed as a composable and extensible desktop environment rather than a simple launcher.

### Main goals

- manage desktop widgets with a grid-based layout
- provide a unified shell with status bar, taskbar, and multi-page desktop support
- build a consistent experience through themes, glass effects, and motion
- extend capabilities through the component and plugin systems

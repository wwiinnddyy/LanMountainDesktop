# 宿主侧插件运行时

## 中文

本目录保存阑山桌面宿主程序中的插件运行时实现。

### 主要职责

- 发现已安装插件
- 安装和替换 `.laapp` 插件包
- 加载插件程序集
- 接入插件贡献的设置页和桌面组件
- 在宿主设置界面中展示插件与市场信息

### 市场安装优先级

1. 宿主先连接 `LanAirApp/airappmarket/index.json`。
2. 当条目同时提供 `releaseTag` 和 `releaseAssetName` 时，宿主优先按精确标签读取插件仓库的 GitHub Release 资产。
3. 如果 Release 不存在、资产缺失、GitHub API 失败，或当前是本地工作区测试但找不到远程资产，宿主会退回 `downloadUrl` 指向的仓库根目录 `.laapp`。
4. 插件介绍始终读取仓库根目录 `README.md`。
5. 安装完成后只做暂存，重启后生效，不在运行时热重载市场安装插件。

### 核心文件

- `PluginLoader.cs`
- `PluginLoadContext.cs`
- `PluginRuntimeService.cs`
- `PluginCatalogEntry.cs`
- `PluginSettingsPage.axaml`
- `PluginSettingsPage.Host.cs`
- `PluginMarketIndexService.cs`
- `PluginMarketInstallService.cs`

### 与 `LanAirApp` 的分工

- `LanAirApp` 负责插件开发文档、示例、市场索引和校验工具。
- 宿主目录负责运行时发现、安装、加载和界面接入。

## English

This directory contains the host-side plugin runtime for LanMountainDesktop.

### Responsibilities

- discover installed plugins
- install and replace `.laapp` packages
- load plugin assemblies
- integrate plugin settings pages and desktop components
- expose market and plugin management in the host UI

### Market install order

1. The host reads `LanAirApp/airappmarket/index.json`.
2. If an entry declares both `releaseTag` and `releaseAssetName`, the host first resolves the exact GitHub Release asset.
3. If Release resolution fails, the host falls back to the repository root `.laapp` from `downloadUrl`.
4. Plugin details always come from the repository root `README.md`.
5. Market installs are staged and take effect after restart.

# 宿主侧插件运行时 / Host Plugin Runtime

## 中文

本目录保存阑山桌面宿主侧插件运行时实现。

### 主要职责

- 发现、安装和替换 `.laapp` 插件包
- 加载插件程序集和共享契约
- 接入插件设置页、桌面组件与市场界面
- 为 `3.0.0` API 基线插件构建插件作用域的 `IServiceCollection` / `ServiceProvider`
- 在激活前解析共享契约缓存，并暴露显式插件导出

### 与 LanAirApp 的分工

- `LanAirApp` 负责官方市场索引、开发文档、校验工具和镜像样例
- 本目录负责宿主运行时发现、安装、加载和界面接入
- 权威示例插件是独立仓库 `LanMountainDesktop.SamplePlugin`，`LanAirApp` 中的样例目录只是镜像模板

### 市场安装顺序

1. 宿主读取官方 `LanAirApp/airappmarket/index.json`
2. 若条目同时包含 `releaseTag` 与 `releaseAssetName`，优先解析 GitHub Release 资产
3. 若 Release 解析失败，则回退到仓库根目录 `.laapp`
4. 插件详情始终读取插件仓库根目录 `README.md`
5. 市场安装为暂存安装，重启后生效

## English

This directory contains the host-side plugin runtime for LanMountainDesktop.

### Responsibilities

- discover, install, and replace `.laapp` packages
- load plugin assemblies and shared contracts
- integrate plugin settings pages, desktop components, and market UI
- build a plugin-scoped `IServiceCollection` / `ServiceProvider` for API `3.0.0` plugins
- resolve shared contract caches before activation and expose explicit plugin exports

### Relationship with LanAirApp

- `LanAirApp` owns the official market index, developer docs, validation tools, and mirrored sample templates
- this directory owns host-side discovery, installation, loading, and UI integration
- the authoritative sample plugin lives in the standalone `LanMountainDesktop.SamplePlugin` repository; the `LanAirApp` sample directory is only a mirror/template copy

### Market install order

1. The host reads the official `LanAirApp/airappmarket/index.json`
2. If an entry contains both `releaseTag` and `releaseAssetName`, the host first resolves the exact GitHub Release asset
3. If Release resolution fails, the host falls back to the repository-root `.laapp`
4. Plugin details always come from the plugin repository root `README.md`
5. Market installs are staged and take effect after restart

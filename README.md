# 阑山桌面 / LanMountainDesktop

## 中文

`LanMontainDesktop` 是阑山桌面的宿主应用权威仓库，负责应用本体、宿主侧插件运行时，以及宿主侧 `PluginSdk` API 基线。

### 本仓库负责什么

- `LanMountainDesktop/`：桌面宿主应用
- `LanMountainDesktop.PluginSdk/`：宿主侧插件 API 真源
- `LanMountainDesktop/plugins/`：插件发现、安装、加载、市场接入
- `LanMountainDesktop.Tests/`：宿主与插件运行时测试
- `LanAirApp/`：仅用于联调的镜像副本，权威版本仍以独立 `LanAirApp` 仓库为准

### 生态边界

- 应用本体：`LanMontainDesktop`
- 插件市场与开发资料：独立 `LanAirApp`
- 权威示例插件：独立 `LanMountainDesktop.SamplePlugin`

### 当前插件 API 基线

- 宿主插件 API 基线：`3.0.0`
- `SampleClock` 共享契约：`2.0.0`

## English

`LanMontainDesktop` is the authoritative host repository for LanMountainDesktop. It owns the desktop application, the host-side plugin runtime, and the host-side `PluginSdk` API baseline.

### What this repository owns

- `LanMountainDesktop/`: the desktop host application
- `LanMountainDesktop.PluginSdk/`: the canonical host-side plugin API
- `LanMountainDesktop/plugins/`: plugin discovery, installation, loading, and market integration
- `LanMountainDesktop.Tests/`: host and plugin runtime tests
- `LanAirApp/`: a mirror kept for local workspace integration only; the standalone `LanAirApp` repository remains the source of truth

### Ecosystem boundaries

- Application host: `LanMontainDesktop`
- Plugin market and developer-facing materials: standalone `LanAirApp`
- Authoritative sample plugin: standalone `LanMountainDesktop.SamplePlugin`

### Current plugin API baseline

- Host plugin API baseline: `3.0.0`
- `SampleClock` shared contract: `2.0.0`

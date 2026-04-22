# Plugin Process Isolation

## Why

现有插件体系仍是“同进程 + AssemblyLoadContext 隔离”，无法阻止插件 fatal crash 拖垮 Host，也无法阻止插件直接访问 Host 进程内对象和内存。

## What Changes

- 增加插件运行模式概念：`in-proc`、`isolated-background`、`isolated-window`
- 一期落地 `isolated-background`
- 新建独立 IPC 契约包和 IPC 封装包
- 在 `PluginSdk` 中新增 Worker 入口与 `runtime.mode`
- 明确隔离模式下不再兼容对象实例共享型 API
- 新增正式架构文档说明 UI 方案、迁移策略、残余风险和 ClassIsland 借鉴

## Impact

- `LanMountainDesktop.PluginSdk/`
- `LanMountainDesktop.PluginTemplate/`
- 新增 `LanMountainDesktop.PluginIsolation.Contracts/`
- 新增 `LanMountainDesktop.PluginIsolation.Ipc/`
- `docs/ARCHITECTURE.md`
- `docs/PLUGIN_PROCESS_ISOLATION_ARCHITECTURE.md`

## Requirements

### Requirement 1

宿主必须同时支持存量 `in-proc` 插件与未来的隔离插件，不得以本次改造打断旧插件加载。

### Requirement 2

隔离插件的 Host/Worker 通信必须基于显式 IPC 路由和 DTO，而不是 Host 服务对象实例共享。

### Requirement 3

一期必须把后台逻辑隔离为独立 Worker 进程，并显式记录 Host UI 壳层的残余风险。

### Requirement 4

仓库文档必须把 ClassIsland IPC 的借鉴点和不照搬的部分写清楚，避免后续实现阶段误把插件协议做成远程对象模型。

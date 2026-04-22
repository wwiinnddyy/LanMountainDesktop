# 插件进程隔离架构

## 1. 背景与问题

当前插件系统只做了程序集隔离，没有做进程隔离。

- 宿主通过 `LanMountainDesktop/plugins/PluginRuntimeService.cs` 和 `LanMountainDesktop/plugins/PluginLoader.cs` 在 Host 进程内发现、加载并初始化插件。
- 插件依赖 `PluginLoadContext` 获得 `AssemblyLoadContext` 级别的依赖隔离，但代码、线程、托管堆和原生句柄仍与 Host 共处同一进程。
- 插件 `IHostedService` 也由 Host 直接构造并启动，所以插件后台逻辑和 Host 生命周期强耦合。
- 桌面组件、组件编辑器、设置页当前都直接返回 `Avalonia Control` 或 `SettingsPageBase`，并由 Host 直接插入视觉树。

这带来三个核心风险：

1. 插件崩溃会拖垮 Host，典型场景包括 `StackOverflowException`、`AccessViolationException`、原生依赖崩溃。
2. 插件可直接访问 Host 进程中的服务实例与内存对象，缺少安全边界与权限审计点。
3. 现有“对象实例共享”模型难以迁移到跨进程，因为它默认调用成本近似于内存内方法调用。

## 2. 目标与非目标

### 2.1 一期目标

- 保持增量兼容，未声明新运行模式的插件继续走 `in-proc`。
- 新增 `isolated-background` 运行模式，为每个隔离插件启动独立 Worker 进程。
- 把后台逻辑、定时任务、网络调用、原生高风险代码迁移到 Worker。
- UI 仍保留 Host 侧薄壳，通过 IPC 获取状态并发送命令。
- 新建独立 IPC 契约与封装层，为后续实际接线和插件升级提供稳定边界。

### 2.2 二期预留

- 预留 `isolated-window` 模式。
- 插件 UI 在进程外窗口中渲染，Host 通过平台能力嵌入窗口句柄。
- Windows 侧可评估 `SetParent`，Linux 侧可评估 `XEmbed` 或等价方案。

### 2.3 非目标

- 一期不强制所有插件升级。
- 一期不把现有 `IPluginExportRegistry`、`IPluginMessageBus` 直接升级成跨进程远程对象模型。
- 一期不实现完整的窗口嵌入渲染。

## 3. 运行模式设计

### 3.1 `in-proc`

- 默认模式。
- 继续使用当前 `PluginRuntimeService` + `PluginLoader` + `PluginLoadContext` 路径。
- 适合存量插件和仍依赖直接控件构造的插件。

### 3.2 `isolated-background`

- 一期目标模式。
- Host 为每个插件启动独立 Worker 进程。
- 启动时通过环境变量或命令行参数下发：
  - `pluginId`
  - `sessionId`
  - `hostPipeName`
  - `protocolVersion`
  - `runtimeMode`
- Worker 内承载后台逻辑和 IPC 端点。
- Host 只保留 UI 壳层与状态同步逻辑。

### 3.3 `isolated-window`

- 二期预留模式。
- Worker 自己创建窗口并负责 UI 渲染。
- Host 负责窗口嵌入、生命周期协调、焦点与尺寸同步。
- 这是彻底切断插件 UI 崩溃影响 Host 的最终方案。

## 4. UI 方案取舍

### 4.1 方案一：进程外窗口

优点：

- 最强崩溃隔离。
- 插件 UI 不再进入 Host 视觉树。
- 安全边界更清晰。

缺点：

- 跨平台复杂度高。
- 窗口句柄嵌入、焦点管理、输入法、缩放、多屏和无障碍都需要额外设计。
- Avalonia 与平台窗口宿主的交互验证成本高。

### 4.2 方案二：Host 薄 UI 壳层

优点：

- 与现有组件系统、编辑器系统、设置页系统的迁移成本最低。
- 可以先隔离最危险的后台与原生逻辑。
- 适合做增量兼容与插件生态迁移。

缺点：

- 如果 Host 仍执行插件提供的 UI 代码，仍有残余稳定性风险。
- 无法从根本上解决所有 UI 级崩溃。

### 4.3 一期结论

一期采用方案二，也就是 `isolated-background`。

这意味着：

- 后台逻辑先隔离。
- UI 交互先代理。
- 文档必须明确残余风险。
- `isolated-window` 的架构接口要预留，但不进入一期实现。

## 5. IPC 协议设计

底层 IPC 继续基于 [dotnetCampus.Ipc](https://github.com/dotnet-campus/dotnetCampus.Ipc)，但插件协议采用“显式路由 + DTO + 会话/心跳/故障管理”的方式，而不是把 Host 服务对象直接远程化。

### 5.1 路由分组

- `session/*`
  - `session/handshake`
  - `session/capabilities`
  - `session/ready`
- `lifecycle/*`
  - `lifecycle/initialize`
  - `lifecycle/stop`
  - `lifecycle/restart-request`
  - `lifecycle/state-changed`
- `settings/*`
  - `settings/get-snapshot`
  - `settings/write`
  - `settings/changed`
- `appearance/*`
  - `appearance/get-snapshot`
  - `appearance/changed`
- `ui/*`
  - `ui/attach`
  - `ui/detach`
  - `ui/command`
  - `ui/state-changed`
- `heartbeat/*`
  - `heartbeat/ping`
  - `heartbeat/pong`
- `log/*`
  - `log/write`
- `fault/*`
  - `fault/report`

### 5.2 契约原则

- 只传 DTO，不传 Host 内存对象。
- 所有 handler 必须在 `StartServer()` 前注册完成。
- 使用 source-generated `System.Text.Json` 上下文统一序列化。
- 协议版本通过 `session/handshake` 协商。
- 能力通过显式 capability 列表声明和授予，不做隐式远程对象暴露。

### 5.3 明确不兼容的旧能力

- `IPluginExportRegistry` 的对象实例共享不延续到隔离模式。
- 现有 `IPluginMessageBus` 不作为隔离插件主通信通道。
- Worker 不直接创建 `Avalonia Control` 并返回给 Host。

## 6. 工程拆分

### 6.1 `LanMountainDesktop.PluginIsolation.Contracts`

职责：

- 纯 DTO
- 协议版本
- 路由常量
- 错误码
- capability 声明
- source-generated JSON context

约束：

- 不引用 Avalonia
- 不依赖 Host 服务实现
- 作为 Host、Worker、SDK 共享的传输边界

### 6.2 `LanMountainDesktop.PluginIsolation.Ipc`

职责：

- 对标 ClassIsland 的轻量 IPC 封装外壳
- 统一 `PluginIpcClient`
- 统一 `PluginIpcServer`
- 统一启动参数、环境变量、通知路由常量

约束：

- 借鉴 ClassIsland 的“包装层 + 常量集中 + 客户端低接入成本”
- 但不把插件系统主协议设计成大面积远程属性模型

### 6.3 `LanMountainDesktop.PluginSdk`

新增内容：

- `runtime.mode` Manifest 支持
- `PluginRuntimeMode`
- `IPluginWorker`
- `IPluginWorkerContext`
- `PluginWorkerBase`
- `[PluginWorkerEntrance]`

## 7. ClassIsland IPC 借鉴与取舍

参考资料：

- [ClassIsland 仓库](https://github.com/ClassIsland/ClassIsland)
- [ClassIsland.Shared.IPC/IpcClient.cs](https://github.com/ClassIsland/ClassIsland/blob/master/ClassIsland.Shared.IPC/IpcClient.cs)
- [ClassIsland.Shared.IPC/IpcRoutedNotifyIds.cs](https://github.com/ClassIsland/ClassIsland/blob/master/ClassIsland.Shared.IPC/IpcRoutedNotifyIds.cs)
- [ClassIsland.Shared.IPC Abstractions](https://github.com/ClassIsland/ClassIsland/tree/master/ClassIsland.Shared.IPC/Abstractions/Services)

借鉴点：

- IPC 能力独立成包，边界清晰。
- `IpcClient` 对底层库做轻量封装，接入成本低。
- 通知路由有集中定义，事件名稳定。
- 通过公共接口暴露很小的可用面，减少耦合。

不照搬的部分：

- 不把插件隔离主协议做成“远程对象/远程属性”模型。
- 不隐藏跨进程调用成本。
- 不让 UI 状态同步变成一串隐式属性访问。

最终结论：

- 采用“ClassIsland 风格的封装外壳”。
- 协议主线仍是显式路由和明确 DTO。

## 8. 迁移策略

### 8.1 Manifest

`plugin.json` 新增：

```json
{
  "runtime": {
    "mode": "in-proc"
  }
}
```

默认值为 `in-proc`。

### 8.2 插件迁移顺序

1. 保持现有 UI 注册方式不变。
2. 把后台任务和风险代码收敛到 Worker。
3. 让 Host UI 通过 `ui/*` 与 `settings/*` 路由访问 Worker 状态。
4. 在二期再评估 `isolated-window` 迁移。

## 9. 故障模型与残余风险

一期必须满足以下行为：

- Worker 启动失败时，Host 仅禁用该插件并记录诊断。
- Worker 心跳超时或被强杀时，Host 不崩溃。
- Worker 上报 `fault/report` 时，Host 将插件标记为 degraded 或 faulted。

一期残余风险也必须明确写出：

- 如果 Host 仍执行插件提供的 UI 代码，UI 级崩溃仍可能影响 Host。
- 因此 `isolated-background` 不是最终隔离形态，只是第一阶段收益最高的落点。
- 完整 UI 崩溃隔离依赖二期 `isolated-window`。

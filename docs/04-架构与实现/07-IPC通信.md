# IPC 通信

宿主的 IPS 层建立在 **dotnetCampus.Ipc** 之上：宿主进程是服务端，把若干 `[IpcPublic]` 契约暴露给外部进程；AirApp 窗口链路另有一条 Runtime 管道。

本文讲**管道、契约、注册、通知，以及那条必须遵守的异步约束**。

最后核对：2026-11-14。

---

## 1. 两端

| 角色 | 类型 | 位置 |
|---|---|---|
| 服务端 | `PublicIpcHostService` | `core/LanMountainDesktop.Core/PublicIpcHostService.cs` |
| 客户端 | `LanMountainDesktopIpcClient` | `core/LanMountainDesktop.Core/LanMountainDesktopIpcClient.cs` |

- 服务端：`IpcProvider(pipeName)` + `Start()`，注册路由处理器并启动服务。
- 客户端：`ConnectAsync`（**3 次重试 + 抖动**）、`CreateProxy<T>` 取代理、注册通知处理器。
- 底层用到 `IpcProvider` / `JsonIpcDirectRoutedProvider` / `GeneratedIpcFactory`。

---

## 2. 管道与协议常量

唯一权威：`core/LanMountainDesktop.Core/IpcConstants.cs`。

| 常量 | 值 | 承载 |
|---|---|---|
| `DefaultPipeName` | `LanMountainDesktop.IPC.v1.Server` | 宿主的对外公共服务 |
| `AirAppRuntimePipeName` | `LanMountainDesktop.AirAppRuntime.v1` | AirApp 生命周期 + Runtime 控制服务 |
| `ProtocolVersion` | `external-ipc-public-api.v1` | 对外 API 协议版本 |
| `Routes.SessionGetInfo` | `lanmountain.session.get-info` | 路由：取会话信息 |
| `Routes.CatalogGet` | `lanmountain.catalog.get` | 路由：取目录 |

`AirAppLifecyclePipeName` 仍存在但已 `[Obsolete]`，值等于 `AirAppRuntimePipeName`——**不要在代码或文档里再引用它**。

---

## 3. 对外公共契约

| 契约 | 文件 |
|---|---|
| `IPublicShellControlService` | `core/.../Abstractions/Services/IPublicShellControlService.cs` |
| `IPublicAppInfoService` | `core/.../Abstractions/Services/IPublicAppInfoService.cs` |
| `IPublicAirAppCatalogService` | `core/.../Abstractions/Services/IPublicAirAppCatalogService.cs` |

`IAirAppLifecycleService` 与 `IAirAppRuntimeControlService` 也在同一 Abstractions 目录，但属于 **AirApp 运行时域**，注册在 Runtime 管道上（见 [AirApp 运行时](04-AirApp运行时.md)）。

**宿主侧注册**：`App.InitializePublicIpc` 直接 `RegisterPublicService<T>(实现)` 三个契约，然后 `Start() → PublishStartupProgressAsync`。

**注册机制**：`PublicIpcHostService.RegisterPublicService<T>` 通过反射调 `GeneratedIpcFactory.CreateIpcJoint` 建立接合点，并维护一份服务目录快照（外部进程可据此发现你能提供什么）。

**DI 路径**（供测试与其他进程复用）：`AddLanMountainDesktopIpcHost` / `AddPublicIpcService`，见 `core/.../DependencyInjection/ServiceCollectionExtensions.cs`。

---

## 4. 通知（notify）

```
IExternalIpcNotificationPublisher.NotifyAsync<TPayload>
  → PublicIpcHostService 广播实现
      遍历已连 peer，用 JsonIpcDirectRoutedClientProxy.NotifyAsync
      单 peer 超时 1500ms，best-effort，不阻断启动
```

通知 ID 常量（`core/.../IpcRoutedNotifyIds.cs`）：

| 常量 | 值 |
|---|---|
| `CatalogChanged` | `lanmountain.catalog.changed` |
| `LauncherStartupProgress` | `lanmountain.launcher.startup-progress` |
| `LauncherLoadingState` | `lanmountain.launcher.loading-state` |

封装好的推送方法：`PublishStartupProgressAsync` / `PublishLoadingStateAsync`；服务注册完成后会自动 `NotifyCatalogChangedAsync`。

**超时是设计的一部分**：通知面向"已经连上的 peer"，单个 peer 卡住不会拖慢宿主启动。所以通知是**不可靠投递**——需要可靠性就不能只靠它。

---

## 5. 异步硬约束

**新增 `[IpcPublic]` 方法必须返回 `Task` / `Task<T>` / `ValueTask`，不要写同步方法。**

原因是死锁而不是风格：`dotnetCampus.Ipc` 为同步方法生成的代理内部用 `Task.Wait()` 等回包，而库在自己的读循环线程上投递通知、也在那条线程上续接 `ConnectAsync` 的延续。调用方一旦在这类线程上同步等，回包就只能由当前线程投递 → **进程静默挂死，不报错、不超时**。

唯一例外是随 `LanMountainDesktop.Core` 1.0.0 发出的两个历史同步方法（`IPublicAppInfoService.GetAppInfo`、`IPublicAirAppCatalogService.GetCatalog`）：它们是已公开的 API，只能"加异步版本 + 标废弃"两步撤。

约束由测试机器检查，例外名单只减不增：

| 测试 | 钉住的约束 |
|---|---|
| `IpcPublicContractArchitectureTests` | `[IpcPublic]` 契约禁新增同步方法（含基线例外与陈旧条目检查） |
| `ExternalIpcPublicApiTests` | 强类型服务暴露、catalog 变更通知 |
| `InProcAirAppIpcTransportTests` | AirApp IPC 传输往返 / 通知 / 错误传播 |
| `LauncherIpcStreamIoTests` | 启动器 IPC 流读写收口 |
| `SourceIntegrityTests` | `launch-source` 词表只准住 `LauncherIpc.cs` |

---

## 6. AirApp 运行时管道

AirApp 窗口链路走另一条管道 `LanMountainDesktop.AirAppRuntime.v1`，上面注册 `IAirAppLifecycleService`（`OpenAsync` / `ActivateAsync` / `CloseAsync` / `GetInstancesAsync` / `RegisterAsync` / `UnregisterAsync`）与 `IAirAppRuntimeControlService`（`AttachHostAsync` / `GetStatusAsync`）。

调用方有三处：

| 调用方 | 动作 |
|---|---|
| 宿主 `AirAppLauncherService` | 打开/激活窗口（首次失败自拉起 Runtime，重试 4 次） |
| 启动器 `AirAppRuntimeBridge` | 预启动 + `AttachHostAsync(hostPid)` 交接 |
| AirAppHost 窗口 | `RegisterAsync` / `UnregisterAsync`（best-effort） |

细节见 [AirApp 运行时](04-AirApp运行时.md) 第 9 节与 [AirApp 运行时与窗口轻应用](../02-AirApp运行时/README.md)。

---

## 7. 约束与坑

| 坑 | 说明 |
|---|---|
| 同步方法挂死 | 见第 5 节；症状是"没反应、没异常、没超时" |
| 通知不可靠 | 单 peer 1500ms 超时、best-effort；不能作为业务数据通道 |
| 管道名散落 | 一律引用 `IpcConstants`；`AirAppLifecyclePipeName` 已废弃 |
| 契约类型来源受限 | 轻应用的公共 IPC 契约必须是 SDK 内类型或清单声明的共享契约类型，否则拒载 |

---

## 相关文档

- [整体架构](01-整体架构.md) · [AirApp 运行时](04-AirApp运行时.md)
- [AirApp 运行时与窗口轻应用](../02-AirApp运行时/README.md) —— 对外契约只看异步这条规矩的完整说明

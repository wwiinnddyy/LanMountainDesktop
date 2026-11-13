# AirApp 运行时

本文讲**宿主侧的 AirApp 运行时**：一个 `.laapp` 从"躺在磁盘上"到"贡献出现在桌面/设置/其他进程可调用"之间的全部机制，以及它什么时候生效、失败时会怎样。

- 结构总览与进程拓扑：[整体架构](01-整体架构.md)
- 窗口形态怎么用：[AirApp 运行时与窗口轻应用](../02-AirApp运行时/README.md)
- 开发者视角的加载语义：[轻应用生命周期](../01-AirApp开发/02-核心概念/01-轻应用生命周期.md)

代码位置：宿主侧运行时在 `desktop/LanMountainDesktop/AirApps/`；窗口进程在 `airapp/LanMountainDesktop.AirAppHost/`；
实例协调在 `airapp/LanMountainDesktop.AirAppRuntime/`；SDK 在 `airapp/LanMountainDesktop.AirAppSdk/`。

最后核对：2026-11-14。

---

## 1. 组成

| 组件 | 文件 | 职责 |
|---|---|---|
| `AirAppRuntimeService` | `AirApps/AirAppRuntimeService.cs` | 运行时门面：发现、加载编排、目录（catalog）、启用/禁用/卸载、贡献收集、外观广播 |
| `AirAppLoader` | `AirApps/AirAppLoader.cs` | 单个轻应用的加载：ALC、入口解析、DI 容器、贡献读取 |
| `AirAppLoadContext` | `AirApps/AirAppLoadContext.cs` | 每个轻应用一个 `isCollectible: true` 的 ALC + 共享程序集白名单 |
| `AirAppPackageReader` | `AirApps/AirAppPackageReader.cs` | 从 `.laapp` 里读清单，全宿主唯一一处 |
| `AirAppSharedContractManager` | `AirApps/AirAppSharedContractManager.cs` | 共享契约的下载、校验、加载、回收 |
| `AirAppMarketIndexService` | `AirApps/AirAppMarketIndexService.cs` | 取市场索引（本地 → 网络 → 缓存） |
| `AirAppMarketInstallService` | `AirApps/AirAppMarketInstallService.cs` | 下载、校验、暂存安装，必要时提权 |
| `PendingAirAppUpgradeService` | `Services/PendingAirAppUpgradeService.cs` | 待安装/升级队列，重启时统一落盘 |
| `AirAppLauncherService` | `Services/AirAppLauncherService.cs` | 组装打开请求并通过 IPC 交给 Runtime |
| `AirAppHostLocator` / `AirAppProcessStarter` | `airapp/...AirAppRuntime/` | 找 AirAppHost 可执行文件并拼启动参数 |

`AirAppRuntimeService` 由 `App.axaml.cs` 在后台线程创建并调用 `LoadInstalledAirApps()`，完成后换到 UI 线程替换旧实例。

---

## 2. 一次加载的完整时序

```
LoadInstalledAirApps()
  1. Directory.CreateDirectory(AirAppsDirectory)
  2. UnloadInstalledAirApps()          ← 先卸干净（可重复调用）
  3. ApplyPendingAirAppDeletions()     ← 处理上次留下的待删队列
  4. ApplyPendingAirAppOperations()    ← 落盘上次排队的安装/升级（先 EnsureInstalled 契约）
  5. MergeDevSettingsFromSnapshot()    ← 把设置里的开发模式/开发路径并进来
  6. 读禁用集合 + 语言 + 组装 hostProperties
  7. DiscoverCandidates()              ← 扫描，产出候选 + 扫描期失败
  8. 逐个候选：
       重复 id 检查 → 禁用则只进目录 → RegisterSharedContractsForLoad
       → LoadFromPackage / LoadFromManifest（dev）
       → 成功：加入 loaded、CollectContributions、写目录
       → 失败：写目录（带错误信息）+ 记日志
  9. PruneUnusedSharedContracts()      ← 回收没人引用的契约
```

四个必须知道的顺序约定：

1. **先卸后装**。`UnloadInstalledAirApps` 会把已加载的全部 `Dispose`（停 hosted service → `OnStoppingAsync` → 释放容器 → `ALC.Unload()`），再清空目录/导出/设置节。它是幂等的，所以异常重试路径也走它。
2. **待办先于发现**。第 3、4 步在扫描之前执行，所以"上次排队的安装"这一轮就能被扫到——这是"重启生效"能成立的关键。
3. **契约先于加载**。第 8 步里 `RegisterSharedContractsForLoad` 早于 `LoadFromPackage`：契约缺失会当场下载；下载或校验失败则这个候选记为失败，**不阻塞其他候选**。
4. **禁用不卸载**。禁用项只写进目录（`IsEnabled=false`）并跳过加载，磁盘包保留。

---

## 3. 发现（Discovery）

安装根：`<数据根>/Extensions/AirApps`。

候选来源与优先级（`OrderByDescending(SourceKind)`，枚举值 `Package=0`、`Manifest=1`、`DevAirApp=2`）：

| 来源 | 扫描内容 | 加载入口 |
|---|---|---|
| `Package` | 递归 `*.laapp`，读包内清单 | `LoadFromPackage` |
| `Manifest` | 递归散放的 `airapp.json` | `LoadFromManifest` |
| `DevAirApp` | `--dev-airapp` / `LMD_DEV_AIRAPP` 指向的目录或 `.laapp` | `LoadFromManifest` / `LoadFromPackage` |

细节：

- `.runtime` 子树被显式排除（`PathContainment.IsSameOrChild` 判断），否则解包出来的副本会被再次扫到。
- 扫描期读清单失败（例如包内没有 `airapp.json`、或清单校验不过）会进 `LoadResults` 但不致命，日志记 `ManifestValidation`。
- **同 id 只有开发期覆盖是静默生效的**：`selectedAirAppIds.Add` 失败时，若候选来自 dev 则只打一条 Info，否则构造一条"重复 id"的失败结果。这样"本地开发顶掉已装的同名包"是日常操作，而产品环境里的重复件会留下明确证据。
- 开发期路径支持 `;` 分隔多个；不存在的路径只告警并跳过。
- 开发期轻应用**始终启用**，不接受禁用（`SetAirAppEnabled` 会拒绝）。

---

## 4. 加载（Load）

`AirAppLoader.LoadCore` 的九步与失败语义：

| 步 | 动作 | 失败会怎样 |
|---|---|---|
| 1 | 建 `Data` 目录；`ValidateAirAppRuntimeAssets` 校验 `.deps.json`、空 `runtimes/` | 拒载（dev 模式 `.deps.json` 只告警） |
| 2 | `new AirAppLoadContext(assemblyPath, sharedAssemblyNames)` | — |
| 3 | `LoadFromAssemblyPath` | 拒载 |
| 4 | `ResolveAirAppType`：`[AirAppEntrance]` 或唯一 `IAirApp` 实现 | 拒载（零个、多个且无标记） |
| 5 | 建实例、`AirAppRuntimeContext`、`ServiceCollection` | 拒载 |
| 6 | `IAirApp.Initialize(context, services)` | 拒载 |
| 7 | `BuildServiceProvider(ValidateOnBuild: true)` → `SetServices` → `OnStartedAsync` | 拒载 |
| 8 | 读设置节/组件/编辑器/导出/公共 IPC；`ValidateManifestComponentContract` | 拒载（清单声明了没注册的组件） |
| 9 | `StartHostedServices` → 组装 `LoadedAirApp` | 拒载并回滚已启动的 hosted service |

失败路径统一走 `catch`：`StopHostedServices` → 逐层 `Dispose` → `loadContext?.Unload()` → 返回 `AirAppLoadResult.Failure`，异常先过 `ExplainBindingFailure` 翻译成人话。**单个包失败不会中断整轮加载**。

### 共享程序集白名单

`AirAppRuntimeService.CreateOptions()` 组装的名单比想象中宽：

- 宿主自身程序集（`typeof(App).Assembly`）
- `IServiceCollection` / `HostBuilderContext` 所在程序集
- `IExternalIpcNotificationPublisher` 与 `dotnetCampus.Ipc` 所在程序集
- **运行时已加载的**所有 `Avalonia*`、`FluentAvaloniaUI`、`FluentIcons.Avalonia`、`Material.Avalonia`、`Material.Icons.Avalonia`、`MicroCom.Runtime`

再加每个候选清单声明的共享契约程序集名（`RegisterSharedContractsForLoad` 时追加）。

白名单决定"哪些类型两边必须是同一个"。轻应用工程因此必须 `ExcludeAssets="runtime;native"` `PrivateAssets="all"`——否则包内会带一份 SDK 或 Avalonia，出现同名类型不相等。

### 清单契约检查

`ValidateManifestComponentContract` 只钉一个方向：**清单 `components[].id` 里的每一项都必须有对应的 `AddAirAppComponent` 注册**，否则拒载并指名。反方向（注册了但清单没声明）只告警——它不产生用户可见的坏状态，只是作者白写。

---

## 5. 贡献与落点

加载成功后 `CollectContributions` 把五类贡献分发到各自宿主落点：

| 贡献 | 落点 | 生效范围 |
|---|---|---|
| 设置节 | `SettingsCatalogService.RegisterAirAppSections` + `_settingsSections` | 宿主设置窗口（进程内） |
| 桌面组件 | `_desktopComponents` → `DesktopComponentRegistryFactory` | 桌面网格，按放置位实例化 |
| 组件编辑器 | `_desktopComponentEditors` | 组件编辑器窗口 |
| 服务导出 | `AirAppExportRegistry.ReplaceExports` | 其他轻应用按 `providerAirAppId` 取 |
| 公共 IPC | `PublicIpcHostService.RegisterPublicService` | 宿主外部进程 |
| `IHostedService` | 加载时启动 | 随 `LoadedAirApp.Dispose` 停止 |

外观广播不走贡献点：宿主 `IMaterialColorService.MaterialColorChanged` 触发时，运行时把快照推给每个已加载轻应用的 `AirAppAppearanceContext`（`OnMaterialColorChanged`）。

---

## 6. 状态变更与生效时机

| 操作 | 磁盘动作 | 生效时机 |
|---|---|---|
| 安装（新装） | 下载 → 校验 → 写 `<AirApps>/.pending-plugin-upgrades.json` 队列 | 下次启动第 4 步 |
| 升级 | 同上 | 同上 |
| 启用 / 禁用 | 写 `AppSettingsSnapshot.DisabledAirAppIds` + `PendingRestartStateService.SetPending` | 下次启动第 8 步 |
| 卸载 | 立刻尝试删除；删不掉则登记到 `.pending-airapp-deletions.json` | 立即 + 下次启动第 3 步补刀 |
| 开发期覆盖 | 无 | 立即（下一轮发现即可见） |

三个实现细节：

- **卸载是"尽力删 + 待办补刀"**。`TryDeleteAirAppTarget` 对文件走 `TryDeleteFile`（会先清只读属性）、对目录走 `TryDeleteDirectory`；**路径本来就不存在算成功**，否则会把不存在的路径塞进待办，下次启动白跑一趟。目录形式的散放清单删除时删的是它所在的目录。
- **待删队列的文件名有历史包袱**：写入 `.pending-airapp-deletions.json`，读之前会先尝试从 `.pending-plugin-deletions.json` 改名归位（`GetPendingDeletionFilePath` 里集中做，保证四个调用点都走同一条迁移）。
- **队列应用失败会保留**：`ApplyPendingOperations` 里失败的条目留在队列里，下次启动重试，并在日志里以 Warn 记明。

---

## 7. 市场与安装

```
AirAppMarketIndexService.LoadAsync
  本地优先（工作区兄弟仓 ..\LanAirApp\airappmarket\index.json）
  → 网络（AirAppMarketDefaults.DefaultIndexUrl）
  → 磁盘缓存
  索引是自包含的：展示信息、兼容性、下载地址都在条目里，不再回调 GitHub 拼装
        ↓
AirAppMarketInstallService.InstallAsync
  ValidateCompatibility（宿主版本 + API 主版本）
  → 按来源顺序尝试：ReleaseAsset（可解析到具体 release 资源）→ RawFallback → WorkspaceLocal
  → 下载（ResumableDownloadService）→ 若条目带 sha256 则校验
  → 目标目录不可写？→ ElevatedAirAppInstallService 提权安装 → RestartRequired
  → 否则写待升级队列 → RestartRequired
```

几个行为特征：

- **哈希校验是可选的**：只有索引条目带 `sha256` 时才比对；缺失就跳过。
- **多来源串行兜底**：`GetPackageSourcesInInstallOrder()` 逐个尝试，失败信息累积到 `sourceErrors`，全部失败才返回错误。
- **安装不立即生效**：成功也是 `RestartRequired: true`，因为运行时不做热替换。
- **工作区调试捷径**：`AirAppMarketDefaults.TryResolveWorkspaceFile` 允许把索引/下载地址解析成本地文件，所以本地开发可以不联网。

---

## 8. 共享契约

契约是由市场分发、被多个轻应用引用的程序集。`AirAppSharedContractManager` 的完整语义：

1. **位置**：`<数据根>/AirAppMarket/SharedContracts/{id}/{version}/{assembly}`（构造时传入的是**市场目录**，所以契约与市场缓存共用存储根）。
2. **校验**：下载到 `.download` 临时文件 → 比对 `PackageSizeBytes` 与 SHA-256 → 通过才 `File.Move` 覆盖生效；任一步失败都删临时文件并抛错。
3. **索引一致性**：索引里的 `assemblyName` 必须与清单声明的一致，否则拒装。
4. **加载目标**：`AssemblyLoadContext.Default`。**契约一旦加载就钉死在进程里**，不随某个轻应用卸载而释放。
5. **单版本约束**：同一程序集名进程内只允许一个版本。若已被加载为另一个 `id@version`，会明确抛错而不是静默用错版本——两个应用依赖同一契约的不同大版本时，后加载者必然失败。
6. **回收**：`PruneUnused(installedManifests)` 保留集合 = 盘上现存包（含被禁用的）的清单引用 + 内存中已加载的契约；**清单集合为空时一律不删**（视为"发现环节坏了"而不是"用户卸载光了"）。`_loadedContracts` 与磁盘墓碑的取舍写在这个方法的注释里，理由是"删了既不会真的释放磁盘，又可能让后续加载走到半路"。

---

## 9. 窗口轻应用（宿主侧视角）

宿主侧的职责只有"组装请求 + 发出去"：

```
桌面组件的 OpenWindowHandler
  → AirAppLauncherService.OpenThirdPartyAirAppWindow(appId, windowId, packageDir, componentId, placementId)
  → AirAppOpenRequest { ..., TargetEntryId = windowId, AppPackagePath = packageDir }
  → SendOpenRequestAsync：连 LanMountainDesktop.AirAppRuntime.v1
        首次失败 → TryStartRuntime()（按 --app-root / --data-root / --requester-pid 拉起 Runtime）
        重试 4 次，退避 250ms × attempt
  → IAirAppLifecycleService.OpenAsync
```

宿主**不参与**实例去重、不创建窗口进程、不渲染窗口内容。实例键、激活、存活判定都在 Runtime；窗口内容解析在 AirAppHost。详见 [AirApp 运行时与窗口轻应用](../02-AirApp运行时/README.md)。

`BuildSingleInstanceKey` 是本文件里唯一与实例身份有关的逻辑：`world-clock` → `world-clock:clock-suite:global`，`rss-reader` → `rss-reader:global`，其余 `{appId}:{componentId}:{placementId}`。

---

## 10. 诊断与失效表现

| 现象 | 真实原因 | 看哪里 |
|---|---|---|
| 面板里有格子、点下去没反应 | 清单声明了组件但代码没注册（本该拒载，若绕过则如此） | `ValidateManifestComponentContract` |
| "与本宿主的 AirAppSdk 二进制不兼容，已拒绝加载" | 包用另一版 SDK 编译；`apiVersion` 相同也会发生 | `ExplainBindingFailure` |
| 加载失败但只看到 IL 级方法名 | 未经翻译的绑定异常（不该发生，属缺陷） | `LoadCore` 的 `catch` |
| `.laapp` 解出来的副本被当成第二个包 | 有人手工把包解到了 `.runtime` 之外 | `EnumerateCandidatePaths` 的排除规则 |
| "需重启生效"却没有任何提示 | 只写了快照/队列，未触发 `PendingRestartStateService` | 第 6 节各行 |
| 契约目录一直涨 | `PruneUnused` 的保守策略（空清单不删） | `AirAppSharedContractManager.PruneUnused` |

日志类别：`AirAppRuntime`、`AirAppLoader`、`AirAppWindow`、`AirAppSharedContracts`、`AirAppMarket`、`PendingAirAppUpgrade`、`DevAirApp`。

---

## 11. 验证边界（现状）

| 能力 | 状态 |
|---|---|
| 加载与清单契约 | **已验**：外部六份真实仓库的构建产物在本地被加载并校验组件声明（`ExternalAirAppLoadProbeTests`） |
| 跨进程 IPC 握手 | **未验**：探针到"加载 + 清单 + 组件注册"为止，全文无 IPC 调用；`AirAppProcessStarterRuntimeTests` 只验 `StartInfo` 拼装 |
| CI 是否覆盖 | **不覆盖**：`code-quality.yml` 用 `FILTER='Category!=EcosystemProbe'` 有意排除，所以"CI 绿"对上一行不是证据 |
| 隔离模式 | **未实现**：`runtime.mode` 的 `isolated-*` 只通过校验，无任何分支消费；`AirAppIpcClient` / `AirAppIpcServer` 是未接传输层的桩 |

结论：说"外部轻应用验收已过"时必须带上这一刀切——**加载与清单契约已验，跨进程握手未验**。

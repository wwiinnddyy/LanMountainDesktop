# 命名与标识符冻结登记

## 目的

`LanMountainDesktop` 的插件体系已从 Plugin SDK 升级为 AirApp SDK，但代码里仍有 `plugin` 字样。
本文件区分**哪些是清理时漏掉的**、**哪些是故意保留的**。没有这份登记，每次重构都会重新争论一遍，
而且很容易把冻结项改掉造成线上故障。

判定顺序：先看第 3 节（冻结）→ 命中则不要动；再看第 2 节（需迁移）→ 命中则必须配迁移器；
其余属于第 1 节（自由），可直接改名。

## 1. 自由：仓库内部符号

改这些不需要协调任何外部方，也不影响用户数据。

- C# 类型名、成员名、参数名、局部变量、文件名。
- XML 文档注释与 IntelliSense 文案。
- 异常与日志里的描述性文字（前提：没有测试按字符串断言它，改前先 grep）。
- 设置节 id `"plugins"` 与存储分类 id `"plugins"`：经复核**不落盘**（没有任何地方持久化选中的页/节 id），
  所以是自由项。但两者都是**成对匹配**的：节 id 在 `SettingsCatalogService.cs:23` 定义、
  由 `Views/SettingsPages/AirAppsSettingsPage.axaml.cs:8` 的 `[AirAppSettingsPageInfo("plugins", …)]` 消费，
  分类 id 在 `DataStorageService.cs:47` 定义、由同文件 `category.Id switch` 消费。
  只改一侧会静默失配（分节不显示 / 扫描归类落空），构建和测试都抓不到，必须同批改并加接线断言。
- 同类成对项还有 `plugin-catalog`：页面 id + `Styles/SettingsCardStyles.axaml` 里的 5 个 `Button.plugin-catalog-*`
  样式选择器与 3 处 `Classes=`。改名收益纯属观感，失配是静默失样，因此当前有意搁置。

已按此规则完成的收敛：`core/` 的 14 个 `Plugin*` 类型已改为 `AirApp*`（含文件名）；
`LanMountainDesktop.AirAppSdk` 的 `AirAppComponentContext` / `AirAppComponentEditorContext` /
`AirAppLocalizer` 构造参数 `pluginDirectory` / `pluginSettings` 已改为 `airAppDirectory` / `airAppSettings`。

判定这些改名安全的前提（复核于 2026-09）：所有插件仓库只 `PackageReference`
`LanMountainDesktop.AirAppSdk`，无一直接引用 `LanMountainDesktop.Core`，
对 `LanMountainDesktop.AirAppPackaging` 命名空间零 using，对 `Core` 里那些 `Plugin*` 类型零源码引用，
且无任何仓库使用命名实参调用上述构造函数。

## 2. 需迁移：已安装实例的本地数据

改这些会让老用户升级后读不到自己的数据，必须先做一次性迁移器 + 一个双读窗口。

| 标识符 | 位置 | 说明 |
| --- | --- | --- |
| `.pending-plugin-upgrades.json` | `core/.../AirAppPackagingConstants.cs` | 磁盘文件名。**目前不要改**，原因见下方"跨二进制读取方" |
| `PendingAirAppUpgrade.PluginId` 等成员 | `core/.../PendingAirAppUpgradeStore.cs` | 该 store 的 `JsonSerializerOptions` 未设 `PropertyNamingPolicy`，成员名即磁盘 JSON 字段名 |
| 设置节 id `"plugins"` | `Services/Settings/SettingsCatalogService.cs:23`、`Views/SettingsPages/AirAppsSettingsPage.axaml.cs:8` | 不写进 settings.json，但两处必须同批改 |
| 组件 ID `Desktop*` | 布局快照 `Models/DesktopComponentPlacementSnapshot.cs`、`FusedDesktopLayoutSnapshot.cs` | 用户桌面布局里存的是组件 ID |
| `ComponentDomainStorage.cs:669` 的 `"pluginSettings"` | — | 是判别已落盘 JSON 文档形状的属性名字符串（该 store 用 CamelCase），不是变量名；改它会误判旧文档结构 |

### 跨二进制读取方 —— 改名前必须先问这一句

一个磁盘名能不能改，不看它"是否持久化"，而看**谁读它**。`settings.json` 的键只有宿主自己读，
所以可以按下面的模式原地迁移；而 `.pending-plugin-upgrades.json` 是**宿主写、Launcher 读**，
Launcher 是随版本切换选中的独立二进制，升级窗口里它可能比宿主旧：

- 宿主改写新名 → 老 Launcher 仍按旧名查找 → 排队中的升级**静默永不应用**。
- 因此该文件要改必须两步走：先发一个"两个名字都读"的 Launcher，等它铺开后，再让宿主改写新名。
  单独改常量等于制造静默故障。

### 已迁移完成

`settings.json` 的两个键已按下述模式迁移，不要再往回退：

- `DisabledPluginIds` → `DisabledAirAppIds`
- `DevPluginPath` → `DevAirAppPath`

模式是「新成员 + 旧名只读别名 + 首次加载即合并并回写」，仿 `LauncherSettingsService.cs:62-81` 的
`loadedFromLegacy` 范式：`AppSettingsSnapshot` 上保留 `[JsonPropertyName("旧名")]` +
`[JsonIgnore(Condition = WhenWritingNull)]` 的 `Legacy*` 别名属性，`AppSettingsService.Load()` 调
`TryMergeLegacySettingsKeys` 合并后立刻写盘，因此旧键只读不写、迁移一次即自愈。
契约由 `AppSettingsLegacyKeyMigrationTests` 钉住（含"新旧并存时新键优先"和"迁移后不再序列化旧键"）。

同类做法适用于将来处理 `"plugins"` 节 id 等仍待迁移的项。

另外两个**只有宿主自己读写**的磁盘文件名也已改名归位（走 `AppDataPathProvider.TryMoveLegacyPath`，
语义是"只改名、不合并、不删除，两侧都在就保持原样并告警"）：

- `plugin-settings.json` → `airapp-settings.json`（在 `SettingsService` 构造函数里、算出新路径之前归位）
- `.pending-plugin-deletions.json` → `.pending-airapp-deletions.json`（放在 `GetPendingDeletionFilePath()`
  这个路径计算出口，因为 `Register`/`Read`/`Save`/`Apply` 四个方法都用它，散点插迁移必有漏径）

旧名字仍以 `Legacy*` 常量的形式留在原处，只用于改名，新代码不要引用。

### 已迁移完成：本地化键

键名只存在于「源码字面量 ↔ 四个语言文件」之间，不写进用户磁盘，因此**没有新旧并存期**，必须同批原子改完。
它同时也是静默故障高发区：`LocalizationService.GetString` 取不到键时直接返回调用方传的 fallback
（`Services/LocalizationService.cs:57-59`），界面只会退回默认文案，不抛异常、不写日志。

原 67 个含 `plugin` 的键：**22 个活键改名，45 个死键删除**。死键判据是全树字面量搜索零命中
（覆盖 `desktop`/`core`/`airapp`/`install`/`mobile` 的 `.cs` 与 `.axaml`，以及 4 个同级 AirApp 仓库），
且源码里不存在拼接本地化键的代码路径。映射规则：

| 旧 | 新 |
| --- | --- |
| `settings.plugins.*` | `settings.airapps.*` |
| `settings.plugin_catalog.*` | `settings.airapp_catalog.*` |
| `settings.dev.plugin_path_*` | `settings.dev.airapp_path_*` |
| `settings.nav.plugins`、`settings.nav.plugin_catalog`、`market.detail.plugin_information` | 已删除（无引用） |

`settings.dev.airapp_path_*` 三条在 `ja-JP` / `ko-KR` 原本就没有，改名后齐平断言会红，因此为这两种语言
补了译文。注意这是例外：整族 `settings.dev.*`（29 键）和 `rss.*` 的部分键在 ja/ko 一直是缺失状态，
靠 fallback 兜着。

**键名不等于值。** 138 条译文值和 7 处代码 fallback 仍写着 plugin / 插件 / プラグイン / 플러그인，那是产品
文案决策，不在改名范围内；其中 `settings.dev.cli_example`、`settings.dev.env_example` 展示的是冻结协议
（`--dev-plugin`、`LMD_DEV_PLUGIN`），**必须保持原样**。

### 已迁移完成：市场缓存目录

`{DataRoot}/PluginMarket` → `{DataRoot}/AirAppMarket`。市场目录在 AirApp 改名时换了名，但当时没搬旧数据，
已安装实例里留下一份代码再也读不到的 `PluginMarket`（`AirAppMarketCacheService.cs:13-16` 只找
`AirAppMarket/cache/index.json`）。现在 `AppDataPathProvider.Initialize()` 末尾做一次改名。

搬迁是安全的，因为相对路径没变（新旧都是 `cache/index.json`），且 `AirAppMarketIndexService` 在网络成功时
总是覆写缓存（`:38`、`:70`），只在网络失败时把它当离线兜底读（`:92`）——不会把过期清单当真。

**只改名、不合并、不删除**：若 `PluginMarket` 与 `AirAppMarket` 同时存在，保持原样并告警，因为无法判断
哪一份是用户要的。要清理这份残留请单独决策，不要塞进重命名里。

## 3. 冻结：跨进程或跨仓库协议

**不要重命名。** 改这些会破坏与"不在本仓库、不随本次构建更新"的一方之间的兼容。

| 标识符 | 位置 | 对端是谁 |
| --- | --- | --- |
| `LANMOUNTAIN_PLUGIN_ID` / `_SESSION_ID` / `_HOST_PIPE` / `_PROTOCOL_VERSION` / `_RUNTIME_MODE`、`--plugin-id` | `airapp/LanMountainDesktop.AirAppSdk/AirAppIpcConstants.cs` | 已缓存的 `AirAppRuntime` / `AirAppHost` 二进制 |
| 市场索引字段 `plugins` / `pluginId` | `AirApps/AirAppMarketModels.cs`（`JsonPropertyName`）、兄弟仓库 `LanAirApp` 的 `airappmarket/schema` | 已发布的市场索引（schemaVersion 3.0.0） |
| `PublicIpcCatalogSnapshot.Plugins`、`PublicAirAppDescriptor.PluginId`、`PublicIpcServiceDescriptor.PluginId` | `core/LanMountainDesktop.Core/` | 宿主与外部进程的 IPC 应答按成员名反序列化 |
| `airapp.json` 的 `id` / `entranceAssembly` / `components[].id` 值 | 各插件仓库已分发的 `.laapp` | 已安装包；按名解析程序集 |
| 第三方程序集文件名（如 `ClassworksPlugin.dll`、`LanWordPlugin.dll`） | 外部仓库 | 已分发包的 `entranceAssembly` |
| Launcher 命令 token `"plugin"` | 写方 `Services/ElevatedAirAppInstallService.cs`，读方 `Launcher/CommandContext.cs`、`Launcher/Infrastructure/Commands.cs` | 升级窗口内新宿主可能调用旧 Launcher 二进制。要改必须双 token 并存，`CommandContext.cs` 里已有 `IsLegacyAirAppInstall \|\|` 这种双读先例可沿用 |
| Inno Setup `MyAppId` GUID、`com.lanmountain.desktop`、XMLNS `http://lanmountain.tech/schemas/xaml/sdk` | `install/`、`airapp/.../AssemblyInfo.cs` | 永久标识符 |

### 已知例外：Launcher 的 `runtime`（少一个点）

`desktop/LanMountainDesktop.Launcher/AirApps/AirAppInstallerService.cs` 里
`RuntimeDirectoryName = "runtime"`，而宿主与 Core 用 `.runtime`。这让
`RemoveExistingAirAppPackages` 的排除条件形同虚设。

**没有顺手改成 `.runtime`，因为那是行为变更而非改名**：当前失效的排除条件在客观上会连带删掉本应用
在 `.runtime` 下的旧包副本；对齐后旧副本会存活，而 `AirAppLoader.EnumerateCandidatePaths` 是递归扫描
候选包的，可能出现同一 AirApp 被重复加载。要修必须连同加载器的去重策略一起决策。

## 4. Plugin SDK 的支持状态

`LanMountainDesktop.PluginSdk`（4.x / 5.x）与 `plugin.json` **已停止支持**：宿主不再识别 `plugin.json`，
也不会加载基于 `IPlugin` / `PluginBase` 的程序集；`apiVersion` 主版本必须等于
`AirAppSdkInfo.ApiVersion`（当前 `1.0.0`），否则加载直接抛错。

`AirApps/AirAppLoader.cs` 内不存在任何读取 `plugin.json` 或识别 `PluginEntrance` 的回退分支。
迁移映射见 `docs/AIRAPP_SDK_V1_MIGRATION.md`；唯一的对外废弃声明原文在 `docs/01-AirApp开发/README.md`。

已知滞留者：`elysia.LanDesktopConnect` 仍引用 `LanMountainDesktop.PluginSdk 5.0.0` 并携带 `plugin.json`，
因此它在当前宿主上无法加载（清单名与主版本双重不匹配）。

## 5. 防回归

`tests/LanMountainDesktop.Tests/DataIdentifierArchitectureTests.cs` 断言第 2 节里的磁盘/包字面量
在整个解决方案中只出现一处。新增同类标识符时，请扩展现有常量类而不是就地写字面量。

`manifest.json` 被有意排除在该断言之外：`AirAppMarketAssetCacheService` 与 `ZhiJiaoHubCacheService`
里的 `manifest.json` 是各自缓存目录的清单文件，与"旧版包清单"同名但不同概念。

本地化键由 `tests/LanMountainDesktop.Tests/AirAppLocalizationKeyTests.cs` 四条断言守住：语言文件里不得再
出现含 `plugin` 的键；含 `airapp` 的键必须四语齐平、必须被生产源码字面量引用、源码引用的含 `airapp` 键
必须在四语里都有。四条都做过变异验证。范围只到 `airapp` 命名的键族 —— 全仓库既有的一千多个孤儿键不在
这份契约里，别把它当通用 i18n 清理工具。

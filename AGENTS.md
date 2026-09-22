# LanMountainDesktop AI Guide

本文件是 AI 助手进入本仓库时的第一入口。面向 Codex、Cursor、Trae 等工具，目标是减少重复探索，快速定位权威文档、关键目录和执行约束。

## 1. 项目目标与仓库边界

- 本仓库是阑山桌面桌面宿主、宿主侧 AirApp 运行时、AirApp SDK、共享契约与基础外观/设置能力的权威来源。
- 不要把轻应用市场元数据、开发者门户或官方示例轻应用实现当作本仓库内容维护。
- 市场和生态材料属于兄弟仓库 `LanAirApp`。
- 官方示例轻应用属于独立仓库 `LanMountainDesktop.SamplePlugin`。

边界详情看：

- `docs/archive/ECOSYSTEM_BOUNDARIES.md`
- `docs/archive/ARCHITECTURE.md`（新结构见 `docs/04-架构与实现/01-整体架构.md`）

## 2. 关键目录地图

- `desktop/LanMountainDesktop/`: 主宿主应用，包含 UI、服务、组件系统、主题与 AirApp 运行时接入
- `desktop/LanMountainDesktop/ComponentSystem/`: 内置组件定义、注册、扩展加载
- `desktop/LanMountainDesktop/AirApps/`: 宿主侧 AirApp 运行时、安装与 market 集成
- `desktop/LanMountainDesktop/Views/` and `ViewModels/`: UI 页面、窗口与视图模型
- `desktop/LanMountainDesktop/Services/`: 设置、遥测、启动、持久化、业务服务
- `airapp/LanMountainDesktop.AirAppSdk/`: AirApp SDK 公共接口和默认打包行为（含隔离层与宿主桥接契约）
- `platform/LanMountainDesktop.Platform/`: 平台差异层（接口 + Windows/macOS 实现）
- `core/LanMountainDesktop.Core/`: 宿主/AirApp 共享契约、IPC 基础设施与打包
- `mobile/LanMountainDesktop.Mobile/`: 共享移动 UI 壳（组件面板）
- `mobile/LanMountainDesktop.Mobile.Android/`: Android head（入口）
- `tests/LanMountainDesktop.Tests/`: 宿主与 SDK 测试
- `.trae/specs/`: feature 级规格、任务拆解和验收清单

更详细映射看 `docs/ai/CODEBASE_MAP.md`。

## 3. 常用命令

```bash
dotnet restore
dotnet build LanMountainDesktop.slnx -c Debug
dotnet run --project desktop/LanMountainDesktop/LanMountainDesktop.csproj
dotnet test LanMountainDesktop.slnx -c Debug
```

AirApp 本地包生成：

```powershell
./scripts/Pack-AirAppPackages.ps1
```

## 4. 改动前后必做检查

改动前：

- 先确认需求是否已经在 `.trae/specs/` 中存在
- 先确认产品、架构、专题规范分别以哪份文档为准
- 避免沿用旧根目录产品文档中的过时事实

改动后：

- 至少检查构建和与改动相关的测试
- 如果行为、流程、边界或命令变化，更新对应文档
- 如果是新功能或行为调整，补齐或更新 `.trae/specs/<feature>/`

## 5. 高频区域注意事项

### UI

- 主题、资源和视觉语义优先遵守 `docs/03-组件设计规范/02-视觉规范.md`（归档见 `docs/archive/VISUAL_SPEC.md`）与 `docs/archive/CORNER_RADIUS_SPEC.md`
- **圆角规范 (AI 强制建议)**：
    - **桌面组件根容器**：必须且仅能使用 `{DynamicResource DesignCornerRadiusComponent}`。
    - **内部元素**：必须根据嵌套层级使用 `DesignCornerRadiusSm/Md/Lg` 等 Token，严禁硬编码像素值。
    - **禁止修改系数**：严禁在圆角资源上乘以任何 `scale` 变量，圆角现在由全局样式固定控制。
- 设置页相关改动通常同时落在 `Views/`、`ViewModels/`、`Services/` 和 `.trae/specs/`
- UI 启动与窗口生命周期主线在 `Program.cs` 和 `App.axaml.cs`
- **headless 里取真像素要三件事同时成立**，缺一件就退回"帧是 null / 整幅透明 / 逐次不一致"：
  `UseHeadlessDrawing = false`、显式 `.UseSkia()`（关掉桩绘制后 Skia 的后端服务不会自动注册，
  实测表现为启动即抛 `Unable to locate 'Avalonia.Platform.IFontManagerImpl'`），
  以及采集前泵一次 `AvaloniaHeadlessPlatform.ForceRenderTimerTick()`。
  帧缓冲实测是 RGBA8888（红像素 = `[255,0,0,255]`），逐行按 `RowBytes` 拷。
  入口在 `tests/LanMountainDesktop.Tests/Visual/VisualTestApp.cs`，判据在
  `VisualTestAppHarnessTests.CapturedFrame_*`：一条测管线（红块压在蓝底上，两区各是各的颜色），
  一条测样式真的落到画面（**同一个位置贴类 vs 不贴类两帧必须不同**）。
  为什么不是"和背景比颜色"：实测发现不贴类的 `Border` 画出来也不是底板色（主题对 `Border` 有默认外观），
  所以"与底板不同"这种判据在类名拼错时照样绿——变异验证抓到过。
  取像素这几条把整套闸门从 1m46s–3m59s 的区间推到 3m06s/4m17s 两次；区间本来就宽，
  不足以断定是渲染的开销。真在意就把它们拆进独立的 visual 测试工程，别为此关掉桩绘制。
- **样式类普查的边界要按实测读**：`tests/.../Visual/BorderStyleClassPixelTests.cs` 对"App 级样式字典里
  设过 Background 的 Border 类"逐个比对贴类/不贴类两帧（当前 5 个：`glass-panel` 与 `surface-*` 四档）。
  它只声称"这个类改没改画面"：样式常一并设 `Opacity` / `BoxShadow` / `CornerRadius`，所以**把主题资源注册
  整个摘掉它照样绿**（实测），别拿它当"画刷注册得上"的证据——那条键断言加过、实测不红，属于假守卫，已删。
  "有人要某个 `Adaptive*` 键、没人注册"归 `CapabilityEntryPointTests` 的键覆盖探针管。
  普查锚点是"设过 Background"，删 setter 会让类从普查里静默消失，所以另配一条覆盖面下限守卫兜住
  （实测删掉一个 setter：它红并点名少了哪个类）。
- **另一半在文件作用域**：`<Window.Styles>` / `<UserControl.Styles>` 里设过 Background 的 Border 类
  App 级普查根本看不见（2026-09-22 数出来：App 字典 5 个、文件级 9 个）。
  `tests/.../Visual/FileScopedBorderClassPixelTests.cs` 按**真实例**判其中 4 个
  （`music-progress-track` / `music-progress-fill` / `notification-card` / `about-hero-card`）：
  造出拥有者、贴主题、走一次布局，要求带这个类的 `Border` 解析出的 `Background` 非空、非 Transparent、不透明度 > 0。
  **这里不比两帧像素**——真实例的几何取决于数据（进度条宽度、列表有没有项），像素判据会造出偶发红灯。
  剩下 5 个（编辑器 3 个 + 主窗体弹层 2 个）在同一个文件的 `NotCoveredYet` 里各带**实测到的拦路原因**
  （把 `ComponentEditorThemeResources.axaml` 单独 StyleInclude 进裸窗口会抛
  `KeyNotFoundException: Static resource 'MaterialRadioButton' not found`——它依赖 Material 主题），
  账本测试保证"类改名/删掉/悄悄从名单里掉出去"都是红灯（变异验过：改一个类名 → 同时报"没登记的新类"
  与"已判名单里磁盘上没有的"，并且那行 theory 报"找不到带该类的元素"）。
  另记一条实测：给这个测试**故意不贴窗口级主题资源**照样全绿——画刷在底座注册的应用级资源表里就找得到，
  所以它声称的是"样式落得上 + 应用级注册在"，别当成"窗口级注册也验证了"。

### AirApp

- SDK 公共 API 以 `airapp/LanMountainDesktop.AirAppSdk/` 为准
- 共享契约以 `core/LanMountainDesktop.Core/` 为准
- market 数据来源默认是兄弟仓库 `..\\LanAirApp`
- 迁移或 breaking change 优先同步 `docs/AIRAPP_SDK_V1_MIGRATION.md`
- 统一用 AirApp 措辞：新增的类型、目录、设置键、日志文案不要再引入 `plugin` / `Plugin`
- 改名前先查 `docs/ai/NAMING_AND_FROZEN_IDENTIFIERS.md`：其中第 3 节是跨进程/跨仓协议冻结项，第 2 节是需要一次性迁移器的本地数据标识符，两者都不能当作"漏改"直接重命名
- **清单 id 是唯一真源**：`airapp.json` 的 `components[].id` 必须与 `AddAirAppComponent` 注册的 `ComponentId` 逐字一致，宿主加载时据此校验并**拒载**（`AirAppLoader.ValidateManifestComponentContract`）。添加面板按清单列、创建控件按注册 id 找，两边漂移的症状是"面板里有、点下去没反应"且原本不报错（LanWord 实测踩过）。注册了却没声明只警告，不拒载
- **"从包里挑 airapp.json"只认两处**：宿主侧一律走 `AirApps/AirAppPackageReader.ReadManifest`（返回完整的 SDK 清单），
  打包/安装期只要 id/name/version 时走 Core 的 `AirAppPackageManifestReader`。2026-09-21 收口前这个动作有 6 份实现
  （宿主 4 份 + 启动器 1 份 + Core 1 份），启动器那份还自带一个只有 5 个属性的 `AirAppManifest` 模型，
  于是同一个包"装得进去、宿主不认"，用户看到的是装完没东西出来；现在启动器复用 Core 的读取器，缺 id/name 当场安装失败。
  守卫 `SourceIntegrityTests.AirAppPackageManifestReading_LivesInExactlyOnePlacePerBinary`
  （也拦第二份 `AirAppManifest` 模型声明）。要读松散目录里的清单（开发态）请继续直接 `AirAppManifest.Load(path)`，
  那条不在"开包"这一族里。

### 设置与主题

- 设置持久化和 scope 变化优先检查 `airapp/LanMountainDesktop.AirAppSdk/`（合并自 `LanMountainDesktop.Settings.Core`）
- 外观、圆角、主题资源优先检查 `airapp/LanMountainDesktop.AirAppSdk/`（合并自 `LanMountainDesktop.Appearance`）与专题规范
- **圆角统一**：桌面组件（Widget）必须统一使用动态资源 `DesignCornerRadiusComponent`。严禁在组件根容器使用硬编码数值或非组件级令牌（如 `Xs`, `Md` 等），以确保全局圆角缩放设置能正确应用到所有组件。

### 死代码清理

判"零引用 = 死码"之前，先记住类名级 grep 会漏掉四类真实引用（本仓库都实测踩过）：

- **扩展方法**：调用点只写方法名（`.WithArgument(...)`、`ResolveCornerRadius(...)`），永远不出现所在类名。
- **同文件其他类型**：一个文件里删"死类型"时，同文件的活类型会被一起带走（`ObservableHelper.cs` 里的 `ActionObserver<T>`）。整文件删除只在该文件只有一个类型时安全。
- **反射挂载**：Harmony 补丁（`Platform/Windows/Patches/**`）、`[ComImport]` + CLSID 的 COM 互操作、`JsonSerializerContext` 源生成类型。
- **已发布包的表面**：`core/` 与 `AirAppSdk` 的 public 类型在本仓库零引用不代表没人用——要按**方法名**去同级 AirApp 仓库查（实测 `ResolveCornerRadius` 命中 8 个外部文件）。

批量删完必须 `dotnet build`（编译器只是最低那道闸），再跑回归闸门：
`dotnet test LanMountainDesktop.slnx --no-build --filter "Category!=EcosystemProbe"`。
**但编译器不是够用的闸**：它不在乎换行、也不把重复 using 当错误，于是两类只有按行看才暴露的伤会一路绿过去
——把前后两条声明并成一行（守卫 `MemberDeclarations_OwnTheirOwnLine`，2026-09-21 两次批量脚本共留下 12 处，
2026-09-22 把口径从"声明"扩到"语句"后又抓到 1 处方法体开括号与首条 `if` 并线），
以及同一条 using 写两遍（CS0105 只是警告，守卫 `UsingDirectives_AreNotDeclaredTwiceInAFile`，
`plugin → airapp` 那次留下 28 条 / 21 个文件）。批量脚本改完要把这两条也跑到 0，而不是只确认能编译。
注释吞语句、IDE0051 未使用成员这两类由 `tests/LanMountainDesktop.Tests/SourceIntegrityTests.cs` 与
`dotnet format style --diagnostics IDE0051 --severity hidden` 守；新增死码规则时请同步扩这两处。

**只写不读成员（IDE0052）**：字段被赋值却从不读取，等于"看着接了线其实没接"，是本项目最密集的假实现来源。逐项目量一遍（改项目路径即可）：

```
dotnet format style <csproj> --diagnostics IDE0051 IDE0052 --severity hidden --verify-no-changes
```

2026-09-20 基线：宿主 43 → **0**（已并入 `.github/workflows/code-quality.yml` 的 Check unused members 闸门），
Core / Platform / AirAppSdk / AirAppRuntime / tests / installer 均为 0；剩 Launcher 5、AirAppHost 1、
AirAppDevServer 1，都是"UI 收了输入但不落地"或"CLI 承诺了没实现"的待决项，不要当噪声删掉。
2026-09-22 把这条闸门从"只有宿主"扩到**进闸的 11 个二进制**（宿主 + Core + 启动器 + AirApp 的
Sdk/Runtime/Host/DevServer/Template + 安装器 + Platform + Mobile，逐个量过 IDE0051+IDE0052 全为 0；
`Mobile.Android` 要 android workload，本地也量到 0，但不进 CI 循环），扩的同时清了 Launcher 那 5 处里的 4 处：

- `OobeStateService._stateDirectory`、`LoadingDetailsWindow._startTime` —— 纯解构/赋值剩余，删。
- `OobeWindow._isDebugMode` 与 `OobeWindow.SetDebugMode(bool)` —— 全仓**没人调用**这个 setter
  （Splash 与 ErrorWindow 上的同名方法有调用，OOBE 这个没有），是接了一半的线，连字段一起删。
- `OobeWindow._selectedMonetSource` —— **留着并 `#pragma warning disable IDE0052` 带说明**：向导第三步的
  莫奈单选框是真的（点了会换单选框状态），但没人读它，接上它等于替产品决定"OOBE 到底给不给莫奈"。
  同理 `ClockAirAppView._options`（宿主打开时钟时传 `AirAppLaunchOptions`，视图收下从不读）也留着带说明。
  这两处是"入口有效果没有"的实物证据，删掉闸门会绿，但就再没人看得出这里本来打算有这个能力。

删这类字段时注意：赋值常发生在接口实现里（`SetComponentPlacementContext`、`SetDesktopPageContext`），
先确认该接口是否只有这一个消费者，是的话连同接口实现一起摘掉，别只留一个空方法。

**零使用类型棘轮**：`tests/.../ZeroUseTypeRatchetTests.cs` 把探针固化成测试——扫全仓声明的类型名（约 1200 个），
数出现次数并减掉"自身文本"，为 0 即零使用。
2026-09-22 把口径从"名字在仓库里出现过"改成**"产品可达"**：用量只算生产目录（core/desktop/airapp/install/mobile/
platform/packaging/scripts）加 `tests/.../ApprovalFiles`（已发布 SDK 公共面的契约快照）；
`tests` 与 `docs` 不再算使用者——**只有测试在调 = 没接线，历史计划文档提过一句 = 更不是使用**。
这条改动直接挖出了两个安全能力：`ManifestSignatureVerifier`（清单 RSA-PSS 校验，内置公钥还是空串）与
`AuthenticodeVerifier`（WinVerifyTrust + 环境变量开关）在生产里**零调用点**，只有 `InstallerSecurityTests` 在用——
已按"等用户拍板"记进名单，别当死码删，也别擅自接线（改的是安装流程的安全语义）。
反向判据同一条测试里也有：名单中的条目若被真引用了（假欠账）同样红；把语料放回旧口径，
红名恰好就是这两个校验器（变异验过）。名单文件自己不算使用者（`*RatchetTests.cs` 已从语料排除）。
名单**只许缩短**：新引入一个零使用类型直接红；条目对应的类型没了也会红（防名单烂掉）。
**"名字只出现在字符串字面量里"不算使用**（2026-09-22 补进 `IsSelfText` 的规则，名单 15 → **16**）：
`LoadingTimeoutHandler` 整个类没有任何构造点，却因为自家 6 处 `AppLogger.Info("LoadingTimeoutHandler", …)`
把日志分类名写成同名而躲过了这条棘轮——一个写完从没被 new 的类（超时监控 + 重试计数）就是这么藏住的，
现登记在名单里等拍板（待办 G1-AZ）。补这条规则后**新报出来的只有它一个**，另两个方向都做了变异验证：
同名只出现在字符串里的临时类型必须报，同文件里真 `new` 出来的不许报。
按行剥字符串字面量，跨行的 raw/verbatim 串剥不干净，方向是漏报不是误报。
2026-09-20 基线：17 → **13**，删掉的 4 个是 `ShutdownCoordinator` / `SettingsWindowHost` /
`DesktopStartupCoordinator`（三个纯穿透壳，被包的动作早已由真实路径直接调用）和 `ObservableHelper<T>`；
2026-09-22 换口径后 13 → **15**（新增的两个就是那两个校验器），同批清掉一个真死码
`PlondsManifestSelector.SelectHighestVersion`（单数包装只有测试在用，生产走 `SelectHighestVersionCandidates`）。
名单里三类要分清：扩展方法宿主与 Harmony/COM/Android 反射入口是**探针假阳性**（调用点不出现类型名），
泛型实参/属性类型也算同一种假阳性（`PlondsClientChangedFileEntry`、`InstallerPlondsChangedFileEntry`
被 `TryGetValue` 摸到，名字不出现在调用点——"基列表"自述规则已改成只认行首分隔符，否则这类会被误判成死码），
其余标着"等用户拍板"的是**已实现但没入口**的能力（分离式组件库窗口、考勤整模块、
主备双清单组合器、Core 的第二套 IPC 装配入口），删它们等于删产品决定，必须先问。
按文件删时要先确认该文件只声明这一个类型——`ObservableHelper.cs` 里还住着一个在用的 `ActionObserver<T>`。

**组件可达性棘轮**：`tests/LanMountainDesktop.Tests/DesktopComponentReachabilityTests.cs` 钉住四件事——
声明 `AllowDesktopPlacement` 必须真有运行期注册、运行期注册必须真有允许摆放的定义、
库里每个条目必须有在 zh/en/ja/ko 都能取到文案的 `DisplayNameLocalizationKey`、
每个条目都要能在 LibraryPreview 下被创建并布局出非零尺寸（`DesktopBrowser` 因 WebView2 会改线程套间而免检，
免检清单本身也有守卫防烂掉）。`LocalizationParityRatchetTests` 以 zh-CN 为源语言记着
en/ja/ko 还缺 25/313/275 条，只许降不许升；补翻译就把数字改小。
组件的 `DisplayName` 必须是语言中立的兜底文案（历史上 5 个写死中文，已改；守卫会拦新增）。

**词表一键一条、键不许重复**：`desktop/LanMountainDesktop/Localization/*.json` 是扁平点号键表，
运行期用 `JsonSerializer.Deserialize<Dictionary<string, string>>` 读——重复键不报错，后一份覆盖前一份，
所以写在前面那条永远读不到（改它的人以为改了）。2026-09-21 实测四份词表共 182 条这种被覆盖的键
（en-US 独占 103 条，整页 `settings.update.*` 被粘了两遍），其中 zh/ja/ko 的
`settings.update.preferences_description` 两份内容不同，界面上一直显示的是后一份；同批还删掉 359 条
"产品里拼不出来"的死键（`AcceptedUnreferencedKeyCount` 已收到 0）。守卫
`LocalizationParityRatchetTests.LocaleFile_DeclaresEachKeyOnceOnItsOwnLine` 两条都管：重复声明、
以及一行塞两个键（按行取键的清理脚本与 grep 会静默漏掉后一个）。
判"死没死"必须把内插串洞里的调用算进去——`$"{L("rss.refresh_failed", "…")}"` 只在洞里出现，
漏看时实测误删了 3 条活键（第二方向 `EveryKeyRequestedFromCode_ExistsInZhCn` 当场变红把它抓回）。

**"有实现没入口"守卫**：`tests/LanMountainDesktop.Tests/CapabilityEntryPointTests.cs` 两条，都读磁盘上的源码文本，
不需要重新构建就能跑。一是每个 `[RelayCommand]` 生成的命令名必须在仓库里被绑到（`.axaml` 的 `Command=` 或代码引用），
二是宿主 `.axaml` 里每个 `Button`/`ToggleButton` 要么有 `Command`/`Click`/`x:Name`，要么带 `Flyout`——
后者是"看得见点不动"那一类宿主 UI 缺陷。2026-09-21 基线：38 条命令全有绑定、185 个按钮 0 个失灵，
所以两条都不留名单，新增即红。设置页可达性另一半在 `SettingsWindow.axaml.cs:113`（导航只列非 `HideDefault` 的页）
与 `SettingsSearchService` 读 `GroupId` 的路径上，目前没有 hidden 又没人深链的页。

**黑夜模式判定只认一处**：组件要判断当前是不是黑夜，用 `Views/Components/ComponentThemeMode.cs`
（`ResolveIsNight(this, fallbackToNightWhenSurfaceUnknown: …)`），不要再抄一份。此前 19 个组件各抄一遍，
且兜底互相矛盾（11 个当"夜"、7 个当"昼"、MusicControlWidget 回退应用级主题档），同一个异常状态不同组件会选反。
兜底不同的确是既有事实，所以参数化保留而不是统一改死；唯一的例外白名单是 `MusicControlWidget`，
`SourceIntegrityTests.NightModeResolution_LivesInExactlyOnePlace` 会拦新抄，多一个例外就得先改那张名单。

**亮度/对比度算式只认一处**：WCAG 相对亮度、α 合成、最低对比度一律走 `Theme/ColorMath.cs`
（`RelativeLuminance` / `ToOpaqueAgainst` / `MinContrastRatio` / `ContrastRatio`）。此前 15 个组件各自复制了
`CalculateRelativeLuminance`、6 个学习组件又各自复制了另一套同名不同实现的算式，且两份 sRGB 阈值不一样
（0.03928 与 0.04045 并存），组件之间的取色判定因此可能悄悄不一致。
`SourceIntegrityTests.WcagColorMath_LivesInExactlyOnePlace` 会拦新的复制实现。

**打开外部链接只认一处**：组件要点开一条网页链接，一律 `Helpers/ExternalLinkLauncher.cs`
（`TryOpen(url)` 打开、`NormalizeHttpUrl(url)` 只做归一化）。此前 http/https 归一化被逐字抄了 6 份
（含 `RecommendationDataService`），shell 打开又抄了 9 份，其中 3 份（`DailyNewsView`、`JuyaNewsWidget`、
`AirAppCatalogMarkdownHelper`）连 scheme 校验都没有——RSS 与第三方 AirApp README 里的串会被直接喂给
ShellExecute，`file://` 或 `ms-msp:` 这类都能起进程。打开文件/文件夹/本地程序
（`FileManager`、`Shortcut`、`ZhiJiaoHub`）语义不同，仍走各自的代码。
`SourceIntegrityTests.ExternalLinkHandling_LivesInExactlyOnePlace` 同时拦两种复发形态：第二份归一化/打开实现，
和把 url 变量直接绑上 `ProcessStartInfo.FileName` 的绕过。

**界面语言口径只认一处**：默认语言写 `LocalizationService.DefaultLanguageCode`，读当前语言走
`ResolveLanguageCode(() => 快照.LanguageCode)`（内部含"读盘失败退回默认语言"的兜底），判断是不是中文走
`IsChineseLanguage(code)`。此前宿主里有 41 处 `_languageCode = "zh-CN"` 初值/兜底、12 份各自复制的
try/catch 读取、8 份手写的中文比较。`CultureInfo.GetCultureInfo("zh-CN")` 这类区域性名不在禁止之列
（`WindowsStartMenuService` 拿的是中文排序规则，跟默认语言无关）。守卫只管宿主自己的二进制：
AirApp 子进程的语言兜底在 `AirAppSdk` 的 `AirAppLocalizer` 里，它引用不到宿主，而 SDK 公开面动一次就要重发一次包。
`SourceIntegrityTests.DefaultLanguagePolicy_LivesInExactlyOnePlace` 会拦第 42 处。

**学习监测租约只认一处**：组件该不该持有租约，走 `StudyMonitoringLease.Sync(ref _monitoringLease, 协调器, 开关, 已挂载, 在当前页)`，
释放走 `StudyMonitoringLease.Release(ref ...)`（两者都在 `Services/StudyAnalyticsMonitoringLeaseCoordinator.cs`）。
此前 7 个学习组件各抄了同一段"开关+挂载+当前页"判定，另有 11 处手写的 Dispose-再置空。
协调器本身是 `CreateDefault()` 返回的共享单例，所以租约计数是全局的——组件里自己 `AcquireLease()` 会把计数加歪。
`SourceIntegrityTests.MonitoringLeasePolicy_LivesInExactlyOnePlace` 拦第三种写法。

**学习面板取色只认一处**：面板底色、深/浅衬底、可采样替身色一律走 `Views/Components/StudyPanelPalette.cs`
（`Resolve(this, RootBorder.Background)` / `Dark` / `Light` / `BuildSamples`）。此前底色求法抄了 7 份、
衬底常量抄了 7 份、采样色抄了 6 份，三种状态徽章底色（会话绿/实时蓝/停用灰）以 `Color.Parse` 字面量写了 15 遍
（现在是 `SuccessBadge` / `RealtimeBadge` / `DisabledBadge`）。学习历史面板那份"混合更弱、少一个采样"的既有分歧作为
`BuildSoftSamples` 显式留在同一个文件里——它是排版口径，不是复制漂移。守卫 `StudyPanelPalette_LivesInExactlyOnePlace`。
同一处还钉住了三种状态徽章底色（会话绿 / 实时蓝 / 停用灰，此前 `Color.Parse` 字面量写了 15 遍），
以及"徽章压在玻璃上"的那套算法：`ResolveBadgeColor`（面板亮度分三档不透明度，状态徽章那档更实写成
`StatusBadgeTiers`）、`BadgeBorderBrush`、`ResolveBadgeForeground`。组件里只留 4 行赋值（各自的控件与调色板不同）。
阈值 0.58 这一档徽章和内嵌卡片共用，判档走 `IsBrightPanel(panelColor)`，不要再各写一遍。

**快照事件的订/解只认一处**：学习组件订 `IStudyAnalyticsService.SnapshotUpdated` 走
`Services/StudySnapshotSubscription.cs`（`Subscribe(ref _isSubscribed, service, _renderGate.HandleSnapshotUpdated)` 与 `Unsubscribe`）。
"只订一次、卸载或销毁时必须解掉"这对判断此前被 8 个组件各抄两遍、一共 21 处；解订那一半漏抄就是
一个组件已经从桌面上摘掉却还在被回调的事件泄漏。`_isSubscribed` 现在只由该助手读写，
守卫 `StudySnapshotSubscription_LivesInExactlyOnePlace`。
2026-09-22 同族再收一层：回调本身（"不可见就别排队"那 9 行）也在 8 个组件里各抄了一份（逐字相同，
只有 `StudyNoiseDistributionWidget` 多一句 `_ = sender;`），现在归 `Views/Components/StudySnapshotRenderGate.cs`
的 `HandleSnapshotUpdated` ——`canRender` 本来就是传进这个门的，回调 shape 没有理由留在组件里；
同一条守卫现在禁组件再声明 `OnStudySnapshotUpdated`。行为由 `StudyComponentRenderingTests` 两条钉住
（不可见时不排队、可见时排队；把 `_canRender()` 判断去掉后第一条立刻红）。

**对比度取色只认一处**：在玻璃面板上从候选色里挑一个读得清的前景，一律
`Theme/AdaptiveBrushFactory.cs`（`Create(...)` 出画刷、`Pick(...)` 只出颜色，后者可直接单测）。
策略是"取第一个在**所有**背景样本上都达标的候选；一个都不达标就退而取对比度最高的那个"，
不许因达不到阈值而放弃对比度。此前 7 个学习组件各抄了这 30 行，其中学习历史面板是另写的单遍循环变体
（候选表为空时它会越界，现在统一返回白色）。守卫 `AdaptiveContrastPick_LivesInExactlyOnePlace`，
语义由 `AdaptiveBrushFactoryTests` 钉住。

**组件画笔与文字测量只认两处**：`Views/Components/ComponentPaint.cs`（`CreateBrush` 收十六进制串也收 `Color`、
`CreateLinearGradientBrush` 出对角渐变）与 `ComponentTypography.cs`（`MeasureTextSize` 用一次性 TextBlock 探针量排版、
`ToVariableWeight` 把可变字重夹到 1..1000、`Lerp` 与 `LerpClamped`）。此前 20 个组件把这几个四行助手各抄了一份，
合计 30 个私有实现（`CreateBrush` 11、`ToVariableWeight` 9、`Lerp` 9、`MeasureTextSize` 4、渐变 3）。
插值故意留两个名字：日历/时钟传的是已算好的比例（不夹紧），学习组件传的是可能过冲的进度（夹在 0..1），
两种语义都是既成的，别再统一。守卫 `WidgetPaintAndTextHelpers_LiveInExactlyOnePlace`。

**组件外框的玻璃面板只认一处**：切"透明背景"这一档走 `Views/Components/ComponentChromePanel.Apply(RootBorder, 是否透明)`，
样式类名用它的 `GlassPanelClass` 常量（C# 侧此前 11 处各写 `"glass-panel"` 字面量，3 个组件还把"摘类 + 清四个属性"
那段序列抄了 3 份——两边不对称写就会留下"关了透明还留着阴影"的残留视觉）。`.axaml` 里的选择器仍是字面量。
守卫 `GlassPanelClassLiteral_OnlyLivesInComponentChromePanel`。

**第三方 JSON 的路径读法只认一处**：`Services/Json/JsonNodeReader.cs`（`TryGetNode` / `ReadString` / `ReadInt` /
`ReadDouble` / `ReadBool`）。此前 `HolidayCalendarService`、`RecommendationDataService`、`XiaomiWeatherService`
各抄了一份私有实现（同体副本合计 11 个声明、约 111 行），同一份云端返回在三个服务里可能被读成不一样的值。
调用点用 `using static` 接上，不改几百处调用。宽松读法是刻意的（这几家接口同一字段有时给数字有时给字符串）：
数字/布尔都接受字符串形式，读不出来返回 null 而不是抛。
两处故意不并：`RecommendationDataService.ReadBoolean`（读不出来当 false 的严格版）和 Launcher 自己的 `ReadBool`
（另一个二进制、签名不同）。守卫 `JsonPathReaders_LiveInExactlyOnePlace`。

**IDE0051 有一个盲区要注意**：`internal` 静态类上的 `public` 成员不会被它判为未使用。
`ComponentChromeCornerRadiusHelper` 上就有 6 个零调用成员（`Apply`/`Mini`/`Medium`/`Large`/`SafeRadius`/`Scale`，
其中 `Mini` 与 `Micro` 同体、`Scale` 与 `SafeValue` 同体、`SafeRadius` 与 `ScaleRadius` 同体），已删。
量这类成员要靠引用计数（`ComponentChromeCornerRadiusHelper\.成员\(` 全仓搜），别指望 IDE。
这条轴已经做成棘轮：`tests/.../ZeroUseMemberRatchetTests.cs` 扫宿主工程所有静态类上的 public/internal 静态方法，
数方法名出现次数（减掉声明行与注释），为 0 即零引用；名单只许缩短、每条必须写理由。
2026-09-22 与类型棘轮同步换口径：只算生产目录 + `ApprovalFiles`（一份 `docs/archive/` 的历史计划里抄过
`GetScreenInfo()` 的代码块，旧口径把它算成了使用者），`*ForTests` 结尾的测试专用入口按约定豁免，
并加了"名单条目若已被真引用就算假欠账"的反向判据。
基线：宿主首量 15 个零引用静态成员 → 已删 8 个 → 名单剩 7 条。删掉的是 `AppRestartService` 的
legacy 转发壳、一条**会绕过关机闸门直接重启**的第二真源、两个纯别名，
以及三个几何包装（`ComponentPlacementRules.ClampToGrid`、`DesktopPlacementMath.GetGridBounds`/`GetSnappedCellRect`，
live 路径各自走 `TryGetSnappedCell`+`GetCellRect`）和一个 `[Obsolete]` 别名（`FromAppearanceSnapshot` →
`FromCompatibilityAppearanceSnapshot`）。名单里 7 条按"待判"或"实现了没入口"记着，
包括 `XiaomiWeatherCodeMapper.ResolveBucket`——它背后那 11 档 `WeatherConditionBucket` 在宿主内除本文件外零引用，
整个"按天气状况分档"的维度从没接进 UI，属产品决定不是死码；
`CanPlaceInStatusBar` 也留着，它是"状态栏组件必须 1 格高"这条设计意图的唯一证据（live 走的注册表版不带这条）。

**主题资源键名只认一处**：C# 里读/写 `Adaptive*` 主题资源一律用 `Theme/ThemeResourceKeys.cs` 的常量，
不要再写字面量。注册方（`GlassEffectService`、`ThemeColorSystemService`）与读取方（组件、`MainWindow` 各 partial）
此前一共把 56 个键名手写了 113 遍，拼错一个字母不报错、只会让那块 UI 静默失色。
`.axaml` 里的 `StaticResource` 仍是字符串（XAML 取不到 C# 常量），这条约束只管 C# 侧。
守卫 `ThemeResourceKeyLiterals_OnlyLiveInThemeResourceKeys`。

**主题资源的读取也只认一处**：取一个 `Adaptive*` 画笔/令牌一律走 `Theme/AdaptiveTokens.cs`
（`TryGet<T>` / `Brush`），不要再自己调 `TryFindResource(` 或 `TryGetResource(`。
这两个底层 API 的差别本身就是缺陷来源：`TryGetResource` 只看你递进去的那一本字典
（实测曾有 5 份私有实现分别在问 MainWindow 自己、Application、组件自身三条不同作用域），
于是同一个键在 A 处取到、B 处取不到；`TryFindResource` 作用域对，但取不到时各文件自己兜一个值。
现在作用域只有一份，**兜底值仍由调用方给**（各块 UI 的兜底口径本来就不一样，别去统一它们）。
守卫 `ThemeResourceReads_GoThroughAdaptiveTokens`；"注册了什么"另有一条
`EveryAdaptiveResourceRequested_IsAlsoRegistered`（它实测抓到预览卡片没底色：
`AdaptiveCardBackgroundBrush` 有人读、没人注册，而兜底是 `Brushes.Transparent`）。

**PLONDS 线上协议字面量**：宿主与服务端没有共享类型，全靠字符串对齐，所以包名
（`Files.zip` / `files.zip` / `changed.zip` / `files-windows-x64.zip` / `PLONDS.json`）与动作值
（`add` / `replace` / `reuse` / `delete`）只许写在 `Services/Plonds/PlondsWireFormat.cs` 一处，
`SourceIntegrityTests.PlondsWireLiterals_OnlyLiveInPlondsWireFormat` 会拦住第二处。
宿主读清单时必须同时认 filemap 的 `action`/`sha256` 和分布元数据的 `op`/`contentHash`
（服务端样例里全是后者），契约由 `PlondsDistributionContractTests` 拿 `sample-data` 真实文件钉住。

**整体替换一个文件只认一处**：设置、缓存、清单、白板笔记、隐私同意书这类"把新内容换成旧文件"的落盘，
一律走 `core/LanMountainDesktop.Core/IO/AtomicFileWriter.cs`（文本 `WriteText`、流 `WriteStreamAsync`），不要再手搓
"写 `.tmp` + `File.Move`"。收口前宿主里有 10 处各写一份，启动器与 Core 又各有自己的版本，差异里有两个会真实咬人的：
目标被资源管理器/杀毒瞬时锁住时没人重试（症状就是用户说的"设置没存上"，而 `FileOperationRetryHelper`
本来就是为这种情况写的却没被用上），以及 Move 失败后 `.tmp` 留在 AppData 里不清理。
`Encoding.UTF8` 那个重载只为磁盘上已经带 BOM 的旧文件保留（白板笔记），新代码别用。
helper 住在 Core 是因为写同一批磁盘文件的是三个进程（宿主、首启向导 Launcher、安装器），
跨二进制没有共享日志器，所以重试告警走 `FileOperationRetryHelper.FailureNotice`，各入口在启动时接自己的日志。
守卫 `SourceIntegrityTests.AtomicFileReplacement_LivesInExactlyOnePlace` 现已覆盖全部二进制；
免检的只剩"拿 `.tmp` 试这块盘能不能写"的可写性探针（`AppLogger`、`AirAppInstallTargetAccess`、
`.write-test-` 开头的文件名）。"把用户选中的图片原子搬成受管文件名"走 `PlaceFile`（换壁纸已经用上），
要写内容用 `WriteText` / `WriteStreamAsync`。

**"尽力删掉，删不掉要出声"只认一处**：清理缓存、卸载残留、删安装包这类"删不动也不该让主流程崩"的删除，
一律走 `core/LanMountainDesktop.Core/IO/FileOperationRetryHelper.cs` 的 `TryDeleteFile` / `TryDeleteDirectory`
（守卫 `SourceIntegrityTests.BestEffortFileDeletion_LivesInExactlyOnePlace`，只禁声明、调用点各带自己的 category）。
收口前这两个方法在宿主/启动器/安装器三个二进制里抄了 13 份，而且已经漂开：7 份文件删除里只有 2 份会先
`File.SetAttributes(Normal)`，缺这一步的那 5 份删只读文件会**静默失败**（2026-09-22 实测：把清属性那行删掉，
`BestEffortDeletionTests.ReadOnlyFile_IsDeletedAfterClearingAttributes` 立刻红，返回 false 而不抛），
症状就是"跑完卸载/AppData 里还留着文件"；13 份里只有 1 份记日志，其余全是空的 `catch`，所以这类残留从来查不到原因。
现在口径取两边并集：先清属性再删，失败送 `FailureNotice`（宿主与启动器已接日志器，安装器没接＝行为不变）。
`recursive` 参数保留各调用点原语义——`DataStorageService` 那处要的就是"只删空目录"，改成递归会连带删掉用户数据，
`BestEffortDeletionTests.NonEmptyDirectory_WithRecursiveFalse_IsKeptAndReported` 钉住它。

**取消并释放一次性 CTS 只认一处**：`Cancel()` + `Dispose()` 这套收尾动作走 `core/LanMountainDesktop.Core/Threading/CancellationHelper.cs`
（字段版 `CancelAndDispose(ref _cts)` 会顺手把字段置空，已摘下来的源用实例版），不要再手写相邻两行。
收口前这套动作在宿主里有 27 份复制：10 个组件各抄了一份逐字相同的 `CancelRefreshRequest()`（各 7 行正文），
另有 17 处把 `X?.Cancel(); X?.Dispose();` 两行写在调用点（10 处刷新路径、`HolidayCalendar` 2、`StickyNote` 2、
`TextCapsule` 1、`MusicControlViewModel` 2）。
守卫 `SourceIntegrityTests.CancelAndDisposeRitual_LivesInExactlyOnePlace` 两种形态都拦（声明 + 相邻 Cancel/Dispose 对），
顺序之所以是"先摘字段再取消"：对已 Dispose 的源再 `Cancel()` 会抛 `ObjectDisposedException`。
**同一把尺子量出来的欠账（逐处读到底之后的真值）**：把窗口放宽到"Cancel 后 5 行内没有 Dispose"再扫，
收口前命中 22 处；修掉宿主高频刷新的 10 处（`WeatherWidgetBase` 2 处——那里的源带 12 秒超时，不释放等于每次刷新
留一个还挂着的定时器；`ZhiJiaoHubWidget` 7 处；`DataSettingsPageViewModel` 1 处）后剩 12 处。
**这 12 处逐处查证后的结论是：只有 3 处真的从不释放，已修**
（`LinuxMprisMusicSessionProvider.Dispose`、`LoadingStateManager.Dispose`、`PostHogUsageTelemetryService.Dispose`——
最后一处的释放刻意排在 `_client.Dispose()` 之后，否则 client 收尾那趟 flush 会在还有注册时抛 `ObjectDisposedException`，
症状＝最后一次上报静默丢失）。其余 9 处是"取消一个仍在飞的操作、释放另有其人"：
`LinuxNotificationListener`(365)、`NotificationListenerService`(401)、`WindowsNotificationListener`(477)、
`UpdateProgressViewModel`(79)、启动器 `LauncherCoordinatorIpcServer`(79)、`UpdateOrchestrator`(469/508)，
以及安装器 `MainWindowViewModel` 的 2 处——**已修**：VM 现在实现 `IDisposable`（取消＋释放两个源），
`installer/Views/MainWindow.axaml.cs` 的 `OnClosed` 里收尾；判据是 `InstallerUxTests.WindowClose_CancelsAndReleasesTheInFlightCheckSource`
（在飞的检查关窗后既被取消、源也真的被释放，把 `Dispose` 里的释放去掉这条就红）。
**顺带量到但没动的一处**：`MainWindowViewModel` 的 232/371 是 `_checkCts?.Dispose()` / `_installCts?.Dispose()`——
只 Dispose 不 Cancel，而 Dispose 一个还有注册的源只会悄悄摘掉注册，等于让上一次检查/安装变成"取消不了"继续跑完，
完成时还会把 `TargetVersion`/`StatusText`/`IsCheckingUpdate` 写回去（与新的一次竞态）。
改成 `CancelAndDispose` 语义上更对，但会让上一次操作立刻走"版本检查已取消。"那条提示，
UI 文案要不要跟着变是产品判断，先登记不擅自动。
教训写在这里：**文本扫描只能当线索，不能当结论**——这条"5 行窗口"的启发式实测 12 命中里 9 个是假阳性。
`CancellationHelper` 已搬到 Core（与 `AtomicFileWriter`、`FileOperationRetryHelper` 同一先例：同一动作散在多个二进制里就住 Core），
启动器与安装器现在够得到它，守卫也据此覆盖全部二进制。

**组件与时区服务之间那对订阅/退订只认一处**：一律走 `desktop/LanMountainDesktop/Views/Components/TimeZoneServiceBinding.cs`
的 `Replace` / `Clear`（两个方法都返回新的字段值，语义与原来逐字一致：换服务时先退旧再订新，退订不刷新），
不要在组件里手写 `TimeZoneChanged += / -=`。收口前 10 个时钟/日历组件各抄了一份 Set 与一份 Clear（20 个方法体，
实测唯一差异是 `StandbyDigitalClockWidget` 把 4 行守卫压成 1 行）。为什么要收成一家：`TimeZoneService` 是应用级
长命对象、事件没有任何退订兜底，谁抄漏 `-=` 那一边，服务就替一个已经从桌面分离掉的控件一直持有整棵 visual tree。
顺带去掉一处潜在崩溃：原来 `SetTimeZoneService(null)` 会在 `+=` 那行抛 `NullReferenceException`（今天没有调用方传 null）。
**已修的真实泄漏**：组件库预览（`FusedDesktopComponentLibraryControl`、`ComponentLibraryWindow`、`MainWindow.ComponentPreviewImages`
三处）每换一次选中项就造一个预览控件、`Detach`/`Dispose` 时只停了计时器、没退订——浏览一轮组件库就往那个长命服务上
挂一串永不释放的控件。现在 `ComponentPreviewRuntimeQuiescer` 在丢预览时统一退订
（`ComponentPreviewTimeZoneReleaseTests` 用服务侧订阅数 0→1→0 钉住，把那一行注释掉立刻红）。
`FusedDesktopManagerService.CreateWidgetWindow`（662 起）那条路也接上了：组件浮窗 `DesktopWidgetWindow.OnClosing`
原本只退自己的事件、`Dispose` 子控件，现在顺手 `ClearTimeZoneService()`
（`DesktopWidgetWindowTimeZoneReleaseTests` 数同一个订阅者计数，去掉那行立刻红）。

**两个纯函数也只有家**：目录路径补末尾分隔符一律
`core/LanMountainDesktop.Core/IO/PathSeparators.cs` 的 `EnsureTrailingSeparator`（收口前 Core/宿主/启动器
三个二进制各抄一份、共 4 份，调用点 7 处；其中 `AirAppLoader` 那份多做了一步 `Path.GetFullPath`——
绝对化是调用点语义，塞进 helper 会把"补个斜杠"变成"可能抛参数异常"，现在那一步显式写在调用点）；
文本裁断（HTTP 错误体这类要拼进异常/日志的长文本）一律 `Helpers/CompactText.cs` 的 `Truncate`
（收口前 4 份、调用点 19 处；其中 GitHub 更新服务那份不带省略号，同一份报错在它那里看着像完整回复，
现已统一到"裁过必须看得出来"）。守卫 `PathAndTextPureHelpers_LiveInExactlyOnePlace` 禁再抄实现、不禁调用。

**安装根目录下那个 `.Launcher` 数据目录名只认一处**：一律用 `core/LanMountainDesktop.Core/Deployment/DeploymentLayout.cs`
的 `LauncherStateDirectoryName`，不要在 Core / 宿主 / 启动器里再抄字面量（该类注释本来就写着"禁止在任何一侧硬编码"，
2026-09-21 实测仍有 4 处各抄一份；安装器倒是用了常量）。同族另一个坑：启动器把启动诊断写进
`LocalAppData/LanMountainDesktop/.launcher/diag`，而它自己的日志与状态兜底用的是
`LocalAppData/LanMountainDesktop/Launcher/{logs,state}` —— 第三种拼法，谁也不读那个目录，已统一。
注意这是两个不同的文件夹：`{安装根}/.Launcher`（数据位置配置、日志、状态）与 `{数据根}/Launcher`（每用户兜底）。
唯一保留的字面量是 `OobeStateService` 里改名前的 `.launcher/state`（只读旧数据，与 `Launcher/state` 不是同一个目录）。
守卫 `SourceIntegrityTests.LauncherStateDirectoryName_LivesInExactlyOnePlace`。

**部署标记文件名同样只认那一处**：`.current` / `.partial` / `.destroy` 一律用 `DeploymentLayout` 的
`CurrentMarkerFileName` / `PartialMarkerFileName` / `DestroyMarkerFileName`。2026-09-22 量出来宿主与 Core 里
攒了 **35 处字面量**（`AppVersionProvider`、`PlondsPreparedPackageInstaller`、`AppDeploymentLocator`、
`DeploymentActivator`、`PlondsUpdateApplier` 五个文件），而这个类的注释从 2026-09 起就写着"禁止在任何一侧硬编码"。
**写在类注释里的规则如果没配守卫，它就只是一句愿望**——同一条注释既管住了安装器（用了常量）也管不住宿主（抄了 35 遍）。
守卫 `SourceIntegrityTests.DeploymentMarkerFileNames_LiveInExactlyOnePlace`；它另外把**发布侧**
`PenguinLogisticsOnlineNetworkDistributionSystem/src/Plonds.Core/Publishing/PayloadUtilities.cs`
的同一组字面量按字节钉住：发布工具链是另一份解决方案、不引用 Core，拿不到那三个常量，
所以这里没有引入工程引用（那是构建结构决定，等拍板），改成"两边拼写一旦不同就红并点名两边"。
这条也是哨兵：发布侧那个文件搬走或改名，守卫会直接要求把检查一起挪过去，不让它静默失效。

**界面语言码只认 `core/LanMountainDesktop.Core/Localization/LanguageCodes.cs`**（`Chinese` / `English` /
`Japanese` / `Korean` / `Default` / `Supported` / `Normalize` / `IsChinese`）。守卫
`LanguageCodes_LiveInExactlyOnePlace`，免检两处且带"免检条目失效即红"的反向检查。
收口前这张归一化表有 **4 份实现**：宿主 `LocalizationService`、启动器 `LanguagePreferenceService`
（**两份二进制**读同一个 `settings.json` 的 `LanguageCode` 字段）、宿主 `ClockAirAppTimeFormatter`、
以及 `AirAppSdk.AirAppLocalizer` 的 `en-US`/`zh-CN` 兜底；字面量共 30 处，其中 10 处在 `airapp/` 下
——只 grep `core desktop` 量不到它们，这就是守卫比 grep 强的地方。漂了不报错，
症状是"启动动画是中文、进桌面变韩文"。
两处**故意不统一**：① `WindowsStartMenuService` 的拼音 `CompareInfo` 与 `StandbyDigitalClockWidget`
的中文数字格式化要的是"简体中文这个语言本身"，不是"默认语言"，改默认语言时它们不该跟着动；
② 天气那三处判据一直是 `string.Equals(code, "en-US", OrdinalIgnoreCase)` 的**逐字**比较，
所以家另外给了不归一化的 `IsEnglishCode`，而不是让 `IsChinese` 那种归一化口径吞掉它——
`"en"` 从"按中文处理"变成"按英文处理"是对第三方接口出参的口径变更，不在这类收口的范围里。
唯一的实际行为变化是 `Normalize` 现在会 Trim：`settings.json` 里写成 `" en-US "` 时以前落回中文、
现在落回英文；这个值只有手改才会出现，取更正确的那个解释（`LanguageCodeContractTests` 里钉着）。

**取文案不许拿键名当兜底**：`GetString(语言, key, key)` 在那种语言缺键时就把标识符印到界面上。
统一走 `LocalizationService.GetStringWithSourceFallback(语言, key)`（当前语言 → 源语言 `zh-CN` → 才回键名），
或者像其余 44 处那样给一句真文案。守卫 `LocalizationLookups_NeverFallBackToTheRawKey`（种过 scratch 文件验红）。
这条是 2026-09-22 量出来的真缺陷：设置页 VM 的 `L(string key)` 是单参形态，15 个调用点里
**ja-JP 与 ko-KR 各缺 7 个键**（`settings.search.placeholder`、`settings.search.no_results`、`settings.window.back` 等，
按 `desktop/LanMountainDesktop/Localization/*.json` 逐键对过），所以日/韩用户打开设置页看到的就是这些英文标识符。
**没有顺手补翻译**——日/韩用词的措辞属内容决定（见待办 G1-K 的缺口口径），这里只把"缺键时说什么"接对：
现在的行为是那 7 处显示 zh-CN 原文，而不是键名。

**天气接口的 locale 拼法另有一处家**：`en_us` / `zh_cn` 是供应商词表，不是 IETF 语言码，一律走
`desktop/LanMountainDesktop/Services/XiaomiWeatherLocales.cs`（守卫 `WeatherProviderLocaleValues_LiveInExactlyOnePlace`，
连"再写一份 `NormalizeWeatherLocale`"一起禁掉）。收口前这两个拼法散在 3 份逐字相同的私有方法与 1 处选项默认值里；
改一份的症状是"设置页天气是英文、桌面组件还是中文"，而两边都看着正常。
`ForLanguageCode` 保持逐字比较（`LanguageCodes.IsEnglishCode`），所以 `"en"` 仍按中文走——这是收口前的口径，
`XiaomiWeatherLocaleTests` 里用一条 InlineData 钉住，别哪天顺手改成归一化。

**崩溃转储的磁盘契约只认一处**：宿主崩溃时写 `LocalApplicationData/LanMountainDesktop/crashes/`，
启动器与错误窗口再读它——两个二进制之间没有共享类型，全靠 `crashes` / `crash-*.txt` / `latest.txt`
三个名字逐字对齐。收口前这三个名字在 4 个文件里各抄一份，抄错一个字母的症状是崩溃对话框什么都不显示
（正是最需要诊断信息的时候）。一律走 `core/LanMountainDesktop.Core/Diagnostics/CrashDumpLayout.cs`，
写文件名用 `BuildDumpFileName(DateTime.Now)`（它保证名字落在 `DumpFilePattern` 里）。
守卫 `SourceIntegrityTests.CrashDumpContract_LivesInExactlyOnePlace`。

**数据位置配置的磁盘契约只认一处**：`{安装根}/.Launcher/data-location.config.json` 由启动器写、
启动器与宿主两侧读，字段是 `dataLocationMode` / `systemDataPath` / `portableDataPath`，模式值只有
`System` / `Portable`——一律走 `core/LanMountainDesktop.Core/Data/DataLocationContract.cs`
（启动器的 `DataLocationConfig` 属性用 `[JsonPropertyName(...)]` 指向它，宿主的字典按键名也指向它）。
抄错的症状是"数据位置设置静默失效"：便携安装被当成系统安装，用户以为数据丢了。
守卫 `SourceIntegrityTests.DataLocationConfigContract_LivesInExactlyOnePlace`。

**用户资料目录下的品牌文件夹名只认一处**：`%LocalAppData%\LanMountainDesktop`（以及录音用的
`%Documents%\LanMountainDesktop`）那一段一律用 `core/LanMountainDesktop.Core/Data/UserDataRoot.cs`
的 `FolderName` / `Resolve()` / `ResolveSettingsPath()`。2026-09-21 收口前四个二进制里有 28 处各写一遍
字面量（宿主 13、启动器 9、Core 3、安装器 3）。抄错的后果不是报错，而是那个二进制去一个空目录里找用户的数据
——"设置没了"就是这么来的。注意仓内还有三类**不是**这一族、别顺手改：`DeploymentLocator` 与
`ErrorWindow` 里 `Path.Combine(solutionRoot, "LanMountainDesktop", "bin", …)` 找的是编译产物目录、
`PublicAppInfoService` 里那个是应用显示名、`"LanMountainDesktop.exe"` 是 exe 名（用
`DeploymentLayout.GetHostExecutableName()`）。守卫
`SourceIntegrityTests.UserDataRootFolderName_LivesInExactlyOnePlace` 只拦"根是用户资料目录 + 拼了这个名字"的组合。
**`settings.json` 这个文件名同样只认一处**：读写它的是三个二进制（宿主读写、首启向导写、Core 的启动偏好读），
目录各不相同但文件名必须同一个；收口前字面量在生产代码里有 7 份，现在一律用
`UserDataRoot.SettingsFileName`，守卫 `SourceIntegrityTests.SettingsFileName_LivesInExactlyOnePlace`。
漂了的后果不是崩，而是"设置读不到、界面回到默认值"。

**更新快照元数据只有一个模型**：`{数据根}/update/snapshots/*.json` 由宿主写、宿主与启动器各自读，
一律用 `core/LanMountainDesktop.Core/Update/SnapshotMetadata.cs`（键名用显式 `[JsonPropertyName]` 钉住，
不依赖两个程序集恰好一致的序列化开关）。收口前它是两份逐字相同的声明
（`ApplySnapshotMetadata` 与 `SnapshotMetadata`）——改一边不会编译报错，只会让另一边把 `sourceDirectory`
读成空串，症状是"旧版本被清理掉、想回滚时没得回滚"。状态值用 `SnapshotMetadata.PendingStatus`。
守卫 `SourceIntegrityTests.SnapshotMetadataModel_LivesInExactlyOnePlace`。

**样式类两个方向都要对上**：`.axaml` 里 `Classes="foo"` 而全仓没有 `Selector="...foo"` → 控件静默走默认样式
（守卫 `StyleClasses_UsedInMarkup_AreAlsoDefined`）；反过来样式定义了却没人贴 → 孤儿样式，删了也没人知道
（守卫 `StyleClasses_DefinedInSelectors_AreAlsoUsed`，两边都要求 0）。2026-09-21 按后一条清了 18 个死类 /
26 个 Style 块 / 255 行，其中 `component-editor-footer-button` 在两个文件里各声明了一份，而那个窗口
早就没有页脚容器了；`icon-l` 是 s/m 两档还在用、最大那档没人用的尺寸残档。
判"用过"取宽松口径：任何 .cs 里的同名字符串字面量都算（`Classes.Add(常量)` 这种绕法），
所以只会偏乐观、不会误红。

**没人引用的图片/字体不要往仓库里放**：`Assets/**` 里的资源不会被编译器检查，放进去了就会被
打进安装包长期占地方。守卫 `tests/LanMountainDesktop.Tests/UnreferencedAssetRatchetTests.cs`
把"文件名/字体家族名/资源目录路径"三种引用方式都算引用，全都不命中就是孤儿，新增即红。
2026-09-21 首扫按这条清掉 `avalonia-logo.ico`（Avalonia 模板默认图标）与 `wechat.svg`
（微信按钮早就改成内联 `<Path Data=…>` 矢量，这张 svg 只是它当初的来源）。
唯一挂名的欠账是 `Assets/endfiled`：24 张表情图 / 1.27MB 全仓零引用，也没有代码按目录枚举它们——
删掉整套是产品决定，先用配额冻住（24 张 / 1303 KiB），不许再扩大。

**一个数只有一个家**：组件自缩放基准 `ComponentDesignMetrics.BaseCellSize`（此前 12 个组件各写一份
`private const double BaseCellSize = 48d`），网格密度/边缘留白的量程与默认值 `DesktopEditing/DesktopGridLimits`
（此前在 `MainWindow` 及其 partial 分片、`FusedDesktopEditGridAdapter`、`AppSettingsSnapshot` 默认值里各一份），
短文本归一化 `Helpers/CompactText.Normalize`（此前 `NormalizeCompactText` 连它专用的 `MultiWhitespaceRegex`
在 9 个组件里逐字抄了 9 遍、23 个调用点）。守卫
`ComponentBaseCellSize_LivesInExactlyOnePlace`、`DesktopGridLimits_LiveInExactlyOnePlace`、
`CompactTextNormalizer_LivesInExactlyOnePlace`。
**立了家不等于收了口**：`BaseCellSize` 那次只删掉 12 份重复声明，2026-09-22 复查时组件里还有 39 处把 48
当基准的字面量（16 处 `_currentCellSize / 48d` 一类缩放换算、23 处 `double _currentCellSize = 48;` 字段默认值），
改基准时它们不会跟着动，症状就是"一半组件缩放不对"。现已全部指向 `ComponentDesignMetrics.BaseCellSize`，
同一条守卫把三种形态（重复声明 / 除法基准字面量 / 字段默认值）一起拦——每立一个家，都要单独数一遍调用点。
找下一族的手段是量出来的，不是凭印象：`python scripts/dump-dup-methods.py`（不带参数扫全部二进制，
按"方法名 + 归一化方法体"分组，报 `Nx 方法名 (行数, body#hash)` 加每个站点的 file:line；
传目录只扫那些文件，`NAMES=A,B` 换要查的方法名）。**这条探针必须先拿已知样本验过再用**：
用 `ce8ed29^` 那三份逐字相同的 `NormalizeWeatherLocale` 做对照能报 3x，才算它有效——
同一轮里更早一版按 K&R 大括号写的探针在真树上报"0 组"，而本仓是 Allman 风格，那条 0 是假的。
**还有第二把尺子专门找漂移**：`python scripts/dump-drift-methods.py`（同名方法按"有几种体"分组，
报 `Nx 处 / M 种体 / K 文件`）。`dump-dup-methods.py` 只看得见逐字相同的族，而"家被人绕开"那一类
（同一个名字、各组件各写一份、每份差一点）只有这把能看见——2026-09-22 就是靠它挖出
`ResolveUnifiedMainRadiusValue`（13 个组件各抄一份，家早已存在）。已知样本：`ApplyCellSize` 应报 44 处 / 32 种体 /
最大同体组 13；`L`（组件的本地化小助手）50 处 / 21 种体。
**第三把尺子数的是"动词配对"，不是方法体**：`python scripts/check-component-pairs.py`
（组件是短命的、服务与计时器是长命的：`Attach` 里起来的东西必须在 `Detach`/`Dispose` 收回去）。
2026-09-22 首跑：46 个有 attach/detach 的组件文件里 **0 处未配对**
（覆盖 timer 起 35 / 收 59、service 事件 5 / 5、PropertyChanged 类 1 / 1、快照订阅 8 / 13、时区 Set/Clear 10 / 10）。
**"报 0"之前必须先拿正对照验它**：临时放一个只 `_probeTimer.Start()` + `X.PropertyChanged += …` 的文件，两形都要报出来才算有效——
这一跑就是这么抓出判据自身的两个洞（`_timer?.Stop()` 的空条件形式漏认；把 `Set/ClearTimeZoneService` 按不同 key 配对，
一度报出 39 个文件的假阳性）。**监视租约故意不数**：它走 `StudyMonitoringLease.Sync(ref …状态位)`，
取放由家按状态对账，按动词数只会得出假结论。
**第四把尺子问的是"定义了有没有人调"**：`python scripts/check-widget-layout-applied.py`
（组件自己那套 `ApplyCellSize` / `UpdateAdaptiveLayout` / `ApplyLayoutMetrics` / `ApplyResponsiveLayout` /
`ApplyTypographyByBackground` / `ApplyChrome` 若没有任何调用点，组件就按 XAML 默认样式画出来、缩放应用不上，
不报错只是不对）。2026-09-22 首跑：87 个组件文件、80 个这类方法，**未被调用 0 处**。
这一条的关键判据是**`override`/`abstract` 要豁免**：`WeatherWidgetBase.cs:86` 会在基类里调子类的
`ApplyResponsiveLayout`，第一版没豁免时 5 个天气组件全被报成缺陷（全是假阳性）。
正对照：临时放一个只定义 `private void UpdateAdaptiveLayout() {}` 的文件必须被报出来，报完删掉。
**这条已经进闸门**：`tests/.../WidgetLayoutAppliedRatchetTests.cs`（脚本仍是报告面，判据一样）。
2026-09-22 移植时踩到两个坑，都记在测试的注释里：① 把"调用点扫描"跟在"本行有声明"后面 `continue`，
同文件的真调用全被吞掉、一次报出整片假阳性；② 跨文件调用点按**名字**认（`ApplyCellSize` 实测 22 处
由运行期注册表统一推），所以同名成员只要任意一处有 `.Name(`，别的文件里没人调的定义也会被算成已调用——
这是漏报方向。两向变异验过：探针文件里未调用的 `ApplyChrome` 被点名，同文件有调用的
`ApplyTypographyByBackground` 不报。
**第五把尺子管"谁 new 了要释放的东西、却既不释放也不交接"**：`python scripts/check-resource-ownership.py`
（范围是 Services / ViewModels / Core / 安装器 / Platform，组件目录由第三把尺子管）。
2026-09-22 首跑 543 个文件：1 处线索、**未解释 0 处**——那 1 处是 `PlondsHttpClientFactory.Create()`，
逐处读后确认是**交接**：client 交给 `PlondsClientServiceFactory.CreateDefault` 塞进
`PlondsManifestClient` / `PlondsHttpPackageDownloader`，而 `CreateDefault` 只在
`SettingsDomainServices.cs:2070` 构造 `UpdateSettingsService`（应用级容器里的单例）时调一次
→ 一次进程一个 client，`PlondsService` 不实现 `IDisposable` 是有意的。这类结论登记在脚本的 `EXPLAINED` 里，
交接方式若改掉（例如变成每请求建一个 client）就会重新变成未解释线索。
两个**实测到的假阳性来源**别忘：释放常写成 `CancellationHelper.CancelAndDispose(...)`（判据里 `Dispose` 前不能加词边界，
否则两处正常站点会被报成缺陷，本轮就踩了）；工厂方法 `Create()` 本身就是交接。
**第六把尺子数的是"实例方法定义了有没有人调"**：`python scripts/dump-dead-instance-methods.py`。
开它的原因是另外两条路都量不到：`ZeroUseMemberRatchetTests` 的口径是 `static class` 上的静态方法，
一条实例方法都不看；编译器也不管——`Directory.Build.props` 里 `EnforceCodeStyleInBuild=false`，
IDE0051（未使用私有成员）在构建里一条都不出（2026-09-22 实测：塞一个零调用 `private` 方法，
`--no-incremental` 重建后错误 0、IDE0051 计数 0）。所以按可见性全收，连 `private` 一起量。
两条**判据边界**（都是踩出来的）：① 语料必须含 `airapp/`、`mobile/`、Plonds 树，只数宿主七个目录会
把 8 条活成员判成死码——它们的调用点在 `AirAppHost` 的视图里（样本：`ClockAirAppStopwatchState.AddLap`
被 `ClockAirAppView.axaml.cs:519` 调）；② 实现"本仓之外的接口"的成员名收不进 `iface_names`，
要按实测进 `EXTERNAL_INTERFACE_MEMBERS`（`IValueConverter.ConvertBack`、`IHostApplicationLifetime.StopApplication`
就是首轮抓到的两条假阳性）。正对照已做：临时文件的零调用 `private` 方法必须报出来，而接口实现、
`virtual`、同类内部被调的三种形状必须都不报。
2026-09-22 首跑真值：**A 级（生产语料里除声明本身零出现）44 处、T 级（只有测试在调）6 处**；
同日判完当天存量后 **A 级 26 / T 级 4，未解释各 0**（退出码 0，可以接进闸门了）。
累计删掉 21 条实例死约定（281 行 / 15 个产品文件，`git diff --shortstat` 实测），
判为"实现了没入口"的 25 条全部登记在脚本 `EXPLAINED` 里带理由，
分待办 G1-AW（分类条手势）、G1-AY（隐私同意只写不读）、G1-AZ（启动进度链 + `LoadingTimeoutHandler`
整类没被 new）、G1-BA（磁盘余量 / 单场学习报告 / 常用时区表）、G1-BB（轻应用没有卸载路径、
"登记外部包"这条能力整体不可达、市场版本摘要没人显示）、G1-U（Core 公开面）；**没有一条被顺手删**。
判据本身被修过三次：跨工程同名声明行会把调用点喂饱（Plonds 自带 `GetCatalogAsync`）、
C# 主构造器会被当成方法声明（`class X(IProgress<…>? p)` 报成一条不存在的方法）、
以及**用 python heredoc 写 `\b` 会落成一个退格控制字符**——规则看着在文件里，正则永远不匹配，
这条直到重跑才发现。所以新增/改动判据后必须**直接调用被改的函数验一次**，别只看总计数。
终判永远是删除法：A 级只是线索，删掉重建看编译红不红。
**这条轴已经进闸门**：判据与名单的唯一权威是 `tests/LanMountainDesktop.Tests/ZeroUseInstanceMemberRatchetTests.cs`
（29 条登记、`CensusAnchors` 钉覆盖面、"条目没了"和"条目已被真引用"两个方向都会红；
种一个零调用 `private` 实例方法立刻红，变异验过）。`scripts/dump-dead-instance-methods.py` 降级成**报告面**：
判据相同、退出码恒 0、**不再持有名单**——理由抄两份就是第二个真源，一定会漂。
剥字符串这件事另有一份共享实现 `tests/LanMountainDesktop.Tests/SourceTextScanning.cs`，
两条棘轮（类型与实例成员）都用它：只剥引号内文本、**插值洞 `{expr}` 必须保留**——
第一版整段换掉就把 `$"… {FormatRelative(…)}"` 里的真调用吞了，当场把活方法报成死码（这条是实测踩的，
`StringLiteralStrippingKeepsInterpolationHoles` 四个方向钉住）。
**滑杆的 `Minimum`/`Maximum` 不许写死数字**，要绑视图模型上从 `DesktopGridLimits` 读的那四个量程属性——
设置页量程是这套数的第四份副本，漂了的症状不是崩，而是"拖到尽头网格不动"或"存进去被运行期悄悄钳掉"。

**组件取圆角只认 `ComponentChromeCornerRadiusHelper` 一家**：`Views/Components/*.cs` 里不许再出现
`CornerRadiusTokens`（无论 `.X` 还是 `?.X`）也不许再自己声明 `ResolveUnifiedMainRectangle()` /
`ResolveUnifiedMainRadiusValue()`（守卫 `ComponentCornerRadius_LivesInExactlyOnePlace`，两条规则都种过 scratch 文件验红）。
2026-09-22 量到的形态：13 个组件各抄了这两个私有方法（合计 26 个方法、读同一张 token 表的 `Lg` 档），
同目录另一批组件走家（`ResolveMainRectangleRadius`，读 `Component` 档）。抄的那 13 份有两处真缺口：
**没有家里的"非有限值兜底"**，且**一律无视 `chromeContext`**——宿主可以按组件下发 chrome（`DesktopComponentRuntimeRegistry`
就在传），这 13 个组件不吃，所以"统一主矩形圆角"名字统一、实际不统一。现已全部改走家新增的
`ResolveLgRectangle` / `ResolveLgRectangleRadiusValue`（行为保持：默认仍取 `Lg`，兜底只在 NaN/∞ 时生效）。
**没替你选的一件事**：家原本读的 `Component` 档与这 13 处读的 `Lg` 档数值不同——
`AppearanceCornerRadiusTokenFactory` 实测 Balanced(默认) 24 vs 28、Rounded 28 vs 32、Open 32 vs 36，
Sharp 与 Fluent 才相同。也就是说默认风格下桌面组件面板本来就有两种外框圆角。统一到哪一档是设计决定（另立待办），
这里只先把"一个数两处实现"变成"一个数、两处显式选档"。

**更新布局的磁盘名只认 Core 的 `UpdatePaths` 一家**：`.Launcher` / `update` / `incoming` / `objects` /
`snapshots` 这几级目录，以及 `plonds-filemap.json`、`plonds-filemap.sig`、`plonds-update.json`、
`files.json`、`files.json.sig`、`update.zip`、`public-key.pem` 这些文件名，跨启动器 / 宿主 / 安装器三份二进制
都按字面量拼同一条路径，所以只有 `core/LanMountainDesktop.Core/Update/UpdatePaths.cs` 能写字面量，
别处一律走它的访问器（守卫 `UpdateLayoutDiskNames_LiveInExactlyOnePlace`：文件名按字面量全文拦，
目录名按"声明常量"这一形态拦——update/objects 这类词另有导航键与 CLI 动词撞名，拦全文会假红）。
2026-09-22 收这一族时实测到的形态是：宿主 `PlondsApplyPaths` 把 7 个文件名 + 4 个目录名各抄一份常量，
而 `UpdatePaths` 那 4 个对应访问器零调用——**家立着、调用点绕过去**，改一边就跨二进制漂移
（症状是更新包读不到清单、回滚找不到快照，而且没有任何东西保证三个进程算出同一个路径）。
同族另外两处：`DataLocationResolver.ResolveLauncherDataPath()` 自己拼 `.Launcher`、
`DeploymentLocator` 自己拼 `snapshots`，现在都指回 `UpdatePaths.GetLauncherDataRoot` / `GetSnapshotsDirectory`。
**零引用成员棘轮的口径要覆盖所有二进制**：它原先只看宿主工程，Core / 启动器 / 安装器 / Platform 里的
静态类完全没被数过——把口径从 1 个目录扩到 7 个，当场量出 21 条零引用成员（判法见该文件里的名单注释）。
**这条探针数的是裸方法名，名字越常用越容易被无关文本喂饱**：`SettingsServiceAppSnapshotExtensions.Save()`
零调用却报不出来（`Save` 在全仓出现几百次），终判靠的是"删掉方法后全解决方案是否仍 0 错误"。
名单里的条目要复核，别只信计数。

**每个 `.cs` 都必须被某个工程认领**：就近目录要有 `.csproj`，且不被该工程的 `Compile Remove/Exclude` 命中
（守卫 `EveryCSharpFile_IsClaimedByAProject`；模板包 `content/**` 与 SDK 的 `_build_verify_*/**/*.cs` 是有意排除，算认领）。
2026-09-22 第一次量的数：18 个工程的 Compile 项共 899 个文件，磁盘上 900 个，多出来的是
`scripts/GitCommitAnalyzer.cs`——662 行 C#，没工程编译、没脚本/工作流调用、只在 `docs/archive/` 的一次自动提交记录里被提过，
而同样的分析在 `scripts/Analyze-GitCommits.ps1` 与 `scripts/analyze_git_commits.py` 里各有一份，已删（**这两份脚本副本还在**，
它们是 dev 工具、不进产品，要不要合一份属另一件事）。这条守卫存在的理由不只是"少 662 行"：
**编译器与 IDE 都不看没被认领的文件，而两条零使用棘轮把 `scripts` 当生产目录数**——那种文件里声明的类型会被当成"待判死码"，
它文件体内部的自引用又会被当成"有人用"，等于让前面的探针说谎。
判据是文本级的（不展开 MSBuild 条件与 `Directory.Build.props`：那只会让"没人编译"更多，不会反过来误判）。
两次变异都验过：在 `scripts/` 放一个野文件→红并报出路径；在 SDK 的 `Compile Remove` glob 下放一个→仍绿（证明排除逻辑真的在跑，不是恒假）。
**顺带量到的一件事**（不改，先记）：`mobile/LanMountainDesktop.Mobile(.Android)` 两个工程不在 `LanMountainDesktop.slnx` 里
（CI 用 `dotnet build <csproj>` 单独编，见 `build.yml:218`、`code-quality.yml:83`），所以本地 `dotnet build` 全量门覆盖 11 个工程、
不等于仓库里的 18 个——移动端编译破了，本地这道门不会响。

**对外报出去的身份只有一处字面量**：所有 HTTP 请求的 User-Agent 都写进
`desktop/LanMountainDesktop/Services/HttpUserAgents.cs`，别处只引用不再抄（守卫
`HttpRequestIdentityStrings_LiveInOnePlace`；允许的字面量只有请求头名 `"User-Agent"` 本身）。
2026-09-21 收口时同一份 Chrome 指纹在 4 个组件里逐字抄了 4 遍、市场身份在 4 个市场服务里抄了 4 遍、
裸产品名抄了 3 遍，另有 3 份单一副本一并登记进去。
**两件事登记等拍板，没有顺手改**（都会改变真正发出去的字节）：
① `RecommendationDataService` 那 9 个第三方接口报的是短指纹 `Mozilla/5.0`，其余组件报的是完整 Chrome 指纹——
统一成哪一种都是对外行为变更；② 除 `Plonds/PlondsHttpClientFactory`（现拼程序集版本，会随发布走）以外，
其余 6 份身份里的 `/1.0` 全是写死的，产品版本涨了就没人动它。

**同一个 using 块里不许把同一条指令写两遍**（编译器 CS0105）。守卫
`UsingDirectives_AreNotDeclaredTwiceInAFile`，只查文件开头那段 using（namespace 块内部的 using 只对自身生效，
删兄弟块的同名指令会真的改变解析结果）。2026-09-21 那次 `plugin → airapp` 批量改名按目录补 using，
一次留下 26 处重复 / 20 个文件——批量脚本补完必须让 CS0105 归零，而不是只确认能编译。

## 6. 权威来源

- 产品定位：`docs/00-快速开始/01-项目介绍.md`（归档见 `docs/archive/PRODUCT.md`）
- 架构与模块职责：`docs/04-架构与实现/01-整体架构.md`（归档见 `docs/archive/ARCHITECTURE.md`）
- 运行、构建、测试、打包：`docs/archive/DEVELOPMENT.md`
- feature 规格：`.trae/specs/`
- 视觉规范：`docs/03-组件设计规范/02-视觉规范.md`（归档见 `docs/archive/VISUAL_SPEC.md`）
- 圆角规范：`docs/archive/CORNER_RADIUS_SPEC.md`
- 生态边界：`docs/archive/ECOSYSTEM_BOUNDARIES.md`
- 跨平台架构：`docs/CROSS_PLATFORM.md`
- AirApp SDK 迁移：`docs/AIRAPP_SDK_V1_MIGRATION.md`
- 命名与标识符冻结：`docs/ai/NAMING_AND_FROZEN_IDENTIFIERS.md`

如果多个文档都提到同一件事，以 `docs/ai/DOC_SOURCES.md` 列出的权威来源为准。

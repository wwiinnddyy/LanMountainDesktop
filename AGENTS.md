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

### AirApp

- SDK 公共 API 以 `airapp/LanMountainDesktop.AirAppSdk/` 为准
- 共享契约以 `core/LanMountainDesktop.Core/` 为准
- market 数据来源默认是兄弟仓库 `..\\LanAirApp`
- 迁移或 breaking change 优先同步 `docs/AIRAPP_SDK_V1_MIGRATION.md`
- 统一用 AirApp 措辞：新增的类型、目录、设置键、日志文案不要再引入 `plugin` / `Plugin`
- 改名前先查 `docs/ai/NAMING_AND_FROZEN_IDENTIFIERS.md`：其中第 3 节是跨进程/跨仓协议冻结项，第 2 节是需要一次性迁移器的本地数据标识符，两者都不能当作"漏改"直接重命名
- **清单 id 是唯一真源**：`airapp.json` 的 `components[].id` 必须与 `AddAirAppComponent` 注册的 `ComponentId` 逐字一致，宿主加载时据此校验并**拒载**（`AirAppLoader.ValidateManifestComponentContract`）。添加面板按清单列、创建控件按注册 id 找，两边漂移的症状是"面板里有、点下去没反应"且原本不报错（LanWord 实测踩过）。注册了却没声明只警告，不拒载

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

批量删完必须 `dotnet build`（编译器是唯一能证伪的闸），再跑回归闸门：
`dotnet test LanMountainDesktop.slnx --no-build --filter "Category!=EcosystemProbe"`。
注释吞语句、IDE0051 未使用成员这两类由 `tests/LanMountainDesktop.Tests/SourceIntegrityTests.cs` 与
`dotnet format style --diagnostics IDE0051 --severity hidden` 守；新增死码规则时请同步扩这两处。

**只写不读成员（IDE0052）**：字段被赋值却从不读取，等于"看着接了线其实没接"，是本项目最密集的假实现来源。逐项目量一遍（改项目路径即可）：

```
dotnet format style <csproj> --diagnostics IDE0051 IDE0052 --severity hidden --verify-no-changes
```

2026-09-20 基线：宿主 43 → **0**（已并入 `.github/workflows/code-quality.yml` 的 Check unused members 闸门），
Core / Platform / AirAppSdk / AirAppRuntime / tests / installer 均为 0；剩 Launcher 5、AirAppHost 1、
AirAppDevServer 1，都是"UI 收了输入但不落地"或"CLI 承诺了没实现"的待决项，不要当噪声删掉。
删这类字段时注意：赋值常发生在接口实现里（`SetComponentPlacementContext`、`SetDesktopPageContext`），
先确认该接口是否只有这一个消费者，是的话连同接口实现一起摘掉，别只留一个空方法。

**零使用类型棘轮**：`tests/.../ZeroUseTypeRatchetTests.cs` 把探针固化成测试——扫全仓声明的类型名（约 1200 个），
在整仓语料（含 `.axaml`/`.json`/`.iss`/同级 AirApp/tests）里数出现次数并减掉"自身文本"，为 0 即零使用。
名单**只许缩短**：新引入一个零使用类型直接红；条目对应的类型没了也会红（防名单烂掉）。
2026-09-20 基线：17 → **13**，删掉的 4 个是 `ShutdownCoordinator` / `SettingsWindowHost` /
`DesktopStartupCoordinator`（三个纯穿透壳，被包的动作早已由真实路径直接调用）和 `ObservableHelper<T>`。
名单里三类要分清：扩展方法宿主与 Harmony/COM/Android 反射入口是**探针假阳性**（调用点不出现类型名），
其余标着"等用户拍板"的是**已实现但没入口**的能力（分离式组件库窗口、考勤整模块、
主备双清单组合器、Core 的第二套 IPC 装配入口），删它们等于删产品决定，必须先问。
按文件删时要先确认该文件只声明这一个类型——`ObservableHelper.cs` 里还住着一个在用的 `ActionObserver<T>`。

**组件可达性棘轮**：`tests/LanMountainDesktop.Tests/DesktopComponentReachabilityTests.cs` 钉住四件事——
声明 `AllowDesktopPlacement` 必须真有运行期注册、运行期注册必须真有允许摆放的定义、
库里每个条目必须有在 zh/en/ja/ko 都能取到文案的 `DisplayNameLocalizationKey`、
每个条目都要能在 LibraryPreview 下被创建并布局出非零尺寸（`DesktopBrowser` 因 WebView2 会改线程套间而免检，
免检清单本身也有守卫防烂掉）。`LocalizationParityRatchetTests` 以 zh-CN 为源语言记着
en/ja/ko 还缺 35/333/289 条，只许降不许升；补翻译就把数字改小。
组件的 `DisplayName` 必须是语言中立的兜底文案（历史上 5 个写死中文，已改；守卫会拦新增）。

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
`Services/StudySnapshotSubscription.cs`（`Subscribe(ref _isSubscribed, service, OnStudySnapshotUpdated)` 与 `Unsubscribe`）。
"只订一次、卸载或销毁时必须解掉"这对判断此前被 8 个组件各抄两遍、一共 21 处；解订那一半漏抄就是
一个组件已经从桌面上摘掉却还在被回调的事件泄漏。`_isSubscribed` 现在只由该助手读写，
守卫 `StudySnapshotSubscription_LivesInExactlyOnePlace`。

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
在整仓语料里数方法名出现次数（减掉声明行与注释），为 0 即零引用；名单只许缩短、每条必须写理由。
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

**PLONDS 线上协议字面量**：宿主与服务端没有共享类型，全靠字符串对齐，所以包名
（`Files.zip` / `files.zip` / `changed.zip` / `files-windows-x64.zip` / `PLONDS.json`）与动作值
（`add` / `replace` / `reuse` / `delete`）只许写在 `Services/Plonds/PlondsWireFormat.cs` 一处，
`SourceIntegrityTests.PlondsWireLiterals_OnlyLiveInPlondsWireFormat` 会拦住第二处。
宿主读清单时必须同时认 filemap 的 `action`/`sha256` 和分布元数据的 `op`/`contentHash`
（服务端样例里全是后者），契约由 `PlondsDistributionContractTests` 拿 `sample-data` 真实文件钉住。

**整体替换一个文件只认一处**：设置、缓存、清单、白板笔记这类"把新内容换成旧文件"的落盘，
一律走 `Services/AtomicFileWriter.cs`（文本 `WriteText`、流 `WriteStreamAsync`），不要再手搓
"写 `.tmp` + `File.Move`"。收口前宿主里有 10 处各写一份，差异里有两个会真实咬人的：
目标被资源管理器/杀毒瞬时锁住时没人重试（症状就是用户说的"设置没存上"，而 `FileOperationRetryHelper`
本来就是为这种情况写的却没被用上），以及 Move 失败后 `.tmp` 留在 AppData 里不清理。
`Encoding.UTF8` 那个重载只为磁盘上已经带 BOM 的旧文件保留（白板笔记），新代码别用。
守卫 `SourceIntegrityTests.AtomicFileReplacement_LivesInExactlyOnePlace`；
Launcher 进程里的 `OobeStateService` / `LauncherBackgroundService` 属另一份二进制，还没并进来。

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

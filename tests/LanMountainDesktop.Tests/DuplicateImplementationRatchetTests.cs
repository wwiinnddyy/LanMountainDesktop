using System.Text.RegularExpressions;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 重复真源的"只许降不许升"棘轮：把两把普查尺子的结果冻成上限。
///
/// 为什么值得冻住：<c>dump-dup-methods.py</c> 与 <c>dump-drift-methods.py</c> 是普查工具——它们排队待办，
/// 但没有任何东西阻止新增一份复制。本仓已经吃过这个亏：圆角算式、亮度算式、JSON 路径读法、
/// 语言码归一化……每一族都是"先有人抄，事后才发现抄了 N 份"，而且抄的时候不报错。
/// 冻住之后，新抄一份就是红灯，收口一份就必须把上限改小（改小是自愿的，改大必须写理由）。
///
/// 两个口径与脚本逐字对齐（2026-09-22 交叉验证过：C# 与 python 在同一棵树上得出同一组数）：
/// ① <b>逐字相同</b>的方法体族（<c>dump-dup-methods.py</c>）：要求签名一行、<c>{</c> 单独一行
///    （本仓是 Allman 风格，按 K&R 写的解析器会一条都不匹配、报出一个假的 0），
///    且方法体去掉空行与 <c>//</c> 注释后**至少 2 行**——单行的转发不算"复制了一份逻辑"。
/// ② <b>同名不同体</b>的漂移族（<c>dump-drift-methods.py</c>）：同一个方法名有 ≥2 种归一化后的体。
///    归一化会折叠空白、把字符串字面量换成占位符，所以"只差一句文案"的不算漂移。
///    这一族专门抓"家被绕开"：2026-09-22 就是它挖出 13 个组件各抄一份
///    <c>ResolveUnifiedMainRadiusValue</c>（家早就存在）。
/// </summary>
public sealed class DuplicateImplementationRatchetTests
{
    /// <summary>
    /// 实测：64 组逐字相同、且**至少两条语句**的方法体。只能降，要升必须在这里写清理由。
    /// 66 → 64 是真收口：6 个学习组件各抄一遍的"活跃页上下文"三步
    /// （<c>_isOnActivePage</c> 落位 → <c>UpdateMonitoringLeaseState()</c> → 只在"从不在到在"时补一次刷新）
    /// 收进 <c>StudyComponentLifecycle.ApplyPageContext</c>；抄本之间只差最后那一下刷新
    /// （4 个 <c>RefreshVisual()</c>、2 个 <c>_renderGate.Queue(…)</c>），
    /// 所以这一笔把两族（4 份同文 + 2 份同文）一起消掉，并补上第一条行为钉
    /// <c>StudyComponentLifecycleTests</c>（钉的就是那个"只首次刷新"的守卫）。
    /// 之前几笔：68 → 66 收 11 个组件各写一遍的 detach 三连（进 <c>ComponentRefreshLifetime.Detach</c>）；
    /// 71 → 68（三族）收 <c>NormalizeAutoRefreshIntervalMinutes</c>（6 处各抄 11 行，
    /// 绕开的正是 <c>RefreshIntervalCatalog.Normalize</c>，净 -108 行，并给家补第一条行为钉）；
    /// 73 → 71 收两族（6 处"判黑夜 + 重排"进 <c>ComponentThemeMode.RefreshNightVisual</c>、
    /// 6 处学习组件 detach 四步进 <c>StudyComponentLifecycle.Detach</c>）；
    /// 74 → 73 收 7 个学习组件各抄一份的
    /// "读快照 → 归一语言 → 取学习监测开关"（进 <c>StudyComponentSettings.Reload</c>）；
    /// 从 86 降到 74 有两笔，别记成一笔：
    /// ① 判据修正（按语句数而不是按行数）去掉 12 族单语句转手——它们本来就不是"复制了一份逻辑"；
    /// ② 18 个组件的 ApplyCellSize 收进 <c>ComponentDesignMetrics.ApplyCellSize</c>。
    ///    这一笔对族数<b>没有净影响</b>：收口前那 13 个组件是一族逐字相同的两行体，
    ///    收口后如果还按行数算，18 处转手调用又是新的一族同文——行数骗人的地方就在这。
    ///    它真正的收益是"钳到最少 1 格再重排"这段逻辑从各写一遍变成只有一处（18 个调用点）。
    /// 64 → 63 收一族（5 个组件各抄的"只在黑夜档真的翻了才重画"进
    /// <c>ComponentThemeMode.RefreshNightVisualIfChanged</c>：原本 3 处逐字 7 行 + 2 处带 <c>force</c> 的漂移体，
    /// 收完 5 个调用点都是单语句转手，按本判据口径不再算"复制了一份逻辑"）。
    /// 63 → 62 收一族（每日一词 1x1 与 2x2 读的是<b>同一对设置键</b>，所以那 13 行是逐字相同的两份，
    /// 进 <c>DailyWordAutoRefresh.Apply</c>；顺带把编辑器注册表里第三份 <c>360</c> 也指回这个常量）。
    /// 62 → 61 收一族（头像占位字 15 行逐字两份：<c>CurrentUserProfileService</c> 与 <c>MainWindow.DesktopPaging</c>
    /// 各一份，进 <c>Services/Monogram.cs</c>，5 个调用点改走家；改了头像规则而磁贴没跟上，
    /// 症状是同一个人/同一个磁贴两种缩写，不报错）。
    /// 61 → 59 收两族（<c>NormalizeExistingDirectory</c> 与 <c>NormalizeExistingFile</c> 各 2 份逐字 17 行：
    /// Core 的 <c>AppVersionProvider</c> 与宿主的 <c>AppRestartService</c> 各抄一对，
    /// 进 <c>Shared/IO/ExistingPath.cs</c>，10 个调用点改走家——两份算的是"这条路径能不能信"，
    /// 漂开的后果是同一个安装被启动器与宿主判成两种结论）。
    /// 59 → 58 收一族（安装期"待删除包暂存目录"的收尾在 Core 的 <c>AirAppPackageInstaller</c>
    /// 与启动器的 <c>AirAppInstallerService</c> 各抄一份逐字 14 行，宿主的 <c>AirAppRuntimeService</c>
    /// 还有第三份、而且**多一段"空目录顺手删掉"**——三份做的不是同一件事。收进
    /// <c>Shared/AirAppPendingDeletionDirectory</c> 时取超集那版：安装器现在也会回收留空的目录，
    /// 目录只在需要时被重建，没有任何一侧依赖它一直存在（这条差异本身写在家的注释里）。
    /// 家新方法刻意取名 <c>PathFor</c>/<c>CleanupAfterInstall</c>：先用了 <c>Resolve</c>/<c>Cleanup</c>，
    /// 漂移普查当场从 192 涨到 194——不是多了重复，是**通用名把无关实现并进同一族**，上限红得对。）
    /// 58 → 57 收一族（启动台隐藏项的兜底显示名 9 行逐字两份：设置页 LauncherSettingsPageViewModel
    /// 与桌面叠加层 MainWindow.DesktopPaging 各一份，进 <c>Services/LauncherHiddenItemNames</c>，
    /// 6 个调用点改走家——只改一份的旧症状是同一个隐藏项在两处显示成两个名字。<c>G1-BH</c> 里
    /// "两份列表构建要不要合成一份"仍是未决项，这一笔只收那条规则，没替你决定 view model 的事。）
    /// 57 → 54 一笔收三族（学习面板"模式角标"三件套 4 个组件各抄 4 行、只差前景候选表用哪张，
    /// 进 <c>StudyPanelPalette.ApplyModeBadge</c>，两族一次消掉；两张自绘图表控件各抄一份
    /// 逐字相同的 <c>AddLine</c> 三步，进 <c>StudyChartGeometry</c>）。行为钉 <c>StudyPanelBadgeTests</c>：
    /// 注入"少涂描边"与"线段画回起点"两处变异，恰好各红 1 条、另 2 条不动，验过。）
    /// 54 → 53 收一族（学习统计的分位数 <c>Percentile</c> 共 3 份：扣分原因面板、成绩总览面板各一份
    /// 逐字 24 行，<c>StudyAnalyticsInternals</c> 又一份——**只差空数组返回什么**（面板 -100、分析服务 0）。
    /// 进 <c>Services/StudyStatistics</c>，把哨兵做成参数由调用方显式给，5 个调用点改走家。
    /// 没替 <c>G1-BK</c> 统一那个分歧：同一场没数据的统计，面板显示刻度尽头、学习报告显示 0 dB，
    /// 这个不一致是既有行为，收口只保证"算法只有一份"，改口径要拍板。
    /// 行为钉 <c>StudyStatisticsTests</c>：注入"去掉钳位"红 2 条（-3 与 5 两行）、
    /// "插值系数取反"红 2 条（0.5 与 0.95 两行）、"哨兵写死成 0"红 1 条，各自只红该红的，验过。）
    /// 53 → 51 一笔收两族（噪声折线的两条判据各 2 份逐字抄本：ResolveFirstTailIndex 13 行×2
    /// （噪声曲线控件 + 噪声分布面积图控件）、ResolveLevel 16 行×2（面积图控件 + 噪声分布组件），
    /// 进 Views/Components/StudyNoiseSeriesRules 的 FirstTailIndex / LevelOf，7 个调用点改走家。
    /// 这两族的错法都不报错：尾窗起点差一格＝动态段多一格或少一格；档线差一格＝同一个 dB 值
    /// 在面板与图表里被涂成两种颜色。它两旁边的 ResolveLayerSourceCounts 已经漂开（面积图那份多一个
    /// isStaticSeries 分支），不在这一笔之内。行为钉 StudyNoiseSeriesRulesTests：注入"截止点不算进
    /// 尾窗"（>= 改成 >）红 3 条——家自己的边界那条，加两个各自消费它的图表渲染钉；
    /// 注入"档线改成右闭"（< 改成 <=）红 2 条，各自只红该红的，验过。）
    /// 这一笔对**漂移族数无影响**（191 族不动、族内站点 1368 不动）：两族各是"2 处 1 种体"，
    /// 按定义不构成漂移族，所以收益是"这两段逻辑从 2 处变成 1 处"，别把它记成族数下降。）
    /// </summary>
    /// 51 → 50 收一族（明暗档取值口径 <c>NormalizeThemeMode</c> 两份逐字 9 行：设置页 view model
    /// <c>SettingsViewModels</c> 与主题领域服务 <c>SettingsDomainServices</c> 各一份，进早就存在的家
    /// <c>Services/ThemeAppearanceValues</c>（它旁边就是 <c>NormalizeThemeColorMode</c>／
    /// <c>NormalizeSystemMaterialMode</c>／<c>NormalizeWallpaperColorSource</c> 三兄弟），3 个调用点改走家。
    /// 读的一侧和写的一侧各按各的理解归一化，症状是同一个设置在界面上显示成一档、落盘被另一档覆盖；
    /// "忽略大小写"这一格不能漂——盘上有早期写的 <c>Dark</c>。
    /// 行为钉 <c>ThemeAppearanceValuesTests.NormalizeThemeMode_ReturnsKnownValue</c> 8 格：
    /// 注入"改成区分大小写"恰好红 2 格（<c>DARK</c> 与 <c>Follow_System</c>），另 6 格不动，验过。）
    /// 50 → 49 收一族（世界时钟的城市名：两个组件各一份逐字相同的 20 行 ResolveCityName，
    /// Clock AirApp 的 ClockAirAppTimeFormatter 还有第三份同样的兜底写法——三份都进
    /// Services/ClockCityNames（FallbackName + Lookup + ResolveForHostWidget）。
    /// 顺带把两组件各抄一份的 12+12 条表并成一份（一份写中文、一份写 Unicode 转义，
    /// 逐字普查对这种数据抄本是瞎的，只能靠人对内容）。表选择口径两端本来就不同，
    /// 没替它们统一（挂 G1-BL）。行为钉 ClockCityNamesTests 14 格：删掉裸 Time 那一步只红
    /// "Samoa Time" 一格、把英文字表的 OrdinalIgnoreCase 去掉只红 "asia/shanghai" 一格，验过。）
    /// 49 → 48 收一族（星期表头那 6 行循环：DateWidget 与 MonthCalendarWidget 各一份逐字相同，
    /// 并进 <c>CalendarWeekLabels.ApplyHeaders(isChinese, blocks)</c>，两个 <c>UpdateWeekdayHeaders</c>
    /// 方法整个删掉、调用点直接走家。上一笔刚把两份的取表分支收成 <c>For(isChinese)</c>,
    /// 这一步收的是"取到表之后往哪儿写"。行为钉 <c>CalendarWeekLabelsTests</c> 4 格：
    /// 注入"最后一列不填"（<c>i &lt; blocks.Count - 1</c>）恰好红 3 格、空表那格照旧绿,验过。）
    /// 48 → 47 收一族（<c>FirstNonEmpty</c> 8 行逐字两份，而且**跨两个二进制**：
    /// Core 的 <c>LauncherRuntimeMetadata</c>（启动器读部署目录/可执行文件名）与宿主的
    /// <c>PlondsManifestParser</c>（解析更新清单的显示名与动作）。进
    /// <c>core/.../Shared/Text/TextValue.cs</c>，7 个调用点改走家。
    /// 这类跨二进制的漂开不会崩，只会让同一份清单在两处"哪个字段算有值"判断不同：
    /// 一边把只含空格的串当有效值，标题就显示成空白、回退链断掉。
    /// 行为钉 <c>TextValueTests</c> 4 格：注入"空白串算有值"（<c>IsNullOrWhiteSpace</c> 改成
    /// <c>IsNullOrEmpty</c>）恰好红那 2 格敏感的、另 2 格不动，验过。）
    /// 47 → 46 收一族（<c>GetWindowHandle</c> 8 行逐字两份，都在 Platform：
    /// <c>WindowsMainWindowDesktopLayerService</c> 与 <c>WindowsWindowPassthroughServices</c>，
    /// 7 个调用点）→ 进 <c>platform/.../WindowHandles.cs</c> 的 <c>OfWindow</c>。
    /// 这条判据的要害是"没有可用句柄时给 0、不抛"：两家的调用方一律是"0 就跳过这次 Win32 调用"
    /// （窗口还没实化、正在关闭都走到这里），在这里抛出去只会把一次装饰/穿透刷新变成崩溃。
    /// 行为钉 <c>WindowHandlesTests</c> 2 格（headless 实测：未 Show 与已 Show 都是 0）。
    /// 覆盖边界写死在测试注释里，并且是量过的：把 <c>?? IntPtr.Zero</c> 改成 <c>-1</c>、
    /// 把 <c>?.</c> 改成 <c>!.</c> 都不红（headless 给的是"有平台句柄对象、Handle 值为 0"，那两条支路根本不走），
    /// 而把返回值整体改成别的数两格立刻红——所以这两格钉的是能观察到的那半条契约，不是整段实现。）
    /// 46 → 44 一笔收两族（两块自绘图表控件把池化点缓冲各抄一份：
    /// <c>EnsurePointBufferCapacity</c> 14 行×2、<c>ReleasePointBuffer</c> 6 行×2）→ 进
    /// <c>Views/Components/PointBufferPool</c> 的 <c>RentPointsAtLeast(ref buffer, required)</c> 与
    /// <c>ReturnPoints(ref buffer)</c>；缓冲仍归各控件自己的字段，家只接 <c>ref</c>，
    /// 不为一小段共享逻辑多塞一层对象。曲线控件 <c>Dispose</c> 里那段"只把超大的缓冲还回去"
    /// 的内联复制也改成走家（尺寸门槛留在调用方，那才是它的本意）。
    /// 这一族的错法不是崩：还两次会污染池子（别处读到脏数组），该还时没还就是每帧漏一个数组——
    /// 两块控件都是 60fps 重绘的。行为钉 <c>PointBufferPoolTests</c> 4 格，三条支路各有独立注入点，
    /// 逐条量过：去掉"够长就不重租"只红 1 格、去掉"非正数请求不动它"只红另 1 格、
    /// 去掉"还回池子前判空"只红第 3 格（那条会把 null 还进池子），各不影响其余格。）
    /// 44 → 43 收一族（十个字段各钳一遍的 23 行，<c>NoiseFramePipeline</c> 与 <c>StudyAnalyticsService</c>
    /// 各一份逐字相同）→ 让类型自己管自己的合法区间：<c>StudyAnalyticsConfig.ClampedToLegalRanges()</c>，
    /// 3 个调用点改走家。区间数值一笔没动（那是策略，要改得拍板），只把"谁能说这几个数合法"收成一处。
    /// 行为钉 <c>StudyAnalyticsConfigRangeTests</c> 4 格：十个字段各钉"两端越界→贴边、界内→原样、
    /// 非钳制字段→原样带过"；把 <c>FrameMs</c> 上限 250 改成 300 只红"贴上界"那一格，验过。）
    /// 43 → 42 收一族（状态行文案那条 25 行的状态机：噪声曲线组件与环境组件各一份逐字相同，
    /// **连本地化键都是同一批 <c>study.environment.status.*</c>**——也就是说曲线组件一直在显示环境组件那套词。
    /// 进 <c>Views/Components/StudyNoiseStatusText.Describe(snapshot, localize)</c>：家收整段判据（含顺序），
    /// 取词交给调用方的 <c>L</c>，于是两个 <c>ResolveStatusText</c> 方法**整个删掉**、调用点直接内联走家
    /// （不像上一笔那样留下转手壳，所以族数真的降了一格）。
    /// 行为钉 <c>StudyNoiseStatusTextTests</c> 9 格把判定顺序逐格钉住；
    /// 两处真实修正：① 我按猜测把 <c>(Ready, Noisy)</c> 写成 ready，被这条判据当场红一次——实测"吵"排在"就绪"之前，
    /// 期望值按实测改正；② 注入"把暂停挪到吵之后"恰好红 <c>(Paused, Noisy)</c> 那一格，验过。）
    /// 42 → 41 收一族（RSS 那对组件取网图的 36 行：Cnr 与 Ifeng 各一份**逐字相同**，
    /// 连 <c>BrowserUserAgent</c> 常量都是两份）→ 进 <c>Views/Components/RemoteImageBitmap.GetAsync(httpClient, url, ct)</c>，
    /// 两个抄本整个删掉、3 个调用点改走家（没有留转手壳）。
    /// <c>DailyArtworkWidget</c> 那份**不算抄本**：它带 referrer 重试、客户端超时 10 秒（RSS 两个 8 秒），
    /// 属另一种实现，这一笔没动它；三个客户端要不要并成一个也是策略问题（挂 #G1-BC 队列注）。
    /// 这一族值得收的理由不是行数：两个抄本的 scheme 校验都来自 <c>NormalizeHttpUrl</c>，
    /// 少了它就把 RSS/第三方给的串直接喂给 <c>HttpClient</c>——外链那一族已经量到 3 份"连校验都没有"的抄本，
    /// 同一条判据在取图这条路上也不能靠"抄的时候记得抄全"。
    /// 行为钉 <c>RemoteImageBitmapTests</c> 11 格，判据的四个错法各有独立注入点，逐条量过：
    /// 去掉"取消原样抛"只红 <c>RethrowsCancellation</c>；把归一化换成原串只红"不该发请求"的 3 格
    /// （file/ftp/javascript；空串与 null 那两格仍绿——它们在 <c>HttpRequestMessage</c> 构造处就炸了，
    /// 被通用 catch 兜成同一个 null，这是实测到的覆盖面边界，不是"全钉住了"）；
    /// 去掉 <c>memory.Position = 0</c>、去掉 UA 那行，各只红成功那一格。
    /// 载体是假 handler 而不是真连保留端口：真连的写法下"守卫挡住了"与"请求发了但失败"
    /// 返回值都是 null，把守卫整段删掉测试照样绿——上一轮那两个注入点就是这么暴露"没钉住"的。
    /// 41 → 40 收一族（桌面图标层宿主那条两趟判据：<c>WindowsMainWindowDesktopLayerService</c> 与
    /// <c>WindowsWindowPassthroughServices</c> 各一份逐字相同的 35 行，连带 <c>EnumWindows</c>／<c>FindWindowEx</c>
    /// 两套 P/Invoke 也各抄一份）→ 进 <c>platform/.../DesktopIconHost.cs</c>：5 个调用点直接走家，
    /// 两个抄本与两套声明一起删掉（家自己只留一份）。判据与取数分开：<c>ResolveFrom(tops, findChild)</c>
    /// 是纯两趟查找、可注入可测，<c>Resolve()</c> 只负责真调 Win32。
    /// 两个名字各起一半（不叫 <c>Resolve</c> 重载）是有理由的：同名两体本身就是一条漂移族，
    /// 用 <c>Resolve</c>／<c>ResolveFrom</c> 之后"收口一族"才在漂移面上如实反映（这里仍不动，理由见下）。
    /// 漂移族 188 **不动**，这是判据口径而不是漏收：这条尺子数的是"同名 ≥2 种体"，
    /// 两份**逐字相同**的抄本只算一种体，所以它从来只在逐字那面记着——本笔就是那一面从 41 降到 40。
    /// 行为钉 <c>DesktopIconHostTests</c> 6 格，四个错法各有注入点、逐条量过：
    /// 第一趟不往 <c>WorkerW</c> 里钻 → 红 2 格；第二趟整个不找 → 红 2 格；
    /// 类名大小写写错（真机上表现为"永远找不到宿主"且不报错）→ 红 4 格；
    /// 找不到时返回第一个顶层窗口而不是 0 → 只红 <c>ReturnsZero</c> 那格。
    /// 覆盖面边界：真调 <c>EnumWindows</c> 的那个重载这里量不到（headless 没有真桌面窗口），
    /// 判据一格都不落在它身上——写清楚，别当成"整条链都钉住了"。
    /// 40 → 39 收一族（与下面 188 → 187 同一笔）：WinRT 操作"结果类型是谁"这条判据，三个服务各抄一份——
    /// <c>LocationService</c> 与 <c>WindowsSmtcMusicControlService</c> 逐字相同（21 行），
    /// <c>WindowsNotificationListener</c> 是等价换写法（一元泛型那步并成一个条件、接口那步用 && 连起来）。
    /// 逐条比过确认三者语义相同，所以三份一起改调 <c>Services/WinRtOperationResult.ResolveType</c>、
    /// 三个抄本整个删掉（不留转手壳）。
    /// 值得收的理由是错法不报错：认不出结果类型时调用方一律把 <c>null</c> 当成"这次不算"，
    /// 症状是定位/通知/播放状态静默拿不到值。
    /// 行为钉 <c>WinRtOperationResultTests</c> 6 格，四条注入点逐条量过：
    /// 去掉 arity 判定 → 只红 <c>WrongArity</c> 一格——**这一格第一版没钉住**：夹具原本写成
    /// <c>TwoArgOperation&lt;TSecond&gt;</c>（其实只有一元参数），注入后照样绿，是这条判据把我猜的"钉住了"
    /// 当场否证，改成两个类型参数才真钉住；<c>FullName</c>→<c>Name</c> → 红 <c>NonGeneric</c> 与 <c>WrongArity</c>
    /// 两格；接口那一趟整个不扫 → 同样红这两格；认不出时回 <c>typeof(object)</c> 而不是 <c>null</c> →
    /// 红 <c>PlainType</c> 与 <c>WholeFullName</c> 两格。夹具是自家声明的
    /// <c>Windows.Foundation.IAsyncOperation&lt;T&gt;</c>（离线可跑，宿主本来就只能按名字认它），
    /// 外加"简单名相同、命名空间不同"的干扰项——"按全名认"这条口径靠这两格一起钉。
    /// 本笔（await 那半族）**逐字面 39 不动**，这条要说清，不然下一个人会以为降了却查不到：
    /// <c>AwaitWinRtOperationAsync</c> 在 <c>LocationService</c> 与 <c>WindowsSmtcMusicControlService</c>
    /// 那两份看着"排版不同而已"，实测差别只有一处——一家写 <c>taskObject\n.GetType()</c>、另一家 <c>taskObject.GetType()</c>，
    /// 归一化后留下一个空格（首个不同点在正文第 446 字符）。**这就是这把尺子的第二条盲点**：
    /// 成员链换行会让"语义逐字相同"的两份只显现在漂移面、不显现在逐字面——
    /// 我按"2×N 逐字族"排的那份队列因此系统性漏掉这一类抄本（本轮实测漏了这一个）。
    /// 两条盲点（多行签名、成员链换行）都记进 #G1-BC，改判据那一笔单独跑。
    /// 2026-09-23 **第二次改判据**（同一常量因此上涨，账记在这儿）：扫描前先过一层
    /// <c>LogicalLines</c>——左括号在本行没闭合、又还没见到大括号的行，与后一行并成一条逻辑行。
    /// 动机是上一条记账里量出的那个盲点：签名换行写的成员声明此前在两个面上都不显形。
    /// 实测影响：认领声明 <c>5426 → 5761</c>（<b>+335</b> 条此前隐身的声明，之前只能写"规模未查证"的
    /// 那条盲点现在有数了）、逐字族 <c>39 → 45</c>、漂移族 <c>186 → 184</c>。
    /// 三个数分开读：逐字面 **+6 是本来就存在、以前看不见的抄本族**（不是有人新抄），
    /// 这 6 族是这笔真正的产出——它们进了 #G1-BC 的队列头；
    /// 漂移面 <b>−2 是上一笔 squeeze 的延续效果</b>（两处只差排版的"两种体"并成一种）；
    /// 认领量 +335 说明此前那个"下界"低了多少。上限随之改 45 / 184，
    /// 声明下限从 5400 抬到 <b>5700</b>——这条floor 是用来抓"判据自己塌掉"的，
    /// 判据变强后不抬就等于把抓瞎的余量放宽了。
    /// 判据自证（正对照，不是只看数字变了）：本笔前完全隐身的 <c>RemoteImageBitmap.GetAsync</c>
    /// 与 <c>WinRtAsyncAwait.AwaitAsync</c> 现在被认成声明；负对照同前一条判据自测。
    /// 报告面 <c>dump-drift-methods.py</c> 已同口径改（它的 <c>logical_lines</c> 与这里逐字对应）；
    /// <c>dump-dup-methods.py</c> 随后也补成同口径（族数两面同数），但**成员清单**仍差 2 项
    /// （那份面把 <c>Retry</c>／<c>TryRemoveExistingPackage</c> 掉出逐字面）——点名队列以本闸门为准，偏差挂在 #G1-BC。
    /// 44 → 43 收一族（<c>SetTimeZoneService</c> 的 9 份"换绑之后立刻重画"外壳 → <c>TimeZoneServiceBinding.Attach</c>）：
    /// 八处逐字六行 + 一处同形状，各写的是"调 <c>TimeZoneServiceBinding.Replace</c> 赋值给字段，紧接着刷新自己"。
    /// 收口的理由是**那条顺序**而不是行数：只留前一半就是"时区换了但组件不重画"，且只有等下一次时区变化才自愈——
    /// 现场症状是"改完时区表盘不动"，不报错。用 <c>ref</c> 传字段也是这条判据的一部分：
    /// 刷新必须看到换好之后的字段值（注入"先刷新再换绑"恰好红 <c>Attach_WritesTheField_BeforeRefreshing</c> 一格，验过）。
    /// 行为钉 <c>TimeZoneServiceBindingTests</c> 新增 3 格（组件形状的 Holder 夹具），三条注入逐条量过：
    /// 顺序反过来红 1 格；换绑后不刷新红 3 格；不写回字段红 3 格。
    /// 收完仍留着的那半不是抄本：每个组件得说出"我刷新的是哪个方法"，那是各自的工作。
    /// </summary>
    private const int IdenticalBodyFamilyCeiling = 43;

    /// <summary>
    /// 今天实测：189 个方法名存在 ≥2 种体。只能降，要升必须在这里写清理由。
    /// 190 → 189 是真收口（与上面 43 → 42 同一笔）：<c>ResolveStatusText</c> 原本三处两体
    /// （两个学习组件逐字相同 + <c>MusicControlViewModel</c> 另一种实现）——两个抄本并掉之后
    /// 这个名字只剩音乐控件那一种实现，整族消失（族数以 C# 闸门为准：python 报告面这一轮的行数解析器
    /// 没匹配上，所以我没有重量站点数，不在这儿写站点账）。
    /// 192 → 191 是真收口（与上面 54 → 53 同一笔）：分位数 <c>Percentile</c> 一家 3 份抄本
    /// （2 种体）全改调 <c>StudyStatistics.Percentile</c>，这个名字连同它的 2 种体一起消失——
    /// 实测族 192→191、族内站点 1371→1368，逐处比对只有 <c>Percentile</c> 这一项变动。
    /// 191 → 192 是**改判据**（又一处"判据瞎了"，与下面 193 → 192 同一类）：
    /// <c>void Foo() { }</c> 这种"有实现、但什么都不做"以前与接口方法的声明一起被当成"没有体"跳过，
    /// 于是全仓 28 处空实现全体隐身；现在空实现算一种体（规范成空串），有一族因此显形。
    /// 这笔账要求同时钉住"空实现"本身：见下面 <c>emptySites</c> 的指名锚点。
    /// 192 → 191 是真收口（与上面 71 → 68 同一笔）：<c>NormalizeAutoRefreshIntervalMinutes</c>
    /// 的 6 处各抄本改调早就存在的 <c>RefreshIntervalCatalog.Normalize</c>，这个名字连同 3 种体一起消失。
    /// 再往前 193 → 192 是**改判据**，不是收口（一处代码都没删）：
    /// <c>NormalizeBody</c> 过去只认"签名行尾的 <c>=></c>"，两种真实写法被它整条丢掉——
    /// ① Allman 箭头体（<c>private void UpdateLanguageCode()</c> 换行 <c>=></c> 表达式）；
    /// ② 插值字符串里的 <c>{message}</c> 被当成方法体的开括号，一路扫到类末尾
    ///    （实测 AirAppRuntimeLogger.cs:7 的 <c>Info</c>、PlondsApplyPaths.cs:39 的 <c>GetSnapshotPath</c>）。
    /// 修完后与 <c>dump-drift-methods.py</c> **逐处对齐**：两边同为 192 族、5449 处声明（收口前），
    /// 站点清单（文件+行号）完全相同——这条口径差异（以前是 196 对 193、差 3 族没对过）就此结清。
    /// 组成变化：<c>nameof</c>／<c>VALUES</c>／<c>SelectionOption</c>／<c>UpdateMonitoringLeaseState</c> 掉出
    /// （它们的"多种体"是吞出来的假体）；<c>GetSessionsAsync</c>／<c>GetThemeBrush</c>／<c>SetValue</c> 新进来
    /// （接口默认实现真的有多种写法，这是以前被假体挤掉的漏报）。
    /// 被这条修反转量的是最大的两族：<c>L</c> 50 处但只有 7 种体（旧数 43 处 / 18 种体），
    /// <c>UpdateLanguageCode</c> 11 处 / 3 种体（旧数 11 处 / 10 种体）——它两仍是待收口的最大两族；
    /// 但 <c>L</c> 的 50 处里 44 处是**单条语句的转手**（<c>return _localizationService.GetString(...)</c>），
    /// 按这把尺子的口径（单语句转手不算复制了一份逻辑）它不构成收口目标，别再为凑族数去动它。
    /// </summary>
    /// 191 → 190 是真收口（与上面 50 → 49 同一笔）：ResolveCityName 三处抄本改调家之后
    /// 只剩 AirApp 一个入口，这一族连同它的 2 种体一起消失；族内站点 1368 → 1363
    /// （ResolveCityName 少 3 处，被普查算成方法的 new Dictionary 少 2 处——两张表并成两张、
    /// 声明处从 4 个文件位降到 2 个）。
    /// 189 → 188 是真收口（与上面 42 → 41 同一笔）：<c>TryDownloadBitmapAsync</c> 两份逐字抄本并掉后
    /// 只剩 <c>DailyArtworkWidget</c> 那一种实现，整族消失。
    /// 这一笔同时量出**这把尺子的一个盲点**（不是这笔做错了什么）：
    /// <c>RemoteImageBitmap.GetAsync</c> 的签名把参数换行写，两个面的正则都要求
    /// <c>\([^)]*\)</c> 在同一行内闭合，于是它根本不被认成声明——
    /// 实测：把 9882a21 与当前树各跑一份普查、按 (方法名, 所在文件) 对账，
    /// 少 59 条 / 多 29 条、净 <c>−30</c>（5456 → 5426），而新增的 29 条里没有 <c>GetAsync</c>。
    /// 后果要说准：家自己的入口不计入认领量，所以这一笔是"降 2、没加回 1"；更要紧的是
    /// **两份多行签名的逐字抄本会同时躲开这两把尺子**。这类抄本现在到底有没有、有多少，我没数过——
    /// 这条盲点的实际大小是**未查证**，不是"已排除"。记进 #G1-BC 队列尾：
    /// 改判据那一笔要单独跑、单独记账（认多声明只会让上限涨，那笔账要写清是改判据不是有人又抄）。
    /// </summary>
    /// 188 → 187 是真收口（与上面 40 → 39 同一笔）：<c>ResolveWinRtOperationResultType</c> 原本三处两体
    /// （两份逐字 + 一份等价换写法），三份一起改调家之后这个名字只剩家那一种实现，整族消失。
    /// 上一笔（<c>DesktopIconHost</c>）族数不动、这一笔动——区别在漂移面数的是"同名 ≥2 种体"：
    /// 两份**逐字相同**的抄本不进漂移面，而"行为相同、写法不同"的三份进。引用数字时别把两笔混着说。
    /// 2026-09-23 **改判据**（不降反平的记账，读数字前先看这条）：<c>Normalise</c> 现在先过一层
    /// <c>Squeeze</c>——只在两个标识符字符之间留一个空格，其余空白一律去掉。
    /// 动机是收 <c>AwaitWinRtOperationAsync</c> 时量到的第二条盲点：两份只差 <c>taskObject</c> 换行再
    /// <c>.GetType()</c> 的实现（首异点在正文第 446 字符）被当成两种体，逐字面因此看不见"换行写法不同"的抄本。
    /// 改完当场重测三个面：逐字族 <c>39 → 39</c>、漂移族 <c>186 → 186</c>、认领声明 <c>5426 → 5426</c>，
    /// 一个数都没动——不是修得没效果，是那对抄本已在上一笔被删掉了（效果靠自证：<c>Normalise_IgnoresWhereAMemberChainWasLineBroken</c>
    /// 同时钉正向"换行两份必须相等"与反向"少一个调用、换一个标识符必须还不等"，另加
    /// <c>return null</c> 不许被压成 <c>returnnull</c>）。上限不必改；要改的那笔在下面。
    /// 顺带把第一条盲点的规模量出来了（先前只能写"未查证"）：全仓**签名行在本行不闭合左括号**的成员声明共
    /// <c>919</c> 处（方法、record 与构造函数都算在内，实测于扫描目录集合），
    /// 这一批在两个面上都不被认成声明 ⇒ 逐字面与漂移面同时看不见它们，5760 这个认领量是**下界**。
    /// 修它要动三处的签名匹配（python 两个面 + 本类的两条正则），会让上限上涨——那是单独一笔，
    /// 记账必须写清"上涨=改判据，不是有人又抄"。
    /// 187 → 186 是真收口：<c>AwaitWinRtOperationAsync</c> 原本三处三体，
    /// <c>LocationService</c> 与 <c>WindowsSmtcMusicControlService</c> 两份改调
    /// <c>Services/WinRtAsyncAwait.AwaitAsync(operation, AsTaskGenericMethodDefinition, ct)</c>
    /// 并整个删掉抄本，这个名字于是只剩 <c>WindowsNotificationListener</c> 一种实现，族消失。
    /// 那两份没并进来是有意的：它内部多一句 <c>ConfigureAwait(false)</c>（部分调用点外面还再配一次），
    /// 换的是"续接在哪个上下文"——不替它三挑，登记在 #G1-BC 等拍板。
    /// 同一族的 <c>ResolveAsTaskGenericMethod</c>（三处三体）实测差别更大（一家带 try/catch 与形参校验，
    /// 另两家 LINQ 挑第一个），同样没动，一并挂在 #G1-BC。
    private const int DriftFamilyCeiling = 184;

    /// <summary>
    /// 漂移普查认领的声明处数下限（今天实测 5760）。掉到 5400 以下＝判据在丢声明，先看下面那段对账。
    /// 钉这个不是为了查新增，是为了查**判据自己塌掉**：
    /// 上面那两处 bug 都是"少认声明"，族数看着像收口（193→189），实际是普查瞎了。
    /// 只冻族数会被这种错法骗过去，冻住认领量就不会。
    ///
    /// 5456 → 5426 这一路是**记账欠的**，不是判据丢了声明：这条注释原先停在 9882a21 那天，
    /// 之后陆续提交的收口各自删掉的私有声明没回写到这儿。逐条点名对过（把 9882a21 与当前树
    /// 各 `git archive` 一份跑同一份普查，按 (方法名, 所在文件) 比声明数）：
    /// **少 59 条、多 29 条，净 −30**（5456 − 59 + 29 = 5426，两边都对得上，不是估的）。
    /// 少的是被家替掉的私有抄本与删掉的空壳/死缝，按名字点齐（括号内为处数）：
    /// AddLine(2)、BuildLauncherHiddenFallbackDisplayName(2)、BuildMonogram(2)、CleanupPendingDeletions(2)、
    /// CleanupPendingDeletionDirectory(1)、字典表被认成的 Dictionary(4)、EnsureComponentLibraryPreviewWarmup(1)、
    /// EnsurePointBufferCapacity(2)、FirstNonEmpty(2)、GetWindowHandle(2)、InitializeSettingsIcons(1)、
    /// NormalizeConfig(2)、NormalizeExistingDirectory(2)、NormalizeExistingFile(2)、NormalizeThemeMode(2)、
    /// Percentile(3)、QueuePlacementPreviewRefresh(1)、ReleasePointBuffer(2)、RemovePlacementPreviewImage(1)、
    /// RemovePlacementPreviewImages(1)、ResolveCityName(2)、ResolveFirstTailIndex(2)、ResolveLevel(2)、
    /// ResolveStatusText(2)、TryDownloadBitmapAsync(2)、UpdateSettingsViewportInsets(1)、UpdateWeekdayHeaders(2)、
    /// ResolveDesktopIconHost(2)、以及 <c>EnumWindows(...)</c> 那两行调用点被算成声明(2)、
    /// ResolveWinRtOperationResultType(3)、AwaitWinRtOperationAsync(2)。
    /// 多的是各家新增的入口（29 条：Monogram.From、TextValue.FirstNonEmpty、WindowHandles.OfWindow、

    /// StudyStatistics.Percentile、PointBufferPool 两条、StudyNoiseSeriesRules 两条、CalendarWeekLabels 两条、
    /// ClockCityNames 五条、ExistingPath 两条、AirAppPendingDeletionDirectory 两条、
    /// LauncherHiddenItemNames.FallbackDisplayName、ThemeAppearanceValues.NormalizeThemeMode、
    /// StudyAnalyticsConfig.ClampedToLegalRanges、SecondHandChoice.Enforce、StudyNoiseStatusText.Describe、
    /// StudyChartGeometry.AddLine、DesktopIconHost 的 Resolve/ResolveFrom/FindChildByClass 三条、
    /// WinRtOperationResult.ResolveType）。普查把 <c>new Dictionary(...)</c> 这类对象初始化也算成声明，
    /// 所以并表也会动这个数——一笔没含糊：降的全是"真少了一条声明"。
    /// </summary>
    private const int DriftCensusSiteFloor = 5700;

    private static readonly string[] ScanDirectories =
        ["core", "desktop", "airapp", "install", "platform", "packaging", "mobile"];

    private static readonly string[] SkipPathParts = ["obj", "bin", "artifacts", "node_modules"];

    private static readonly Regex AllmanSignature = new(
        @"^\s*(private|internal|public)\s+(static\s+)?(async\s+)?[\w<>?\[\],\. ]+?\b(?<name>[A-Za-z_]\w*)\s*\([^)]*\)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex OpeningBraceOnly = new(@"^\s*\{\s*$", RegexOptions.Compiled);

    private static readonly Regex DriftSignature = new(
        @"^\s*(?:public|private|protected|internal)?\s*" +
        @"(?:static\s+|sealed\s+|override\s+|virtual\s+|async\s+|partial\s+|new\s+)*" +
        @"(?:[A-Za-z_][\w<>\[\]?,\. ]*?\s+)?(?<name>[A-Za-z_]\w*)\s*(?:<[^>]*>)?\s*\([^)]*\)\s*(?:=>.*)?\{?\s*\}?\s*$",
        RegexOptions.Compiled);

    private static readonly string[] NonMethodNames =
    [
        "if", "for", "foreach", "while", "switch", "catch", "using", "lock", "return",
        "get", "set", "add", "remove", "init", "when", "where", "select", "from",
    ];

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private static readonly Regex StringLiteral = new(@"""[^""]*""", RegexOptions.Compiled);

    [Fact]
    public void IdenticalMethodBodyFamilies_DoNotGrow()
    {
        var repoRoot = RepoRoot();
        var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var path in EnumerateSources(repoRoot))
        {
            var lines = LogicalLines(path);
            var relative = Relative(repoRoot, path);
            // index 在吃完一个方法体后会跳到 } 之后：与脚本一致，方法体内部的局部函数不算"第二个方法"。
            for (var index = 0; index < lines.Length - 2;)
            {
                var signature = AllmanSignature.Match(lines[index]);
                if (!signature.Success || !OpeningBraceOnly.IsMatch(lines[index + 1]))
                {
                    index++;
                    continue;
                }

                var end = FindBodyEnd(lines, index + 1);
                if (end < 0)
                {
                    index++;
                    continue;
                }

                var body = lines
                    .Skip(index + 2)
                    .Take(end - index - 2)
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0 && !line.StartsWith("//", StringComparison.Ordinal))
                    .ToList();
                // 判的是"复制了一份逻辑"，不是"占了两行"：按深度 0 的分号数语句数，
                // 单条语句（哪怕写成两行）就是转手/委托，不是重复真源。
                // 实测样本：18 个组件的 ApplyCellSize 收口成
                //     ComponentDesignMetrics.ApplyCellSize(
                //         ref _currentCellSize, cellSize, UpdateAdaptiveLayout);
                // 之后仍是一族"逐字相同的两行体"——按行数它会一直算成重复，那是指标在骗人。
                if (CountStatements(body) < 2)
                {
                    index = end + 1;
                    continue;
                }

                var key = $"{signature.Groups["name"].Value}|{body.Count}|{string.Join('\n', body)}";
                if (!groups.TryGetValue(key, out var sites))
                {
                    sites = [];
                    groups[key] = sites;
                }

                sites.Add($"{relative}:{index + 1}");
                index = end + 1;
            }
        }

        var families = groups.Where(pair => pair.Value.Count >= 2).ToList();
        AssertEqual(
            IdenticalBodyFamilyCeiling,
            families.Count,
            "逐字相同的方法体族数变了：变多说明有人又抄了一份（先收口再改上限，或在上面写理由并挂待办）；" +
            "变少是好事，把常量改成新的数就是收口的记账");
    }

    [Fact]
    public void SameNameDifferentBodyFamilies_DoNotGrow()
    {
        var repoRoot = RepoRoot();
        var bodiesByName = new Dictionary<string, NameTally>();
        var emptySites = new List<string>();

        foreach (var path in EnumerateSources(repoRoot))
        {
            var lines = LogicalLines(path);
            var relative = Relative(repoRoot, path);
            for (var index = 0; index < lines.Length; index++)
            {
                var raw = lines[index];
                var trimmed = raw.Trim();
                if (trimmed.Length == 0 ||
                    trimmed.StartsWith("//", StringComparison.Ordinal) ||
                    trimmed.StartsWith("/*", StringComparison.Ordinal) ||
                    trimmed.StartsWith("*", StringComparison.Ordinal))
                {
                    continue;
                }

                var match = DriftSignature.Match(raw);
                if (!match.Success)
                {
                    continue;
                }

                var name = match.Groups["name"].Value;
                if (NonMethodNames.Contains(name, StringComparer.Ordinal))
                {
                    continue;
                }

                var normalized = NormalizeBody(lines, index);
                if (normalized is null)
                {
                    continue;
                }

                if (!bodiesByName.TryGetValue(name, out var tally))
                {
                    tally = new NameTally();
                    bodiesByName[name] = tally;
                }

                if (normalized.Length == 0)
                {
                    emptySites.Add($"{Path.GetFileName(relative)}|{name}");
                }

                tally.Record(relative, normalized);
            }
        }

        // 空实现不能钉"数量下限"——修掉一个桩就会假红；钉**指名锚点**才是"这条区分还活着"的证据。
        // `void Foo() { }` 曾经被当成"没有这个方法"整条跳过，于是 28 处空实现全体隐身。
        Assert.Contains("RssReaderWidget.axaml.cs|ApplyCellSize", emptySites);
        Assert.True(
            emptySites.Count >= 1,
            $"一处空实现都没数到（当前 {emptySites.Count} 处）：空实现与无实现声明的区分又被合并回去了");

        // 与脚本同门槛：≥3 处声明、≥2 种体、且横跨 ≥3 个文件，才算"同名不同体"要收口的族。
        // 认领量下限先查：判据瞎了会让族数"假收口"，那时候比族数没意义。
        var censusSites = bodiesByName.Values.Sum(tally => tally.Sites);
        Assert.True(
            censusSites >= DriftCensusSiteFloor,
            $"漂移普查只认领到 {censusSites} 处声明，低于下限 {DriftCensusSiteFloor}（今天实测 5760）。" +
            "族数没变也说明判据在丢声明：查 NormalizeBody 又漏掉了哪种成员写法（历史上漏过 Allman 箭头体与插值字符串的大括号）");

        var driftFamilies = bodiesByName.Count(pair => pair.Value.VariantCount >= 2 &&
                                                      pair.Value.Sites >= 3 &&
                                                      pair.Value.Files >= 3);
        AssertEqual(
            DriftFamilyCeiling,
            driftFamilies,
            "同名不同体的漂移族数变了：变多说明同一个名字又多了一种实现（这正是「家被绕开」的形态，" +
            "先收口或写理由改上限）；变少是好事，把常量改成新的数就是收口的记账");
    }

    /// <summary>
    /// 与脚本一致：折叠空白、字符串字面量换成占位符，所以"只差一句文案"不算第二种体。
    /// 三种成员形态分开处理，因为本仓**同时**用它们（Allman 大括号、行尾 <c>=></c>、另起一行的 <c>=></c>）：
    /// 只认其中一种就会把别的形态的声明整条丢掉或整段吞掉——2026-09-22 实测这样丢了 53 处声明、5 个整文件。
    /// </summary>
    [Fact]
    public void Normalise_IgnoresWhereAMemberChainWasLineBroken()
    {
        // 判据自身的正/负对照：改 Squeeze 之后必须同时成立这两条，否则"放松排版"会变成"放松语义"。
        var wrapped = "return taskObject\n    .GetType()\n    .GetProperty(\"Result\")?\n    .GetValue(taskObject);";
        var flat = "return taskObject.GetType().GetProperty(\"Result\")?.GetValue(taskObject);";

        Assert.Equal(Normalise(flat), Normalise(wrapped));

        // 反向：真正不同的两份不许被压成一样（少一个调用、换一个标识符都要还能区分）。
        Assert.NotEqual(Normalise(flat), Normalise("return taskObject.GetType().GetProperty(\"Result\");"));
        Assert.NotEqual(Normalise("return value;"), Normalise("return other;"));
        // 两个标识符之间的空格必须留着，否则 `return null` 会跟 `returnnull` 混成一类。
        Assert.Equal("return null;", Normalise("return\r\n    null;"));
    }

    /// <summary>
    /// 把"签名换行写"的成员声明并成一条逻辑行：本行左括号没闭合、又还没见到大括号，就吃掉下一行。
    /// 为什么要这一步（2026-09-23 量出来的第二条盲点）：签名正则要求 <c>\\([^)]*\\)</c> 在同一行闭合，
    /// 而本仓有 <c>919</c> 行是"左括号在本行不闭合"（方法、record、构造函数都算），
    /// 它们此前既不进逐字面也不进漂移面、更不计入认领量——两份这样写的逐字抄本会同时躲开两把尺子。
    /// 只并在括号未闭合且无 <c>{</c> 的行上：带方法体的签名一旦见到大括号就停，不会把函数体吸进签名行。
    /// </summary>
    private static string[] LogicalLines(string path)
    {
        var lines = File.ReadAllLines(path);
        var joined = new List<string>(lines.Length);
        for (var index = 0; index < lines.Length; index++)
        {
            var current = lines[index];
            while (!current.Contains('{') &&
                   OpenCount(current) != CloseCount(current) &&
                   index + 1 < lines.Length)
            {
                index++;
                current = current.TrimEnd() + " " + lines[index].Trim();
            }

            joined.Add(current);
        }

        return joined.ToArray();

        static int OpenCount(string value) => value.Count(c => c == '(' || c == '[');
        static int CloseCount(string value) => value.Count(c => c == ')' || c == ']');
    }

    private static string? NormalizeBody(string[] lines, int signatureIndex)
    {
        var signatureLine = lines[signatureIndex].TrimEnd();
        var trimmed = signatureLine.Trim();
        var ahead = NextNonEmptyLine(lines, signatureIndex + 1);
        var arrow = signatureLine.IndexOf("=>", StringComparison.Ordinal);
        var allmanBrace = !signatureLine.Contains('{') && ahead.StartsWith('{');
        // 表达式体优先，且**先于**"本行有没有大括号"的判断：插值字符串里的 `{message}` 也是大括号，
        // 把它当方法体的开括号会一路扫到类末尾（实测 AirAppRuntimeLogger.cs:7 的 `Info`）。
        // 认"这一行以 `;` 或 `=>` 收尾 + 括号配平"，才不会把 K&R 写的 `{ …() => …; }` 误当成表达式体。
        if (arrow >= 0 && !allmanBrace &&
            (trimmed.EndsWith(";", StringComparison.Ordinal) || trimmed.EndsWith("=>", StringComparison.Ordinal)) &&
            signatureLine.Split('{').Length == signatureLine.Split('}').Length)
        {
            return Normalise(ArrowTail(lines, signatureIndex + 1, signatureLine[(arrow + 2)..]));
        }

        if (!signatureLine.Contains('{') && !allmanBrace)
        {
            // 没有大括号、下一行也不是 `{`：要么 `=>` 另起一行（那是实现），要么是无实现的声明。
            // 两者必须分开：null＝"这不是实现"（接口方法、abstract 声明），
            // 空串＝"有实现但什么都不做"（`void Foo() { }`），后者是站点——"声明了契约却不执行"正是这条尺子要抓的。
            return ahead.StartsWith("=>", StringComparison.Ordinal)
                ? Normalise(ArrowTail(lines, signatureIndex + 2, ahead[2..]))
                : null;
        }

        var depth = 0;
        var started = false;
        var body = new List<string>();
        for (var cursor = signatureIndex; cursor < lines.Length; cursor++)
        {
            var text = lines[cursor];
            depth += text.Split('{').Length - 1;
            depth -= text.Split('}').Length - 1;
            if (text.Contains('{'))
            {
                started = true;
            }

            if (cursor > signatureIndex)
            {
                body.Add(text.Trim());
            }

            if (started && depth <= 0)
            {
                break;
            }
        }

        return Normalise(string.Join(" ", body));
    }

    /// <summary>把 <c>=></c> 之后的表达式收成一条语句：吃到下一个深度 0 的 <c>;</c> 为止，绝不越过别的成员。</summary>
    private static string ArrowTail(string[] lines, int start, string head)
    {
        var parts = new List<string>();
        if (head.Trim().Length > 0)
        {
            parts.Add(head.Trim());
        }

        var depth = parts.Sum(part => part.Split('{').Length - part.Split('}').Length - 1);
        if (depth <= 0 && parts.Count > 0 && parts[^1].EndsWith(";", StringComparison.Ordinal))
        {
            return string.Join(" ", parts).TrimEnd(';').Trim();
        }

        for (var cursor = start; cursor < lines.Length; cursor++)
        {
            var text = lines[cursor].Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (depth <= 0 && (text.StartsWith('}') || text.StartsWith("//", StringComparison.Ordinal)
                    || text.StartsWith("/*", StringComparison.Ordinal) || text.StartsWith('*')))
            {
                break;
            }

            parts.Add(text);
            depth += text.Split('{').Length - text.Split('}').Length - 1;
            if (depth <= 0 && text.EndsWith(";", StringComparison.Ordinal))
            {
                break;
            }
        }

        return string.Join(" ", parts).TrimEnd(';').Trim();
    }

    private static string NextNonEmptyLine(string[] lines, int start)
    {
        for (var cursor = start; cursor < lines.Length; cursor++)
        {
            if (lines[cursor].Trim().Length > 0)
            {
                return lines[cursor].Trim();
            }
        }

        return string.Empty;
    }

    private static string Normalise(string text) =>
        Canonical(StringLiteral.Replace(Squeeze(text), "\"S\"").Trim());

    /// <summary>
    /// 压掉排版，只留语义之间的分界：一段代码写成一行还是三行、成员链在哪换行，都不该影响
    /// "这两份是不是同一份实现"的判定。此前 `taskObject` 换行写 <c>.GetType()</c> 的两份抄本
    /// 被当成不同体（实测首异点在正文第 446 字符），于是逐字面看不见它、只有漂移面看得见——
    /// 那条盲区是 2026-09-23 收 AwaitWinRtOperationAsync 时量出来的。
    /// 只在两个标识符字符之间留一个空格，其余空白一律去掉；不折叠引号内的差异（字符串随后整体抹成 "S"）。
    /// </summary>
    private static string Squeeze(string text)
    {
        var chars = new List<char>();
        for (var i = 0; i < text.Length; i++)
        {
            if (!char.IsWhiteSpace(text[i]))
            {
                chars.Add(text[i]);
                continue;
            }

            var end = i;
            while (end < text.Length && char.IsWhiteSpace(text[end]))
            {
                end++;
            }

            var left = chars.Count > 0 ? chars[chars.Count - 1] : '\0';
            var right = end < text.Length ? text[end] : '\0';
            if (IsWordChar(left) && IsWordChar(right))
            {
                chars.Add(' ');
            }

            i = end - 1;
        }

        return new string(chars.ToArray());

        static bool IsWordChar(char value) => char.IsLetterOrDigit(value) || value == '_' || value == '$';
    }

    /// <summary><c>{ }</c> 与 <c>{}</c> 统一成空串：空实现只许有一个规范形状，否则同一种写法会被数成两种体。</summary>
    private static string Canonical(string text) =>
        text.Length == 0 || Whitespace.Replace(text, string.Empty) == "{}" ? string.Empty : text;

    private static int FindBodyEnd(string[] lines, int openingIndex)
    {
        var depth = 0;
        var seen = false;
        for (var cursor = openingIndex; cursor < lines.Length; cursor++)
        {
            foreach (var character in lines[cursor])
            {
                if (character == '{')
                {
                    depth++;
                    seen = true;
                }
                else if (character == '}')
                {
                    depth--;
                }
            }

            if (seen && depth == 0)
            {
                return cursor;
            }
        }

        return -1;
    }

    private static IEnumerable<string> EnumerateSources(string root)
    {
        foreach (var directory in ScanDirectories)
        {
            var full = Path.Combine(root, directory);
            if (!Directory.Exists(full))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(full, "*.cs", SearchOption.AllDirectories))
            {
                var normalized = Path.DirectorySeparatorChar + file + Path.DirectorySeparatorChar;
                if (SkipPathParts.Any(part => normalized.Contains(
                        Path.DirectorySeparatorChar + part + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
                {
                    continue;
                }

                yield return file;
            }
        }
    }

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path)
        .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "LanMountainDesktop.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root.");
    }

    /// <summary>数深度为 0 的分号：一条语句可能跨多行，字符串里的分号不算。</summary>
    private static int CountStatements(IEnumerable<string> body)
    {
        var statements = 0;
        foreach (var line in body)
        {
            var depth = 0;
            var inString = false;
            foreach (var character in line)
            {
                if (character == '"')
                {
                    inString = !inString;
                }
                else if (!inString && (character == '{' || character == '(' || character == '['))
                {
                    depth++;
                }
                else if (!inString && (character == '}' || character == ')' || character == ']'))
                {
                    depth--;
                }
                else if (!inString && character == ';' && depth == 0)
                {
                    statements++;
                }
            }
        }

        return statements;
    }

    private sealed class NameTally
    {
        private readonly HashSet<string> _variants = new(StringComparer.Ordinal);

        private readonly HashSet<string> _files = new(StringComparer.Ordinal);

        public int Sites { get; private set; }

        public int VariantCount => _variants.Count;

        public int Files => _files.Count;

        public void Record(string file, string normalizedBody)
        {
            Sites++;
            _variants.Add(normalizedBody);
            _files.Add(file);
        }
    }

    private static void AssertEqual(int expected, int actual, string because)
    {
        // 只许降不许升：变多就是有人又抄了一份；变少也红，但红的是"把上限改小"这笔记账动作。
        Assert.True(
            actual == expected,
            $"{because}{Environment.NewLine}当前 {actual} 处，上限 {expected} 处。" +
            (actual > expected
                ? "变多：先收口，或写明理由并挂待办后再改上限"
                : $"变少是进展，把常量改成 {actual} 就是这笔账"));
    }
}

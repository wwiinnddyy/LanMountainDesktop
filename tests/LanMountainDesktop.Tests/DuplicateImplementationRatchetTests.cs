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
    /// 43 → 42 收一族（RSS 那对组件取网图的 36 行：Cnr 与 Ifeng 各一份**逐字相同**，
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
    /// <c>dump-dup-methods.py</c> 随后也补成同口径（族数两面同数）。
    /// 那笔留在账上的一句话（"成员清单仍差 2 项：报告面把 <c>Retry</c>／<c>TryRemoveExistingPackage</c> 掉出逐字面"）
    /// 是**猜的、方向还反了**——本笔按实测改回：把闸门这一族的成员清单整份导出来与报告面对着点，
    /// 报告面**一项都不缺**，那两名也不在逐字面上（它们是漂移面的族：<c>Retry</c> 实测 11 站 / 8 种体 / 3 文件）。
    /// 真正的差在另一边，而且差一族：<c>IsSameOrChildPath</c> 两份抄本（<c>InstallerPathGuard.cs:120</c>
    /// 与 <c>PlondsPackageStore.cs:118</c>）排版不同——安装器那份把 <c>return string.Equals(</c> 的实参一行一个
    /// （体 6 行），宿主那份挤在两行里（体 5 行）——逐字面的旧键带着"行数 + 修剪后的行 join"，于是这两份被认成两种体，
    /// 各 1 站 ⇒ 逐字面不成人族；漂移面又因"同名只有一种体"（它比较前已 Squeeze）不显形。
    /// **换句话说：换个版式的逐字抄本此前两头隐身**，这正是上面那条签名盲点的同一族问题，只是这次在体里。
    /// 44 → 43 收一族（<c>SetTimeZoneService</c> 的 9 份"换绑之后立刻重画"外壳 → <c>TimeZoneServiceBinding.Attach</c>）：
    /// 八处逐字六行 + 一处同形状，各写的是"调 <c>TimeZoneServiceBinding.Replace</c> 赋值给字段，紧接着刷新自己"。
    /// 收口的理由是**那条顺序**而不是行数：只留前一半就是"时区换了但组件不重画"，且只有等下一次时区变化才自愈——
    /// 现场症状是"改完时区表盘不动"，不报错。用 <c>ref</c> 传字段也是这条判据的一部分：
    /// 刷新必须看到换好之后的字段值（注入"先刷新再换绑"恰好红 <c>Attach_WritesTheField_BeforeRefreshing</c> 一格，验过）。
    /// 行为钉 <c>TimeZoneServiceBindingTests</c> 新增 3 格（组件形状的 Holder 夹具），三条注入逐条量过：
    /// 顺序反过来红 1 格；换绑后不刷新红 3 格；不写回字段红 3 格。
    /// 收完仍留着的那半不是抄本：每个组件得说出"我刷新的是哪个方法"，那是各自的工作。
    /// 43 → 42 收一族（Launcher ↔ 宿主那一帧 IPC 的"读满 N 字节"：
    /// <c>LauncherCoordinatorIpcClient</c> 与 <c>LauncherCoordinatorIpcServer</c> 各一份逐字相同的
    /// <c>ReadExactAsync</c>，实测两体归一化后完全相同、家也是逐字搬过去）→
    /// 进 <c>Ipc/LauncherIpcStreamIo.cs</c>，两处私有实现删除、4 个调用点走家（两端都带
    /// <c>ConfigureAwait(false)</c>，家内部不提前后行为逐字不变）。
    /// 为什么这条判据不许各写一遍：管道流允许一次只给一部分，把一次 <c>ReadAsync</c> 的返回值当成
    /// "读完了"就是帧长与帧体错位——症状是对端解出垃圾或超时；两端各错一半时最难看。
    /// 行为钉 <c>LauncherIpcStreamIoTests</c> 5 格（自己控制每次给几个字节的假流），四条注入逐条量过：
    /// 不循环 → 红"小块流"与"提前收线"两格；把"对端收线"当成读满 → 只红后者；
    /// 吞掉取消 → 只红 <c>PropagatesCancellation</c>；边界写成 <c>&lt;=</c> → 红三格（含"空缓冲区不碰流"）。
    /// 家与两份抄本相同这一点不是靠眼睛：脚本按花括号配对切出两份原文，断言二者逐字相等、
    /// 且与家的方法体（去掉访问符后）相等，任一不等就中止不落盘。
    /// 42 → <b>43 是改判据那一笔，不是"有人又抄了一份"</b>（这个账本自己定的规矩：上限变化分两笔记）：
    /// 逐字面的键原先是"方法名 + 体行数 + 修剪后的行 join"，比较前不压排版，于是<b>换个版式的逐字抄本</b>
    /// 整族隐身——上面 <c>IsSameOrChildPath</c> 那一族就是它放出去的。
    /// 改法两处：键里去掉行数、体的比较文本先过 <c>Squeeze</c>（与漂移面同一份实现）。
    /// 判据自证不靠眼睛：同一棵树改前改后各导一份成员清单对着点，差集恰好一族、方向只有"闸门多认"；
    /// 独立的报告面 <c>dump-dup-methods.py</c>（本来就压排版）改后与闸门<b>族数同 43、成员与站数双向差集均为 0</b>
    /// ——两面各写一遍、结果逐条对平，这才是"清单可信"的证据，单看一个数不算。
    /// 去掉行数会不会把两份不同的体并成一个？不会：<c>Squeeze</c> 只吃空白，深度 0 的分号逐个留下，
    /// 语句数不同的文本压完也不相等（同一棵树上 <c>Dispose</c> 的 6 行体与 12 行体仍是两族，实测）。
    /// 43 → 42 收一族（<c>IsSameOrChildPath</c> 就是上面那笔刚露出来的那一族，实为三份抄本、跨两个二进制：
    /// <c>InstallerPathGuard</c> 公开一份、<c>PlondsPackageStore</c> 私有两份之一、
    /// <c>PlondsPreparedPackageInstaller.EnsureChildPath</c> 把同一个判定内联在抛异常的外壳里（三个条件换了顺序））→
    /// 进 <c>Core/IO/PathContainment.IsSameOrChild</c>，三份实现删除、6 个调用点走家；
    /// 行为逐字搬过去没顺手改（含 <c>OrdinalIgnoreCase</c> 与"两侧先绝对化、空串会抛"两条口径），
    /// 要改的那半挂在 #G1-BI／#G1-BC 等拍板，不在这笔里代做。
    /// 同一笔把漂移族 184 → 183：三个 <c>EnsureChildPath</c> 外壳内联的判定搬走之后只剩"判一下、不对就抛"，
    /// 归一化字符串后三种体并成一种（族要的是"同名且 ≥2 种体"），族随判定的收口一起消失。
    /// 行为钉 <c>PathContainmentTests</c> 12 格（10 格真值/假值对 + 大小写现状一格 + 空串抛异常一格），
    /// 三处注入逐个量过（数字是测出来的，不是推的）：只比前缀不补分隔符 → 红 2 格（两格"共享前缀的兄弟目录"）；
    /// 去掉 <c>TrimEnd</c> → 红 2 格（根目录 <c>C:\</c> 那一格 + parent 带尾分隔符那一格）；
    /// 判定写反（拿 parent 去 <c>StartsWith(child + 分隔符)</c>）→ 红 5 格（连大小写现状那一格一起红）。
    /// 顺带量到一处**钉不住的格**：child 带尾分隔符那一格在"去掉 TrimEnd"的注入下仍然绿——它的真值由
    /// <c>StartsWith(parent + 分隔符)</c> 那条给，压根不经过 <c>TrimEnd</c>。这条注在测试注释里，
    /// 免得下一只手把它当成"已经覆盖 TrimEnd"的那一格。
    /// 42 → 41 收一族、183 → 182 同笔（探活"<c>pid</c> 还活着吗"三份抄本全在启动器里：
    /// <c>LaunchResultBuilder.TryGetLiveProcess</c> 与 <c>StartupAttemptRegistry</c> 那份逐字相同（这一族），
    /// <c>LauncherGuiCoordinator</c> 另有一种只回 bool 的形状（漂移族 <c>TryGetLiveProcess</c> 实测 3 站 / 2 种体 / 3 文件））
    /// → 进 <c>Startup/LiveProcessProbe.cs</c>，判据一份实现、按所有权分两个入口：
    /// <c>IsLive</c> 自己释放句柄、<c>TryGet</c> 把句柄交出去（"拿到就要释放"写进签名注释）。
    /// 收口的理由不只是抄本：<b>当时 7 个调用点全写成 <c>out _</c></b>——前两份把 <c>Process</c> 递出来又没人释放，
    /// 每探一次活死留一个内核句柄，而探活是反复做的（协调器状态回灌、收养判定、清理陈旧登记），
    /// 属于慢慢涨、不报错的那种症状。改完之后 <c>out _</c> 0 处、真需要句柄的只有收养那 1 个点。
    /// 行为钉 <c>LiveProcessProbeTests</c> 5 格，三条注入逐个量过，并如实记下两处**钉不住**：
    /// 守卫（<c>processId &lt;= 0</c>）与 <c>catch</c> 彼此遮蔽——守卫写成 <c>&lt; 0</c> 全绿、
    /// 把 <c>catch</c> 改成 <c>throw;</c> 也全绿（非法 pid 走不到 catch）；只有 <c>HasExited</c> 反向红 2 格。
    /// 要钉"探不到不许抛给调用方"得有"pid 合法但正在退"的夹具（要真起真退进程），离线门里没做 → 未覆盖，不是已排除。
    /// 41 → 40 收一族（本机平台标识："os-架构"两份逐字相同 <c>ResolveCurrentPlatform</c> 在
    /// <c>SettingsDomainServices</c> 与 <c>UpdateOrchestrator</c>；同一段架构 <c>switch</c> 又被
    /// <c>UpdateManifestMapper</c> 与 <c>GitHubReleaseUpdateService</c> 的 <c>SelectPreferredInstallerAsset</c>
    /// 各抄一遍——一个值在同一条链上算四次）→ 进 <c>Services/PlatformIdentifiers.cs</c>，
    /// 两处调用点走 <c>CurrentPlatformId</c>、两处走 <c>CurrentArchitectureToken</c>，值逐字不变。
    /// 这套 token 是对外契约（发布资产名里写着 <c>files-windows-x64.zip</c>），所以钉的是"全小写 / 默认档 <c>x64</c> /
    /// 那根连字符"三条；三处注入实测：架构两档对调红 2 格、去掉连字符红 2 格、默认档写成 <c>amd64</c> 红 4 格。
    /// 这一笔**没**动漂移族（182 不变，闸门实测仍绿）：两个 <c>SelectPreferredInstallerAsset</c> 的选包算法本来就不一样
    /// （一个 <c>ScoreAsset</c> 排序、一个 <c>ScoreWindowsInstallerAsset</c> 过滤），搬走的只是共用的架构 token——
    /// "同一个二进制里两条更新路径按不同打分挑安装包"这件事本身是重复真源队列里的一条，记进 #G1-BC。
    /// 40 → 39 收一族（点一条热搜就打开它的外链：百度与 B站两个热搜组件各抄一份逐字相同的 12 行处理）→
    /// 进 <c>Views/Components/HotItemClick.cs</c>，判据 <c>TryGetIndex</c>（左键 + sender 是那个 <c>Border</c> +
    /// <c>Tag</c> 落在 <c>[0, itemCount)</c>）与接线的 <c>Open</c> 分开，两个组件各留一行"我的列表是哪份、url 取哪个字段"。
    /// 为什么值得收：少上界那一格是点最后一条越界抛（崩在点击里）、少左键那一格是中键也弹浏览器，都不报错。
    /// 行为钉 <c>HotItemClickTests</c> 9 格（构个 <c>Border</c> 不要 headless 会话，用普通 <c>[Fact]</c>／<c>[Theory]</c>
    /// 就够——刻意不占 <c>AvaloniaFact</c> 名额，headless 会话是今天偶发红的那条轴）。四路注入逐个量过：
    /// 去掉上界红 2 格、去掉负数下界红 2 格、把认 <c>Border</c> 换成认 <c>TextBlock</c> 红 2 格、去掉左键判定红 1 格。
    /// <c>Open</c> 里"真事件"那几行离线造不出 <c>PointerPressedEventArgs</c> → 未覆盖，不是已排除。
    /// 39 → 38 收一族（时区在两个时钟编辑器的下拉里怎么写：两份逐字相同的六行 <c>FormatTimeZone</c>，
    /// 症状是"同一个时区在两个面板里长得不一样"，不报错也不崩）→ 进 <c>Views/Components/TimeZoneDisplayLabel.cs</c>，
    /// 瞬间参数化后两个编辑器各留一行调用。形状钉 <c>TimeZoneDisplayLabelTests</c> 7 格，夹具一律
    /// <c>CreateCustomTimeZone</c> 自造时区（不查系统时区库——"这台机器有没有 America/Los_Angeles"
    /// 不该决定门是红是绿）；两路注入实测：去掉小时的两位补零红 6 格、把"按当下瞬间取偏移"改成
    /// <c>BaseUtcOffset</c> **红 0 格**（自定义时区没有夏令时区间，测不出差别）→ 那一支记为未覆盖，
    /// 要补的是带 <c>AdjustmentRule</c> 的时区加夏/冬两个瞬间。
    /// 38 → 37 收一族、同笔把漂移族 182 → 181：路径段压形 <c>SanitizePathSegment</c> 全仓四份——
    /// 宿主 <c>PlondsPackageStore</c> 与安装器 <c>InstallerPlondsClient</c> 逐字相同（跨两个二进制，
    /// 这类抄本的错法是"改了宿主忘了安装器"，而包目录命名两边必须一致），
    /// <c>PlondsPreparedPackageInstaller</c> 是同政策换兜底词的一份，
    /// <c>WhiteboardNotePersistenceService</c> 是**另一种判据**（另带 120 字符截断，故意没并进来——
    /// 并进来就是拿别人的策略换掉自己的）。三份私有实现删除、5 个调用点走 <c>Core/IO/PathSegmentSanitizer</c>，
    /// 兜底词仍由调用方给（<c>unknown</c> / <c>0.0.0</c>）：统一它是产品决定，不在这笔里代做。
    /// 行为钉 <c>PathSegmentSanitizerTests</c> 8 格，注入实测：去掉 <c>Trim</c> 红 2 格。
    /// 另有两格是**我自己第一版预期写错、被这两格抓出来的**，如实留在账上当凭据：
    /// ① 剪空之后回的是调用方给的那个兜底词（<c>"   "</c> + <c>0.0.0</c> → <c>0.0.0</c>，是政策不是 bug）；
    /// ② 制表与换行在 Windows 上也算非法字符，**先被换成** <c>_</c> 再剪，所以永远走不到兜底词
    /// （<c>Tab+LF</c> → <c>"__"</c>）——"先换后剪"这个顺序本身就是判据。
    /// 形状只用两平台都非法的 <c>/</c> 钉（反斜杠在 Linux 上合法，拿它钉会让门的红绿取决于跑在哪台机器上）。
    /// 37 → 34 收三族（10 个组件被下推推荐信息服务时各写同一条不变量：<c>_svc = x ?? 自己的默认实例;</c> 紧接着
    /// <c>if (_isAttached) _ = RefreshXxxAsync(forceRefresh: false);</c>，只有"刷新方法是哪个"不同；
    /// 逐字面量到 3 族各 2 站）→ 进 <c>Views/Components/RecommendationServiceBinding.Attach</c>，
    /// 与 <see cref="TimeZoneServiceBinding"/> 同形（<c>ref</c> 字段 + <c>Func&lt;bool&gt;</c> 挂载 +
    /// <c>Func&lt;Task&gt;</c> 刷新），各组件只留一行。第 11 个组件 <c>ZhiJiaoHubWidget</c> **不算抄本**：
    /// 它没有默认实例回落、也没有挂载判定，字段直接赋值（那是另一种判据，别顺手统一）。
    /// 语义逐字保留、没改良：刷新仍"发出去不管"（同步抛出照旧上抛，钉住），默认实例仍归各组件自己 new
    /// （十个 <c>RecommendationDataService</c> 是不是各自留一份缓存是另一件事，挂在 #G1-BC）。
    /// 行为钉 <c>RecommendationServiceBindingTests</c> 4 格，两处注入各红 2 格（实测）：
    /// 把顺序改成"先刷后换"红两格（刷新看到的还是默认实例）、去掉 <c>isAttached()</c> 那一步红两格。
    /// </summary>
    /// 34 → 32 收两族（"设置变了之后重刷卡片"：8 个组件各写同样三步 —— 作废推荐缓存、重读本组件的自动刷新参数、
    /// 只在已挂载时强制刷一次；只有"重读哪个参数、刷哪个方法"不同）→ 同一家再加一个 AfterSettingsChange，
    /// 组件侧各留一行。第一步收的是"怎么作废"这个动作（调用点传 _recommendationService.ClearCache），不传服务本身：
    /// 家只需要这一点，收窄之后整条顺序可钉，也不必为测试造十七个成员的假实现。
    /// 三处注入逐个量过：作废挪到刷新之后 → 红 4 格（全红，顺序真被钉住）；去掉挂载守卫 → 红 1 格；
    /// 去掉"重读参数"那步 → 红 2 格。剩下 7 个组件的 RefreshFromSettings 不在这条流程里
    /// （时钟、课程表、可移动存储、学习环境与、白板、世界时钟、职教 hub 各读自己的设置项），
    /// 是结构性差异不是抄本，逐一点名跳过、没硬并。
    /// 顺手记一条量歪的经过：第一次注入"去掉挂载守卫"报 0 红，因为 needle 命中的是同文件另一个方法
    /// （Attach）里的同名守卫——注入脚本必须指到具体那一个方法，否则"测不出红"会被误读成"钉不住"。

    /// 32 → 31 收一族（兄弟页 VM 那对逐字相同的 6 行 Dispose：守卫 _disposed → 退订 Settings.Changed → 置位）
    /// → 进 Services/Settings/SettingsChangedSubscription.UnsubscribeOnce，两个 VM 的 Dispose 各变一行；
    /// 事件与处理器类型由家固定，调用方只交自己的方法组。
    /// 这一笔顺带改了配对守卫的判据（不是放松）：退订收进家之后"订的处数 − 退的处数"不再是 1:1
    /// （家那一条 settings.Changed -= handler 服务多个订阅点），于是全局计数换成按文件配对——
    /// 一个文件里 += 的处数不许超过"本地 -= + 调用 UnsubscribeOnce 的额度"，唯一越界的仍是被核实无害的
    /// SettingsWindowService。双向重量过：删掉 DevSettingsPageViewModel 那行退订 → 越界文件 1 变 2、守卫红；
    /// 恢复并重建后 → 绿；棘轮三把在 31 / 182 全绿。

    /// 31 → 33 是**改判据**（一行代码都没收口，只是尺子此前瞎了一格）：三份实现共用的"并签名续行"判据
    /// 写的是"括号不等就并"，于是 <c>});</c> 这类**多闭合**的收尾行也被当成没闭合，连着吃掉紧跟其后的
    /// 那条声明。实测全仓"本行无 <c>{</c> 且括号不等"的 7960 行里 <c>3796</c> 行是多闭合，
    /// 只有 <c>4164</c> 行真是要并的续行。改成"只在左&gt;右时并"之后新显形 3 族：
    /// <c>Retry</c>（<c>AirAppPackageInstaller.cs:135</c> 与启动器 <c>AirAppInstallerService.cs:196</c>，
    /// 27 行重试循环逐字相同）、<c>TryRemoveExistingPackage</c>（同两个文件 <c>:100</c> / <c>:161</c>）、
    /// <c>ResolveAsStreamForReadMethod</c>（<c>WindowsNotificationListener.cs:391</c> 与
    /// <c>WindowsSmtcMusicControlService.cs:488</c>）。同时有 1 族掉出逐字面：<c>DeleteFileWithRetry</c>
    /// —— 判据改正后它的体是"单语句转手调一个多行 lambda"，正是 2026-09-22 那条"按语句数 ≥2"要排除的形状
    /// （它在漂移面仍在：3 站 / 2 种体 / 最大同体组 2）。净 <c>31 +3 −1 = 33</c>。
    /// **这笔最该记住的是方法教训**："两个实现互比族数与站点清单"验不出**共用判据**的缺陷——
    /// 上一笔正是拿这个互证把"Retry 不在逐字面"判成"它只是漂移面的一个族"，判据本身瞎着，互证却一路绿。
    /// 所以互证只在两边判据是各写一遍的时候才算证据；共用一份逻辑的那一段，只能靠已知正例喂它。

    /// 33 → 31 收两族（**真收口**，与下面 193 → 189 同一笔）：Core 的 <c>AirAppPackageInstaller</c> 与
    /// 启动器的 <c>AirAppInstallerService</c> 各带一套私有重试件——<c>Retry</c> 那 27 行两份逐字相同
    /// （120/250/500ms、只吞 IOException 与 UnauthorizedAccessException、耗尽后原样抛出），外加三个
    /// 单语句转手与一份 <c>RetryDelays</c>。家早就在（<c>core/IO/FileOperationRetryHelper</c>，
    /// 档位与抛法逐条相同），这两处属"家立着、调用点绕过去"。同批把"旧包删不动就挪进 .pending
    /// 等下次收尾"那 13 行两份逐字相同收进 <c>AirAppPendingDeletionDirectory.RemoveOrMoveToPending</c>
    /// ——那个类本就是这套暂存机制的家，它旁边 <c>CleanupAfterInstall</c> 的注释早在 2026-09 就写着
    /// "这里之前有 3 份实现"。唯一的<b>行为差别</b>是多了一句可注入的重试告警（<c>FailureNotice</c>）：
    /// 抄本闷声重试，家会报"第几次失败、多久之后再试"。
    /// 行为钉 <c>AirAppPendingDeletionDirectoryTests</c> 两条（删得动 → 当场消失且不留 <c>*.pending</c>；
    /// 文件本来就不在 → 不抛也不留）；"挪进 pending"那一支**未覆盖**并写明原因（同进程的锁会把
    /// <c>File.Move</c> 一起挡住，那样测到的是"挪也失败"）。实测净 <c>−109</c> 行（两家各删 73、各加 5，家加 27）。

    /// 31 → 30 收一族（<c>ResolveAsStreamForReadMethod</c> 两份逐字相同，<c>WindowsNotificationListener</c>
    /// 与 <c>WindowsSmtcMusicControlService</c> 各一份）→ 进 <c>Services/WinRtReflection.cs</c>。
    /// 同批把 <c>ResolveWinRtType</c>（三份逐字相同、每份只一句，所以本来不进逐字面）与散在三个服务里的
    /// 两个装配标识（共 8 处字面量）一起收进同一个家，并配字面量守卫
    /// <c>WinRtAssemblyIdentityLiterals_LiveInExactlyOnePlace</c>——这一族的风险不是崩而是"能力安静消失"，
    /// 只比方法体的尺子看不见"同一句 <c>Type.GetType</c> 换个写法"。
    /// <c>AsTask</c> 那个泛型方法定义怎么挑<strong>没</strong>并：三家判据不同（一家带 try/catch 与形参校验），
    /// 那一支等拍板（#G1-BC），这里只把它们的装配名换成 <c>ResolveProjectionType</c>。
    /// 行为钉 <c>WinRtReflectionTests</c> 3 条（夹具含 <c>Bar(int)</c> 与 <c>Foo()</c>/<c>Foo(int,int)</c>
    /// 两个方向的干扰项）；本机真找得到 <c>AsStreamForRead</c> 与否**未覆盖**，headless 里判不出来。

    /// 30 → 29 收一族（<c>PrepareTargetDirectory</c>：宿主 <c>PlondsPreparedPackageInstaller</c> 与安装器
    /// <c>FilesPackageInstaller</c> 各一份逐字相同 9 行、共 3 个调用点跨两个二进制）→ 新家
    /// <c>core/.../Deployment/DeploymentStaging.Prepare</c>。
    /// 量的时候顺手把<strong>同一条不变量的读侧</strong>也数了一遍："带 <c>.partial</c> 的部署目录等于不存在"
    /// 这句在四个二进制里写了 8 遍（<c>AppVersionProvider</c>、<c>AppDeploymentLocator</c> ×2、
    /// 启动器 <c>DeploymentLocator</c> ×3、<c>InstalledProductInspector</c>、<c>PlondsUpdateApplier</c>），
    /// 另有两处"只落标记不清空"的写侧——都收进同一个家的 <c>IsPartial</c> / <c>MarkPartial</c>。
    /// 这些读侧是单语句形状，<b>本来就不进逐字面</b>（"语句数 ≥2"排除它们是对的），所以这一族只 −1，
    /// 但收口的理由正是它们：写侧漏落标记不会报错，读侧全体就把半写完的部署当成可用版本挑中。
    /// "摘标记"那一半故意没并（宿主 / 启动器 / 安装器三处写法不同，还带重试与 .destroy 联动）。
    /// 行为钉 <c>DeploymentStagingTests</c> 4 条。

    /// 29 → 28 收一族（<c>BuildDailySelection</c>：黄历"宜/忌"每天挑几条那 22 行，
    /// <c>DateWidget</c> 与 <c>LunarCalendarWidget</c> 各一份逐字相同，共 4 个调用点）→ 进
    /// <c>LunarCalendarService.BuildDailySelection</c>。四张候选池表本来就在这个家（2026-09-23 那次
    /// 按"字面数据表"收的），属"表是一家、算法各写一遍"。顺带把两个调用点各写死的种子
    /// <c>17</c> / <c>29</c> 提成 <c>AuspiciousSalt</c> / <c>IllicitSalt</c>——种子写两遍时
    /// 同一天两块屏会给出两版宜忌，且两边都看着正常。实测净 <c>−13</c> 行（两家各删 34 / 各加 8，家加 56）。
    /// 行为钉 <c>LunarDailySelectionTests</c> 5 格（稳定、两种子分列、不许重复且池不够就少给、
    /// 空池与非正数兜底、中文空格 vs 其余 ", "）。

    /// 28 → 27 收一族（<c>RefreshWordAsync</c>：每日一词 1x1 与 2x2 两块面板各一份<b>逐字相同的 45 行</b>
    /// 单飞取数协议）→ 拆成两个家：<c>Views/Components/ComponentFeedRefresh</c>（协议本身：不挂载/已忙不起跑、
    /// 起前先置忙再画按钮、换发时先换后取消旧源、等完<b>重新问一次</b>挂载与取消、取不到与抛异常都画失败态而
    /// <c>OperationCanceledException</c> 静默、收尾只在"这把还是我的"时清字段并复位）
    /// 与 <c>Views/Components/DailyWordFeed</c>（组 query 调服务 + "成功但载荷为空也算没取到" + 起前两下的顺序）。
    /// <b>这笔一开始是倒挂的</b>：只把协议收走之后普查报 28 → 29——<c>BeginWordRefresh</c>（2 行）与
    /// <c>RequestWordAsync</c>（8 行）成了新的两份逐字抄本，族数反而涨。这说明"收一族"不能只按行数判完成，
    /// 得等普查说没有新名字才算收干净；补上第二个家之后实测 27，成员清单与上一轮逐行比过，
    /// <b>只掉 <c>RefreshWordAsync</c> 一项、没有新名字进来</b>。
    /// 两块面板各自留下的只有一处 per-widget 的落地 lambda（控件不同，那是本来的差别）。
    /// 行为钉 <c>ComponentFeedRefreshTests</c> 7 格 + <c>DailyWordFeedTests</c> 4 格；
    /// "先换发再取消旧源"那句<strong>没钉</strong>并写明原因（单飞守卫下从公开入口走不到，属防御性写法）。

    /// 27 → 26 收一族（<c>EstimateCellSpan</c>：像素换格数那 5 行，<c>FusedDesktopPlacementMath</c> 与
    /// <c>DesktopWidgetWindow</c> 各一份逐字相同、共 4 个调用点）→ 进同一命名空间里早就存在的
    /// <c>DesktopPlacementMath</c>（网格几何算式本来就住这儿）。漂开的症状不报错：同一个组件
    /// "从桌面拖出来的大小"与"浮窗请求回来的大小"差一格。实测净 <c>−3</c> 行（家加 23，两家各删 13 / 各加 2）。
    /// 行为钉 <c>DesktopPlacementMathTests</c> 3 格（加在已有的 1 格间接测试旁边）：
    /// 半点必须 AwayFromZero（银行家舍入会红）、最小 1 格、几何不合法给 1 格而不是 0 或抛。

    /// 26 → 23 一次收三族（<c>TryGetCachedWithoutProbe</c> / <c>TryGetCachedAfterProbe</c> / <c>UpdateCache</c>，
    /// 宿主 <c>AppSettingsService</c> 与启动器 <c>LauncherSettingsService</c> 各一份逐字相同，
    /// 连同一组 4 个静态字段）→ 进 <c>Services/SettingsSnapshotCache&lt;T&gt;</c>。
    /// 这就是 #G1-T 那条"两个设置服务是近似克隆"里<b>真逐字相同</b>的那三块；两边的锁
    /// （各自 <c>CacheGate</c> 罩住"读盘→落盘→写缓存"整段）与快照类型仍各写各的，
    /// 收进来会改变临界区大小，那属行为变更不属收口，#G1-T 因此仍是待决而不是已完成。
    /// 判据漂开的症状不是崩，而是"改了设置某一边读到旧值"或"每次 Load 都打一次盘"。
    /// 行为钉 <c>SettingsSnapshotCacheTests</c> 6 格：探针窗口内/外、磁盘写时间一致/不一致、
    /// 换一个 settings.json 不许复用、<c>MarkProbed</c> 只动探针时刻不动写时间、
    /// 读出去与存进去都必须是克隆（缓存本体漏出去的话，调用方改一份就等于改了缓存）。
    /// 普查成员清单逐行比过：只掉这三项，无新名字进来。
    /// <b>顺手改对了一个 CI 事实</b>：上限注释与 <c>code-quality.yml</c> 里"本地 Release 全绿"那句
    /// 曾按"CI 也跑过测试"来写，实际 CI 的 Test 步骤因 bash/pwsh 不匹配从没执行过（a387d56 修，
    /// 并加了"必须真看到用例数 ≥1000"的下限判据）。

    /// 23 → 21 一次收两族，两族都收进<b>早就存在的家</b>（不是新抽象）：
    /// <list type="bullet">
    /// <item><description><c>OnSizeChanged</c> 5 份逐字相同（<c>StudyDeductionReasonsWidget</c> /
    /// <c>StudyInterruptDensityWidget</c> / <c>StudyNoiseDistributionWidget</c> /
    /// <c>StudyScoreOverviewWidget</c> / <c>StudySessionControlWidget</c>）→
    /// <c>StudyComponentLifecycle.RefreshOnResize</c>。收的是面板本身而不是它的画刷，为的是保住抄本
    /// 原本的读点（底色在重排<b>之后</b>现读）。行为钉 <c>StudyComponentLifecycleTests</c> +1 格
    /// （回调里换掉 <c>Border.Background</c>，提前读走就红）。
    /// 同族剩下三个不是抄歪，是各用自己的架构：<c>StudyNoiseCurveWidget.axaml.cs:150</c> 做的是同样两件事、
    /// 只是换了拼写并多补一次 <c>ApplyCellSize</c>；<c>StudySessionHistoryWidget.axaml.cs:119</c> 尺寸变了走
    /// <c>RenderSnapshot</c>（那里面本来就现取 <c>StudyPanelPalette.Resolve</c>）；
    /// <c>StudyEnvironmentWidget.axaml.cs:97</c> 一次都没碰取色家，文字色全走 XAML 的
    /// <c>DynamicResource Adaptive*</c>（主题翻档自动跟，所以尺寸变化确实不需要重算）。
    /// 把这三种架构统一起来等于替"采样取色派 vs 主题资源派"选边，属设计决定，不在这一笔里改，
    /// 已另立 #G1-CD 等拍板。</description></item>
    /// <item><description><c>OnActualThemeVariantChanged</c> 3 份逐字相同（<c>DailyWordWidget</c> /
    /// <c>DailyWord2x2Widget</c> / <c>RecordingWidget</c>）→ <c>ComponentThemeMode.RefreshNightVisual</c>
    /// （那一家本来就有，另外 6 个组件一直在用），顺带把 <c>DailyWordWidget.ApplyDesignTimePreview</c>
    /// 与 <c>WorldClockWidget</c> 两处同形状的非族站点也接上。这家此前<b>没有</b>直接的格
    /// （只有 <c>…IfChanged</c> 版有），这次补 2 格：写字段必须先于重排、且这一版没有"变了才画"的备忘。
    /// </description></item>
    /// </list>
    /// 普查成员清单逐行比过：只掉这两项名字，无新名字进来（实测 21 族，站点里已无这两个签名）。

    /// 21 → 19 一次收两族（同一个签名 <c>OnAttachedToVisualTree</c> 的两个不同体）：
    /// 7 个学习组件挂载那五步（落"已挂载"位 → 重读显示设置 → 订快照事件 → 重算监测租约 → 各家刷新）
    /// 此前是 3 份逐字相同 + 2 份"最后一步换成排渲染门"的逐字相同 + 2 份各多一步的孤本，
    /// 现在一律走 <c>StudyComponentLifecycle.Attach</c>（与它已有的 <c>Detach</c> 同一个家、互为反向）。
    /// 有后果的那一条是<b>状态位必须最先落</b>：<c>StudyMonitoringLease.Sync(…, isAttached, isOnActivePage)</c>
    /// 在 <c>!isAttached</c> 时直接 Release，状态位落在后面就等于"组件放回桌面却没拿到租约"，
    /// 症状是学习监测不再采数且不报错。行为钉 <c>StudyComponentLifecycleTests</c> +1 格
    /// （三个回调各自读那个状态位，落晚了就红；假服务靠传 <c>isSubscribed = true</c> 走"已订过"那一支省掉）。
    /// <c>StudySessionHistoryWidget</c> 没进来：它压根没有租约那一步（全文件零 <c>_monitoringLease</c>），
    /// 形状不同，硬套要塞一个空的实参——那才是把判据写没。<b>未查证它该不该有租约</b>，已写进 #G1-CD。
    /// 这一笔还逼出两处判据盲区，都按"判据必须认家"的既有原则修，不是放宽放行：
    /// <list type="number">
    /// <item><description><c>ComponentLifecyclePairingRatchetTests</c> 的 snapshot 族只让<b>收</b>的一侧认家，
    /// 起点收进 <c>Attach</c> 之后"起 1/8"直接红了（覆盖面下限按文本条数算）。已把 <c>Attach</c> 也认成家，
    /// 下限 8 不动（实测 1 处裸 Subscribe + 7 处走家 = 8）。变异验过：摘掉一处 <c>Detach</c> → 红
    /// （"收 12/13"，与这条文件注释里 2026-09-23 记的那条一致：整批交给家之后，族计数才是真哨兵）。</description></item>
    /// <item><description><c>WidgetLayoutAppliedRatchetTests</c> 只认 <c>Name(</c> 形态的调用点，
    /// 方法组当实参递出去（交给家去调）不算——同一笔里 5 个组件的 <c>UpdateAdaptiveLayout</c> 被报成"没人调"。
    /// 已放宽为"完整标识符出现"，双向变异验过：引用留着 → 绿；把同文件两处引用都换成空 lambda → 红且只报那一处。</description></item>
    /// </list>

    /// 15 → 13 收两族（都是噪声曲线控件与噪声分布面积图控件这两块自绘图表，#G1-CC）：
    /// ① <c>BuildPlotPoints</c> 86 行逐字相同（分桶取每桶最低点与最高点的那段降采样）＋它下面的
    /// <c>MapToPlot</c>／<c>MapDbToY</c>／<c>MapTimestampToLogicalX</c> 三小段 →
    /// <c>StudyChartGeometry.BuildPlotPoints</c>（源数组、逻辑原点、dB 窗口、缓冲 <c>ref</c> 都由调用方给：
    /// 曲线窗口固定 20..100，面积图跟着基线走 baseline-5..baseline+25，<b>这是内容口径差异、不是抄漏</b>，
    /// 所以窗口留在调用方，各家用 <c>MinDisplayDb</c>／<c>WindowMinDb</c> 一份真值传进去）；
    /// ② <c>DrawGrid</c> 7 行逐字相同，外加一把<b>看不见的</b> 25 行 <c>BuildGridGeometry</c>——
    /// 它的签名返回元组 <c>(StreamGeometry Grid, StreamGeometry Axis)</c>，<c>AllmanSignature</c> 那条正则
    /// 只认"类型 名字(参数)"的形状，带括号与逗号的返回类型整个不匹配，所以这两份逐字相同的几何构建
    /// 从来没进过普查（<b>判据缺口</b>：两把尺子的签名正则里"返回类型"那一段字符集没有圆括号，
    /// 带元组返回的声明整行不匹配）。本笔一并修：<see cref="AllmanSignature"/> 与
    /// <see cref="DriftSignature"/> 及两份脚本同步放开括号。<b>两笔账分开记</b>——
    /// 修判据这件事在今天这棵树上量到的新增族数是 <b>0</b>（逐字仍 13、漂移仍 ≤189，两把尺子一字未动就过了），
    /// 也就是说这条缺口只咬过 <c>BuildGridGeometry</c> 这一对，而那对已经被本笔收掉；
    /// 修好的判据拿种出来的正例验过：在 desktop 与 airapp 各放一份逐字相同的
    /// <c>private static (int A, int B) ProbeTupleFamily(int seed)</c>，脚本如实报 "2x in 2 files"，验完删掉。
    /// 网格收进 <c>StudyChartGridLayer</c> 这个<b>持有缓存的对象</b>而不是静态方法：这一层的真值就是
    /// "几何缓存配不配当前画布尺寸"这个状态，静态方法要调用方把 <c>Rect</c> 与两个几何用 <c>ref</c> 传进来，
    /// 而漏写 <c>_cachedGridPlot = plot</c> 这一行不报错，症状是"格子尺寸变了网格不跟着变"或"每帧重建几何"。
    /// 代价按满输入实测（1200 点 → 420 点，两趟 <c>Stopwatch</c>：39.1 µs 与 56.9 µs）：
    /// 两块图表每帧各跑一次占 16.7ms 预算的 <b>0.47%~0.68%</b>；这条量测钉在
    /// <c>StudyChartGeometryTests.BuildPlotPoints_CostIsAFractionOfOneFrame_AtTheWidestRealInput</c>（上限 500 µs）。
    /// 行为钉 <c>StudyChartGeometryTests</c> 11 格，7 个变异逐条验过：峰谷只取一边→红 1；
    /// 末点下标偏一格→红 2；直映射不吃窗口起点→红 1；窗口起点连同 clamp 下界一起吃掉→红 1（只改偏移
    /// 会被 <c>Math.Clamp</c> 吸收，故两行一起改才算变异）；每次重租数组→红 1。
    /// <b>两处变异 stayed green 并已写明原因</b>：删掉 <c>second != lastSourceIndex</c> 去重守卫仍全绿——
    /// 时间戳严格递增时它恒为真（<c>first &lt; second</c> 且 <c>lastSourceIndex ≤ first</c>），
    /// 守的是"同一时刻重复采样"那种源，今天没有行为证据；把 clamp 下界单独改坏也全绿，因为它与上一行偏移互为冗余。
    ///
    /// 19 → 18 收一族：<c>OnAttachedToVisualTree</c> 里每日一词 1x1 与 2x2 那两份逐字相同的四行
    /// （落"已附着"位 → 按设置起停表 → 画刷新按钮 → 立刻取一次数）→
    /// <c>ComponentRefreshLifetime.Attach</c>（这个家已有 Detach 与 Reschedule，缺的正是起的那一半），
    /// 同形状的另一批 7 个组件（Baidu/Bilibili/Cnr/Ifeng/Stcn24/每日插画/汇率）一起走家——
    /// 它们只差"字段名与要不要画按钮"，按这把尺子的口径本来就属漂移面，这一笔让漂移面的同一族少 7 处。
    /// 判据两条都写在家里：① <b>状态位必须最先落</b>，因为各组件 <c>Refresh…Async</c> 第一句就是
    /// <c>if (!_isAttached || _isRefreshing) return;</c>（实测 9 个组件逐字如此），落晚了这次取数整个不发，
    /// 症状是"放上台面是空的，等第一次 tick 才出内容"；② 取数排在控件收尾之后，否则"按钮正常、内容已报错"。
    /// 行为钉 <c>ComponentRefreshLifetimeTests</c> +2 格（三个回调各读那个状态位 + 没有收尾口子时两步照做）。
    /// <c>DailyPoetryWidget</c> 故意没进来：它是"画按钮 → 刷模式视觉 → 起表 → 取数"，
    /// 收尾排在起表<b>之前</b>，套这个家等于替它换顺序（行为变更，另拍）。
    /// <b>顺带更正一条已经失效的工具自证</b>：AGENTS.md 里 <c>dump-dup-methods.py</c> 的正对照写的是
    /// "<c>OnSizeChanged</c> 5 处 5 文件逐字相同"，那 5 处今天已收进 <c>RefreshOnResize</c>；
    /// 现在同签名只剩 2 处（FileManager / RemovableStorage 那对空处理器），正对照改成按哈希点名。

    /// 13 → 12 收一族：<c>ResolveCulture</c>（语言码 → CultureInfo，认不出别抛）。守卫基线一次报出 <b>5 家 8 处</b>——比我按 <c>catch (CultureNotFoundException)</c> 数的 4 处多一家： <c>DailyArtworkWidget</c> 那份写的是 <c>catch { }</c> 兜住一切，所以行级 grep 看不见它。现在"怎么试、失败长什么样"只认 <c>Services/LanguageCulture.GetOrFallback</c> 一处，<b>退哪一档仍由各调用点自己说</b>（装界面语言退 <c>LanguageCodes.Default</c>、格式化退 <c>InvariantCulture</c>，这是内容口径差异，留给 #G1-BL 那类账，不在这里替谁选边）。
    /// 三条实测语义边界写在家注释里，因为它们决定"退档到底盖不盖得住"：<c>GetCultureInfo("")</c> <b>不抛</b>（给不变区域，所以调用方传的 fallback 对空码不起作用——今天 5 个调用点传的都先过 <c>LanguageCodes.Normalize</c>，够不到）；纯空白与汉字串才抛 <c>CultureNotFoundException</c>；而 <c>"not-a-real-tag-xx"</c> <b>成功</b>返回 <c>Name="not"</c>——拼错语言码的症状不是退档，是安静地用一个不存在的档位，要防它得在码表那一头。后两条按现状钉成测试（<c>LanguageCulture_…</c> 4 格），哪天有人收紧码表，这两格会红着提醒。
    /// 守卫 <c>CultureResolution_LivesInExactlyOnePlace</c> 拦两条（家外出现该 catch、或 <c>ResolveCulture</c> 自己还接失败），两个方向都变异验过：把 Launcher 那份内联抄本装回去→红并点名 2 处；只把判据里的家路径改成一个不存在的名字→红（家被改名后守卫会永远绿，这条自己也要有红点）。允许 <c>=&gt; LanguageCulture.GetOrFallback(…)</c> 的一行转手壳：调用点实测 15 处，把壳全拆了会让"我这档退哪儿"在每条现场再写一遍，那才是第二个真源。

    private const int IdenticalBodyFamilyCeiling = 12;

    /// 18 → 15 一次收三族，全是同一个签名 <c>ResolveScale</c> 的三个不同体（#66 队列里那条"三对"）：
    /// <list type="bullet">
    /// <item><description><c>BaiduHotSearchWidget</c> 与 <c>BilibiliHotSearchWidget</c> 逐字相同的 12 行、
    /// <c>IfengNewsWidget</c> 与 <c>JuyaNewsWidget</c> 逐字相同的另一 12 行（两对只差上端那一档）→
    /// <c>ComponentDesignMetrics.ResolveFootprintScale</c>。设计格数（2×4 / 4×4）与上限（2.8 / 2.4）
    /// 留在调用方：那是各自占位与视觉口径，不是复制漂移。</description></item>
    /// <item><description><c>AnalogClockWidget</c> 与 <c>TimerWidget</c> 逐字相同的表盘算式 →
    /// <c>ComponentDesignMetrics.ResolveDialScale</c>。它与上面那条是<b>两条不同算式</b>（表盘没有
    /// "设计占几格"这个参照），所以没有并成一个函数。算式里的 44 保留原样并起名
    /// <c>DialCellReference</c>——它不等于 <c>BaseCellSize</c>（48），改成 48 会同时挪动两块时钟的
    /// 字号与指针长度，那属视觉决定；<b>未查证过这两个 44 是刻意还是历史遗留</b>。</description></item>
    /// </list>
    /// 行为钉 <c>ComponentDesignMetricsTests</c> 7 格：短边决定缩放、上下限各夹一次、布局没落定时按 1 画、
    /// 格子或格数为 0 给 1 而不是拿 0 去除、以及"上限由调用方给多少就夹到多少"。
    /// 收的都是私有方法体，调用点一行没动（六家各 1 处），普查成员清单逐行比过：只掉这三族，无新名字。

    /// 这一笔<b>族数不动（仍是 15）</b>，但收掉的量比前面所有笔加起来都大，而且这把尺子全程看不见它：
    /// 7 个组件（Baidu / Bilibili / Cnr / Ifeng / Stcn24 / 每日插画 / 汇率）各写一遍的
    /// <c>Refresh…Async</c> 取数协议（每份 56–59 行，<b>共 405 行</b>：59/56/58/58/59/57/58）改走
    /// <c>ComponentFeedRefresh.RunAsync</c>，收完之后七段实参合起来 169 行。它们<b>不是逐字族</b>——payload 各家不同（query 类型与服务方法都不一样）。
    /// 判"该不该走家"的依据不是族数，是那<b>六条不变量各抄了七遍</b>：入口守卫 / 忙位置起再画控件 /
    /// 换发先换后取消旧源 / await 完重新问一次挂载与取消 / 取消静默而失败画失败态 / finally 里
    /// "这把还是我的那把"才清字段。七份之间已经漂开（实测）：<c>BilibiliHotSearchWidget</c> 的 finally
    /// 少了"画按钮"那一步，<c>CnrDailyNewsWidget</c> 与 <c>Stcn24ForumWidget</c> 是"先画按钮再换语言"
    /// （按钮文案那一帧用的是旧语言）——这两处都随收口自然消失，不算行为变更（没有下游读那个旧值）。
    /// <b>两家故意没进来</b>，各带实测到的形状差别：<c>ZhiJiaoHubWidget</c> 没有忙位、每次换发都是
    /// "取消并新建"（AGENTS.md 早已登记它是另一种判据）；<c>JuyaNewsWidget</c> 整个没有
    /// <c>CancellationTokenSource</c> 也没有忙位——它今天确实能在组件分离之后落地，
    /// 接进这家等于同时给它补取消与单飞，属行为变更，已另立待办而不是顺手改。

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
    /// 这一批在两个面上都不被认成声明 ⇒ 逐字面与漂移面同时看不见它们，5759 这个认领量是**下界**。
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
    // 181 → 182 这一格是**名字跨类撞车被同名尺子认领**，不是有人又抄了一份：本笔新增的
    // RecommendationServiceBinding.Attach 与早已存在的 TimeZoneServiceBinding.Attach 等同名但不同判据，
    // 同名尺子按名字归类，于是把 Attach 这一族点出来（实测 3 站 / 3 种体，站点见报告面 dump-drift-methods.py）。
    // 记账要点：这一族的三条实现分属不同类、各有各的契约，不构成重复真源；要消它得先决定跨类同名是否算事，
    // 那是判据问题（与"两个重载自成一族"同一类假阳性），不是收口问题。
    // 182 → 193 与上面 31 → 33 是**同一笔改判据**（同一份"并签名续行"的逻辑，两个面共用）：
    // 那些被 `});` 吃掉的声明在漂移面同样隐身，判据改正后一次性显形 11 族。不是有人多抄了 11 种实现。
    // 认领量下限那条断言同轮仍绿（≥5700），所以这不是"判据放宽到什么都算"——是被吃掉的行重新算进来了。
    // 这一格里点名的三个新可见族里，Retry 与 TryRemoveExistingPackage 已在下一笔收掉，
    // ResolveAsStreamForReadMethod 与它旁边的 ResolveAsTaskGenericMethod（三处三体）仍挂 #G1-BC。
    // 193 → 189 是**真收口**（与上面 33 → 31 同一笔）：Core 的 AirAppPackageInstaller 与启动器的
    // AirAppInstallerService 各带一套私有重试件（Retry / CopyWithRetry / MoveWithOverwriteRetry /
    // DeleteFileWithRetry 四个方法加一份 RetryDelays），整个删掉改调 core/IO/FileOperationRetryHelper，
    // 这四个名字于是只剩家一种实现、从漂移面消失。报告面按名字逐个比过：掉的正好这 4 个，
    // 没有新名字进来，其余共有族的站点数一处未变——"只动了这一族"是量出来的，不是推断的。
    // 两笔分开记：182 → 193 是改判据（一行代码没删），193 → 189 是收口（git diff --numstat 实测：
    // 两个安装器各删 73 行、各加 5 行，家加 27 行 ⇒ 净 −109），并成"净 +7"会把判据账洗掉。
    private const int DriftFamilyCeiling = 189;

    /// <summary>
    /// 漂移普查认领的声明处数下限（今天实测 5759）。掉到 5400 以下＝判据在丢声明，先看下面那段对账。
    /// 钉这个不是为了查新增，是为了查**判据自己塌掉**：
    /// 上面那两处 bug 都是"少认声明"，族数看着像收口（193→189），实际是普查瞎了。
    /// 只冻族数会被这种错法骗过去，冻住认领量就不会。
    ///
    /// 9882a21 → 当前树这一路是**记账欠的**，不是判据丢了声明：这条注释原先停在 9882a21 那天，
    /// 之后陆续提交的收口各自删掉的私有声明没回写到这儿。2026-09-23 用**同一份（改判据之后的）**普查
    /// 跑两棵树重对过账（`git archive` 一份 9882a21、按 (方法名, 所在文件) 做集合差）：
    /// **5785 → 5759：少 65 条、多 39 条，净 −26**（5785 − 65 + 39 = 5759，等式两边都对得上）。
    /// 旧账写的是 5456 → 5426（少 59 / 多 29、净 −30）——同一批增删，**换口径后每条都重新数过**：
    /// 新口径认得换行写的签名，所以两棵树的总数一起抬高（5456 → 5785），点名清单也多了几项
    /// （<c>FitFontSize(3)</c>、<c>AwaitWinRtOperationAsync(2)</c>、<c>ResolveWinRtOperationResultType(3)</c>、
    /// <c>SetTimeZoneService</c> 那批是收成 <c>Attach</c> 一次调用，故只算新增 <c>Attach(1)</c>）。
    /// 下面那份点名列表是老口径下写的，作为"删了什么"的清单基本对得上——逐条比过新口径的 63 条，
    /// 只有两项不在旧列表里：<c>FitFontSize(3)</c>（本笔上面已点名）与 <c>PrimeDesktopEditPreviewImage(1)</c>
    /// （改判据后新显形的空壳，实测随 <c>ebfa143</c> 那批"只丢参数"的清理一起删掉（现树已无此名）；其余各名称括号里的处数会因签名换行的可见性差 1–3 条。）
    /// 下次收口按新口径逐项加，别再"顺手统一"数字。
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
        @"^\s*(private|internal|public)\s+(static\s+)?(async\s+)?[\w<>?\[\],\.() ]+?\b(?<name>[A-Za-z_]\w*)\s*\([^)]*\)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex OpeningBraceOnly = new(@"^\s*\{\s*$", RegexOptions.Compiled);

    private static readonly Regex DriftSignature = new(
        @"^\s*(?:public|private|protected|internal)?\s*" +
        @"(?:static\s+|sealed\s+|override\s+|virtual\s+|async\s+|partial\s+|new\s+)*" +
        @"(?:[A-Za-z_(][\w<>\[\]?,\.() ]*?\s+)?(?<name>[A-Za-z_]\w*)\s*(?:<[^>]*>)?\s*\([^)]*\)\s*(?:=>.*)?\{?\s*\}?\s*$",
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

                // 键里不带行数、比较前先 Squeeze：抄本换个排版（成员链换行、实参一行拆成五行）就是同一份实现，
                // 按"修剪后的行 join"当键会让它两头隐身——逐字面认成两种体，漂移面又因"只有一种体"不显形。
                var key = $"{signature.Groups["name"].Value}|{Squeeze(string.Join(' ', body))}";
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
            $"漂移普查只认领到 {censusSites} 处声明，低于下限 {DriftCensusSiteFloor}（今天实测 5759）。" +
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
            // 只在"欠闭合"（左 > 右）时并。按"不等"并会把 `});` 这类多闭合的收尾行也当成没闭合，
            // 连着吃掉它后面那条声明：实测全仓这类多闭合行 3796 条（欠闭合 4164 条），
            // Core 的 AirAppPackageInstaller 与启动器 AirAppInstallerService 里那两份逐字相同的 27 行
            // Retry 正好紧跟在 `});` 后面，于是两个面上同时隐身——而两面共用这份逻辑，
            // "两面同数"的互证对共同缺陷是无效的（2026-09-24 量到）。
            while (!current.Contains('{') &&
                   OpenCount(current) > CloseCount(current) &&
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

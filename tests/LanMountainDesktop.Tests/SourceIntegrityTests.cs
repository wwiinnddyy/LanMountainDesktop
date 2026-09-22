using LanMountainDesktop.Shared.Contracts.Deployment;
using System.Text;
using System.Text.RegularExpressions;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 源码完整性守卫。两条规则都来自本仓库真实发生过的事故：
///
/// 1) 注释吞掉整行语句。MainWindow 里有 5 处中文注释丢了换行，紧跟其后的
///    <c>if (visibleCount > 1)</c>、<c>if (_showTextCapsule)</c>、
///    <c>if (e.Scope == AirAppSettingsScope.ComponentInstance)</c> 一起掉进 // 里，
///    花括号块变成无条件执行：文字胶囊关不掉、状态栏宽度按负间隔算、组件实例级设置变更
///    触发整桌面重载。编译器不会报，测试也测不到，因为语法完全合法。
/// 2) 编码损坏留下的替换符（U+FFFD）。同一个文件里还留着被 cp936 反复解码过的乱码注释。
///
/// 同一条事故还有另一种形态：被吞的不是语句头而是整条语句，例如
/// MainWindow.DesktopPaging.cs 里 availableWidth = 600、tileWidth 下限、
/// LauncherRootTilePanel.Width 三条赋值和方法尾部的 Dispatcher.UIThread.Post(...)
/// 全都掉进了乱码注释里（瓦片建完再没补过布局）。由
/// <see cref="NoStatementsShareALineWithAComment"/> 兜住。
/// </summary>
public sealed class SourceIntegrityTests
{
    private static readonly string[] ProductionDirectories = ["core", "desktop", "airapp", "install", "mobile", "platform"];

    private static readonly Regex SwallowedStatement = new(@"^\s*//.*\)\s*$", RegexOptions.Compiled);

    // 注释吞掉换行的第二种形态：整条语句被并到注释行末尾。特征是注释里出现
    // 4 个以上连续空格（那是被吞那行原本的缩进），空格之后又是一眼像代码的片段。
    private static readonly Regex CommentOnlyLine = new(@"^\s*//", RegexOptions.Compiled);

    private static readonly Regex WideGap = new(@"\S {4,}\S", RegexOptions.Compiled);

    private static readonly Regex CodeLikeFragment = new(
        @"^([\w.\[\]()!<>-]+\s*=[^=]|(if|return|throw|foreach|switch|else|do|try|await|var)\b|[A-Za-z_][\w.]*\()",
        RegexOptions.Compiled);

    [Fact]
    public void NoStatementsAreSwallowedIntoComments()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length - 1; index++)
            {
                if (!SwallowedStatement.IsMatch(lines[index]))
                {
                    continue;
                }

                // 下一行只有 "{"：说明这条注释原本后面跟的是 if/foreach/while 之类的语句头。
                if (lines[index + 1].Trim() == "{")
                {
                    offenders.Add($"{RelativeToRepo(file)}:{index + 1}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void NoStatementsShareALineWithAComment()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (!CommentOnlyLine.IsMatch(line))
                {
                    continue;
                }

                var body = line.TrimStart();
                body = body[(body.StartsWith("///", StringComparison.Ordinal) ? 3 : 2)..].Trim();
                if (!WideGap.IsMatch(body))
                {
                    continue;
                }

                var fragments = Regex.Split(body, @"\s{4,}");
                var tail = fragments[fragments.Length - 1].Trim();
                if (CodeLikeFragment.IsMatch(tail))
                {
                    offenders.Add($"{RelativeToRepo(file)}:{index + 1} -> {tail}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void SourceFiles_HoldNoReplacementCharacters()
    {
        var offenders = SourceFiles()
            .Where(file => File.ReadAllText(file).Contains('\uFFFD'))
            .Select(RelativeToRepo)
            .ToArray();

        Assert.Empty(offenders);
    }

    /// <summary>
    /// 宿主与服务端 PLONDS 之间没有共享类型，全靠字符串巧合对齐，所以更新路径上的协议字面量
    /// 必须只在 <c>PlondsWireFormat</c> 里出现一次。此前散在 10 个文件、40 处。
    /// 只看 Services/Plonds 与 Services/Update，且跳过注释行——解析器里的 "sha256"/"sha512"
    /// 是 JSON 键名不是协议取值，不在禁止之列。
    /// </summary>
    [Fact]
    public void PlondsWireLiterals_OnlyLiveInPlondsWireFormat()
    {
        var banned = new[]
        {
            "\"Files.zip\"", "\"files.zip\"", "\"changed.zip\"", "\"files-windows-x64.zip\"", "\"PLONDS.json\"",
            "\"add\"", "\"replace\"", "\"reuse\"", "\"delete\"",
        };

        var offenders = new List<string>();

        foreach (var directory in new[] { "Plonds", "Update" })
        {
            var root = Path.Combine(RepoRoot, "desktop", "LanMountainDesktop", "Services", directory);
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(file).Equals("PlondsWireFormat.cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var lines = File.ReadAllLines(file);
                for (var index = 0; index < lines.Length; index++)
                {
                    var line = lines[index];
                    if (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    foreach (var literal in banned.Where(literal => line.Contains(literal, StringComparison.Ordinal)))
                    {
                        offenders.Add($"{RelativeToRepo(file)}:{index + 1} 用了 {literal}，应改走 PlondsWireFormat");
                    }
                }
            }
        }

        // 不用 Assert.Empty：它会把集合截断成 ???，看不出还欠几条。
        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处协议字面量：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// WCAG 亮度/对比度算式必须只有 <c>ColorMath</c> 一份。此前 15 个组件各自复制了
    /// CalculateRelativeLuminance，6 个学习组件又各自复制了 RelativeLuminance /
    /// ToOpaqueAgainst / MinContrastRatio，MainWindow 还有第四份；四份之间阈值都不一样
    /// （0.03928 与 0.04045 并存），组件间的取色判定因此可能悄悄不一致。
    /// </summary>
    [Fact]
    public void WcagColorMath_LivesInExactlyOnePlace()
    {
        var declRe = new Regex(@"^\s*(?:private|internal|public)\s+static\s+(?:double|Color)\s+(?:Calculate)?RelativeLuminance\s*\(|^\s*(?:private|internal|public)\s+static\s+(?:double|Color)\s+(ToOpaqueAgainst|MinContrastRatio|ToLinear)\s*\(");
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            if (Path.GetFileName(file).Equals("ColorMath.cs", StringComparison.Ordinal))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                if (declRe.IsMatch(lines[index]))
                {
                    offenders.Add($"{RelativeToRepo(file)}:{index + 1} 自己实现了一份亮度/对比度算式，请用 Theme/ColorMath");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处重复实现：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// 黑夜模式判定同样只认一份实现（<c>Views/Components/ComponentThemeMode.cs</c>）。
    /// 此前 19 个组件各抄了一遍，而且兜底不一致：11 个在既没有主题档也找不到
    /// AdaptiveSurfaceBaseBrush 时当"夜"，7 个当"昼"，MusicControlWidget 回退到应用级主题档。
    /// 唯一保留的例外写在白名单里，要再加一个分歧就得先改这张名单。
    /// </summary>
    [Fact]
    public void NightModeResolution_LivesInExactlyOnePlace()
    {
        string[] allowedDivergences = ["MusicControlWidget.axaml.cs"];
        var declRe = new Regex(@"^\s*private bool (ResolveNightMode|ResolveIsNightMode)\s*\(\s*\)\s*$");
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            var name = Path.GetFileName(file);
            if (name.Equals("ComponentThemeMode.cs", StringComparison.OrdinalIgnoreCase) ||
                allowedDivergences.Contains(name))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                if (declRe.IsMatch(lines[index]))
                {
                    offenders.Add($"{RelativeToRepo(file)}:{index + 1} 自己实现了一份黑夜判定，请用 ComponentThemeMode 或把它加进白名单并说明理由");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处重复实现：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// "把链接交给系统默认浏览器"这件事只认 <c>Helpers/ExternalLinkLauncher.cs</c> 一份。
    /// 此前 http/https 归一化被逐字抄了 6 份（含 RecommendationDataService），shell 打开又抄了
    /// 8 份，其中 2 份（DailyNewsView、JuyaNewsWidget）连 scheme 校验都没有——RSS 里的
    /// <c>file://</c> 或 <c>ms-msp:</c> 之类的串会被直接喂给 ShellExecute。
    /// 第二条规则管住复发：FileName 直接绑到某个 url 变量上，就是绕开归一化自己开了。
    /// 打开文件/文件夹/本地程序（FileManager、Shortcut、ZhiJiaoHub）语义不同，不在此列。
    /// </summary>
    [Fact]
    public void ExternalLinkHandling_LivesInExactlyOnePlace()
    {
        var declRe = new Regex(
            @"^\s*(?:private|internal|public|protected)\s+(?:static\s+)?(?:string\?|bool|void)\s+(?:Try)?(?:NormalizeHttpUrl|OpenUrl|OpenInBrowser|TryOpenLink)\s*\(");
        var rawUrlLaunchRe = new Regex(@"FileName\s*=\s*[\w.]*[Uu]rl[\w.]*\s*[,;]");
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            if (Path.GetFileName(file).Equals("ExternalLinkLauncher.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                if (declRe.IsMatch(line))
                {
                    offenders.Add($"{RelativeToRepo(file)}:{index + 1} 自己实现了一份外链归一化/打开，请用 ExternalLinkLauncher");
                }
                else if (rawUrlLaunchRe.IsMatch(line))
                {
                    offenders.Add($"{RelativeToRepo(file)}:{index + 1} 把 url 直接交给 ShellExecute，缺 http/https 校验：{line.Trim()}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处重复/绕过：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// "默认语言是什么"与"当前是不是中文"这两条口径只认 <c>LocalizationService</c> 一份。
    /// 此前宿主里有 41 处 <c>_languageCode = "zh-CN"</c> 兜底/初值、8 处各写一遍的
    /// <c>string.Equals(_languageCode, "zh-CN", OrdinalIgnoreCase)</c>。
    /// 仍然允许 <c>CultureInfo.GetCultureInfo("zh-CN")</c> 这类字面量——那是 .NET 区域性名，
    /// 不是界面语言口径（WindowsStartMenuService 用它的排序规则就跟默认语言无关）。
    /// 只管宿主自己的二进制：AirApp 子进程的语言兜底走 AirAppSdk 的 <c>AirAppLocalizer</c>，
    /// 它引用不到宿主，SDK 的公开面动一次就要重发一次包，所以不在本条范围内。
    /// </summary>
    [Fact]
    public void DefaultLanguagePolicy_LivesInExactlyOnePlace()
    {
        var banned = new (Regex Re, string Why)[]
        {
            (new Regex(@"languageCode\s*=\s*""zh-CN""", RegexOptions.IgnoreCase | RegexOptions.Compiled),
             "默认语言兜底请写 LocalizationService.DefaultLanguageCode"),
            (new Regex(@",\s*""zh-CN""\s*,\s*StringComparison", RegexOptions.Compiled),
             "中文判定请走 LocalizationService.IsChineseLanguage"),
        };

        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            if (!IsHostProjectFile(file) ||
                Path.GetFileName(file).Equals("LocalizationService.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var (re, why) in banned.Where(entry => entry.Re.IsMatch(line)))
                {
                    offenders.Add($"{RelativeToRepo(file)}:{index + 1} {why}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处语言口径副本：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// 学习监测租约的持有/释放只认 <c>StudyMonitoringLease</c> 一处（见
    /// <c>Services/StudyAnalyticsMonitoringLeaseCoordinator.cs</c>）。此前 7 个学习组件各抄了
    /// 同一段"开关+挂载+当前页"判定，外加 11 处手写的 Dispose-再置空序列。
    /// 组件里只许出现 <c>ref _monitoringLease</c> 这一种用法。
    /// </summary>
    [Fact]
    public void MonitoringLeasePolicy_LivesInExactlyOnePlace()
    {
        var banned = new (Regex Re, string Why)[]
        {
            (new Regex(@"_monitoringLease \?\?=", RegexOptions.Compiled), "不要自己抢租约，走 StudyMonitoringLease.Sync"),
            (new Regex(@"_monitoringLease\s*\?\s*\.\s*Dispose", RegexOptions.Compiled), "不要自己释放租约，走 StudyMonitoringLease.Release"),
            (new Regex(@"_monitoringLease\s*=\s*null", RegexOptions.Compiled), "置空也归 StudyMonitoringLease 管"),
        };

        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            if (Path.GetFileName(file).Equals("StudyAnalyticsMonitoringLeaseCoordinator.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                foreach (var (re, why) in banned.Where(entry => entry.Re.IsMatch(lines[index])))
                {
                    offenders.Add($"{RelativeToRepo(file)}:{index + 1} {why}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处租约副本：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// 学习面板的取色底座只认 <c>Views/Components/StudyPanelPalette.cs</c> 一份：面板底色怎么求
    /// 此前抄了 7 份，深/浅衬底常量抄了 7 份，可采样替身色抄了 6 份。
    /// 学习历史面板的采样口径（更弱的混合、少一个采样点）作为 <c>BuildSoftSamples</c> 显式留在同一个文件里，
    /// 是摆得上台面的分歧，不是某个组件的私有复制。
    /// </summary>
    [Fact]
    public void StudyPanelPalette_LivesInExactlyOnePlace()
    {
        var banned = new (Regex Re, string Why)[]
        {
            (new Regex(@"(?:private|internal|public)[^\r\n]*\s(ResolvePanelBackgroundColor|BuildPanelBackgroundSamples)\s*\(", RegexOptions.Compiled),
             "面板取色请走 StudyPanelPalette"),
            (new Regex(@"static readonly Color (DarkSubstrate|LightSubstrate)\b", RegexOptions.Compiled),
             "深浅衬底请用 StudyPanelPalette.Dark / .Light"),
            (new Regex("Color.Parse\\(\"#FF(0F6B49|2F5DA8|9AA0A6)\"\\)", RegexOptions.Compiled),
             "状态徽章底色请用 StudyPanelPalette.SuccessBadge / RealtimeBadge / DisabledBadge"),
            (new Regex(@"badgeAlpha\s*=|panelLuminance > 0\.", RegexOptions.Compiled),
             "徽章不透明度分档请走 StudyPanelPalette.ResolveBadgeColor"),
            (new Regex(@"Color\.FromArgb\(0x96,\s*0xFF,\s*0xFF,\s*0xFF\)", RegexOptions.Compiled),
             "徽章描边请用 StudyPanelPalette.BadgeBorderBrush"),
        };

        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            if (Path.GetFileName(file).Equals("StudyPanelPalette.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal) ||
                    line.TrimStart().StartsWith("///", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var (re, why) in banned.Where(entry => entry.Re.IsMatch(line)))
                {
                    offenders.Add($"{RelativeToRepo(file)}:{index + 1} {why}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处面板取色副本：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// "挑一个在这组背景样本上读得清的前景候选色"这条无障碍策略只认 <c>Theme/AdaptiveBrushFactory.cs</c>。
    /// 7 个学习组件各抄了一遍这 30 行，其中 1 份是另写的单遍循环变体——同一套调色板在组件之间可能选出不一样。
    /// </summary>
    [Fact]
    public void AdaptiveContrastPick_LivesInExactlyOnePlace()
    {
        var decl = new Regex(@"(?:private|internal|public|static)?[^\r\n]*\s(IBrush|SolidColorBrush|Color)\s+CreateAdaptiveBrush\s*\(");
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            if (Path.GetFileName(file).Equals("AdaptiveBrushFactory.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                if (decl.IsMatch(lines[index]))
                {
                    offenders.Add($"{RelativeToRepo(file)}:{index + 1} 自己实现了一份对比度取色，请用 AdaptiveBrushFactory");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处重复实现：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// 组件的画笔与文字测量小工具只认 <c>Views/Components/ComponentPaint.cs</c> 与
    /// <c>ComponentTypography.cs</c> 两处。此前 20 个组件把同几个四行助手各抄了一份：
    /// <c>CreateBrush</c> 11 份、<c>ToVariableWeight</c> 9 份、<c>Lerp</c> 9 份、
    /// <c>MeasureTextSize</c> 4 份、对角渐变 3 份，合计 30 个私有实现。
    /// 插值刻意留两个名字：<c>Lerp</c>（不夹紧，日历/时钟传的是已算好的比例）与
    /// <c>LerpClamped</c>（夹在 0..1，学习组件传的是可能过冲的进度），两种语义都是既成的。
    /// </summary>
    [Fact]
    public void WidgetPaintAndTextHelpers_LiveInExactlyOnePlace()
    {
        string[] allowedFiles = ["ComponentPaint.cs", "ComponentTypography.cs"];
        var decl = new Regex(
            @"^\s*private\s[^\r\n]*\s(CreateBrush|CreateLinearGradientBrush|MeasureTextSize|ToVariableWeight|Lerp|LerpClamped)\s*\(");
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            if (allowedFiles.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                if (decl.IsMatch(lines[index]))
                {
                    offenders.Add($"{RelativeToRepo(file)}:{index + 1} 又抄了一份画笔/文字助手，请用 ComponentPaint 或 ComponentTypography");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处重复实现：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// 学习组件订/解快照事件只认 <c>Services/StudySnapshotSubscription.cs</c> 一处。
    /// "订一次、卸载时必须解掉"这对判断此前被 8 个学习组件各抄了两遍，一共 21 处；
    /// 漏抄解订那一半就是一个看不见的事件泄漏——组件已经从桌面上摘掉了还在被回调。
    /// </summary>
    [Fact]
    public void StudySnapshotSubscription_LivesInExactlyOnePlace()
    {
        var banned = new (Regex Re, string Why)[]
        {
            (new Regex(@"SnapshotUpdated\s*[-+]=\s*On", RegexOptions.Compiled),
             "订/解快照事件请走 StudySnapshotSubscription.Subscribe / Unsubscribe"),
            (new Regex(@"_isSubscribed\s*=", RegexOptions.Compiled),
             "_isSubscribed 只由 StudySnapshotSubscription 维护"),
            (new Regex(@"void\s+OnStudySnapshotUpdated\s*\(", RegexOptions.Compiled),
             "快照回调的可见性判断在 StudySnapshotRenderGate.HandleSnapshotUpdated 里，别再抄一份"),
        };

        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            if (Path.GetFileName(file).Equals("StudySnapshotSubscription.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var (re, why) in banned.Where(entry => entry.Re.IsMatch(line)))
                {
                    offenders.Add($"{RelativeToRepo(file)}:{index + 1} {why}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处订阅生命周期副本：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// 自适应主题资源的键名在 C# 这一侧只许出现在 <c>Theme/ThemeResourceKeys.cs</c>。
    /// 注册方（GlassEffectService 35 处、ThemeColorSystemService 20 处）与读取方（组件、MainWindow 各 partial）
    /// 此前一共写了 113 遍字符串，拼错一个字母不报错、只会让那块 UI 静默失色。
    /// .axaml 的 StaticResource 引用不在此列（XAML 取不到 C# 常量）。
    /// </summary>
    [Fact]
    public void ThemeResourceKeyLiterals_OnlyLiveInThemeResourceKeys()
    {
        var keyLiteral = new Regex("\"Adaptive[A-Za-z0-9]+\"");
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            if (Path.GetFileName(file).Equals("ThemeResourceKeys.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (Match match in keyLiteral.Matches(line))
                {
                    offenders.Add($"{RelativeToRepo(file)}:{index + 1} 写死了 {match.Value}，请用 ThemeResourceKeys");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处主题键字面量：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// 玻璃面板样式类名在 C# 侧只认 <c>ComponentChromePanel.GlassPanelClass</c> 一处：
    /// 注册方（<c>MainWindow.DesktopPaging</c> 建按钮/容器时挂类）与读取方（组件切透明档时摘类）
    /// 此前各写各的字面量，写错就是那块 UI 悄悄丢掉玻璃效果。".axaml" 里的选择器不在此列。
    /// </summary>
    [Fact]
    public void GlassPanelClassLiteral_OnlyLivesInComponentChromePanel()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            if (Path.GetFileName(file).Equals("ComponentChromePanel.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                if (lines[index].Contains("\"glass-panel\"", StringComparison.Ordinal))
                {
                    offenders.Add($"{RelativeToRepo(file)}:{index + 1} 写死了类名，请用 ComponentChromePanel.GlassPanelClass");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处类名字面量：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// 第三方 JSON 的"按路径取值"只认 <c>Services/Json/JsonNodeReader.cs</c> 一处。此前宿主里三个服务
    /// 各抄了一份私有实现（<c>TryGetNode</c> 3 份同体、<c>ReadString</c> 3 份、<c>ReadInt</c> 2 份、
    /// <c>ReadBool</c> 2 份、<c>ReadDouble</c> 1 份，合计 11 个实现），同一份云端返回可能被读成不一样的值。
    /// 只管宿主自己的二进制：Launcher 里那个 <c>ReadBool</c> 是另一份实现、返回非空 bool，
    /// 属另一个进程口径；<c>RecommendationDataService.ReadBoolean</c> 同理是"读不出来当 false"的严格版，
    /// 与宽松版语义不同，不并。
    /// </summary>
    [Fact]
    public void JsonPathReaders_LiveInExactlyOnePlace()
    {
        var decl = new Regex(
            @"^\s*(?:private|internal|public)\s+static\s[^\r\n]*\s(TryGetNode|ReadString|ReadInt|ReadDouble|ReadBool)\s*\(");
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            if (!IsHostProjectFile(file) ||
                Path.GetFileName(file).Equals("JsonNodeReader.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                if (decl.IsMatch(lines[index]))
                {
                    offenders.Add($"{RelativeToRepo(file)}:{index + 1} 又抄了一份 JSON 路径读法，请用 JsonNodeReader");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处重复实现：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// "整体替换一个文件"只认 <c>core/.../IO/AtomicFileWriter.cs</c> 一处。此前宿主里有 10 处、
    /// 启动器与安装器又各有自己的版本，差异是会咬人的：目标被瞬时锁住时没人重试（用户看到的就是"设置没存上"）、
    /// 固定 ".tmp" 名让两个写者互相覆盖、<c>Delete</c>+<c>Move</c> 之间断电就把文件丢了、
    /// Move 失败后 .tmp 永远留在 AppData 里。
    ///
    /// helper 挪进 Core 之后这条覆盖全部二进制（原先只管宿主，剩下的启动器两处就是这么漏掉的）。
    /// 免检的只有一类：拿 ".tmp" 试"这块盘能不能写"的探针（<c>AppLogger</c>、
    /// <c>AirAppInstallTargetAccess</c>，以及文件名以 <c>.write-test-</c> 开头的可写性检查），
    /// 它们不替换任何正式文件。<c>LauncherBackgroundService</c> 曾经挂在名单里等
    /// "原子落一个已有文件"这个原语，<c>AtomicFileWriter.PlaceFile</c> 补上后已经收进来。
    /// </summary>
    [Fact]
    public void AtomicFileReplacement_LivesInExactlyOnePlace()
    {
        string[] handWrittenProbes = ["AppLogger.cs", "AirAppInstallTargetAccess.cs"];
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            if (Path.GetFileName(file).Equals("AtomicFileWriter.cs", StringComparison.OrdinalIgnoreCase) ||
                handWrittenProbes.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                // 只认代码里的临时文件名收尾（$"...{x}.tmp" 或 ".tmp"），注释里提到 .tmp 不算。
                if (!lines[index].Contains(".tmp\"", StringComparison.Ordinal))
                {
                    continue;
                }

                if (lines[index].Contains(".write-test-", StringComparison.Ordinal))
                {
                    continue;
                }

                offenders.Add($"{RelativeToRepo(file)}:{index + 1} 自己搓了临时文件写盘，请用 AtomicFileWriter");
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处手搓的原子写盘：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// 主题资源的"要"和"给"必须对得上。读的一方是 C# 的 <c>ThemeResourceKeys.X</c> 与
    /// .axaml 的 <c>{StaticResource AdaptiveX}</c>；给的一方只有 <c>resources[ThemeResourceKeys.X] = ...</c>
    /// 这一种写法。少一个注册不会报错，只会让那块 UI 静默失色，是最难发现的一类视觉缺陷。
    ///
    /// 反向（注册了没人读）只当信息：AirApp 也能读宿主主题资源，注册得多不等于浪费。
    /// </summary>
    [Fact]
    public void EveryAdaptiveResourceRequested_IsAlsoRegistered()
    {
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        var keyFile = Path.Combine(RepoRoot, "desktop", "LanMountainDesktop", "Theme", "ThemeResourceKeys.cs");
        foreach (Match match in Regex.Matches(
                     File.ReadAllText(keyFile),
                     @"const string (?<name>\w+)\s*=\s*""(?<key>[^""]+)"""))
        {
            keys[match.Groups["name"].Value] = match.Groups["key"].Value;
        }

        Assert.True(keys.Count > 0, "ThemeResourceKeys.cs 一个键都没解析出来，先修这条探针");

        var csFiles = SourceFiles().Where(IsHostProjectFile)
            .Where(file => !file.EndsWith("ThemeResourceKeys.cs", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(file => RelativeToRepo(file), File.ReadAllText, StringComparer.Ordinal);
        var axamlFiles = new[] { "desktop" }.SelectMany(part => Directory.EnumerateFiles(
                Path.Combine(RepoRoot, part), "*.axaml", SearchOption.AllDirectories))
            .Where(file => file.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(file => RelativeToRepo(file), File.ReadAllText, StringComparer.Ordinal);

        var registered = Regex.Matches(
                string.Concat(csFiles.Values),
                @"\[\s*ThemeResourceKeys\.(?<name>\w+)\s*\]\s*=")
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        var offenders = new List<string>();
        foreach (var (name, key) in keys)
        {
            if (registered.Contains(name))
            {
                continue;
            }

            var firstRequest = csFiles.FirstOrDefault(pair =>
                    pair.Value.Contains($"ThemeResourceKeys.{name}", StringComparison.Ordinal))
                .Key;
            var axamlRequest = axamlFiles.FirstOrDefault(pair =>
                    pair.Value.Contains($"StaticResource {key}", StringComparison.Ordinal) ||
                    pair.Value.Contains($"DynamicResource {key}", StringComparison.Ordinal))
                .Key;

            if (firstRequest is not null || axamlRequest is not null)
            {
                offenders.Add(
                    $"{key}（常量 {name}）有人要没人给：读取点 " +
                    $"{firstRequest ?? axamlRequest}；注册方一个都没有");
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 个主题资源被引用却从没注册（那块 UI 会静默失色）：{Environment.NewLine}" +
            string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// 读主题资源只许走 <c>Theme/AdaptiveTokens.cs</c>。两个底层 API 各自都藏着作用域坑：
    /// <c>Resources.TryGetResource(key, variant)</c> 只看一本字典（MainWindow 以前就这样，
    /// 圆角助手只看 Application.Resources），<c>control.TryFindResource(key)</c> 虽然沿链往上找，
    /// 但取不到时每人自己兜一个值 —— 于是同一个键在 A 处取到、B 处取不到，
    /// 症状还是"那块 UI 静默失色"。口径与兜底口径都在一处才看得见。
    /// </summary>
    [Fact]
    public void ThemeResourceReads_GoThroughAdaptiveTokens()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles().Where(IsHostProjectFile))
        {
            if (Path.GetFileName(file).Equals("AdaptiveTokens.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                if (lines[index].Contains("TryGetResource(", StringComparison.Ordinal) ||
                    lines[index].Contains("TryFindResource(", StringComparison.Ordinal))
                {
                    offenders.Add($"{RelativeToRepo(file)}:{index + 1} 自己读主题资源，请用 AdaptiveTokens.TryGet / Brush");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处绕开统一入口的主题资源读取：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// <c>airapp dev/preview</c> 不许再声称自己在监听什么。它实际只做"构建 + 文件监视"，
    /// 但曾打印 `预览地址: http://localhost:{port}` 并让作者去用一个宿主根本没有的参数
    /// <c>--debug-airapp</c> —— 照着做的人半天得不到任何结果，还以为是自己的包坏了。
    /// </summary>
    [Fact]
    public void AirAppDevServer_DoesNotClaimAPreviewEndpoint()
    {
        var directory = Path.Combine(RepoRoot, "airapp", "LanMountainDesktop.AirAppDevServer");
        if (!Directory.Exists(directory))
        {
            return;
        }

        string[] forbidden = ["http://localhost", "--debug-airapp", "\"--port\""];
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                     .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                     .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
        {
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                // 注释里讲"当年打印过 localhost"是这段历史的一部分，只拦真代码。
                var trimmed = lines[index].AsSpan().TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith('*'))
                {
                    continue;
                }

                var hit = forbidden.FirstOrDefault(marker => lines[index].Contains(marker, StringComparison.Ordinal));
                if (hit is not null)
                {
                    offenders.Add($"{RelativeToRepo(file)}:{index + 1} 声称了不存在的监听（{hit}）");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处假承诺：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// 安装根目录下那个 <c>.Launcher</c> 数据目录名只许写在 <c>DeploymentLayout</c> 一处。
    /// 该类自己的注释就写着"禁止在任何一侧硬编码这些标记文件名或目录前缀"，但 2026-09-21 实测
    /// 仍有 4 处各自抄了一遍（Core 两份、宿主 <c>PlondsApplyPaths</c> 一份、启动器
    /// <c>DataLocationResolver</c> 一份），安装器倒是老老实实用了常量。抄一份不会报错，
    /// 改拼写时只会让某一条路径指向不存在的目录。
    /// 另禁 <c>".launcher"</c>：启动器曾把启动诊断写进 <c>LocalAppData/LanMountainDesktop/.launcher/diag</c>，
    /// 与它自己的 <c>Launcher/{logs,state}</c> 兜底布局是第三种拼法，谁也不读那个目录。
    /// </summary>
    [Fact]
    public void LauncherStateDirectoryName_LivesInExactlyOnePlace()
    {
        string[] allowedFiles =
        [
            // 真源。
            @"core\LanMountainDesktop.Core\Deployment\DeploymentLayout.cs",
            // 改名前的老目录名，只用于读旧数据，与新布局不是同一个文件夹。
            @"desktop\LanMountainDesktop.Launcher\Oobe\OobeStateService.cs",
        ];

        var offenders = new List<string>();

        foreach (var file in SourceFiles().Where(file => !allowedFiles.Contains(
                     RelativeToRepo(file), StringComparer.OrdinalIgnoreCase)))
        {
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var trimmed = lines[index].AsSpan().TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith('*'))
                {
                    continue;
                }

                if (lines[index].Contains("\".Launcher\"", StringComparison.Ordinal) ||
                    lines[index].Contains("\".launcher\"", StringComparison.Ordinal))
                {
                    offenders.Add($"{RelativeToRepo(file)}:{index + 1} 自己写了 .Launcher 目录名，请用 DeploymentLayout.LauncherStateDirectoryName");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处硬编码启动器数据目录名：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// "从 .zip 包里挑出 airapp.json 再解析"只认两处：宿主的 <c>AirAppPackageReader</c>（返回带完整契约的
    /// SDK 清单）与 Core 的 <c>AirAppPackageManifestReader</c>（打包/安装期只要 Id/Name/Version）。
    /// 2026-09-21 收口前这个动作有 6 份：宿主 4 份（加载器、市场安装、运行时服务、待升级队列）、
    /// 启动器 1 份、Core 1 份，启动器那份还自带一个只有 5 个属性的 <c>AirAppManifest</c>——
    /// 于是同一个包，安装期"缺 id 也能装"，宿主加载期却按完整契约拒，用户看到的是"装成功了但没东西出来"。
    /// 现在启动器走 Core 的读取器（缺 id/name 当场安装失败，错误说清是哪个包）。
    /// </summary>
    [Fact]
    public void AirAppPackageManifestReading_LivesInExactlyOnePlacePerBinary()
    {
        string[] allowedReaders =
        [
            @"desktop\LanMountainDesktop\AirApps\AirAppPackageReader.cs",
            @"core\LanMountainDesktop.Core\AirAppPackageManifestReader.cs",
        ];

        var manifestTypeDeclaration = new Regex(
            @"\b(class|record)\s+AirAppManifest\b",
            RegexOptions.Compiled);
        var sdkModelFile = @"airapp\LanMountainDesktop.AirAppSdk\AirAppManifest.cs";

        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            var relative = RelativeToRepo(file);
            var source = File.ReadAllText(file);

            if (source.Contains("ZipFile.Open", StringComparison.Ordinal) &&
                Regex.IsMatch(source, @"\bManifestFileName\b|""airapp\.json""") &&
                !allowedReaders.Contains(relative, StringComparer.OrdinalIgnoreCase))
            {
                offenders.Add($"{relative} 自己开包挑清单");
            }

            if (manifestTypeDeclaration.IsMatch(source) &&
                !string.Equals(relative, sdkModelFile, StringComparison.OrdinalIgnoreCase))
            {
                offenders.Add($"{relative} 又声明了一个 AirAppManifest 模型");
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处绕开统一读取器：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// 崩溃转储的磁盘契约（目录名 <c>crashes</c>、通配 <c>crash-*.txt</c>、标记 <c>latest.txt</c>）
    /// 只认 <c>core/.../Diagnostics/CrashDumpLayout.cs</c>。宿主写、启动器读，两个二进制之间没有共享类型，
    /// 2026-09-21 收口前这三个名字在 4 个文件里各抄一份；抄错一个字母的症状是崩溃对话框什么也不显示，
    /// 也就是最需要诊断信息的那一刻静默失效。
    /// </summary>
    [Fact]
    public void CrashDumpContract_LivesInExactlyOnePlace()
    {
        string[] forbidden = ["\"crashes\"", "\"crash-*.txt\"", "\"latest.txt\""];
        var allowedFile = @"core\LanMountainDesktop.Core\Diagnostics\CrashDumpLayout.cs";
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            if (string.Equals(RelativeToRepo(file), allowedFile, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var trimmed = lines[index].AsSpan().TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith('*'))
                {
                    continue;
                }

                var hit = forbidden.FirstOrDefault(marker => lines[index].Contains(marker, StringComparison.Ordinal));
                if (hit is not null)
                {
                    offenders.Add($"{RelativeToRepo(file)}:{index + 1} 自己写了崩溃转储契约（{hit}），请用 CrashDumpLayout");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处绕开 CrashDumpLayout 的崩溃转储字面量：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// 数据位置配置（<c>data-location.config.json</c> 与它的三个字段名）只认
    /// <c>core/.../Data/DataLocationContract.cs</c>：写它的是启动器，读它的是启动器与宿主两侧。
    /// 抄错的症状不是报错，而是"数据位置设置静默失效"——便携安装会被当成系统安装，
    /// 用户看到的是"我的数据不见了"。<c>"Desktop"</c> 这个词太通用，不在这条判据里（真源仍是指向常量的别名）。
    /// </summary>
    [Fact]
    public void DataLocationConfigContract_LivesInExactlyOnePlace()
    {
        string[] forbidden =
        [
            "\"data-location.config.json\"",
            "\"dataLocationMode\"",
            "\"systemDataPath\"",
            "\"portableDataPath\"",
        ];

        var allowedFile = @"core\LanMountainDesktop.Core\Data\DataLocationContract.cs";
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            if (string.Equals(RelativeToRepo(file), allowedFile, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var trimmed = lines[index].AsSpan().TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith('*'))
                {
                    continue;
                }

                var hit = forbidden.FirstOrDefault(marker => lines[index].Contains(marker, StringComparison.Ordinal));
                if (hit is not null)
                {
                    offenders.Add($"{RelativeToRepo(file)}:{index + 1} 自己写了数据位置契约（{hit}），请用 DataLocationContract");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处绕开 DataLocationContract 的落盘契约字面量："
            + $"{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// 用户资料目录下的那个品牌文件夹名（<c>%LocalAppData%\LanMountainDesktop</c>、
    /// <c>%Documents%\LanMountainDesktop</c>）只认 <c>core/.../Data/UserDataRoot.cs</c>。
    /// 宿主、首启向导、Core 的路径解析器、安装器四个二进制都往这里落东西（设置、日志、崩溃转储、
    /// 隐私同意书、录音、缓存），2026-09-21 收口前有 27 处各自写了字面量。抄错的后果不是报错，
    /// 而是某个二进制去一个空目录里找用户的数据——"设置没了"。
    ///
    /// 判据只看"根是用户资料目录 + 拼了这个名字"的组合：<c>DeploymentLocator</c> 与 <c>ErrorWindow</c>
    /// 里那些 <c>Path.Combine(solutionRoot, \"LanMountainDesktop\", \"bin\", …)</c> 找的是仓内编译产物，
    /// 不是用户数据，不在这一族；<c>PublicAppInfoService</c> 里那个是应用显示名，同理。
    /// </summary>
    [Fact]
    public void UserDataRootFolderName_LivesInExactlyOnePlace()
    {
        var allowedFile = @"core\LanMountainDesktop.Core\Data\UserDataRoot.cs";
        var userProfileRoot = new Regex(
            @"LocalApplicationData|localAppData|appData|MyDocuments|SpecialFolder\.Documents",
            RegexOptions.Compiled);
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            if (string.Equals(RelativeToRepo(file), allowedFile, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                if (!lines[index].Contains("\"LanMountainDesktop\"", StringComparison.Ordinal))
                {
                    continue;
                }

                var trimmed = lines[index].AsSpan().TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith('*'))
                {
                    continue;
                }

                var statement = string.Join(
                    ' ',
                    lines[Math.Max(0, index - 4)..(index + 1)]);
                if (statement.Contains("Path.Combine(", StringComparison.Ordinal) && userProfileRoot.IsMatch(statement))
                {
                    offenders.Add($"{RelativeToRepo(file)}:{index + 1} 自己拼了用户数据目录，请用 UserDataRoot");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处硬编码的用户数据目录名：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// 快照元数据（<c>{数据根}/update/snapshots/*.json</c>）只有一个模型：
    /// <c>core/.../Update/SnapshotMetadata.cs</c>。宿主写、宿主与启动器两侧读，收口前是两份逐字相同的声明
    /// （<c>ApplySnapshotMetadata</c> 与 <c>SnapshotMetadata</c>，7 个属性一个不差），
    /// 改一边不会有任何编译错误，只会让另一边把 <c>sourceDirectory</c> 读成空串——
    /// 症状是"旧版本被清理掉、想回滚时没得回滚"。同理，<c>"pending"</c> 这个落盘状态值也只许出现在真源里。
    /// </summary>
    [Fact]
    public void SnapshotMetadataModel_LivesInExactlyOnePlace()
    {
        var allowedFile = @"core\LanMountainDesktop.Core\Update\SnapshotMetadata.cs";
        var declaration = new Regex(@"\b(class|record)\s+SnapshotMetadata\b", RegexOptions.Compiled);
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            if (string.Equals(RelativeToRepo(file), allowedFile, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var trimmed = lines[index].AsSpan().TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith('*'))
                {
                    continue;
                }

                if (declaration.IsMatch(lines[index]))
                {
                    offenders.Add($"{RelativeToRepo(file)}:{index + 1} 又声明了一个 SnapshotMetadata");
                }

                if (lines[index].Contains("\"pending\"", StringComparison.Ordinal))
                {
                    offenders.Add($"{RelativeToRepo(file)}:{index + 1} 自己写了快照状态值，请用 SnapshotMetadata.PendingStatus");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处绕开共享快照模型：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// <c>.axaml</c> 里写 <c>Classes="foo"</c> 而全仓没有任何 <c>Selector="...foo"</c> 定义它，
    /// 控件就静默走默认样式：不报错、不警告，只是那块 UI 少了它本该有的样子——
    /// 2026-09-21 首扫量到 4 个，其中 <c>component-editor-primary-text</c> 是"主/次文本"配对里
    /// 只有次文本有样式（另一个 <c>component-editor-numeric</c> 从来没定义过，已删标记；
    /// 补样式算设计决定，没替他做）。外部主题库自带的类名要登记在名单里并写清来源。
    /// </summary>
    [Fact]
    public void StyleClasses_UsedInMarkup_AreAlsoDefined()
    {
        // 本仓扫不到、由外部主题库定义的类名。新增就往这里加一条并写清是哪来的。
        string[] externalClasses =
        [
            "accent",       // Avalonia FluentTheme 的强调色按钮类
            "AppBarButton", // FluentAvalonia 的应用栏按钮
        ];

        var markupFiles = ProductionDirectories
            .Select(part => Path.Combine(RepoRoot, part))
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.axaml", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var defined = new HashSet<string>(StringComparer.Ordinal);
        var classInSelector = new Regex(@"\.([A-Za-z][\w\-]*)", RegexOptions.Compiled);

        foreach (var file in markupFiles.Concat(SourceFiles()))
        {
            var text = File.ReadAllText(file);
            foreach (Match selector in Regex.Matches(text, "Selector=\"([^\"]+)\"").Cast<Match>())
            {
                foreach (Match className in classInSelector.Matches(selector.Groups[1].Value))
                {
                    defined.Add(className.Groups[1].Value);
                }
            }

            foreach (Match added in Regex.Matches(text, "Classes\\.Add\\(\\s*\"([^\"]+)\"").Cast<Match>())
            {
                defined.Add(added.Groups[1].Value);
            }
        }

        var orphans = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in markupFiles)
        {
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var match = Regex.Match(lines[index], "\\sClasses=\"([^\"]+)\"");
                if (!match.Success)
                {
                    continue;
                }

                var undefined = match.Groups[1].Value
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(className => !defined.Contains(className)
                        && !externalClasses.Contains(className, StringComparer.Ordinal))
                    .ToArray();

                foreach (var className in undefined)
                {
                    orphans.Add($"{className}  ({RelativeToRepo(file)}:{index + 1})");
                }
            }
        }

        Assert.True(
            orphans.Count == 0,
            $"{orphans.Count} 个样式类只在标记里被贴上、没有任何选择器定义它，控件实际走默认样式："
            + $"{Environment.NewLine}{string.Join(Environment.NewLine, orphans)}");
    }

    /// <summary>
    /// 上一条守卫的反方向：样式定义了却没人用，也是孤儿。2026-09-21 首扫量到 18 个真正没人用的
    /// 样式类（GlassModule 8 个、NavigationStyles 3 个、SettingsAnimations 2 个、icon-l 尺寸档、
    /// 组件编辑器的 footer 按钮样式两份重复声明等），合计 26 个 Style 块 / 255 行——其中
    /// <c>component-editor-footer-button</c> 连"页脚"这个容器都已经不在那个窗口里了。
    ///
    /// 判"用过"很宽松：任何 .axaml 的 <c>Classes="…"</c>、或任何 .cs 里出现的同名字符串字面量
    /// （<c>Classes.Add(ComponentChromePanel.GlassPanelClass)</c> 这种经常量绕过去的也算）。
    /// 宽松只会让守卫偏乐观，不会误红。附带的属性选择器（<c>[(x|Y.Prop)=True]</c>）不算类名。
    /// </summary>
    [Fact]
    public void StyleClasses_DefinedInSelectors_AreAlsoUsed()
    {
        var selectorPattern = new Regex("Selector\\s*=\\s*\"([^\"]+)\"", RegexOptions.Compiled);
        var attachedPropertyBracket = new Regex(@"\[[^\]]*\]", RegexOptions.Compiled);
        var classNamePattern = new Regex(@"\.([A-Za-z][\w\-]*)", RegexOptions.Compiled);

        var defined = new Dictionary<string, string>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);

        var styleSources = SourceFiles().Concat(MarkupFiles());

        foreach (var file in styleSources)
        {
            var text = File.ReadAllText(file);

            foreach (Match selector in selectorPattern.Matches(text).Cast<Match>())
            {
                var withoutAttachedProps = attachedPropertyBracket.Replace(selector.Groups[1].Value, " ");
                foreach (Match className in classNamePattern.Matches(withoutAttachedProps).Cast<Match>())
                {
                    defined.TryAdd(className.Groups[1].Value, RelativeToRepo(file));
                }
            }
        }

        foreach (var file in SourceFiles())
        {
            var text = File.ReadAllText(file);

            // 代码里任何同名字符串字面量都算"可能用它贴过类"，包括经常量绕的写法。
            foreach (Match literal in Regex.Matches(text, "\"([A-Za-z][\\w\\-]*)\"").Cast<Match>())
            {
                used.Add(literal.Groups[1].Value);
            }
        }

        foreach (var file in MarkupFiles())
        {
            foreach (Match classes in Regex.Matches(File.ReadAllText(file), "Classes=\"([^\"]+)\"").Cast<Match>())
            {
                foreach (var className in classes.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    used.Add(className);
                }
            }
        }

        var orphans = defined
            .Where(entry => !used.Contains(entry.Key))
            .Select(entry => $"{entry.Key}  ({entry.Value})")
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            orphans.Length == 0,
            $"{orphans.Length} 个样式类只有定义、没有任何控件贴它，是删了也没人知道的孤儿样式："
            + $"{Environment.NewLine}{string.Join(Environment.NewLine, orphans)}");
    }

    /// <summary>
    /// 主程序设置文件的文件名只认 <c>UserDataRoot.SettingsFileName</c> 一处。
    /// 读写它的是三个二进制（宿主读写、首启向导写、Core 的启动偏好读），各自的目录不同但文件名必须同一个；
    /// 2026-09-21 收口前 <c>"settings.json"</c> 这个字面量在生产代码里有 7 份。
    /// 漂了的后果不是崩，而是"设置读不到、界面回默认值"——和它本来要防的那类事故一模一样。
    /// </summary>
    [Fact]
    public void SettingsFileName_LivesInExactlyOnePlace()
    {
        var allowedFile = @"core\LanMountainDesktop.Core\Data\UserDataRoot.cs";
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            if (string.Equals(RelativeToRepo(file), allowedFile, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var trimmed = lines[index].AsSpan().TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith('*'))
                {
                    continue;
                }

                if (lines[index].Contains("\"settings.json\"", StringComparison.Ordinal))
                {
                    offenders.Add($"{RelativeToRepo(file)}:{index + 1} 自己写了设置文件名，请用 UserDataRoot.SettingsFileName");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处硬编码的 settings.json 文件名：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// 组件自缩放的设计基准格子边长只有 <c>desktop/.../Views/Components/ComponentDesignMetrics.cs</c> 一处声明。
    /// 收口前 12 个组件各自写了 <c>private const double BaseCellSize = 48d;</c>：改一处基准，
    /// 其余 11 个组件在同一个网格里按旧基准缩放，症状是"某个卡片比别的胖一圈"，
    /// 而且没人会想到去数 12 份。
    /// 2026-09-22 补第二半：那次的收口只删了重复的常量声明，调用点里把 48 当基准的字面量还有 39 处
    /// （16 处 <c>_currentCellSize / 48d</c> 一类换算、23 处 <c>double _currentCellSize = 48;</c> 默认值），
    /// 它们同样在绕过这个家——把基准从 48 改到别处时，这 39 处不会跟着动，症状正是"一半组件缩放不对"。
    /// 现在三种形态（声明、除法基准字面量、字段默认值）一起拦。
    /// </summary>
    [Fact]
    public void ComponentBaseCellSize_LivesInExactlyOnePlace()
    {
        var declRe = new Regex(@"(?:private|internal|public|protected)\s+(?:static\s+)?const\s+double\s+BaseCellSize\b");
        var divisionRe = new Regex(@"\b(?:_?currentCellSize|_lastAppliedCellSize|cellSize|CellSize)\w*\s*/\s*48(?:\.0+)?d?\b");
        var initRe = new Regex(@"double\s+_\w*[Cc]ellSize\s*=\s*48;");
        var allowedFile = @"desktop\LanMountainDesktop\Views\Components\ComponentDesignMetrics.cs";

        var offenders = new List<string>();
        foreach (var file in SourceFiles())
        {
            if (string.Equals(RelativeToRepo(file), allowedFile, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var (line, number) in CodeLines(file))
            {
                if (declRe.IsMatch(line))
                {
                    offenders.Add($"{RelativeToRepo(file)}:{number} 自己声明了 BaseCellSize，请用 ComponentDesignMetrics.BaseCellSize");
                }
                else if (divisionRe.IsMatch(line))
                {
                    offenders.Add($"{RelativeToRepo(file)}:{number} 拿字面量 48 当缩放基准，请用 ComponentDesignMetrics.BaseCellSize");
                }
                else if (initRe.IsMatch(line))
                {
                    offenders.Add($"{RelativeToRepo(file)}:{number} 格子边长字段默认值写了字面量 48，请用 ComponentDesignMetrics.BaseCellSize");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处重复/绕过家的组件设计基准：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// 更新布局的磁盘名只许 Core 的 <c>UpdatePaths</c> 一家有。2026-09-22 实测：宿主
    /// <c>PlondsApplyPaths</c> 把 7 个文件名 + 4 个目录名各抄了一份常量，而 Core 那边
    /// <c>GetPlondsFileMapPath</c> / <c>GetPlondsSignaturePath</c> / <c>GetPublicKeyFileName</c> /
    /// <c>GetLauncherDataRoot</c> 四个访问器零调用——"立了家不等于收了口"。
    /// 两份常量今天还逐字相同，改一边就是跨二进制的磁盘契约漂移（更新包读不到、回滚找不到快照），
    /// 而且启动器与安装器各自算同一个路径时没有任何东西保证它们算得一样。
    /// 文件名按字面量判（除家以外任何生产 .cs 出现都红）；目录名 update/incoming/objects/snapshots
    /// 是常用词、另有导航键与 CLI 动词撞名，所以只判"常量声明"这一形态——复制真源的动作必然是声明一个常量。
    /// </summary>
    [Fact]
    public void UpdateLayoutDiskNames_LiveInExactlyOnePlace()
    {
        var home = @"core\LanMountainDesktop.Core\Update\UpdatePaths.cs";
        var fileNames = new[]
        {
            "plonds-filemap.json", "plonds-filemap.sig", "plonds-update.json",
            "files.json", "files.json.sig", "update.zip", "public-key.pem",
        };
        var directoryNames = new[] { "update", "incoming", "objects", "snapshots" };
        var dirDeclRe = directoryNames
            .Select(name => new Regex($@"const\s+string\s+\w+\s*=\s*""{name}""\s*;"))
            .ToList();

        var offenders = new List<string>();
        foreach (var file in SourceFiles())
        {
            if (string.Equals(RelativeToRepo(file), home, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var (line, number) in CodeLines(file))
            {
                foreach (var name in fileNames)
                {
                    if (line.Contains($"\"{name}\"", StringComparison.Ordinal))
                    {
                        offenders.Add(
                            $"{RelativeToRepo(file)}:{number} 自己写了磁盘文件名 \"{name}\"，请用 UpdatePaths 的 Get*Name() / Get*Path()");
                    }
                }

                for (var index = 0; index < dirDeclRe.Count; index++)
                {
                    if (dirDeclRe[index].IsMatch(line))
                    {
                        offenders.Add(
                            $"{RelativeToRepo(file)}:{number} 自己声明了更新布局目录名 \"{directoryNames[index]}\"，请用 UpdatePaths 的目录访问器");
                    }
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处绕开 UpdatePaths 的更新布局磁盘名：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// 每个 <c>.cs</c> 都得被某个工程认领：就近目录有 <c>.csproj</c>，且不被那个工程的
    /// <c>Compile Remove/Exclude</c> 排除。2026-09-22 第一次量：18 个工程的 Compile 项共 899 个文件，
    /// 磁盘上 900 个，多出来的那个是 <c>scripts/GitCommitAnalyzer.cs</c>——662 行 C#，没有任何工程编译它、
    /// 没有任何脚本或工作流调用它，而同样的活在 <c>scripts/Analyze-GitCommits.ps1</c> 与
    /// <c>scripts/analyze_git_commits.py</c> 里各有一份（那两份是 dev 工具，不进产品，先只删这份没人认领的）。
    /// 为什么值得立一条守卫：编译器不看它、IDE 也不看它，而两条零使用棘轮把 <c>scripts</c> 当生产目录数——
    /// 它声明的类型会被当成"待判死码"，它自己文件体内的互相引用又会被当成"有人用"。
    /// 判据是文本级的（只认"就近有 csproj"与"被 Remove/Exclude 命中"），不展开 MSBuild 条件与
    /// <c>Directory.Build.props</c>：那些只会让"其实没人编译"更多，不会把没人编译的判成有人编译。
    /// </summary>
    [Fact]
    public void EveryCSharpFile_IsClaimedByAProject()
    {
        var projectDirectories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in Directory.EnumerateFiles(RepoRoot, "*.csproj", SearchOption.AllDirectories))
        {
            if (IsUnderBuildArtifactDirectory(project))
            {
                continue;
            }

            projectDirectories[Path.GetDirectoryName(project)!] = project;
        }

        var scannedDirectories = new[] { "core", "desktop", "airapp", "install", "mobile", "platform", "packaging", "scripts", "tests" };
        var offenders = new List<string>();
        var claimed = 0;

        foreach (var directory in scannedDirectories)
        {
            var root = Path.Combine(RepoRoot, directory);
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (IsUnderBuildArtifactDirectory(file))
                {
                    continue;
                }

                var owner = FindNearestProjectDirectory(file, projectDirectories.Keys);
                if (owner is null)
                {
                    offenders.Add($"{RelativeToRepo(file)} 没有任何工程认领（就近目录里没有 .csproj），它不会被编译进任何二进制");
                    continue;
                }

                var relativeToProject = Path.GetRelativePath(owner, file).Replace('\\', '/');
                if (IsExcludedByProject(File.ReadAllText(projectDirectories[owner]), relativeToProject))
                {
                    continue;
                }

                claimed++;
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{claimed} 个 .cs 被工程认领，另有 {offenders.Count} 个没人认领：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    private static bool IsUnderBuildArtifactDirectory(string path) => path
        .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        .Any(segment => segment is "obj" or "bin" or ".git" or "artifacts" or "node_modules");

    private static string? FindNearestProjectDirectory(string file, IEnumerable<string> projectDirectories)
    {
        var current = Path.GetDirectoryName(file);
        while (!string.IsNullOrEmpty(current))
        {
            if (projectDirectories.Contains(current))
            {
                return current;
            }

            current = Path.GetDirectoryName(current);
        }

        return null;
    }

    /// <summary>
    /// 工程自己说"这些不编译"（<c>Compile Remove/Exclude</c>）就当它有意为之：
    /// 模板包 <c>content/**</c> 与 SDK 的 <c>_build_verify_*/**/*.cs</c> 临时目录都是这种。
    /// </summary>
    private static bool IsExcludedByProject(string projectXml, string relativeToProject) => Regex
        .Matches(projectXml, @"<Compile\s+(?:Remove|Exclude)=""([^""]+)""")
        .Select(match => match.Groups[1].Value.Replace('\\', '/'))
        .Any(pattern => GlobMatches(pattern.TrimEnd('/'), relativeToProject));

    /// <summary>只支持 MSBuild 通配里会出现的三种：<c>**/</c>（任意层级，可空）、<c>**</c>、<c>*</c>（单层）。</summary>
    private static bool GlobMatches(string pattern, string path)
    {
        var builder = new StringBuilder("^");
        for (var index = 0; index < pattern.Length; index++)
        {
            if (pattern.Length >= index + 3 && pattern[index..(index + 3)] == "**/")
            {
                builder.Append("(?:.*/)?");
                index += 2;
            }
            else if (pattern.Length >= index + 2 && pattern[index..(index + 2)] == "**")
            {
                builder.Append(".*");
                index++;
            }
            else if (pattern[index] == '*')
            {
                builder.Append("[^/]*");
            }
            else
            {
                builder.Append(Regex.Escape(pattern[index..(index + 1)]));
            }
        }

        return new Regex(builder.Append('$').ToString()).IsMatch(path);
    }

    /// <summary>
    /// 网格密度/边缘留白的量程与默认值只在 <c>desktop/.../DesktopEditing/DesktopGridLimits.cs</c> 声明一份，
    /// 并且设置页的滑杆必须绑 <c>ComponentsSettingsPageViewModel</c> 暴露的量程，不能在 .axaml 里另写数字。
    /// 收口前这 6 个数在 <c>MainWindow</c>（含 partial 分片）、<c>FusedDesktopEditGridAdapter</c>、
    /// <c>AppSettingsSnapshot</c> 默认值里各一份，滑杆又在 markup 里抄了第四份 6/96 与 0/30。
    /// 漂了的症状不是崩，而是"滑杆拖到尽头网格不动"或"存进去的值被运行期悄悄钳掉"。
    /// </summary>
    [Fact]
    public void DesktopGridLimits_LiveInExactlyOnePlace()
    {
        var declRe = new Regex(
            @"(?:private|internal|public|protected)\s+(?:static\s+)?const\s+int\s+(MinShortSideCells|MaxShortSideCells|DefaultShortSideCells|MinEdgeInsetPercent|MaxEdgeInsetPercent|DefaultEdgeInsetPercent)\b");
        var allowedFile = @"desktop\LanMountainDesktop\DesktopEditing\DesktopGridLimits.cs";

        var offenders = new List<string>();
        foreach (var file in SourceFiles())
        {
            if (string.Equals(RelativeToRepo(file), allowedFile, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var (line, number) in CodeLines(file))
            {
                var match = declRe.Match(line);
                if (match.Success)
                {
                    offenders.Add($"{RelativeToRepo(file)}:{number} 自己声明了 {match.Groups[1].Value}，请用 DesktopGridLimits");
                }
            }
        }

        var sliderMarkup = Path.Combine(
            RepoRoot,
            @"desktop\LanMountainDesktop\Views\SettingsPages\ComponentsSettingsPage.axaml".Replace('\\', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(sliderMarkup), $"找不到网格密度设置页 {RelativeToRepo(sliderMarkup)}");

        foreach (var (line, number) in CodeLines(sliderMarkup))
        {
            if (Regex.IsMatch(line, @"(?:Minimum|Maximum)\s*=\s*""\d+"""))
            {
                offenders.Add($"{RelativeToRepo(sliderMarkup)}:{number} 滑杆量程写死了数字，请绑 ShortSideCellsMinimum/Maximum 或 EdgeInsetPercentMinimum/Maximum");
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处重复的网格量程真源：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// 同一个文件的前导 using 块里不许把同一条指令写两遍（编译器 CS0105）。
    /// 看着无害，但它是 2026-09-21 那次 plugin→airapp 批量改名留下的疤：脚本按目录补 using，
    /// 补了 26 处重复，把"这个文件到底依赖谁"读成了三行一样的话。
    /// 只查前导块，因为 namespace 块内部的 using 只对自身生效，删掉兄弟块里的同名指令会真的改变解析结果。
    /// </summary>
    [Fact]
    public void UsingDirectives_AreNotDeclaredTwiceInAFile()
    {
        var offenders = new List<string>();
        foreach (var file in RepositoryCSharpFiles())
        {
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (line, number) in RawLines(file))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal)
                    || trimmed.StartsWith("/*", StringComparison.Ordinal) || trimmed[0] == '*')
                {
                    continue;
                }

                if (!trimmed.StartsWith("using ", StringComparison.Ordinal) || !trimmed.EndsWith(';'))
                {
                    break;
                }

                var normalized = Regex.Replace(trimmed, @"\s+", " ");
                if (seen.TryGetValue(normalized, out var first))
                {
                    offenders.Add($"{RelativeToRepo(file)}:{number} 与第 {first} 行重复：{normalized}");
                }
                else
                {
                    seen[normalized] = number;
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处重复的 using 指令：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// 一条成员声明（或一条语句）占一行。批量删声明的脚本如果只删内容不删换行，就会把前后两条并成一行：
    /// 编译器不在乎，所以构建全绿、测试全绿，但下一次按行做的扫描（本文件里那一大排守卫全是按行的）
    /// 会成批漏掉这条，人读 diff 也看不出少了什么。
    /// 2026-09-21 删 12 份 <c>BaseCellSize</c> 与 b21d296 删租约字段时各留下过这种并线，共 12 处；
    /// 2026-09-22 又把口径从"声明"扩到"语句"，抓到 <c>LocalizationService.NormalizeLanguageCode</c> 的
    /// 方法体开括号与首条 <c>if</c> 并在同一行（1 处，非本轮脚本所伤）。
    /// 认的是"分号或大括号后紧跟 4 个以上空格再跟声明/语句关键字"，单空格的 <c>{ get; set; }</c> 这类不会误报。
    /// </summary>
    [Fact]
    public void MemberDeclarations_OwnTheirOwnLine()
    {
        var joined = new Regex(@".*[;{]\s{4,}(?:private|internal|public|protected|static|readonly|const|sealed|if|foreach|for|while|switch|return|throw|var|try|lock)[ \t(;]");
        var offenders = new List<string>();

        foreach (var file in RepositoryCSharpFiles())
        {
            foreach (var (line, number) in RawLines(file))
            {
                var trimmed = line.AsSpan().TrimStart();
                if (trimmed.IsEmpty || trimmed.StartsWith("//") || trimmed.StartsWith('*'))
                {
                    continue;
                }

                if (joined.IsMatch(line))
                {
                    offenders.Add($"{RelativeToRepo(file)}:{number}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处把两条成员声明写在同一行：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// 发往对端的身份字符串只有 <c>desktop/.../Services/HttpUserAgents.cs</c> 一处字面量。
    /// 收口前同一个完整 Chrome UA 在 4 个组件里逐字抄了 4 遍、市场身份在 4 个市场服务里抄了 4 遍、
    /// 裸产品名抄了 3 遍。这类副本漂移不会崩，只会"某个组件的图片突然 403"，而且只在那一家 CDN 改口径时出现。
    /// 允许出现的字面量只有 <c>"User-Agent"</c> 本身（那是请求头的名字，不是身份）。
    /// </summary>
    [Fact]
    public void HttpRequestIdentityStrings_LiveInOnePlace()
    {
        var allowedFile = @"desktop\LanMountainDesktop\Services\HttpUserAgents.cs";
        var mentionsIdentity = new Regex(@"User-?Agent");
        var anyLiteral = new Regex(@"""(?:[^""\\]|\\.)*""");
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            if (string.Equals(RelativeToRepo(file), allowedFile, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var (line, number) in CodeLines(file))
            {
                if (!mentionsIdentity.IsMatch(line) && !line.Contains("Mozilla/", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (Match literal in anyLiteral.Matches(line))
                {
                    if (literal.Value == "\"User-Agent\"")
                    {
                        continue;
                    }

                    offenders.Add($"{RelativeToRepo(file)}:{number} 自带了一份请求身份字面量 {literal.Value}，请用 HttpUserAgents");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处自带请求身份字面量：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// 部署标记文件名（<c>.current</c> / <c>.partial</c> / <c>.destroy</c>）只准写
    /// <c>core/.../Deployment/DeploymentLayout.cs</c> 那一处。这条规则 2026-09 就写在了那个类的注释里
    /// （"禁止在任何一侧硬编码这些标记文件名"），却一条守卫都没配，于是宿主与 Core 里攒了 33 处字面量。
    /// 这是跨二进制的磁盘契约：安装器打的标记、启动器与宿主按名字判"这份部署能不能用"，
    /// 任何一侧改了拼写都不会编译报错——症状是把没复制完的目录当可用版本启动（或永远清不掉旧版本）。
    /// 发布侧 PLONDS 工具链在另一份解决方案里、不引用 Core，所以它那份抄写单独按字节钉住（见方法末尾）。
    /// </summary>
    [Fact]
    public void DeploymentMarkerFileNames_LiveInExactlyOnePlace()
    {
        var allowedFile = @"core\LanMountainDesktop.Core\Deployment\DeploymentLayout.cs";
        var markers = new[] { "\".current\"", "\".partial\"", "\".destroy\"" };
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            if (string.Equals(RelativeToRepo(file), allowedFile, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var (line, number) in CodeLines(file))
            {
                foreach (var marker in markers)
                {
                    if (line.Contains(marker, StringComparison.Ordinal))
                    {
                        offenders.Add($"{RelativeToRepo(file)}:{number} 自带了部署标记 {marker}，请用 DeploymentLayout");
                    }
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处硬编码的部署标记文件名：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");

        // 发布侧（PLONDS 工具链）是另一份解决方案，也不引用 Core，拿不到上面那三个常量，
        // 所以它只能自己抄一份。这里不引入工程引用（那是构建结构决定，不是收口能顺手做的），
        // 改成把它的字面量按字节钉住：任何一侧改拼写，这条都会红并点名两边。
        var publisher = Path.Combine(
            RepoRoot,
            @"PenguinLogisticsOnlineNetworkDistributionSystem\src\Plonds.Core\Publishing\PayloadUtilities.cs");
        Assert.True(
            File.Exists(publisher),
            $"找不到发布侧的标记判定 {RelativeToRepo(publisher)}；它搬走或改名了，请把上面那段契约检查一起挪过去，别让它静默失效");

        var publisherSource = File.ReadAllText(publisher);
        var diverged = new List<string>();
        foreach (var (name, value) in new[]
                 {
                     ("CurrentMarkerFileName", DeploymentLayout.CurrentMarkerFileName),
                     ("PartialMarkerFileName", DeploymentLayout.PartialMarkerFileName),
                     ("DestroyMarkerFileName", DeploymentLayout.DestroyMarkerFileName),
                 })
        {
            if (!publisherSource.Contains("\"" + value + "\"", StringComparison.Ordinal))
            {
                diverged.Add($"{name} = \"{value}\"");
            }
        }

        Assert.True(
            diverged.Count == 0,
            $"发布侧与部署契约对不上，改一边另一边不会编译报错，只会把没复制完的目录当可用版本启动："
            + $"{Environment.NewLine}{string.Join(Environment.NewLine, diverged)}");
    }

    /// <summary>
    /// 界面语言码只认 <c>core/LanMountainDesktop.Core/Localization/LanguageCodes.cs</c> 一处。
    /// 收口前同一张归一化表有三份实现，其中两份在两份二进制里（宿主 <c>LocalizationService</c>
    /// 与启动器 <c>LanguagePreferenceService</c>，读的是 <c>settings.json</c> 里同一个 <c>LanguageCode</c> 字段），
    /// 第三份在宿主的 <c>ClockAirAppTimeFormatter</c>；默认值另有 3 处绕开宿主那份直接写字面量。
    /// 漂了不报错，症状是"启动动画是中文、进桌面变韩文"。
    ///
    /// 免检的两处不是漏网，是另一种语义：它们要的是"简体中文这个语言本身"（拼音排序、中文数字格式化），
    /// 不是"界面的默认语言"——把默认语言从中文改成别的，这两处不该跟着变。
    /// </summary>
    [Fact]
    public void LanguageCodes_LiveInExactlyOnePlace()
    {
        var homeFile = @"core\LanMountainDesktop.Core\Localization\LanguageCodes.cs";
        var semanticExceptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [@"desktop\LanMountainDesktop\Services\WindowsStartMenuService.cs"] =
                "开始菜单按拼音排序用的是简体中文的 CompareInfo，与默认语言无关",
            [@"desktop\LanMountainDesktop\Views\Components\StandbyDigitalClockWidget.axaml.cs"] =
                "待机时钟按简体中文格式化数字/日期，与默认语言无关",
        };
        var codes = new[] { "\"zh-CN\"", "\"en-US\"", "\"ja-JP\"", "\"ko-KR\"" };
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            var relative = RelativeToRepo(file);
            if (string.Equals(relative, homeFile, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var (line, number) in CodeLines(file))
            {
                foreach (var code in codes)
                {
                    if (!line.Contains(code, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var normalized = relative.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                    if (semanticExceptions.TryGetValue(normalized, out _))
                    {
                        continue;
                    }

                    offenders.Add($"{relative}:{number} 自带了语言码 {code}，请用 LanguageCodes（要逐字比较用 IsEnglishCode）");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处硬编码的界面语言码：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");

        // 免检条目也会过期：那两处要是哪天不再写字面量了，条目就该删掉，不然它会替下一次抄写挡枪。
        var stale = new List<string>();
        foreach (var (relative, reason) in semanticExceptions)
        {
            var fullPath = Path.Combine(RepoRoot, relative);
            if (!File.Exists(fullPath) || !codes.Any(code => File.ReadAllText(fullPath).Contains(code, StringComparison.Ordinal)))
            {
                stale.Add($"{relative}（登记理由：{reason}）");
            }
        }

        Assert.True(
            stale.Count == 0,
            $"{stale.Count} 条语言码免检条目已失效，请删掉：{Environment.NewLine}{string.Join(Environment.NewLine, stale)}");
    }

    /// <summary>
    /// 小米天气接口 locale 参数的拼法（<c>en_us</c> / <c>zh_cn</c>，供应商自己的写法）只认
    /// <c>desktop/.../Services/XiaomiWeatherLocales.cs</c> 一处，也不许再各写一份 <c>NormalizeWeatherLocale</c>。
    /// 收口前这两个拼法散在 3 份逐字相同的私有方法（刷新服务、设置页视图模型、天气组件基类）
    /// 与 1 处选项默认值里。改一份剩下三份照旧，而两边都"看起来工作正常"——
    /// 症状是设置页的天气是英文、桌面上那个组件还是中文。
    /// </summary>
    [Fact]
    public void WeatherProviderLocaleValues_LiveInExactlyOnePlace()
    {
        var homeFile = @"desktop\LanMountainDesktop\Services\XiaomiWeatherLocales.cs";
        var literals = new[] { "\"en_us\"", "\"zh_cn\"" };
        var declRe = new Regex(@"^\s*(?:private|internal|public|protected)\s+static\s+string\s+NormalizeWeatherLocale\s*\(");
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            var relative = RelativeToRepo(file);
            if (string.Equals(relative, homeFile, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var (line, number) in CodeLines(file))
            {
                foreach (var literal in literals)
                {
                    if (line.Contains(literal, StringComparison.Ordinal))
                    {
                        offenders.Add($"{relative}:{number} 自带了天气 locale {literal}，请用 XiaomiWeatherLocales");
                    }
                }

                if (declRe.IsMatch(line))
                {
                    offenders.Add($"{relative}:{number} 又写了一份 NormalizeWeatherLocale，请用 XiaomiWeatherLocales.ForLanguageCode");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处重复的天气 locale 真源：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// 短文本归一化只认 <c>desktop/LanMountainDesktop/Helpers/CompactText.cs</c> 一处。
    /// 收口前 <c>NormalizeCompactText</c> 连同它专用的 <c>MultiWhitespaceRegex</c> 在 9 个组件里逐字抄了
    /// 9 遍、23 个调用点。这份重复的代价很具体：同一个信息源的标题里带换行时，
    /// 一张卡显示成两个空格、另一张显示成一个洞，而两张卡贴在同一个桌面上。
    /// 方法名和那个正则字段名一起禁，防止有人再抄一份只抄一半（漏了 RegexOptions.Compiled 那种）。
    /// </summary>
    [Fact]
    public void CompactTextNormalizer_LivesInExactlyOnePlace()
    {
        var allowedFile = @"desktop\LanMountainDesktop\Helpers\CompactText.cs";
        var declRe = new Regex(@"(?:private|internal|public|protected)\s+(?:static\s+)?string\s+NormalizeCompactText\s*\(");
        var fieldRe = new Regex(@"(?:private|internal|public|protected)\s+static\s+readonly\s+Regex\s+MultiWhitespace\w*");
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            if (string.Equals(RelativeToRepo(file), allowedFile, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var (line, number) in CodeLines(file))
            {
                if (declRe.IsMatch(line))
                {
                    offenders.Add($"{RelativeToRepo(file)}:{number} 又写了一份 NormalizeCompactText，请用 CompactText.Normalize");
                }
                else if (fieldRe.IsMatch(line))
                {
                    offenders.Add($"{RelativeToRepo(file)}:{number} 又抄了一份压空白的正则字段，请用 CompactText.Normalize");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处重复的短文本归一化：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// "尽力删掉，删不掉别静默"只认 <c>core/LanMountainDesktop.Core/IO/FileOperationRetryHelper.cs</c> 一处。
    /// 收口前 <c>TryDeleteFile</c> 有 7 份、<c>TryDeleteDirectory</c> 有 6 份，散在宿主、启动器、安装器三个二进制里，
    /// 而且已经各自漂开：只有 2 份删前清只读属性（另 5 份删只读文件是静默失败，症状＝卸载后 AppData 里还留着东西），
    /// 只有 1 份把失败记进日志（其余是空的 catch，所以这类残留从来查不到原因）。
    /// 只禁声明不禁调用：调用点要带上自己那条流水线的 category，那是各处的差异，不是第二真源。
    /// </summary>
    [Fact]
    public void BestEffortFileDeletion_LivesInExactlyOnePlace()
    {
        var homeFile = @"core\LanMountainDesktop.Core\IO\FileOperationRetryHelper.cs";
        var declRe = new Regex(
            @"(?:private|internal|public|protected)\s+(?:static\s+)?bool\s+TryDelete(?:File|Directory)\s*\(");
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            if (string.Equals(RelativeToRepo(file), homeFile, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var (line, number) in CodeLines(file))
            {
                if (declRe.IsMatch(line))
                {
                    offenders.Add(
                        $"{RelativeToRepo(file)}:{number} 又写了一份尽力删除，" +
                        "请用 FileOperationRetryHelper.TryDeleteFile 或 TryDeleteDirectory");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处重复的尽力删除实现：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// "取消 + 释放一个一次性 CTS"这套动作只认 <c>core/LanMountainDesktop.Core/Threading/CancellationHelper.cs</c> 一处。
    /// 收口前它有两条复制路径：10 个组件各有一份逐字相同的 <c>CancelRefreshRequest()</c>（实测 10 份、每份 7 行正文），
    /// 以及 17 处 <c>X?.Cancel(); X?.Dispose();</c> 的相邻两行。守卫两条都拦：
    /// 声明拦"再抄一份方法"，相邻两行拦"把三步拆回两步"。
    /// 同一把尺子放宽到"Cancel 后 5 行内没有 Dispose"另命中 22 处只取消不释放的字段级调用点，
    /// 但那是线索不是结论：12 处逐处读到底后只有 3 处真的从不释放（已修），其余是"取消仍在飞的操作、释放另有其人"。
    /// 清单、豁免理由与"为什么不能无脑补 Dispose"记在 AGENTS.md。
    /// </summary>
    [Fact]
    public void CancelAndDisposeRitual_LivesInExactlyOnePlace()
    {
        var homeFile = @"core\LanMountainDesktop.Core\Threading\CancellationHelper.cs";
        var declarationRe = new Regex(@"\bvoid\s+CancelRefreshRequest\s*\(");
        var stepRe = new Regex(@"^\s*(?<expr>[A-Za-z_][\w\(\)\[\]\.]*)\??\.(?<op>Cancel|Dispose)\(\);$");
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            var relative = RelativeToRepo(file);
            if (string.Equals(relative, homeFile, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var trimmed = lines[index].AsSpan().TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith('*'))
                {
                    continue;
                }

                if (declarationRe.IsMatch(lines[index]))
                {
                    offenders.Add($"{relative}:{index + 1} 又抄了一份 CancelRefreshRequest，请用 CancellationHelper.CancelAndDispose");
                    continue;
                }

                if (index + 1 >= lines.Length)
                {
                    continue;
                }

                var current = stepRe.Match(lines[index]);
                var next = stepRe.Match(lines[index + 1]);
                if (current.Success
                    && next.Success
                    && current.Groups["expr"].Value == next.Groups["expr"].Value
                    && current.Groups["op"].Value == "Cancel"
                    && next.Groups["op"].Value == "Dispose")
                {
                    offenders.Add(
                        $"{relative}:{index + 1} 手写 Cancel+Dispose 这一步，" +
                        "请用 CancellationHelper.CancelAndDispose");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处重复的取消/释放动作：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// 时区服务的订阅/退订只认 <c>desktop/LanMountainDesktop/Views/Components/TimeZoneServiceBinding.cs</c> 一处。
    /// 收口前 10 个组件各抄了一份 Set 与一份 Clear（共 20 个方法体，实测差异只有一处把 4 行守卫压成 1 行），
    /// 而 <c>TimeZoneService</c> 是应用级长命对象、事件没有任何退订兜底——抄漏 <c>-=</c> 那一边的话，
    /// 服务就替一个已经从桌面上分离掉的控件一直持有整棵 visual tree。
    /// 两条都拦：谁手写 <c>TimeZoneChanged += / -=</c>，以及谁声明了 Set/Clear 却没走 binding。
    /// </summary>
    [Fact]
    public void TimeZoneServiceSubscription_LivesInExactlyOnePlace()
    {
        var homeFile = @"desktop\LanMountainDesktop\Views\Components\TimeZoneServiceBinding.cs";
        var eventRe = new Regex(@"TimeZoneChanged\s*[-+]?=");
        var bodyRe = new Regex(@"void (?:Set|Clear)TimeZoneService\s*\([^;]*\)\s*$");
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            var relative = RelativeToRepo(file);
            var isHome = string.Equals(relative, homeFile, StringComparison.OrdinalIgnoreCase);

            foreach (var (line, number) in CodeLines(file))
            {
                if (eventRe.IsMatch(line) && !isHome)
                {
                    offenders.Add($"{relative}:{number} 自己订阅/退订 TimeZoneChanged，请用 TimeZoneServiceBinding");
                }

                if (bodyRe.IsMatch(line)
                    && !isHome
                    && !File.ReadAllLines(file).Any(l => l.Contains("TimeZoneServiceBinding.", StringComparison.Ordinal)))
                {
                    offenders.Add($"{relative}:{number} 这对方法没走 TimeZoneServiceBinding，退订漏一边就是泄漏");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处绕开 TimeZoneServiceBinding 的时区订阅：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// 两个纯函数各只有一个家：目录末尾分隔符 <c>core/.../IO/PathSeparators.cs</c>、
    /// 文本裁断 <c>desktop/.../Helpers/CompactText.Truncate</c>。
    /// 收口前它们分别是 4 份与 4 份逐字复制：分隔符那 4 份散在 Core/宿主/启动器三个二进制里，
    /// 其中 <c>AirAppLoader</c> 那份还多做了一步 <c>Path.GetFullPath</c>（把"补个斜杠"变成"可能抛参数异常"）；
    /// <c>Truncate</c> 那 4 份里 GitHub 更新服务那份不带省略号，同一份 HTTP 错误文本在它那里看着像完整回复。
    /// 禁的是"再抄一份实现"，不禁调用。
    /// </summary>
    [Fact]
    public void PathAndTextPureHelpers_LiveInExactlyOnePlace()
    {
        var homes = new (Regex Decl, string Home, string Why)[]
        {
            (new Regex(@"string\s+EnsureTrailingSeparator\s*\("),
             @"core\LanMountainDesktop.Core\IO\PathSeparators.cs",
             "又抄了一份目录分隔符补齐，请用 PathSeparators.EnsureTrailingSeparator"),
            (new Regex(@"string\s+Truncate\s*\(\s*string\??\s*\w+,\s*int\s+\w+\s*\)"),
             @"desktop\LanMountainDesktop\Helpers\CompactText.cs",
             "又抄了一份文本裁断，请用 CompactText.Truncate"),
        };

        var offenders = new List<string>();
        foreach (var file in SourceFiles())
        {
            var relative = RelativeToRepo(file);
            foreach (var (decl, home, why) in homes)
            {
                if (string.Equals(relative, home, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var (line, number) in CodeLines(file))
                {
                    if (decl.IsMatch(line))
                    {
                        offenders.Add($"{relative}:{number} {why}");
                    }
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处重复的纯函数实现：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    private static IEnumerable<(string Line, int Number)> CodeLines(string file)
    {
        var all = File.ReadAllLines(file);
        for (var index = 0; index < all.Length; index++)
        {
            var trimmed = all[index].AsSpan().TrimStart();
            if (trimmed.StartsWith("//") || trimmed.StartsWith('*'))
            {
                continue;
            }

            yield return (all[index], index + 1);
        }
    }

    private static IEnumerable<(string Line, int Number)> RawLines(string file)
    {
        var all = File.ReadAllLines(file);
        for (var index = 0; index < all.Length; index++)
        {
            yield return (all[index], index + 1);
        }
    }

    private static IEnumerable<string> RepositoryCSharpFiles() => new[] { "core", "desktop", "tests", "airapp", "install", "mobile", "platform" }
        .Select(part => Path.Combine(RepoRoot, part))
        .Where(Directory.Exists)
        .SelectMany(dir => Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
        .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

    private static bool IsHostProjectFile(string file) => RelativeToRepo(file)
        .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
        .StartsWith($"desktop{Path.DirectorySeparatorChar}LanMountainDesktop{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static IEnumerable<string> MarkupFiles() => ProductionDirectories
        .Select(part => Path.Combine(RepoRoot, part))
        .Where(Directory.Exists)
        .SelectMany(dir => Directory.EnumerateFiles(dir, "*.axaml", SearchOption.AllDirectories))
        .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> SourceFiles() => ProductionDirectories
        .Select(part => Path.Combine(RepoRoot, part))
        .Where(Directory.Exists)
        .SelectMany(dir => Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
        .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

    private static string RelativeToRepo(string path) => Path.GetRelativePath(RepoRoot, path);

    private static string RepoRoot
    {
        get
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
    }
}

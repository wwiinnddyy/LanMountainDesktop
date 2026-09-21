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
    /// "整体替换一个文件"只认 <c>Services/AtomicFileWriter.cs</c> 一处。宿主里此前有 10 处各搓一份
    /// "写临时文件 + Move 覆盖"，差异是会咬人的：目标被瞬时锁住时没人重试（用户看到的就是"设置没存上"）、
    /// 固定 ".tmp" 名让两个写者互相覆盖、<c>Delete</c>+<c>Move</c> 之间断电就把文件丢了、
    /// Move 失败后 .tmp 永远留在 AppData 里。
    ///
    /// 只管宿主自己的二进制：<c>Launcher/Oobe/OobeStateService</c> 与 <c>Launcher/Shell/LauncherBackgroundService</c>
    /// 是另一个进程，要收口得先把helper挪到共享面，另议。写权限探针
    /// （<c>AppLogger</c>、<c>AirAppInstallTargetAccess</c>）拿 .tmp 是为了试写，不属这一族。
    /// </summary>
    [Fact]
    public void AtomicFileReplacement_LivesInExactlyOnePlace()
    {
        string[] handWrittenProbes = ["AppLogger.cs", "AirAppInstallTargetAccess.cs"];
        var offenders = new List<string>();

        foreach (var file in SourceFiles().Where(IsHostProjectFile))
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
                if (lines[index].Contains(".tmp\"", StringComparison.Ordinal))
                {
                    offenders.Add($"{RelativeToRepo(file)}:{index + 1} 自己搓了临时文件写盘，请用 AtomicFileWriter");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处手搓的原子写盘：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    private static bool IsHostProjectFile(string file) => RelativeToRepo(file)
        .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
        .StartsWith($"desktop{Path.DirectorySeparatorChar}LanMountainDesktop{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

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

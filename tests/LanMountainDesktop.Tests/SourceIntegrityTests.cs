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
    /// "整体替换一个文件"只认 <c>core/.../IO/AtomicFileWriter.cs</c> 一处。此前宿主里有 10 处、
    /// 启动器与安装器又各有自己的版本，差异是会咬人的：目标被瞬时锁住时没人重试（用户看到的就是"设置没存上"）、
    /// 固定 ".tmp" 名让两个写者互相覆盖、<c>Delete</c>+<c>Move</c> 之间断电就把文件丢了、
    /// Move 失败后 .tmp 永远留在 AppData 里。
    ///
    /// helper 挪进 Core 之后这条覆盖全部二进制（原先只管宿主，剩下的启动器两处就是这么漏掉的）。
    /// 免检的两类：<c>.write-test-</c> 开头的是"这块盘能不能写"的探针，不是替换文件；
    /// <c>LauncherBackgroundService</c> 是"把用户选中的图片搬成目标名"，需要的是"原子落一个已有文件"
    /// 这个原语（现在还只有 WriteText / WriteStreamAsync），已登记待办，不是漏网。
    /// 写权限探针（<c>AppLogger</c>、<c>AirAppInstallTargetAccess</c>）拿 .tmp 也是为了试写。
    /// </summary>
    [Fact]
    public void AtomicFileReplacement_LivesInExactlyOnePlace()
    {
        string[] handWrittenProbes = ["AppLogger.cs", "AirAppInstallTargetAccess.cs", "LauncherBackgroundService.cs"];
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

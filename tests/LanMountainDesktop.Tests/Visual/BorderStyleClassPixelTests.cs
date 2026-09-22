using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LanMountainDesktop.Services;
using LanMountainDesktop.Theme;
using Xunit;

namespace LanMountainDesktop.Tests.Visual;

/// <summary>
/// App 级样式字典里每个"设过 Background 的 Border 类"都必须真的改变画面：
/// 同一个位置贴类与不贴类各采一帧，两帧必须不同。
///
/// 判据声称的边界（都是实测出来的，别扩大解读）：
/// ① 只到"这个类改没改画面"。样式往往还一并设了 Opacity / BoxShadow / CornerRadius，
///    所以把主题注册摘掉它照样通过——它不证明"背景画刷注册得上"。
///    曾用 <c>TryGetResource</c> 加过一条键断言，摘掉注册时它并不红，属于假守卫，已删。
///    "有人要某个 Adaptive 键、没人注册"由 <c>CapabilityEntryPointTests</c> 的键覆盖探针管。
/// ② 不跟底板色比，只跟"同一个位置不贴类"比：主题对裸 Border 本身就有默认外观，
///    "和底板不一样"在类名拼错时照样绿（2026-09-22 实测踩过）。
/// ③ 覆盖面靠 <see cref="CensusCoversTheKnownGlassAndSurfaceClasses"/> 兜底：锚点是"样式设过 Background"，
///    删掉 setter 会让类从普查里消失，那条守卫把这种静默收窄变成红灯。
/// </summary>
public sealed class BorderStyleClassPixelTests
{
    /// <summary>
    /// 判据用的固定语境。刻意不叫"默认主题"：宿主真正的默认值要跑完整 appearance 服务栈才能解析，
    /// 这里只需要一套能让 Adaptive* 画刷落到资源表里的语境。
    /// </summary>
    private static readonly ThemeColorContext JudgeContext = new(
        Color.FromRgb(0x40, 0x80, 0xC0),
        IsLightBackground: true,
        IsLightNavBackground: true,
        IsNightMode: false);

    /// <summary>已知"贴类与不贴类同帧"的项，每条必须带理由；理由过期即红。</summary>
    private static readonly Dictionary<string, string> KnownIdentical = new(StringComparer.Ordinal)
    {
    };

    public static TheoryData<string> BackgroundStyledClasses()
    {
        var data = new TheoryData<string>();
        foreach (var (styleClass, _) in Scan())
        {
            data.Add(styleClass);
        }

        return data;
    }

    [AvaloniaTheory]
    [MemberData(nameof(BackgroundStyledClasses))]
    public void BorderClassChangesWhatIsPainted(string styleClass)
    {
        // 先把主题资源按一套固定语境注册上，让这些类拿得到 DynamicResource。
        // 注意这条不是判据的一部分：实测把下面两行整个注释掉，5 个类照样通过，
        // 因为样式还设了 Opacity / BoxShadow / CornerRadius，光这些就足以让像素变化。
        // 所以本判据只声称"这个类到底改没改画面"，不声称"它的背景画刷注册得上"——
        // 后者试过用 TryGetResource 来断言，摘掉注册时它并不红，属于假守卫，已经删掉；
        // "有人要某个 Adaptive 键、没人注册"由 CapabilityEntryPointTests 的键覆盖探针管。
        var resources = Application.Current!.Resources;
        ThemeColorSystemService.ApplyThemeResources(resources, JudgeContext);
        GlassEffectService.ApplyGlassResources(resources, JudgeContext);

        var styled = CaptureCard(styleClass);
        var plain = CaptureCard(null);
        var styledPixel = styled.At(styled.Width / 6, styled.Height / 6);
        var plainPixel = plain.At(plain.Width / 6, plain.Height / 6);
        var distance = Math.Abs(styledPixel.Red - plainPixel.Red)
            + Math.Abs(styledPixel.Green - plainPixel.Green)
            + Math.Abs(styledPixel.Blue - plainPixel.Blue);

        if (distance <= 2)
        {
            Assert.True(
                KnownIdentical.TryGetValue(styleClass, out var reason) && reason.Length > 0,
                $"'{styleClass}' 在样式里设了 Background，贴类与不贴类却同帧（通道总差 {distance}）："
                + $"styled={styledPixel} plain={plainPixel}。确实可接受的话，去 KnownIdentical 登记理由");
            return;
        }

        Assert.True(
            !KnownIdentical.ContainsKey(styleClass),
            $"'{styleClass}' 现在画得出差别（通道总差 {distance}），把 KnownIdentical 里那条理由删掉："
            + KnownIdentical.GetValueOrDefault(styleClass, string.Empty));
    }

    /// <summary>
    /// 普查的覆盖面下限。锚点是"这条样式设过 Background"，所以把某个 setter 删掉就会让那个类
    /// 从普查里消失——判据静默失效比红灯贵，这里把它变成红灯：要收窄覆盖面必须同时改这张表并写清理由。
    /// </summary>
    [Fact]
    public void CensusCoversTheKnownGlassAndSurfaceClasses()
    {
        var scanned = Scan().Select(item => item.StyleClass).ToHashSet(StringComparer.Ordinal);
        var expected = new[]
        {
            "glass-panel",
            "surface-solid-strong",
            "surface-translucent-island",
            "surface-translucent-panel",
            "surface-translucent-strong",
        };

        var missing = expected.Where(className => !scanned.Contains(className)).ToArray();
        Assert.True(
            missing.Length == 0,
            $"这些 Border 类不再被普查覆盖（样式里 Background setter 被删了？还是选择器写法变了）：{string.Join(", ", missing)}");
    }

    private static IEnumerable<(string StyleClass, string BrushKey)> Scan()
    {
        var stylesDirectory = Path.Combine(RepoRoot, "desktop", "LanMountainDesktop", "Styles");
        var results = new SortedSet<(string StyleClass, string BrushKey)>();

        foreach (var file in AppIncludedDictionaries(stylesDirectory))
        {
            var text = File.ReadAllText(file);
            // 只看基选择器（不带 :pointerover 这类伪类）：伪类块里的画刷在没悬停的采样点上本来不该出现，
            // 拿它当"这个类应有的背景"会自造假红灯。
            foreach (Match style in Regex.Matches(
                         text,
                         @"<Style Selector=""Border\.([\w-]+)"">([\s\S]*?)</Style>"))
            {
                // 锚点是"这条样式设过 Background"，不是"设的是 DynamicResource"：
                // 以后者锚定的话，把 setter 删掉就等于用例从普查里消失——判据静默失效比红灯贵。
                var background = Regex.Match(style.Groups[2].Value, "Property=\"Background\"[^\n]*");
                if (!background.Success)
                {
                    continue;
                }

                var dynamicKey = Regex.Match(background.Value, "\\{DynamicResource ([\\w.]+)");
                results.Add((style.Groups[1].Value, dynamicKey.Success ? dynamicKey.Groups[1].Value : string.Empty));
            }
        }

        return results;
    }

    private static IEnumerable<string> AppIncludedDictionaries(string stylesDirectory)
    {
        var appAxaml = Path.Combine(RepoRoot, "desktop", "LanMountainDesktop", "App.axaml");
        foreach (Match match in Regex.Matches(
                   File.ReadAllText(appAxaml), @"Source=""avares://LanMountainDesktop/Styles/([\w.]+\.axaml)"""))
        {
            var path = Path.Combine(stylesDirectory, match.Groups[1].Value);
            if (File.Exists(path))
            {
                yield return path;
            }
        }
    }

    private static Frame CaptureCard(string? styleClass)
    {
        var card = new Border
        {
            Width = 60,
            Height = 60,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        if (styleClass is not null)
        {
            card.Classes.Add(styleClass);
        }

        var window = new Window
        {
            Width = 100,
            Height = 100,
            Background = Brushes.Magenta,
            Content = card,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();

        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException($"采集 '{styleClass}' 失败：headless 取像素的三个条件被改动了");
        var width = frame.PixelSize.Width;
        var height = frame.PixelSize.Height;
        using var locked = frame.Lock();
        var rowBytes = locked.RowBytes;
        var buffer = new byte[rowBytes * height];
        for (var y = 0; y < height; y++)
        {
            Marshal.Copy(locked.Address + (y * rowBytes), buffer, y * rowBytes, rowBytes);
        }

        window.Close();
        Dispatcher.UIThread.RunJobs();
        return new Frame(width, height, rowBytes, buffer);
    }

    private readonly record struct Pixel(byte Red, byte Green, byte Blue, byte Alpha)
    {
        public override string ToString() => $"rgba({Red},{Green},{Blue},{Alpha})";
    }

    private sealed record Frame(int Width, int Height, int Stride, byte[] Pixels)
    {
        public Pixel At(int x, int y)
        {
            var i = (y * Stride) + (x * 4);
            return new Pixel(Pixels[i], Pixels[i + 1], Pixels[i + 2], Pixels[i + 3]);
        }
    }

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LanMountainDesktop.slnx")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? AppContext.BaseDirectory;
        }
    }
}

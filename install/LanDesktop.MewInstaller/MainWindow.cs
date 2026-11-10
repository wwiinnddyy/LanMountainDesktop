using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using LanDesktop.MewInstaller.Installer;
using LanDesktopPLONDS.Installer.Models;

namespace LanDesktop.MewInstaller;

/// <summary>
/// MewUI 五步安装向导主窗口。布局：左侧步骤导航 + 右侧内容页 + 底部导航条。
/// 所有交互状态来自 <see cref="InstallerState"/>，本类只做绑定与转发。
/// </summary>
/// <summary>嵌入式资源的锚点类型，用于 IconSource.FromResource 推导资源全名。</summary>
internal sealed class AppResources
{
}

internal static class StepGlyphs
{
    // 与 PLONDS 安装器保持同一套 Segoe 字形语义。
    public const string Welcome = "\uE768";      // 前进箭头
    public const string Location = "\uE838";     // 文件夹
    public const string Privacy = "\uE946";      // 隐私
    public const string Deploy = "\uE896";       // 下载
    public const string Complete = "\uE73E";     // 对勾
    public const string Back = "\uE72B";         // 后退
    public const string Next = "\uE72A";         // 前进
    public const string Cancel = "\uE711";       // 取消
    public const string Elevation = "\uEA18";    // 权限盾
}

internal static class MainWindowExtensions
{
    /// <summary>Segoe Fluent Icons（Win11）/ MDL2（Win10）系统字形，与 PLONDS 安装器同源。</summary>
    public const string IconFontFamily = "Segoe Fluent Icons, Segoe MDL2 Assets";

    public static TextBlock Glyph(this TextBlock _, string glyph) =>
        _.Text(glyph)
         .FontFamily(IconFontFamily);
}

internal sealed class MainWindow : Window
{
    private readonly InstallerState _state;

    public MainWindow(InstallerState state)
    {
        _state = state;

        var pages = new StackPanel[]
        {
            BuildWelcomePage(),
            BuildLocationPage(),
            BuildPrivacyPage(),
            BuildDeployPage(),
            BuildCompletePage(),
        };

        for (var i = 0; i < pages.Length; i++)
        {
            var step = (InstallerStepId)i;
            pages[i].Bind(
                UIElement.IsVisibleProperty,
                _state.CurrentStep,
                (InstallerStepId s) => s == step,
                _ => step,
                BindingMode.OneWay);
        }

        var content = new Grid()
            .Columns("*")
            .Rows("*")
            .Children(pages);

        Title = "阑山桌面 安装程序";
        Icon = IconSource.FromResource<AppResources>("LanDesktop.MewInstaller.Assets.logo.ico");
        this.Resizable(1000, 680, minWidth: 900, minHeight: 620);
        // 底部导航条说明：DockBottom / Grid 底行两种定位在 MewUI 0.22.1 实测均不渲染
        // （2026-11-08 二分定位，详见 AGENTS 记录），DockTop 是已验证可渲染的过渡方案。
        Content =
                new Grid()
                    .Columns("230,12,*")
                    .Margin(10)
                    .Children(
                        BuildSidebar().Column(0),
                        new DockPanel()
                            .Column(2)
                            .Children(
                                BuildBottomBar().DockTop(),
                                content));
    }

    // ---------------- 侧栏 ----------------

    private StackPanel BuildSidebar()
    {
        var steps = new (InstallerStepId Id, string Title)[]
        {
            (InstallerStepId.Welcome, "开始安装"),
            (InstallerStepId.InstallLocation, "安装位置"),
            (InstallerStepId.PrivacyConfirm, "数据确认"),
            (InstallerStepId.Deploy, "开始部署"),
            (InstallerStepId.Complete, "完成安装"),
        };

        var buttons = new StackPanel().Vertical().Spacing(4).Margin(0, 24, 0, 0);
        foreach (var (id, title) in steps)
        {
            var captured = id;
            buttons.Children(
                new Button()
                    .Content(
                        new StackPanel()
                            .Horizontal()
                            .Spacing(10)
                            .Margin(10, 0)
                            .Children(
                                new TextBlock().Glyph(GlyphFor(captured)).CenterVertical(),
                                new TextBlock().Text(title).CenterVertical()))
                    .Height(40)
                    .HorizontalAlignment(HorizontalAlignment.Stretch)
                    .Bind(
                        UIElement.IsEnabledProperty,
                        _state.MaxUnlockedStep,
                        (int max) => (int)captured <= max,
                        _ => (int)captured,
                        BindingMode.OneWay)
                    .OnClick(() => _state.SelectStep(captured)));
        }

        return new StackPanel()
            .Vertical()
            .Margin(14, 12, 0, 12)
            .Children(
                new TextBlock().Text("阑山桌面").FontSize(22).Bold(),
                new TextBlock().Text(_state.ModeLabel).FontSize(12),
                buttons);
    }

    private static string GlyphFor(InstallerStepId step) => step switch
    {
        InstallerStepId.Welcome => StepGlyphs.Welcome,
        InstallerStepId.InstallLocation => StepGlyphs.Location,
        InstallerStepId.PrivacyConfirm => StepGlyphs.Privacy,
        InstallerStepId.Deploy => StepGlyphs.Deploy,
        _ => StepGlyphs.Complete,
    };

    // ---------------- 页面 ----------------

    private static StackPanel PageShell(string title, string description) =>
        new StackPanel()
            .Vertical()
            .Spacing(16)
            .Margin(36, 30, 42, 16)
            .Children(
                new TextBlock().Text(title).FontSize(28).Bold(),
                new TextBlock().Text(description).FontSize(13));

    private StackPanel BuildWelcomePage() => PageShell("安装阑山桌面", "安装程序会把最新版本部署到本机版本目录。")
        .Children(
            new Border()
                .Padding(18)
                .CornerRadius(8)
                .WithTheme((theme, border) => border.Background(theme.Palette.ControlBackground))
                .Child(
                    new Grid()
                        .Columns("Auto,*")
                        .Spacing(14)
                        .Children(
                            new Border()
                                .Width(40)
                                .Height(40)
                                .CornerRadius(8)
                                .WithTheme((theme, border) => border.Background(theme.Palette.ControlBackground))
                                .Child(new TextBlock().Glyph(GlyphFor(InstallerStepId.Deploy)).FontSize(20).CenterVertical().CenterHorizontal()),
                            new StackPanel()
                                .Vertical()
                                .Spacing(6)
                                .Column(1)
                                .Children(
                                    new TextBlock().Text("准备开始").FontSize(16).Bold(),
                                    new TextBlock()
                                        .Text(_state.ModeLabel == "本地包安装程序"
                                            ? "将从随附的本地完整包部署应用，无需联网。"
                                            : "安装器会检查最新版本、下载完整包、校验文件并激活部署。")
                                        .FontSize(13)))));

    private StackPanel BuildLocationPage() => PageShell("选择安装位置", "请选择一个专用文件夹。默认位置需要管理员权限。")
        .Children(
            new Grid()
                .Columns("*,Auto")
                .Spacing(10)
                .Children(
                    new TextBox()
                        .BindText(_state.InstallPath)
                        .Column(0),
                    new Button()
                        .Content("浏览…")
                        .Column(1)
                        .OnClick(BrowseAsync)),
            new CheckBox().Content("创建桌面快捷方式").BindIsChecked(_state.CreateDesktopShortcut),
            new CheckBox().Content("开机时自动启动阑山桌面").BindIsChecked(_state.CreateStartupShortcut),
            BuildElevationCard());

    private Border BuildElevationCard() => new Border()
        .Padding(14)
        .CornerRadius(8)
        .Bind(
            UIElement.IsVisibleProperty,
            _state.IsElevationRequired,
            (bool v) => v,
            _ => false,
            BindingMode.OneWay)
        .WithTheme((theme, border) => border.Background(theme.Palette.ControlBackground))
        .Child(
            new Grid()
                .Columns("Auto,*")
                .Spacing(14)
                .Children(
                    new Border()
                        .Width(40)
                        .Height(40)
                        .CornerRadius(8)
                        .WithTheme((theme, border) => border.Background(theme.Palette.ControlBackground))
                        .Child(new TextBlock().Glyph(StepGlyphs.Elevation).FontSize(20).CenterVertical().CenterHorizontal()),
                    new StackPanel()
                        .Vertical()
                        .Spacing(8)
                        .Column(1)
                        .Children(
                            new TextBlock().Text("需要管理员权限").FontSize(16).Bold(),
                            new TextBlock().BindText(_state.ElevationMessage).FontSize(13),
                            new Button()
                                .Content(
                                    new StackPanel()
                                        .Horizontal()
                                        .Spacing(6)
                                        .Children(
                                            new TextBlock().Glyph(StepGlyphs.Elevation).CenterVertical(),
                                            new TextBlock().Text("以管理员身份重新启动").CenterVertical()))
                                .OnClick(_state.RelaunchElevated))));

    private StackPanel BuildPrivacyPage() => PageShell("确认数据使用", "安装阶段需要使用匿名设备码和基础请求信息，用于安装、风控和用户量统计。")
        .Children(
            new Border()
                .Padding(14)
                .CornerRadius(8)
                .WithTheme((theme, border) => border.Background(theme.Palette.ControlBackground))
                .Child(
                    new Grid()
                        .Columns("Auto,*")
                        .Spacing(14)
                        .Children(
                            new Border()
                                .Width(40)
                                .Height(40)
                                .CornerRadius(8)
                                .WithTheme((theme, border2) => border2.Background(theme.Palette.WindowBackground))
                                .Child(new TextBlock().Glyph(StepGlyphs.Privacy).FontSize(20).CenterVertical().CenterHorizontal()),
                            new StackPanel()
                                .Vertical()
                                .Spacing(6)
                                .Column(1)
                                .Children(
                                    new TextBlock().Text("匿名设备码").FontSize(16).Bold(),
                                    new TextBlock().Text(_state.DeviceIdPreview).FontSize(13)))),
            new CheckBox()
                .Content("我确认上述匿名数据可用于安装、风控和用户量统计。")
                .BindIsChecked(_state.PrivacyConfirmed));

    private StackPanel BuildDeployPage() => PageShell("开始部署", "安装时会准备完整包，并写入当前版本目录。")
        .Children(
            new Grid()
                .Columns("80,*")
                .Spacing(6)
                .Children(
                    new TextBlock().Text("版本").CenterVertical(),
                    new TextBlock().BindText(_state.TargetVersion).CenterVertical().Column(1),
                    new TextBlock().Text("来源").CenterVertical().Row(1),
                    new TextBlock().BindText(_state.SourceId).CenterVertical().Row(1).Column(1)),
            new TextBlock().BindText(_state.StatusText).Bold(),
            new TextBlock().Text("下载进度").FontSize(12),
            new ProgressBar().Height(8).Bind(ProgressBar.ValueProperty, _state.DownloadProgress, BindingMode.OneWay),
            new Grid()
                .Columns("*,Auto")
                .Children(
                    new TextBlock().BindText(_state.DownloadBytesText).FontSize(12),
                    new TextBlock().BindText(_state.DownloadSpeedText).FontSize(12).Bold().Column(1)),
            new TextBlock().Text("安装进度").FontSize(12),
            new ProgressBar().Height(8).Bind(ProgressBar.ValueProperty, _state.InstallProgress, BindingMode.OneWay),
            new TextBlock().BindText(_state.CurrentFile).FontSize(12),
            new StackPanel()
                .Horizontal()
                .Spacing(8)
                .Children(
                    new Button()
                        .Content(
                            new StackPanel()
                                .Horizontal()
                                .Spacing(6)
                                .Children(
                                    new TextBlock().Glyph(StepGlyphs.Deploy).CenterVertical(),
                                    new TextBlock().Text("开始安装").CenterVertical()))
                        .Bind(
                            UIElement.IsEnabledProperty,
                            _state.CanStartInstall,
                            (bool v) => v,
                            _ => false,
                            BindingMode.OneWay)
                        .OnClick(() => _ = _state.StartInstallAsync()),
                    new Button()
                        .Content(
                            new StackPanel()
                                .Horizontal()
                                .Spacing(6)
                                .Children(
                                    new TextBlock().Glyph(StepGlyphs.Cancel).CenterVertical(),
                                    new TextBlock().Text("取消").CenterVertical()))
                        .Bind(
                            UIElement.IsEnabledProperty,
                            _state.IsInstalling,
                            (bool v) => v,
                            _ => false,
                            BindingMode.OneWay)
                        .OnClick(_state.CancelInstall)));

    private StackPanel BuildCompletePage() => PageShell("完成安装", "阑山桌面已经部署完成。")
        .Children(
            new Grid()
                .Columns("Auto,*")
                .Spacing(14)
                .Children(
                    new Border()
                        .Width(40)
                        .Height(40)
                        .CornerRadius(8)
                        .WithTheme((theme, border) => border.Background(theme.Palette.ControlBackground))
                        .Child(new TextBlock().Glyph(GlyphFor(InstallerStepId.Complete)).FontSize(22).CenterVertical().CenterHorizontal()),
                    new TextBlock().Text("可以启动应用").FontSize(18).Bold().CenterVertical().Column(1)),
            new Button()
                .Content(
                    new StackPanel()
                        .Horizontal()
                        .Spacing(6)
                        .Children(
                            new TextBlock().Glyph(StepGlyphs.Welcome).CenterVertical(),
                            new TextBlock().Text("打开阑山桌面").CenterVertical()))
                .OnClick(_state.Launch));

    // ---------------- 底部导航 ----------------

    private UIElement BuildBottomBar()
    {
        var errorText = new TextBlock()
            .BindText(_state.ErrorMessage)
            .CenterVertical()
            .Bind(
                UIElement.IsVisibleProperty,
                _state.ErrorMessage,
                (string e) => !string.IsNullOrEmpty(e),
                _ => string.Empty,
                BindingMode.OneWay);

        var cancelButton = new Button()
            .Content(
                new StackPanel()
                    .Horizontal()
                    .Spacing(6)
                    .Children(
                        new TextBlock().Glyph(StepGlyphs.Cancel).CenterVertical(),
                        new TextBlock().Text("取消").CenterVertical()))
            .Bind(
                UIElement.IsVisibleProperty,
                _state.IsCheckingUpdate,
                (bool v) => v,
                _ => false,
                BindingMode.OneWay)
            .OnClick(_state.CancelCheck);

        var prevButton = new Button()
            .Content(
                new StackPanel()
                    .Horizontal()
                    .Spacing(6)
                    .Children(
                        new TextBlock().Glyph(StepGlyphs.Back).CenterVertical(),
                        new TextBlock().Text("上一步").CenterVertical()))
            .Bind(
                UIElement.IsEnabledProperty,
                _state.CanGoBack,
                (bool v) => v,
                _ => false,
                BindingMode.OneWay)
            .OnClick(_state.Back);

        var nextButton = new Button()
            .Content(
                new StackPanel()
                    .Horizontal()
                    .Spacing(6)
                    .Children(
                        new TextBlock().Text("下一步").CenterVertical(),
                        new TextBlock().Glyph(StepGlyphs.Next).CenterVertical()))
            .Bind(
                UIElement.IsEnabledProperty,
                _state.CanGoNext,
                (bool v) => v,
                _ => false,
                BindingMode.OneWay)
            .OnClick(() => _ = _state.NextAsync());

        // 注意：嵌套水平 StackPanel 上不要设 HorizontalAlignment.Right —— MewUI 0.22.1
        // 实测该组合会让整条 bar 不渲染（2026-11-08 二分定位），按钮组保持默认对齐。
        return new StackPanel()
            .Vertical()
            .Spacing(6)
            .Margin(0, 8, 0, 0)
            .Children(
                errorText,
                new StackPanel()
                    .Horizontal()
                    .Spacing(8)
                    .Children(cancelButton, prevButton, nextButton));
    }

    // ---------------- 交互 ----------------

    private async void BrowseAsync()
    {
        try
        {
            var selected = FileDialog.SelectFolder(new FolderDialogOptions
            {
                Title = "选择安装位置",
                Owner = this,
            });

            if (!string.IsNullOrWhiteSpace(selected))
            {
                _state.InstallPath.Value = selected;
                _state.NotifyInstallPathChanged();
            }
        }
        catch (Exception ex)
        {
            _state.ErrorMessage.Value = $"选择安装位置失败：{ex.Message}";
        }
    }
}

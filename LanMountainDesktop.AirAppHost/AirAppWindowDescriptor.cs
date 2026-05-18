namespace LanMountainDesktop.AirAppHost;

public sealed record AirAppWindowDescriptor(
    string WindowTitle,
    string TitleBarTitle,
    string TitleBarSubtitle,
    AirAppWindowChromeMode ChromeMode,
    bool CanResize,
    bool ShowInTaskbar,
    double Width,
    double Height,
    double MinWidth,
    double MinHeight)
{
    public string Title => WindowTitle;

    public string TitleText => TitleBarTitle;

    public string SubtitleText => TitleBarSubtitle;

    public static AirAppWindowDescriptor Create(AirAppLaunchOptions options)
    {
        if (string.Equals(options.AppId, AirAppLaunchOptions.WorldClockAppId, StringComparison.OrdinalIgnoreCase))
        {
            return Standard(
                "World Clock - Air APP",
                "World Clock",
                "Air APP",
                width: 360,
                height: 220,
                minWidth: 320,
                minHeight: 220);
        }

        if (string.Equals(options.AppId, AirAppLaunchOptions.WhiteboardAppId, StringComparison.OrdinalIgnoreCase))
        {
            return FullScreen(
                "Whiteboard - Air APP",
                "Whiteboard",
                "Air APP");
        }

        return Standard(
            "Air APP",
            "Air APP",
            options.AppId);
    }

    public static AirAppWindowDescriptor Standard(
        string windowTitle,
        string titleBarTitle,
        string titleBarSubtitle,
        double width = 520,
        double height = 360,
        double minWidth = 360,
        double minHeight = 260)
    {
        return new AirAppWindowDescriptor(
            windowTitle,
            titleBarTitle,
            titleBarSubtitle,
            AirAppWindowChromeMode.Standard,
            CanResize: true,
            ShowInTaskbar: true,
            width,
            height,
            minWidth,
            minHeight);
    }

    public static AirAppWindowDescriptor FullScreen(
        string windowTitle,
        string titleBarTitle,
        string titleBarSubtitle)
    {
        return new AirAppWindowDescriptor(
            windowTitle,
            titleBarTitle,
            titleBarSubtitle,
            AirAppWindowChromeMode.FullScreen,
            CanResize: false,
            ShowInTaskbar: true,
            Width: 1280,
            Height: 720,
            MinWidth: 360,
            MinHeight: 260);
    }

    public static AirAppWindowDescriptor Borderless(
        string windowTitle,
        double width = 520,
        double height = 360)
    {
        return new AirAppWindowDescriptor(
            windowTitle,
            string.Empty,
            string.Empty,
            AirAppWindowChromeMode.Borderless,
            CanResize: true,
            ShowInTaskbar: true,
            width,
            height,
            MinWidth: 240,
            MinHeight: 180);
    }

    public static AirAppWindowDescriptor Tool(
        string windowTitle,
        string titleBarTitle,
        string titleBarSubtitle,
        double width = 360,
        double height = 260)
    {
        return new AirAppWindowDescriptor(
            windowTitle,
            titleBarTitle,
            titleBarSubtitle,
            AirAppWindowChromeMode.Tool,
            CanResize: false,
            ShowInTaskbar: false,
            width,
            height,
            MinWidth: 240,
            MinHeight: 180);
    }

    public static AirAppWindowDescriptor BackgroundOnly(string appId)
    {
        return new AirAppWindowDescriptor(
            $"{appId} - Air APP",
            string.Empty,
            string.Empty,
            AirAppWindowChromeMode.BackgroundOnly,
            CanResize: false,
            ShowInTaskbar: false,
            Width: 1,
            Height: 1,
            MinWidth: 1,
            MinHeight: 1);
    }
}

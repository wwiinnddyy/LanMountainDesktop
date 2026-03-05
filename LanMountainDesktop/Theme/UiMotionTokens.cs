using System;

namespace LanMountainDesktop.Theme;

public static class UiMotionTokens
{
    public static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(120);
    public static readonly TimeSpan Standard = TimeSpan.FromMilliseconds(160);
    public static readonly TimeSpan Slow = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan Page = TimeSpan.FromMilliseconds(240);
    public static readonly TimeSpan Intro = TimeSpan.FromMilliseconds(320);

    public static readonly TimeSpan StaggerStepInterval = TimeSpan.FromMilliseconds(24);
    public static readonly TimeSpan WeatherAnimationFrameInterval = TimeSpan.FromMilliseconds(64);

    public const string StandardBezier = "0.22, 1, 0.36, 1";
}

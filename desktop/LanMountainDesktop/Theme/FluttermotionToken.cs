using System;

namespace LanMountainDesktop.Theme;

public static class FluttermotionToken
{
    public static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(120);
    public static readonly TimeSpan Standard = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan Slow = TimeSpan.FromMilliseconds(280);
    public static readonly TimeSpan Page = TimeSpan.FromMilliseconds(320);
    public static readonly TimeSpan Intro = TimeSpan.FromMilliseconds(400);

    public static readonly TimeSpan StaggerStepInterval = TimeSpan.FromMilliseconds(32);

    public const string StandardBezier = "0.05, 0.75, 0.10, 1.00";
}

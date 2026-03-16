using System;

namespace LanMountainDesktop.Views.Components;

public sealed class OfficeRecentDocumentViewModel
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string TimeAgo { get; set; } = string.Empty;
}

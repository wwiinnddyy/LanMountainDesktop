using FluentIcons.Common;

namespace LanMountainDesktop.Views;

internal sealed record IndependentSettingsPageDefinition(
    string Tag,
    string Title,
    string Description,
    Symbol Icon,
    IndependentSettingsPageCategory Category,
    int SortOrder,
    string? ToolTip = null);

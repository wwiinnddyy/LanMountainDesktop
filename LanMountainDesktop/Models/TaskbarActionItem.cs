namespace LanMountainDesktop.Models;

public sealed record TaskbarActionItem(
    TaskbarActionId Id,
    string Title,
    string IconKey,
    bool IsVisible,
    string CommandKey);


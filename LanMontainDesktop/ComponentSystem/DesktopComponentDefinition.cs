namespace LanMontainDesktop.ComponentSystem;

public sealed record DesktopComponentDefinition(
    string Id,
    string DisplayName,
    string IconKey,
    string Category,
    int MinWidthCells,
    int MinHeightCells,
    bool AllowStatusBarPlacement,
    bool AllowDesktopPlacement);

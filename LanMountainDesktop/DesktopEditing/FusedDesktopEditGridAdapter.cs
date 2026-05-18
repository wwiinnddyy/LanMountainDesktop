using System;
using Avalonia;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.DesktopEditing;

internal readonly record struct FusedDesktopEditGridContext(
    DesktopGridGeometry Geometry,
    DesktopGridMetrics Metrics)
{
    public bool IsValid => Geometry.IsValid && Metrics.CellSize > 0;
}

internal sealed class FusedDesktopEditGridAdapter
{
    private const int MinShortSideCells = 6;
    private const int MaxShortSideCells = 96;
    private const int DefaultShortSideCells = 12;
    private const int MinEdgeInsetPercent = 0;
    private const int MaxEdgeInsetPercent = 30;

    private readonly ISettingsFacadeService _settingsFacade;

    public FusedDesktopEditGridAdapter(ISettingsFacadeService settingsFacade)
    {
        _settingsFacade = settingsFacade;
    }

    public bool TryCreate(Size viewportSize, out FusedDesktopEditGridContext context)
    {
        context = default;
        if (viewportSize.Width <= 1 || viewportSize.Height <= 1)
        {
            return false;
        }

        var state = _settingsFacade.Grid.Get();
        var shortSideCells = Math.Clamp(
            state.ShortSideCells > 0 ? state.ShortSideCells : DefaultShortSideCells,
            MinShortSideCells,
            MaxShortSideCells);
        var spacingPreset = _settingsFacade.Grid.NormalizeSpacingPreset(state.SpacingPreset);
        var gapRatio = _settingsFacade.Grid.ResolveGapRatio(spacingPreset);
        var edgeInsetPercent = Math.Clamp(state.EdgeInsetPercent, MinEdgeInsetPercent, MaxEdgeInsetPercent);
        var edgeInset = _settingsFacade.Grid.CalculateEdgeInset(
            viewportSize.Width,
            viewportSize.Height,
            shortSideCells,
            edgeInsetPercent);
        var metrics = _settingsFacade.Grid.CalculateGridMetrics(
            viewportSize.Width,
            viewportSize.Height,
            shortSideCells,
            gapRatio,
            edgeInset);

        if (metrics.CellSize <= 0 || metrics.ColumnCount <= 0 || metrics.RowCount <= 0)
        {
            return false;
        }

        var geometry = new DesktopGridGeometry(
            Origin: new Point(metrics.EdgeInsetPx, metrics.EdgeInsetPx),
            CellSize: metrics.CellSize,
            CellGap: metrics.GapPx,
            ColumnCount: metrics.ColumnCount,
            RowCount: metrics.RowCount);

        context = new FusedDesktopEditGridContext(geometry, metrics);
        return context.IsValid;
    }
}

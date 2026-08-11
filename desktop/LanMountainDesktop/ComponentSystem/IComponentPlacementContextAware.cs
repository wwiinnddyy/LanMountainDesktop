namespace LanMountainDesktop.ComponentSystem;

public interface IComponentPlacementContextAware
{
    void SetComponentPlacementContext(string componentId, string? placementId);
}

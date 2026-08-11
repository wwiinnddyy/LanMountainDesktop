using LanMountainDesktop.Host.Abstractions;

namespace LanMountainDesktop.ComponentSystem;

public interface IComponentChromeContextAware
{
    void SetComponentChromeContext(ComponentChromeContext context);
}

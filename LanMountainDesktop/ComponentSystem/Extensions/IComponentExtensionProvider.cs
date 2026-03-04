using System.Collections.Generic;

namespace LanMountainDesktop.ComponentSystem.Extensions;

public interface IComponentExtensionProvider
{
    IReadOnlyList<DesktopComponentDefinition> GetComponents();
}

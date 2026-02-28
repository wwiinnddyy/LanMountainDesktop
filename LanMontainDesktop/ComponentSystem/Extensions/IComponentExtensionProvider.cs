using System.Collections.Generic;

namespace LanMontainDesktop.ComponentSystem.Extensions;

public interface IComponentExtensionProvider
{
    IReadOnlyList<DesktopComponentDefinition> GetComponents();
}

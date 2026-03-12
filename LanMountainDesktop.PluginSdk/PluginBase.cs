using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LanMountainDesktop.PluginSdk;

public abstract class PluginBase : IPlugin
{
    public virtual void Initialize(HostBuilderContext context, IServiceCollection services)
    {
    }
}

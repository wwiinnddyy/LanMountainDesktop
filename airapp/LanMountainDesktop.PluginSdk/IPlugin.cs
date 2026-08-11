using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LanMountainDesktop.PluginSdk;

public interface IPlugin
{
    void Initialize(HostBuilderContext context, IServiceCollection services);
}

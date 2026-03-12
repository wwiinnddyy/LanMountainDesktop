using System;
using System.Collections.Generic;
using System.Linq;
using LanMountainDesktop.PluginSdk;

namespace LanMountainDesktop.Plugins;

internal sealed class PluginExportRegistry : IPluginExportRegistry
{
    private readonly object _gate = new();
    private readonly List<PluginServiceExportDescriptor> _exports = [];

    public IReadOnlyList<PluginServiceExportDescriptor> GetExports()
    {
        lock (_gate)
        {
            return _exports.ToArray();
        }
    }

    public IReadOnlyList<PluginServiceExportDescriptor> GetExports(Type contractType)
    {
        ArgumentNullException.ThrowIfNull(contractType);

        lock (_gate)
        {
            return _exports
                .Where(descriptor => descriptor.ContractType == contractType)
                .ToArray();
        }
    }

    public PluginServiceExportDescriptor? GetExport(Type contractType, string providerPluginId)
    {
        ArgumentNullException.ThrowIfNull(contractType);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerPluginId);

        lock (_gate)
        {
            return _exports.FirstOrDefault(descriptor =>
                descriptor.ContractType == contractType &&
                string.Equals(descriptor.ProviderPluginId, providerPluginId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public TContract? GetExport<TContract>(string providerPluginId)
        where TContract : class
    {
        return GetExport(typeof(TContract), providerPluginId)?.ServiceInstance as TContract;
    }

    public void ReplaceExports(string pluginId, IEnumerable<PluginServiceExportDescriptor> descriptors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(descriptors);

        lock (_gate)
        {
            _exports.RemoveAll(descriptor =>
                string.Equals(descriptor.ProviderPluginId, pluginId, StringComparison.OrdinalIgnoreCase));
            _exports.AddRange(descriptors);
        }
    }

    public void RemoveExports(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);

        lock (_gate)
        {
            _exports.RemoveAll(descriptor =>
                string.Equals(descriptor.ProviderPluginId, pluginId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _exports.Clear();
        }
    }
}

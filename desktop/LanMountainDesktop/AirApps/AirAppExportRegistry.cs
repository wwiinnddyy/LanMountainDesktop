using System;
using System.Collections.Generic;
using System.Linq;
using LanMountainDesktop.AirAppSdk;

namespace LanMountainDesktop.AirApps;

internal sealed class AirAppExportRegistry : IAirAppExportRegistry
{
    private readonly object _gate = new();
    private readonly List<AirAppServiceExportDescriptor> _exports = [];

    public IReadOnlyList<AirAppServiceExportDescriptor> GetExports()
    {
        lock (_gate)
        {
            return _exports.ToArray();
        }
    }

    public IReadOnlyList<AirAppServiceExportDescriptor> GetExports(Type contractType)
    {
        ArgumentNullException.ThrowIfNull(contractType);

        lock (_gate)
        {
            return _exports
                .Where(descriptor => descriptor.ContractType == contractType)
                .ToArray();
        }
    }

    public AirAppServiceExportDescriptor? GetExport(Type contractType, string providerAirAppId)
    {
        ArgumentNullException.ThrowIfNull(contractType);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerAirAppId);

        lock (_gate)
        {
            return _exports.FirstOrDefault(descriptor =>
                descriptor.ContractType == contractType &&
                string.Equals(descriptor.ProviderAirAppId, providerAirAppId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public TContract? GetExport<TContract>(string providerAirAppId)
        where TContract : class
    {
        return GetExport(typeof(TContract), providerAirAppId)?.ServiceInstance as TContract;
    }

    public void ReplaceExports(string pluginId, IEnumerable<AirAppServiceExportDescriptor> descriptors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(descriptors);

        lock (_gate)
        {
            _exports.RemoveAll(descriptor =>
                string.Equals(descriptor.ProviderAirAppId, pluginId, StringComparison.OrdinalIgnoreCase));
            _exports.AddRange(descriptors);
        }
    }

    public void RemoveExports(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);

        lock (_gate)
        {
            _exports.RemoveAll(descriptor =>
                string.Equals(descriptor.ProviderAirAppId, pluginId, StringComparison.OrdinalIgnoreCase));
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

namespace LanMountainDesktop.PluginSdk;

public sealed class PluginServiceExportRegistration
{
    public PluginServiceExportRegistration(Type contractType, Type implementationType)
    {
        ArgumentNullException.ThrowIfNull(contractType);
        ArgumentNullException.ThrowIfNull(implementationType);

        ContractType = contractType;
        ImplementationType = implementationType;
    }

    public Type ContractType { get; }

    public Type ImplementationType { get; }
}

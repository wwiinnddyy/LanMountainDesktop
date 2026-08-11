namespace LanMountainDesktop.AirAppSdk;

public sealed class AirAppServiceExportRegistration
{
    public AirAppServiceExportRegistration(Type contractType, Type implementationType)
    {
        ArgumentNullException.ThrowIfNull(contractType);
        ArgumentNullException.ThrowIfNull(implementationType);

        ContractType = contractType;
        ImplementationType = implementationType;
    }

    public Type ContractType { get; }

    public Type ImplementationType { get; }
}

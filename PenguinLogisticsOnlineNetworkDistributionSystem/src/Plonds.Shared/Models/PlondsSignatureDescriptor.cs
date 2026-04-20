namespace Plonds.Shared.Models;

public sealed record PlondsSignatureDescriptor(
    string Algorithm,
    string KeyId,
    string Signature);


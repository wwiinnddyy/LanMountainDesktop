namespace LanMountainDesktop.Shared.Contracts.Privacy;

public interface IPrivacyDeviceIdentityProvider
{
    string GetOrCreateDeviceId();
}

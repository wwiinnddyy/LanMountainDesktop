using System.Security.Cryptography;

namespace LanMountainDesktop.Services.Update;

internal sealed class UpdateSignatureVerifier(PlondsApplyPaths paths)
{
    public (bool Success, string Message) Verify(string payloadPath, string signaturePath, string signatureName)
    {
        if (!File.Exists(signaturePath))
        {
            return (false, $"Missing {signatureName}.");
        }

        if (!File.Exists(paths.PublicKeyPath))
        {
            return (false, $"Missing public key: {paths.PublicKeyPath}");
        }

        var payloadBytes = File.ReadAllBytes(payloadPath);
        var signatureBase64 = File.ReadAllText(signaturePath).Trim();
        if (string.IsNullOrWhiteSpace(signatureBase64))
        {
            return (false, "Signature is empty.");
        }

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(signatureBase64);
        }
        catch (FormatException)
        {
            return (false, "Signature is not valid base64.");
        }

        using var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(paths.PublicKeyPath));
        var isValid = rsa.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return isValid ? (true, "ok") : (false, "Signature verification failed.");
    }
}

using System.Security.Cryptography;
using System.Text;

namespace Plonds.Core.Security;

public sealed class RsaFileSigner
{
    public string SignFile(string filePath, string privateKeyPath, string? outputPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Manifest file not found.", filePath);
        }

        if (!File.Exists(privateKeyPath))
        {
            throw new FileNotFoundException("Private key PEM file not found.", privateKeyPath);
        }

        outputPath ??= filePath + ".sig";

        var payload = File.ReadAllBytes(filePath);
        var privateKeyPem = File.ReadAllText(privateKeyPath, Encoding.ASCII);
        if (string.IsNullOrWhiteSpace(privateKeyPem))
        {
            throw new InvalidOperationException("Private key PEM is empty.");
        }

        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        var signature = rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        File.WriteAllText(outputPath, Convert.ToBase64String(signature), Encoding.ASCII);
        return outputPath;
    }
}

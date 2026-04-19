param(
    [Parameter(Mandatory = $true)]
    [string]$FilesJsonPath,

    [Parameter(Mandatory = $true)]
    [string]$PrivateKeyPath,

    [Parameter(Mandatory = $false)]
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw "Sign-FileMap.ps1 requires PowerShell 7 or newer."
}

if (-not (Test-Path -LiteralPath $FilesJsonPath)) {
    throw "Manifest file not found: $FilesJsonPath"
}

if (-not (Test-Path -LiteralPath $PrivateKeyPath)) {
    throw "Private key file not found: $PrivateKeyPath"
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = "$FilesJsonPath.sig"
}

$resolvedManifestPath = (Resolve-Path -LiteralPath $FilesJsonPath).Path
$manifestBytes = [System.IO.File]::ReadAllBytes($resolvedManifestPath)

$privateKeyPem = Get-Content -LiteralPath $PrivateKeyPath -Raw
if ([string]::IsNullOrWhiteSpace($privateKeyPem)) {
    throw "Private key PEM is empty: $PrivateKeyPath"
}

$rsa = [System.Security.Cryptography.RSA]::Create()
try {
    $rsa.ImportFromPem($privateKeyPem)
    $signatureBytes = $rsa.SignData(
        $manifestBytes,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1
    )
}
finally {
    $rsa.Dispose()
}

$signatureBase64 = [Convert]::ToBase64String($signatureBytes)
[System.IO.File]::WriteAllText($OutputPath, $signatureBase64, [System.Text.Encoding]::ASCII)

Write-Host "Signed manifest file."
Write-Host "Manifest:  $FilesJsonPath"
Write-Host "Signature: $OutputPath"

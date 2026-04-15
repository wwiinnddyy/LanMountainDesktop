# Sign-FileMap.ps1
# 对 files.json 进行 RSA 签名

param(
    [Parameter(Mandatory=$true)]
    [string]$FilesJsonPath,
    
    [Parameter(Mandatory=$true)]
    [string]$PrivateKeyPath,
    
    [Parameter(Mandatory=$false)]
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"

Write-Host "=== 签名文件清单 ===" -ForegroundColor Cyan
Write-Host "文件清单: $FilesJsonPath"
Write-Host "私钥: $PrivateKeyPath"
Write-Host ""

# 检查文件是否存在
if (-not (Test-Path $FilesJsonPath)) {
    Write-Error "文件清单不存在: $FilesJsonPath"
    exit 1
}

if (-not (Test-Path $PrivateKeyPath)) {
    Write-Error "私钥文件不存在: $PrivateKeyPath"
    exit 1
}

# 确定输出路径
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = "$FilesJsonPath.sig"
}

# 读取文件内容
$jsonBytes = [System.IO.File]::ReadAllBytes($FilesJsonPath)

# 读取私钥
$privateKeyPem = Get-Content -Path $PrivateKeyPath -Raw

# 使用 .NET 进行 RSA 签名
Add-Type -AssemblyName System.Security.Cryptography

$rsa = [System.Security.Cryptography.RSA]::Create()
$rsa.ImportFromPem($privateKeyPem)

# 生成签名
$signature = $rsa.SignData(
    $jsonBytes,
    [System.Security.Cryptography.HashAlgorithmName]::SHA256,
    [System.Security.Cryptography.RSASignaturePadding]::Pkcs1
)

# 转换为 Base64
$signatureBase64 = [Convert]::ToBase64String($signature)

# 写入签名文件
Set-Content -Path $OutputPath -Value $signatureBase64 -Encoding ASCII

Write-Host "=== 完成 ===" -ForegroundColor Green
Write-Host "签名文件: $OutputPath"
Write-Host "签名长度: $($signature.Length) 字节"

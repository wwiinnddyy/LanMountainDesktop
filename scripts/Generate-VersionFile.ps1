# 生成版本信息文件
param(
    [Parameter(Mandatory=$true)]
    [string]$OutputPath,
    
    [Parameter(Mandatory=$true)]
    [string]$Version,
    
    [Parameter(Mandatory=$false)]
    [string]$Codename = "Administrate"
)

$versionInfo = @{
    Version = $Version
    Codename = $Codename
}

$json = $versionInfo | ConvertTo-Json -Compress
$dir = Split-Path -Parent $OutputPath

if (!(Test-Path $dir)) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}

Set-Content -Path $OutputPath -Value $json -Encoding UTF8
Write-Host "Generated version file: $OutputPath" -ForegroundColor Green
Write-Host "  Version: $Version" -ForegroundColor Gray
Write-Host "  Codename: $Codename" -ForegroundColor Gray

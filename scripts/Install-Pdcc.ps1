param(
    [string]$Repository = "ClassIsland/PhainonDistributionCenter",
    [string]$AssetName = "out_app_linux_x64.zip",
    [string]$Version = "",
    [string]$OutputDir = (Join-Path $PSScriptRoot "..\pdcc")
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Repository)) {
    throw "Repository is required."
}

if ([string]::IsNullOrWhiteSpace($AssetName)) {
    throw "AssetName is required."
}

$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)
if (-not (Test-Path -LiteralPath $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$clientName = if ($env:OS -eq "Windows_NT") { "PhainonDistributionCenter.Client.exe" } else { "PhainonDistributionCenter.Client" }
$clientPath = Join-Path $OutputDir $clientName
if (Test-Path -LiteralPath $clientPath) {
    Write-Host "PDCC client already installed at $clientPath"
    return
}

$releaseTag = $Version
if ([string]::IsNullOrWhiteSpace($releaseTag)) {
    $releaseTag = $env:PDC_CLIENT_VERSION
}

if ([string]::IsNullOrWhiteSpace($releaseTag)) {
    $releaseTag = $env:PDCC_VERSION
}

$tempDir = Join-Path $env:RUNNER_TEMP "pdcc-install"
if (Test-Path -LiteralPath $tempDir) {
    Remove-Item -LiteralPath $tempDir -Recurse -Force
}
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

$zipPath = Join-Path $tempDir $AssetName

if (Get-Command gh -ErrorAction SilentlyContinue) {
    Write-Host "Downloading PDCC via gh release download from $Repository ..."
    $ghArgs = @("release", "download", "--repo", $Repository, "--pattern", $AssetName, "--dir", $tempDir, "--clobber")
    if (-not [string]::IsNullOrWhiteSpace($releaseTag)) {
        $ghArgs = @("release", "download", $releaseTag, "--repo", $Repository, "--pattern", $AssetName, "--dir", $tempDir, "--clobber")
    }

    & gh @ghArgs
    if ($LASTEXITCODE -ne 0) {
        throw "gh release download failed for $Repository/$AssetName."
    }
}
else {
    if ([string]::IsNullOrWhiteSpace($releaseTag)) {
        throw "PDCC_VERSION is required when gh is unavailable."
    }

    $downloadUrl = "https://github.com/$Repository/releases/download/$releaseTag/$AssetName"
    Write-Host "Downloading PDCC from $downloadUrl ..."
    Invoke-WebRequest -Uri $downloadUrl -OutFile $zipPath
}

$extractDir = Join-Path $tempDir "extract"
if (Test-Path -LiteralPath $extractDir) {
    Remove-Item -LiteralPath $extractDir -Recurse -Force
}
New-Item -ItemType Directory -Path $extractDir -Force | Out-Null
Expand-Archive -LiteralPath $zipPath -DestinationPath $extractDir -Force

$copied = $false
foreach ($file in Get-ChildItem -LiteralPath $extractDir -Recurse -File) {
    if ($file.Name -ieq $clientName) {
        Copy-Item -LiteralPath $file.FullName -Destination $clientPath -Force
        $copied = $true
        break
    }
}

if (-not $copied) {
    throw "PDCC client executable not found in downloaded archive."
}

if ($IsLinux) {
    try {
        chmod +x $clientPath | Out-Null
    }
    catch {
    }
}

Write-Host "PDCC installed to $clientPath"

param(
    [Parameter(Mandatory = $true)]
    [string]$SourceDir,

    [Parameter(Mandatory = $true)]
    [string]$OutputDir,

    [string]$PlatformKey = "",

    [string[]]$InstallerFiles = @()
)

$ErrorActionPreference = "Stop"

$SourceDir = [System.IO.Path]::GetFullPath($SourceDir)
$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)

if (-not (Test-Path -LiteralPath $SourceDir)) {
    throw "Source directory not found: $SourceDir"
}

if (Test-Path -LiteralPath $OutputDir) {
    Remove-Item -LiteralPath $OutputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$payloadRoot = if ([string]::IsNullOrWhiteSpace($PlatformKey)) {
    $OutputDir
} else {
    Join-Path $OutputDir $PlatformKey
}

New-Item -ItemType Directory -Path $payloadRoot -Force | Out-Null
Get-ChildItem -LiteralPath $SourceDir -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $payloadRoot -Recurse -Force
}

if ($InstallerFiles.Count -gt 0) {
    $installerRoot = Join-Path $OutputDir "installers"
    if (-not (Test-Path -LiteralPath $installerRoot)) {
        New-Item -ItemType Directory -Path $installerRoot -Force | Out-Null
    }

    foreach ($installer in $InstallerFiles) {
        if ([string]::IsNullOrWhiteSpace($installer)) {
            continue
        }

        $installerPath = [System.IO.Path]::GetFullPath($installer)
        if (-not (Test-Path -LiteralPath $installerPath)) {
            throw "Installer file not found: $installerPath"
        }

        $targetPath = Join-Path $installerRoot ([System.IO.Path]::GetFileName($installerPath))
        Copy-Item -LiteralPath $installerPath -Destination $targetPath -Force
    }
}

Write-Host "Prepared PDCC staging directory: $payloadRoot"

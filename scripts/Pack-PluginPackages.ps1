[CmdletBinding()]
param(
    [string]$OutputPath,
    [string]$Configuration = "Release",
    [string]$Version,
    [string]$NuGetPackagesPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot "artifacts\nuget"
}
if ([string]::IsNullOrWhiteSpace($NuGetPackagesPath)) {
    $NuGetPackagesPath = Join-Path $repoRoot ".nuget\packages"
}

$resolvedOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force -Path $resolvedOutputPath | Out-Null
$resolvedNuGetPackagesPath = [System.IO.Path]::GetFullPath($NuGetPackagesPath)
New-Item -ItemType Directory -Force -Path $resolvedNuGetPackagesPath | Out-Null
$env:NUGET_PACKAGES = $resolvedNuGetPackagesPath

$projects = @(
    "LanMountainDesktop.Shared.Contracts\LanMountainDesktop.Shared.Contracts.csproj",
    "LanMountainDesktop.PluginSdk\LanMountainDesktop.PluginSdk.csproj",
    "LanMountainDesktop.PluginTemplate\LanMountainDesktop.PluginTemplate.csproj"
)

$versionArgs = @()
if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $versionArgs += "-p:Version=$Version"
}

foreach ($project in $projects) {
    $projectPath = Join-Path $repoRoot $project
    if (-not (Test-Path -Path $projectPath)) {
        throw "Project '$projectPath' was not found."
    }

    Write-Host "Packing $project..."
    $args = @(
        "pack",
        $projectPath,
        "-c", $Configuration,
        "-o", $resolvedOutputPath
    ) + $versionArgs

    & dotnet @args
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet pack failed for '$projectPath' with exit code $LASTEXITCODE."
    }
}

Write-Host "Plugin packages generated to '$resolvedOutputPath'."

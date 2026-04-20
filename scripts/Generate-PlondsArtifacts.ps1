param(
    [Parameter(Mandatory = $true)]
    [string]$CurrentVersion,

    [Parameter(Mandatory = $true)]
    [string]$CurrentDir,

    [Parameter(Mandatory = $true)]
    [string]$Platform,

    [Parameter(Mandatory = $true)]
    [string]$OutputDir,

    [Parameter(Mandatory = $false)]
    [string]$PreviousVersion = "",

    [Parameter(Mandatory = $false)]
    [string]$PreviousDir = "",

    [Parameter(Mandatory = $false)]
    [string]$Channel = "stable",

    [Parameter(Mandatory = $false)]
    [string]$DistributionId = "",

    [Parameter(Mandatory = $false)]
    [string]$RepoBaseUrl = "",

    [Parameter(Mandatory = $false)]
    [string]$FileMapUrl = "",

    [Parameter(Mandatory = $false)]
    [string]$FileMapSignatureUrl = "",

    [Parameter(Mandatory = $false)]
    [string]$InstallerDirectory = "",

    [Parameter(Mandatory = $false)]
    [string]$InstallerBaseUrl = ""
)

$ErrorActionPreference = "Stop"

$toolProject = Join-Path $PSScriptRoot "..\PenguinLogisticsOnlineNetworkDistributionSystem\src\Plonds.Tool\Plonds.Tool.csproj"
if (-not (Test-Path -LiteralPath $toolProject)) {
    throw "PLONDS tool project not found: $toolProject"
}

$arguments = @(
    "run",
    "--project", $toolProject,
    "--",
    "generate",
    "--current-version", $CurrentVersion,
    "--current-dir", $CurrentDir,
    "--platform", $Platform,
    "--output-dir", $OutputDir,
    "--previous-version", $(if ([string]::IsNullOrWhiteSpace($PreviousVersion)) { "0.0.0" } else { $PreviousVersion }),
    "--channel", $Channel
)

if (-not [string]::IsNullOrWhiteSpace($PreviousDir)) {
    $arguments += @("--previous-dir", $PreviousDir)
}
if (-not [string]::IsNullOrWhiteSpace($DistributionId)) {
    $arguments += @("--distribution-id", $DistributionId)
}
if (-not [string]::IsNullOrWhiteSpace($RepoBaseUrl)) {
    $arguments += @("--repo-base-url", $RepoBaseUrl)
}
if (-not [string]::IsNullOrWhiteSpace($FileMapUrl)) {
    $arguments += @("--file-map-url", $FileMapUrl)
}
if (-not [string]::IsNullOrWhiteSpace($FileMapSignatureUrl)) {
    $arguments += @("--file-map-signature-url", $FileMapSignatureUrl)
}
if (-not [string]::IsNullOrWhiteSpace($InstallerDirectory)) {
    $arguments += @("--installer-directory", $InstallerDirectory)
}
if (-not [string]::IsNullOrWhiteSpace($InstallerBaseUrl)) {
    $arguments += @("--installer-base-url", $InstallerBaseUrl)
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "PLONDS generate command failed."
}

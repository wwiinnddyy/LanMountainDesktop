param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$AppArtifactsRoot,

    [Parameter(Mandatory = $true)]
    [string]$InstallerArtifactsRoot,

    [Parameter(Mandatory = $true)]
    [string]$OutputDir,

    [Parameter(Mandatory = $true)]
    [string]$PrivateKeyPath,

    [Parameter(Mandatory = $false)]
    [string]$Channel = "stable",

    [Parameter(Mandatory = $false)]
    [string]$S3Endpoint = "",

    [Parameter(Mandatory = $false)]
    [string]$S3Bucket = "",

    [Parameter(Mandatory = $false)]
    [string]$S3Region = ""
)

$ErrorActionPreference = "Stop"

function Get-PlatformConfigurations {
    return @(
        @{
            Platform = "windows-x64"
            ArtifactName = "app-payload-windows-x64"
        },
        @{
            Platform = "windows-x86"
            ArtifactName = "app-payload-windows-x86"
        },
        @{
            Platform = "linux-x64"
            ArtifactName = "app-payload-linux-x64"
        }
    )
}

function Resolve-AppDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SearchRoot,

        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    $preferred = Get-ChildItem -LiteralPath $SearchRoot -Recurse -Directory -Filter "app-$Version" -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($preferred) {
        return $preferred.FullName
    }

    $fallback = Get-ChildItem -LiteralPath $SearchRoot -Recurse -Directory -Filter "app-*" -ErrorAction SilentlyContinue |
        Sort-Object FullName |
        Select-Object -First 1
    return $fallback?.FullName
}

function Invoke-AwsSyncIfPossible {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $false)]
        [switch]$IgnoreFailure
    )

    if ([string]::IsNullOrWhiteSpace($S3Endpoint) -or [string]::IsNullOrWhiteSpace($S3Bucket)) {
        return
    }

    & aws @Arguments
    if ($LASTEXITCODE -ne 0 -and -not $IgnoreFailure) {
        throw "aws command failed: aws $($Arguments -join ' ')"
    }
}

if (-not (Test-Path -LiteralPath $PrivateKeyPath)) {
    throw "Private key file not found: $PrivateKeyPath"
}

$toolProject = Join-Path $PSScriptRoot "..\PenguinLogisticsOnlineNetworkDistributionSystem\src\Plonds.Tool\Plonds.Tool.csproj"
if (-not (Test-Path -LiteralPath $toolProject)) {
    throw "PLONDS tool project not found: $toolProject"
}

$supportedPlatforms = Get-PlatformConfigurations
$publishedRoot = Join-Path $OutputDir "published"
$releaseAssetsRoot = Join-Path $OutputDir "release-assets"
$baselineRoot = Join-Path $OutputDir "_baselines"

New-Item -ItemType Directory -Path $publishedRoot -Force | Out-Null
New-Item -ItemType Directory -Path $releaseAssetsRoot -Force | Out-Null
New-Item -ItemType Directory -Path $baselineRoot -Force | Out-Null

foreach ($config in $supportedPlatforms) {
    $platform = $config.Platform
    $platformBaselineRoot = Join-Path $baselineRoot $platform
    $baselineCurrentDir = Join-Path $platformBaselineRoot "current"
    $baselineVersionPath = Join-Path $platformBaselineRoot "version.txt"

    New-Item -ItemType Directory -Path $baselineCurrentDir -Force | Out-Null

    if (-not [string]::IsNullOrWhiteSpace($S3Endpoint) -and -not [string]::IsNullOrWhiteSpace($S3Bucket)) {
        Invoke-AwsSyncIfPossible -Arguments @(
            "--endpoint-url", $S3Endpoint,
            "--region", $S3Region,
            "s3", "sync",
            "s3://$S3Bucket/lanmountain/update/baselines/$platform/current/",
            $baselineCurrentDir,
            "--only-show-errors"
        ) -IgnoreFailure

        Invoke-AwsSyncIfPossible -Arguments @(
            "--endpoint-url", $S3Endpoint,
            "--region", $S3Region,
            "s3", "cp",
            "s3://$S3Bucket/lanmountain/update/baselines/$platform/version.txt",
            $baselineVersionPath,
            "--only-show-errors"
        ) -IgnoreFailure
    }
}

$repoBaseUrl = if ([string]::IsNullOrWhiteSpace($S3Endpoint) -or [string]::IsNullOrWhiteSpace($S3Bucket)) {
    $null
}
else {
    "$($S3Endpoint.TrimEnd('/'))/$S3Bucket/lanmountain/update/repo/sha256"
}

$installerBaseUrl = if ([string]::IsNullOrWhiteSpace($S3Endpoint) -or [string]::IsNullOrWhiteSpace($S3Bucket)) {
    $null
}
else {
    "$($S3Endpoint.TrimEnd('/'))/$S3Bucket/lanmountain/update/installers"
}

$legacySnapshots = @{}
foreach ($config in $supportedPlatforms) {
    $platform = $config.Platform
    $platformBaselineRoot = Join-Path $baselineRoot $platform
    $baselineCurrentDir = Join-Path $platformBaselineRoot "current"
    $baselineVersionPath = Join-Path $platformBaselineRoot "version.txt"
    $snapshotRoot = Join-Path $platformBaselineRoot "previous-snapshot"

    if (Test-Path -LiteralPath $snapshotRoot) {
        Remove-Item -LiteralPath $snapshotRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $snapshotRoot -Force | Out-Null

    $previousVersion = if (Test-Path -LiteralPath $baselineVersionPath) {
        (Get-Content -LiteralPath $baselineVersionPath -Raw).Trim()
    }
    else {
        "0.0.0"
    }

    $baselineHasContent = Get-ChildItem -LiteralPath $baselineCurrentDir -Force -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($baselineHasContent) {
        Copy-Item -LiteralPath (Join-Path $baselineCurrentDir '*') -Destination $snapshotRoot -Recurse -Force
        $snapshotDir = $snapshotRoot
    }
    else {
        $snapshotDir = Join-Path $platformBaselineRoot "empty"
        New-Item -ItemType Directory -Path $snapshotDir -Force | Out-Null
    }

    $legacySnapshots[$platform] = @{
        PreviousVersion = $previousVersion
        PreviousDir = $snapshotDir
    }
}

$publishArguments = @(
    "run",
    "--project", $toolProject,
    "--",
    "publish",
    "--version", $Version,
    "--app-artifacts-root", $AppArtifactsRoot,
    "--installer-artifacts-root", $InstallerArtifactsRoot,
    "--output-dir", $publishedRoot,
    "--private-key", $PrivateKeyPath,
    "--baseline-root", $baselineRoot,
    "--channel", $Channel
)

if (-not [string]::IsNullOrWhiteSpace($repoBaseUrl)) {
    $publishArguments += @("--repo-base-url", $repoBaseUrl)
}
if (-not [string]::IsNullOrWhiteSpace($installerBaseUrl)) {
    $publishArguments += @("--installer-base-url", $installerBaseUrl)
}

& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "PLONDS publish command failed."
}

foreach ($config in $supportedPlatforms) {
    $platform = $config.Platform
    $artifactRoot = Join-Path $AppArtifactsRoot $config.ArtifactName
    if (-not (Test-Path -LiteralPath $artifactRoot)) {
        throw "App payload artifact root not found for ${platform}: $artifactRoot"
    }

    $currentAppDir = Resolve-AppDirectory -SearchRoot $artifactRoot -Version $Version
    if ([string]::IsNullOrWhiteSpace($currentAppDir)) {
        throw "Unable to locate app payload directory for $platform under $artifactRoot"
    }

    $distributionId = "plonds-$Version-$platform"
    $manifestPath = Join-Path $publishedRoot "manifests/$distributionId/plonds-filemap.json"
    $manifestSignaturePath = "$manifestPath.sig"

    $legacyOutputDir = Join-Path $OutputDir "legacy/$platform"
    New-Item -ItemType Directory -Path $legacyOutputDir -Force | Out-Null

    $legacyState = $legacySnapshots[$platform]
    & (Join-Path $PSScriptRoot "Generate-DeltaPackage.ps1") `
        -PreviousVersion $legacyState.PreviousVersion `
        -CurrentVersion $Version `
        -PreviousDir $legacyState.PreviousDir `
        -CurrentDir $currentAppDir `
        -OutputDir $legacyOutputDir
    if ($LASTEXITCODE -ne 0) {
        throw "Generate-DeltaPackage.ps1 failed for $platform"
    }

    $legacyManifestPath = Join-Path $legacyOutputDir "files.json"
    $legacySignaturePath = Join-Path $legacyOutputDir "files.json.sig"
    & (Join-Path $PSScriptRoot "Sign-FileMap.ps1") `
        -FilesJsonPath $legacyManifestPath `
        -PrivateKeyPath $PrivateKeyPath `
        -OutputPath $legacySignaturePath
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to sign legacy manifest for $platform"
    }

    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $releaseAssetsRoot "plonds-filemap-$platform.json") -Force
    Copy-Item -LiteralPath $manifestSignaturePath -Destination (Join-Path $releaseAssetsRoot "plonds-filemap-$platform.json.sig") -Force
    Copy-Item -LiteralPath (Join-Path $publishedRoot "meta/distributions/$distributionId.json") -Destination (Join-Path $releaseAssetsRoot "plonds-distribution-$platform.json") -Force
    Copy-Item -LiteralPath (Join-Path $publishedRoot "meta/channels/$Channel/$platform/latest.json") -Destination (Join-Path $releaseAssetsRoot "plonds-latest-$platform.json") -Force

    Copy-Item -LiteralPath $legacyManifestPath -Destination (Join-Path $releaseAssetsRoot "files-$platform.json") -Force
    Copy-Item -LiteralPath $legacySignaturePath -Destination (Join-Path $releaseAssetsRoot "files-$platform.json.sig") -Force
    Copy-Item -LiteralPath (Join-Path $legacyOutputDir "update.zip") -Destination (Join-Path $releaseAssetsRoot "update-$platform.zip") -Force
}

if (-not [string]::IsNullOrWhiteSpace($S3Endpoint) -and -not [string]::IsNullOrWhiteSpace($S3Bucket)) {
    Invoke-AwsSyncIfPossible -Arguments @(
        "--endpoint-url", $S3Endpoint,
        "--region", $S3Region,
        "s3", "sync",
        $publishedRoot,
        "s3://$S3Bucket/lanmountain/update/",
        "--only-show-errors"
    )

    foreach ($config in $supportedPlatforms) {
        $platform = $config.Platform
        $platformBaselineRoot = Join-Path $baselineRoot $platform
        $baselineCurrentDir = Join-Path $platformBaselineRoot "current"
        $baselineVersionPath = Join-Path $platformBaselineRoot "version.txt"

        Invoke-AwsSyncIfPossible -Arguments @(
            "--endpoint-url", $S3Endpoint,
            "--region", $S3Region,
            "s3", "sync",
            $baselineCurrentDir,
            "s3://$S3Bucket/lanmountain/update/baselines/$platform/current/",
            "--delete",
            "--only-show-errors"
        )

        Invoke-AwsSyncIfPossible -Arguments @(
            "--endpoint-url", $S3Endpoint,
            "--region", $S3Region,
            "s3", "cp",
            $baselineVersionPath,
            "s3://$S3Bucket/lanmountain/update/baselines/$platform/version.txt",
            "--only-show-errors"
        )
    }
}

Write-Host "PLONDS publish staging completed."
Write-Host "Published root: $publishedRoot"
Write-Host "Release assets: $releaseAssetsRoot"

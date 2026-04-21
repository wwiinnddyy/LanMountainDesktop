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

function Invoke-AwsCommandIfPossible {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $false)]
        [switch]$IgnoreFailure
    )

    if ([string]::IsNullOrWhiteSpace($S3Endpoint) -or [string]::IsNullOrWhiteSpace($S3Bucket)) {
        return
    }

    $previousRequestChecksumCalculation = $env:AWS_REQUEST_CHECKSUM_CALCULATION
    $previousResponseChecksumValidation = $env:AWS_RESPONSE_CHECKSUM_VALIDATION

    # Rainyun's S3-compatible endpoint rejects AWS CLI v2's default checksum headers
    # during multipart uploads. Restrict checksum behavior to API-required cases only.
    $env:AWS_REQUEST_CHECKSUM_CALCULATION = "WHEN_REQUIRED"
    $env:AWS_RESPONSE_CHECKSUM_VALIDATION = "WHEN_REQUIRED"

    try {
        if ($IgnoreFailure) {
            & aws @Arguments 2>$null
        }
        else {
            & aws @Arguments
        }
    }
    finally {
        if ($null -eq $previousRequestChecksumCalculation) {
            Remove-Item Env:AWS_REQUEST_CHECKSUM_CALCULATION -ErrorAction SilentlyContinue
        }
        else {
            $env:AWS_REQUEST_CHECKSUM_CALCULATION = $previousRequestChecksumCalculation
        }

        if ($null -eq $previousResponseChecksumValidation) {
            Remove-Item Env:AWS_RESPONSE_CHECKSUM_VALIDATION -ErrorAction SilentlyContinue
        }
        else {
            $env:AWS_RESPONSE_CHECKSUM_VALIDATION = $previousResponseChecksumValidation
        }
    }

    if ($LASTEXITCODE -ne 0 -and -not $IgnoreFailure) {
        throw "aws command failed: aws $($Arguments -join ' ')"
    }
}

function Get-S3Key {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Prefix,

        [Parameter(Mandatory = $true)]
        [string]$RelativePath
    )

    $trimmedPrefix = $Prefix.Trim('/').Replace('\', '/')
    $trimmedRelativePath = $RelativePath.TrimStart('\', '/').Replace('\', '/')
    return "$trimmedPrefix/$trimmedRelativePath"
}

function Get-RelativePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,

        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $rootPath = [System.IO.Path]::GetFullPath($Root)
    if (-not $rootPath.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $rootPath += [System.IO.Path]::DirectorySeparatorChar
    }

    $pathValue = [System.IO.Path]::GetFullPath($Path)
    return [System.IO.Path]::GetRelativePath($rootPath, $pathValue)
}

function Get-RemoteS3Keys {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Prefix
    )

    $keys = [System.Collections.Generic.List[string]]::new()
    $continuationToken = $null

    do {
        $arguments = @(
            "--endpoint-url", $S3Endpoint,
            "--region", $S3Region,
            "s3api", "list-objects-v2",
            "--bucket", $S3Bucket,
            "--prefix", $Prefix,
            "--output", "json"
        )

        if (-not [string]::IsNullOrWhiteSpace($continuationToken)) {
            $arguments += @("--continuation-token", $continuationToken)
        }

        $json = Invoke-AwsCommandIfPossible -Arguments $arguments

        if ([string]::IsNullOrWhiteSpace($json)) {
            break
        }

        $response = $json | ConvertFrom-Json
        if ($response.Contents) {
            foreach ($item in $response.Contents) {
                if (-not [string]::IsNullOrWhiteSpace($item.Key)) {
                    $keys.Add($item.Key)
                }
            }
        }

        if ($response.IsTruncated -and -not [string]::IsNullOrWhiteSpace($response.NextContinuationToken)) {
            $continuationToken = $response.NextContinuationToken
        }
        else {
            $continuationToken = $null
        }
    } while (-not [string]::IsNullOrWhiteSpace($continuationToken))

    return $keys
}

function Upload-DirectoryToS3 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LocalRoot,

        [Parameter(Mandatory = $true)]
        [string]$RemotePrefix,

        [Parameter(Mandatory = $false)]
        [switch]$DeleteExtraRemoteObjects
    )

    if (-not (Test-Path -LiteralPath $LocalRoot)) {
        throw "Local upload root not found: $LocalRoot"
    }

    $files = Get-ChildItem -LiteralPath $LocalRoot -Recurse -File | Sort-Object FullName
    $uploadedKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)

    if ($files.Count -eq 0) {
        Write-Host "No files found under $LocalRoot; skipping upload."
    }

    $index = 0
    foreach ($file in $files) {
        $index++
        $relativePath = Get-RelativePath -Root $LocalRoot -Path $file.FullName
        $key = Get-S3Key -Prefix $RemotePrefix -RelativePath $relativePath
        $null = $uploadedKeys.Add($key)

        if ($index -eq 1 -or $index % 25 -eq 0 -or $index -eq $files.Count) {
            Write-Host "Uploading $index/$($files.Count): $key"
        }

        Invoke-AwsCommandIfPossible -Arguments @(
            "--endpoint-url", $S3Endpoint,
            "--region", $S3Region,
            "s3api", "put-object",
            "--bucket", $S3Bucket,
            "--key", $key,
            "--body", $file.FullName
        )
    }

    if ($DeleteExtraRemoteObjects) {
        $remoteKeys = Get-RemoteS3Keys -Prefix $RemotePrefix.Trim('/').Replace('\', '/')
        foreach ($remoteKey in $remoteKeys) {
            if (-not $uploadedKeys.Contains($remoteKey)) {
                Write-Host "Deleting stale remote object: $remoteKey"
                Invoke-AwsCommandIfPossible -Arguments @(
                    "--endpoint-url", $S3Endpoint,
                    "--region", $S3Region,
                    "s3api", "delete-object",
                    "--bucket", $S3Bucket,
                    "--key", $remoteKey
                )
            }
        }
    }
}

function Upload-FileToS3 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LocalPath,

        [Parameter(Mandatory = $true)]
        [string]$RemoteKey
    )

    if (-not (Test-Path -LiteralPath $LocalPath)) {
        throw "Local upload file not found: $LocalPath"
    }

    Invoke-AwsCommandIfPossible -Arguments @(
        "--endpoint-url", $S3Endpoint,
        "--region", $S3Region,
        "s3api", "put-object",
        "--bucket", $S3Bucket,
        "--key", $RemoteKey.Trim('/').Replace('\', '/'),
        "--body", $LocalPath
    )
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
        Invoke-AwsCommandIfPossible -Arguments @(
            "--endpoint-url", $S3Endpoint,
            "--region", $S3Region,
            "s3", "sync",
            "s3://$S3Bucket/lanmountain/update/baselines/$platform/current/",
            $baselineCurrentDir,
            "--only-show-errors"
        ) -IgnoreFailure

        Invoke-AwsCommandIfPossible -Arguments @(
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

    $baselineItems = @(Get-ChildItem -LiteralPath $baselineCurrentDir -Force -ErrorAction SilentlyContinue)
    if ($baselineItems.Count -gt 0) {
        foreach ($baselineItem in $baselineItems) {
            Copy-Item -LiteralPath $baselineItem.FullName -Destination $snapshotRoot -Recurse -Force
        }
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
    Upload-DirectoryToS3 -LocalRoot $publishedRoot -RemotePrefix "lanmountain/update"

    foreach ($config in $supportedPlatforms) {
        $platform = $config.Platform
        $platformBaselineRoot = Join-Path $baselineRoot $platform
        $baselineCurrentDir = Join-Path $platformBaselineRoot "current"
        $baselineVersionPath = Join-Path $platformBaselineRoot "version.txt"

        Upload-DirectoryToS3 `
            -LocalRoot $baselineCurrentDir `
            -RemotePrefix "lanmountain/update/baselines/$platform/current" `
            -DeleteExtraRemoteObjects

        Upload-FileToS3 `
            -LocalPath $baselineVersionPath `
            -RemoteKey "lanmountain/update/baselines/$platform/version.txt"
    }
}

Write-Host "PLONDS publish staging completed."
Write-Host "Published root: $publishedRoot"
Write-Host "Release assets: $releaseAssetsRoot"

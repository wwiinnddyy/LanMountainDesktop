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
    [string]$S3Region = "",

    [Parameter(Mandatory = $false)]
    [string]$IncrementalStrategy = "release-payload",

    [Parameter(Mandatory = $false)]
    [string]$PublishIncrementalRelease = "true",

    [Parameter(Mandatory = $false)]
    [string]$BaselineRef = "",

    [Parameter(Mandatory = $false)]
    [string]$GitHubRepository = "",

    [Parameter(Mandatory = $false)]
    [string]$GitHubTag = "",

    [Parameter(Mandatory = $false)]
    [string]$MirrorInstallersToS3 = "false",

    [Parameter(Mandatory = $false)]
    [string]$UploadMetaToS3 = "true"
)

$ErrorActionPreference = "Stop"

function ConvertTo-Boolean {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value,

        [Parameter(Mandatory = $false)]
        [bool]$DefaultValue = $false
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $DefaultValue
    }

    return $Value.Trim().ToLowerInvariant() -in @("1", "true", "yes", "y", "on")
}

function Get-GitHubReleaseBaseUrl {
    param(
        [Parameter(Mandatory = $false)]
        [string]$Repository,

        [Parameter(Mandatory = $false)]
        [string]$Tag
    )

    if ([string]::IsNullOrWhiteSpace($Repository) -or [string]::IsNullOrWhiteSpace($Tag)) {
        return $null
    }

    $normalizedRepository = $Repository.Trim().Trim('/')
    $normalizedTag = $Tag.Trim()
    if ($normalizedTag.StartsWith("refs/tags/", [System.StringComparison]::OrdinalIgnoreCase)) {
        $normalizedTag = $normalizedTag.Substring("refs/tags/".Length)
    }

    return "https://github.com/$normalizedRepository/releases/download/$normalizedTag"
}

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

function Clear-Directory {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (Test-Path -LiteralPath $Path) {
        Get-ChildItem -LiteralPath $Path -Force -ErrorAction SilentlyContinue | ForEach-Object {
            Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    else {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Invoke-AwsCommandIfPossible {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $false)]
        [switch]$IgnoreFailure
    )

    if ([string]::IsNullOrWhiteSpace($S3Endpoint) -or [string]::IsNullOrWhiteSpace($S3Bucket)) {
        return $null
    }

    $previousRequestChecksumCalculation = $env:AWS_REQUEST_CHECKSUM_CALCULATION
    $previousResponseChecksumValidation = $env:AWS_RESPONSE_CHECKSUM_VALIDATION

    $env:AWS_REQUEST_CHECKSUM_CALCULATION = "WHEN_REQUIRED"
    $env:AWS_RESPONSE_CHECKSUM_VALIDATION = "WHEN_REQUIRED"

    try {
        if ($IgnoreFailure) {
            return (& aws @Arguments 2>$null)
        }

        return (& aws @Arguments)
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

function Test-S3ObjectExists {
    param([Parameter(Mandatory = $true)][string]$Key)

    if ([string]::IsNullOrWhiteSpace($S3Endpoint) -or [string]::IsNullOrWhiteSpace($S3Bucket)) {
        return $false
    }

    Invoke-AwsCommandIfPossible -Arguments @(
        "--endpoint-url", $S3Endpoint,
        "--region", $S3Region,
        "s3api", "head-object",
        "--bucket", $S3Bucket,
        "--key", $Key.Trim('/').Replace('\', '/')
    ) -IgnoreFailure | Out-Null

    return $LASTEXITCODE -eq 0
}

function Copy-S3ObjectToLocal {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Key,

        [Parameter(Mandatory = $true)]
        [string]$DestinationPath
    )

    New-Item -ItemType Directory -Path ([System.IO.Path]::GetDirectoryName($DestinationPath)) -Force | Out-Null

    Invoke-AwsCommandIfPossible -Arguments @(
        "--endpoint-url", $S3Endpoint,
        "--region", $S3Region,
        "s3", "cp",
        "s3://$S3Bucket/$($Key.Trim('/').Replace('\', '/'))",
        $DestinationPath,
        "--only-show-errors"
    ) -IgnoreFailure | Out-Null

    return ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $DestinationPath))
}

function Get-S3JsonDocument {
    param([Parameter(Mandatory = $true)][string]$Key)

    $tempPath = Join-Path $OutputDir ("_tmp_" + [System.Guid]::NewGuid().ToString("N") + ".json")
    try {
        if (-not (Copy-S3ObjectToLocal -Key $Key -DestinationPath $tempPath)) {
            return $null
        }

        return Get-Content -LiteralPath $tempPath -Raw | ConvertFrom-Json -AsHashtable
    }
    finally {
        Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue
    }
}

function New-ZipFromDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceDirectory,

        [Parameter(Mandatory = $true)]
        [string]$DestinationPath
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path -LiteralPath $DestinationPath) {
        Remove-Item -LiteralPath $DestinationPath -Force
    }

    New-Item -ItemType Directory -Path ([System.IO.Path]::GetDirectoryName($DestinationPath)) -Force | Out-Null
    [System.IO.Compression.ZipFile]::CreateFromDirectory($SourceDirectory, $DestinationPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)
}

function Expand-PayloadSnapshot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Platform,

        [Parameter(Mandatory = $true)]
        [string]$BaselineVersion,

        [Parameter(Mandatory = $true)]
        [string]$DestinationPath
    )

    $payloadKey = "lanmountain/update/payloads/$Platform/$BaselineVersion/app-payload.zip"
    if (-not (Test-S3ObjectExists -Key $payloadKey)) {
        return $false
    }

    $tempZip = Join-Path $OutputDir ("payload-" + $Platform + "-" + $BaselineVersion + ".zip")
    try {
        if (-not (Copy-S3ObjectToLocal -Key $payloadKey -DestinationPath $tempZip)) {
            return $false
        }

        Clear-Directory -Path $DestinationPath
        Expand-Archive -LiteralPath $tempZip -DestinationPath $DestinationPath -Force
        return $true
    }
    finally {
        Remove-Item -LiteralPath $tempZip -Force -ErrorAction SilentlyContinue
    }
}

function Restore-LegacyBaseline {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Platform,

        [Parameter(Mandatory = $true)]
        [string]$DestinationPath,

        [Parameter(Mandatory = $true)]
        [string]$VersionFilePath
    )

    Clear-Directory -Path $DestinationPath
    Remove-Item -LiteralPath $VersionFilePath -Force -ErrorAction SilentlyContinue

    if ([string]::IsNullOrWhiteSpace($S3Endpoint) -or [string]::IsNullOrWhiteSpace($S3Bucket)) {
        return
    }

    Invoke-AwsCommandIfPossible -Arguments @(
        "--endpoint-url", $S3Endpoint,
        "--region", $S3Region,
        "s3", "sync",
        "s3://$S3Bucket/lanmountain/update/baselines/$Platform/current/",
        $DestinationPath,
        "--only-show-errors"
    ) -IgnoreFailure | Out-Null

    Copy-S3ObjectToLocal -Key "lanmountain/update/baselines/$Platform/version.txt" -DestinationPath $VersionFilePath | Out-Null
}

function ConvertTo-NormalizedVersion {
    param([Parameter(Mandatory = $false)][string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $null
    }

    $trimmed = $Value.Trim()
    if ($trimmed.StartsWith("refs/tags/", [System.StringComparison]::OrdinalIgnoreCase)) {
        $trimmed = $trimmed.Substring("refs/tags/".Length)
    }

    if ($trimmed.StartsWith("v", [System.StringComparison]::OrdinalIgnoreCase)) {
        $trimmed = $trimmed.Substring(1)
    }

    if ($trimmed -match '^\d+(\.\d+){1,3}$') {
        return $trimmed
    }

    return $null
}

function Resolve-GitTagFromRef {
    param([Parameter(Mandatory = $true)][string]$GitRef)

    $tag = (& git describe --tags --match "v*" --abbrev=0 $GitRef 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($tag)) {
        return $null
    }

    return $tag.Trim()
}

function Get-LatestChannelPointer {
    param([Parameter(Mandatory = $true)][string]$Platform)

    return Get-S3JsonDocument -Key "lanmountain/update/meta/channels/$Channel/$Platform/latest.json"
}

function Get-CommitRangeInfo {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RangeStart,

        [Parameter(Mandatory = $true)]
        [string]$RangeEnd
    )

    $files = (& git diff --name-only "$RangeStart..$RangeEnd" 2>$null)
    if ($LASTEXITCODE -ne 0) {
        return @{
            Start = $RangeStart
            End = $RangeEnd
            ChangeCount = 0
            HasPotentialPayloadImpact = $true
            RequiresComponentExpansion = $true
            SamplePaths = ""
        }
    }

    $changes = @($files | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $ignoredPrefixes = @(".github/", ".trae/", "docs/", "PenguinLogisticsOnlineNetworkDistributionSystem/")
    $ignoredExtensions = @(".md", ".txt")
    $expansionPrefixes = @(
        "LanMountainDesktop/",
        "LanMountainDesktop.Launcher/",
        "LanMountainDesktop.Appearance/",
        "LanMountainDesktop.PluginSdk/",
        "LanMountainDesktop.Settings.Core/",
        "LanMountainDesktop.Shared.Contracts/",
        "LanMountainDesktop.Tests/",
        "scripts/"
    )
    $expansionExtensions = @(".csproj", ".props", ".targets", ".sln", ".slnx", ".json", ".axaml", ".resx")

    $impactfulChanges = [System.Collections.Generic.List[string]]::new()
    $requiresExpansion = $false

    foreach ($change in $changes) {
        $normalized = $change.Replace('\', '/')
        $extension = [System.IO.Path]::GetExtension($normalized)

        $isIgnored = $false
        foreach ($ignoredPrefix in $ignoredPrefixes) {
            if ($normalized.StartsWith($ignoredPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                $isIgnored = $true
                break
            }
        }
        if (-not $isIgnored -and $ignoredExtensions -contains $extension.ToLowerInvariant()) {
            $isIgnored = $true
        }

        if ($isIgnored) {
            continue
        }

        $impactfulChanges.Add($normalized)

        foreach ($expansionPrefix in $expansionPrefixes) {
            if ($normalized.StartsWith($expansionPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                $requiresExpansion = $true
                break
            }
        }

        if ($requiresExpansion -or $expansionExtensions -contains $extension.ToLowerInvariant()) {
            $requiresExpansion = $true
        }
    }

    return @{
        Start = $RangeStart
        End = $RangeEnd
        ChangeCount = $changes.Count
        HasPotentialPayloadImpact = ($impactfulChanges.Count -gt 0)
        RequiresComponentExpansion = $requiresExpansion
        SamplePaths = (($impactfulChanges | Select-Object -First 10) -join "; ")
    }
}

function Update-JsonMetadata {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [hashtable]$Metadata
    )

    $document = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -AsHashtable
    if (-not $document.ContainsKey("metadata") -or $null -eq $document.metadata) {
        $document.metadata = @{}
    }

    foreach ($key in $Metadata.Keys) {
        if ($null -ne $Metadata[$key] -and -not [string]::IsNullOrWhiteSpace([string]$Metadata[$key])) {
            $document.metadata[$key] = [string]$Metadata[$key]
        }
    }

    $document | ConvertTo-Json -Depth 64 | Set-Content -LiteralPath $Path -Encoding utf8NoBOM
}

function Get-FileMapChangeSummary {
    param([Parameter(Mandatory = $true)][string]$Path)

    $document = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -AsHashtable
    $summary = @{
        Add = 0
        Replace = 0
        Reuse = 0
        Delete = 0
    }

    foreach ($component in @($document.components)) {
        foreach ($file in @($component.files)) {
            $operation = [string]$file.op
            if ($summary.ContainsKey($operation.Substring(0, 1).ToUpperInvariant() + $operation.Substring(1))) {
                $summary[$operation.Substring(0, 1).ToUpperInvariant() + $operation.Substring(1)]++
            }
        }
    }

    return $summary
}

function Upload-DirectoryToS3 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LocalRoot,

        [Parameter(Mandatory = $true)]
        [string]$RemotePrefix,

        [Parameter(Mandatory = $false)]
        [switch]$SkipExisting
    )

    if (-not (Test-Path -LiteralPath $LocalRoot)) {
        Write-Host "Skipping missing upload root: $LocalRoot"
        return
    }

    $files = Get-ChildItem -LiteralPath $LocalRoot -Recurse -File | Sort-Object FullName
    if ($files.Count -eq 0) {
        Write-Host "No files found under $LocalRoot; skipping upload."
        return
    }

    $index = 0
    foreach ($file in $files) {
        $index++
        $relativePath = Get-RelativePath -Root $LocalRoot -Path $file.FullName
        $key = Get-S3Key -Prefix $RemotePrefix -RelativePath $relativePath

        if ($SkipExisting -and (Test-S3ObjectExists -Key $key)) {
            if ($index -eq 1 -or $index % 25 -eq 0 -or $index -eq $files.Count) {
                Write-Host "Skipping existing $index/$($files.Count): $key"
            }
            continue
        }

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
        ) | Out-Null

        if ($LASTEXITCODE -ne 0) {
            throw "Failed to upload $key"
        }
    }
}

function Upload-InstallerDirectoryToS3 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LocalRoot,

        [Parameter(Mandatory = $true)]
        [string]$RemotePrefix
    )

    if (-not (Test-Path -LiteralPath $LocalRoot)) {
        Write-Host "Skipping missing installer upload root: $LocalRoot"
        return
    }

    $files = Get-ChildItem -LiteralPath $LocalRoot -Recurse -File | Sort-Object FullName
    if ($files.Count -eq 0) {
        Write-Host "No installer files found under $LocalRoot; skipping installer upload."
        return
    }

    $tempDir = Join-Path $OutputDir ("_aws-installer-config-" + [System.Guid]::NewGuid().ToString("N"))
    $tempConfigPath = Join-Path $tempDir "config"
    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
    @"
[default]
s3 =
  preferred_transfer_client = classic
  addressing_style = path
  max_concurrent_requests = 4
  max_queue_size = 32
  multipart_threshold = 64MB
  multipart_chunksize = 32MB
  payload_signing_enabled = false
"@ | Set-Content -LiteralPath $tempConfigPath -Encoding ascii

    $previousConfigFile = $env:AWS_CONFIG_FILE
    $previousRetryMode = $env:AWS_RETRY_MODE
    $previousMaxAttempts = $env:AWS_MAX_ATTEMPTS
    $env:AWS_CONFIG_FILE = $tempConfigPath
    $env:AWS_RETRY_MODE = "adaptive"
    $env:AWS_MAX_ATTEMPTS = "6"

    try {
        $index = 0
        foreach ($file in $files) {
            $index++
            $relativePath = Get-RelativePath -Root $LocalRoot -Path $file.FullName
            $key = Get-S3Key -Prefix $RemotePrefix -RelativePath $relativePath

            if (Test-S3ObjectExists -Key $key) {
                if ($index -eq 1 -or $index % 10 -eq 0 -or $index -eq $files.Count) {
                    Write-Host "Skipping existing installer $index/$($files.Count): $key"
                }
                continue
            }

            Write-Host "Uploading installer $index/$($files.Count): $key"
            Invoke-AwsCommandIfPossible -Arguments @(
                "--cli-connect-timeout", "60",
                "--cli-read-timeout", "0",
                "--endpoint-url", $S3Endpoint,
                "--region", $S3Region,
                "s3", "cp",
                $file.FullName,
                "s3://$S3Bucket/$key",
                "--only-show-errors",
                "--no-progress"
            ) -IgnoreFailure | Out-Null

            if ($LASTEXITCODE -eq 0) {
                continue
            }

            Write-Warning "Multipart installer upload failed for $key, falling back to put-object."
            Invoke-AwsCommandIfPossible -Arguments @(
                "--endpoint-url", $S3Endpoint,
                "--region", $S3Region,
                "s3api", "put-object",
                "--bucket", $S3Bucket,
                "--key", $key,
                "--body", $file.FullName
            ) | Out-Null

            if ($LASTEXITCODE -ne 0) {
                throw "Failed to upload installer mirror: $key"
            }
        }
    }
    finally {
        if ($null -eq $previousConfigFile) {
            Remove-Item Env:AWS_CONFIG_FILE -ErrorAction SilentlyContinue
        }
        else {
            $env:AWS_CONFIG_FILE = $previousConfigFile
        }

        if ($null -eq $previousRetryMode) {
            Remove-Item Env:AWS_RETRY_MODE -ErrorAction SilentlyContinue
        }
        else {
            $env:AWS_RETRY_MODE = $previousRetryMode
        }

        if ($null -eq $previousMaxAttempts) {
            Remove-Item Env:AWS_MAX_ATTEMPTS -ErrorAction SilentlyContinue
        }
        else {
            $env:AWS_MAX_ATTEMPTS = $previousMaxAttempts
        }

        Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
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
$legacyRoot = Join-Path $OutputDir "legacy"
$publishIncremental = ConvertTo-Boolean -Value $PublishIncrementalRelease -DefaultValue $true
$isFullPayloadRelease = -not $publishIncremental
$mirrorInstallers = ConvertTo-Boolean -Value $MirrorInstallersToS3 -DefaultValue $false
$uploadMetaToS3 = ConvertTo-Boolean -Value $UploadMetaToS3 -DefaultValue $true
$gitHubReleaseBaseUrl = Get-GitHubReleaseBaseUrl -Repository $GitHubRepository -Tag $GitHubTag
$sourceCommit = (& git rev-parse HEAD 2>$null)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sourceCommit)) {
    $sourceCommit = ""
}
else {
    $sourceCommit = $sourceCommit.Trim()
}

New-Item -ItemType Directory -Path $publishedRoot -Force | Out-Null
New-Item -ItemType Directory -Path $releaseAssetsRoot -Force | Out-Null
New-Item -ItemType Directory -Path $baselineRoot -Force | Out-Null
New-Item -ItemType Directory -Path $legacyRoot -Force | Out-Null

$repoBaseUrl = if ([string]::IsNullOrWhiteSpace($S3Endpoint) -or [string]::IsNullOrWhiteSpace($S3Bucket)) {
    $null
}
else {
    "$($S3Endpoint.TrimEnd('/'))/$S3Bucket/lanmountain/update/repo/sha256"
}

$installerBaseUrl = if (-not [string]::IsNullOrWhiteSpace($gitHubReleaseBaseUrl)) {
    $gitHubReleaseBaseUrl
}
elseif ([string]::IsNullOrWhiteSpace($S3Endpoint) -or [string]::IsNullOrWhiteSpace($S3Bucket)) {
    $null
}
else {
    "$($S3Endpoint.TrimEnd('/'))/$S3Bucket/lanmountain/update/installers"
}
$installerMirrorMode = if (-not [string]::IsNullOrWhiteSpace($gitHubReleaseBaseUrl)) {
    "github-release"
}
elseif ($mirrorInstallers -and -not [string]::IsNullOrWhiteSpace($installerBaseUrl)) {
    "s3"
}
else {
    "none"
}

$resolvedBaselineVersionOverride = ConvertTo-NormalizedVersion -Value $BaselineRef
$resolvedBaselineRefOverride = if ([string]::IsNullOrWhiteSpace($BaselineRef)) {
    $null
}
elseif (-not [string]::IsNullOrWhiteSpace($resolvedBaselineVersionOverride)) {
    "v$resolvedBaselineVersionOverride"
}
else {
    $BaselineRef.Trim()
}

$platformStates = @{}
foreach ($config in $supportedPlatforms) {
    $platform = $config.Platform
    $platformBaselineRoot = Join-Path $baselineRoot $platform
    $baselineCurrentDir = Join-Path $platformBaselineRoot "current"
    $baselineVersionPath = Join-Path $platformBaselineRoot "version.txt"
    $snapshotRoot = Join-Path $platformBaselineRoot "previous-snapshot"
    $emptyRoot = Join-Path $platformBaselineRoot "empty"

    New-Item -ItemType Directory -Path $platformBaselineRoot -Force | Out-Null
    Clear-Directory -Path $baselineCurrentDir
    Clear-Directory -Path $snapshotRoot
    Clear-Directory -Path $emptyRoot

    $latestPointer = $null
    $resolvedBaselineVersion = $resolvedBaselineVersionOverride
    $resolvedBaselineRef = $resolvedBaselineRefOverride

    if (-not $resolvedBaselineVersion) {
        if (-not [string]::IsNullOrWhiteSpace($BaselineRef)) {
            $resolvedBaselineRef = if ($resolvedBaselineRef) { $resolvedBaselineRef } else { $BaselineRef.Trim() }
            $tag = Resolve-GitTagFromRef -GitRef $BaselineRef.Trim()
            if ($tag) {
                $resolvedBaselineVersion = ConvertTo-NormalizedVersion -Value $tag
                if (-not $resolvedBaselineRef) {
                    $resolvedBaselineRef = $tag
                }
            }
        }
        else {
            $latestPointer = Get-LatestChannelPointer -Platform $platform
            if ($latestPointer) {
                $resolvedBaselineVersion = [string]$latestPointer.version
                $resolvedBaselineRef = if ([string]::IsNullOrWhiteSpace([string]$latestPointer.version)) { $null } else { "v$($latestPointer.version)" }
            }
        }
    }

    $baselineSource = "none"
    if ($isFullPayloadRelease) {
        "0.0.0" | Set-Content -LiteralPath $baselineVersionPath -Encoding ascii
        $baselineSource = "empty"
    }
    else {
        $restored = $false
        if (-not [string]::IsNullOrWhiteSpace($resolvedBaselineVersion)) {
            $restored = Expand-PayloadSnapshot -Platform $platform -BaselineVersion $resolvedBaselineVersion -DestinationPath $baselineCurrentDir
            if ($restored) {
                $baselineSource = "payload"
                $resolvedBaselineRef = if ($resolvedBaselineRef) { $resolvedBaselineRef } else { "v$resolvedBaselineVersion" }
            }
        }

        if (-not $restored) {
            Restore-LegacyBaseline -Platform $platform -DestinationPath $baselineCurrentDir -VersionFilePath $baselineVersionPath
            $legacyVersion = if (Test-Path -LiteralPath $baselineVersionPath) {
                (Get-Content -LiteralPath $baselineVersionPath -Raw).Trim()
            }
            else {
                ""
            }

            if (-not [string]::IsNullOrWhiteSpace($legacyVersion)) {
                $resolvedBaselineVersion = $legacyVersion
                $resolvedBaselineRef = if ($resolvedBaselineRef) { $resolvedBaselineRef } else { "v$legacyVersion" }
                $baselineSource = "legacy-baseline"
            }
            else {
                "0.0.0" | Set-Content -LiteralPath $baselineVersionPath -Encoding ascii
                $resolvedBaselineVersion = "0.0.0"
                $baselineSource = "empty"
            }
        }

        if (-not (Test-Path -LiteralPath $baselineVersionPath)) {
            $versionToPersist = if ([string]::IsNullOrWhiteSpace($resolvedBaselineVersion)) { "0.0.0" } else { $resolvedBaselineVersion }
            $versionToPersist | Set-Content -LiteralPath $baselineVersionPath -Encoding ascii
        }
    }

    $baselineItems = @(Get-ChildItem -LiteralPath $baselineCurrentDir -Force -ErrorAction SilentlyContinue)
    if ($baselineItems.Count -gt 0) {
        foreach ($baselineItem in $baselineItems) {
            Copy-Item -LiteralPath $baselineItem.FullName -Destination $snapshotRoot -Recurse -Force
        }
        $legacyPreviousDir = $snapshotRoot
    }
    else {
        $legacyPreviousDir = $emptyRoot
    }

    $commitInfo = @{
        Start = $null
        End = $sourceCommit
        ChangeCount = 0
        HasPotentialPayloadImpact = $true
        RequiresComponentExpansion = $true
        SamplePaths = ""
    }

    if ($IncrementalStrategy -eq "commit-range") {
        $rangeStart = if (-not [string]::IsNullOrWhiteSpace($resolvedBaselineRef)) {
            $resolvedBaselineRef
        }
        elseif (-not [string]::IsNullOrWhiteSpace($resolvedBaselineVersion)) {
            "v$resolvedBaselineVersion"
        }
        else {
            $null
        }

        if (-not [string]::IsNullOrWhiteSpace($rangeStart) -and -not [string]::IsNullOrWhiteSpace($sourceCommit)) {
            $commitInfo = Get-CommitRangeInfo -RangeStart $rangeStart -RangeEnd $sourceCommit
        }
    }

    $platformStates[$platform] = @{
        Platform = $platform
        ArtifactName = $config.ArtifactName
        BaselineVersion = if ([string]::IsNullOrWhiteSpace($resolvedBaselineVersion)) { "0.0.0" } else { $resolvedBaselineVersion }
        BaselineRef = $resolvedBaselineRef
        BaselineSource = $baselineSource
        LegacyPreviousDir = $legacyPreviousDir
        CommitInfo = $commitInfo
    }

    Write-Host "Prepared baseline for $platform => version=$($platformStates[$platform].BaselineVersion), source=$baselineSource, strategy=$IncrementalStrategy"
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
    "--channel", $Channel,
    "--incremental-strategy", $IncrementalStrategy,
    "--is-full-payload-release", $isFullPayloadRelease.ToString().ToLowerInvariant()
)

if (-not [string]::IsNullOrWhiteSpace($repoBaseUrl)) {
    $publishArguments += @("--repo-base-url", $repoBaseUrl)
}

if (-not [string]::IsNullOrWhiteSpace($installerBaseUrl)) {
    $publishArguments += @("--installer-base-url", $installerBaseUrl)
}

if (-not [string]::IsNullOrWhiteSpace($sourceCommit)) {
    $publishArguments += @("--source-commit", $sourceCommit)
}

if (-not [string]::IsNullOrWhiteSpace($resolvedBaselineVersionOverride)) {
    $publishArguments += @("--baseline-version", $resolvedBaselineVersionOverride)
}

if (-not [string]::IsNullOrWhiteSpace($resolvedBaselineRefOverride)) {
    $publishArguments += @("--baseline-ref", $resolvedBaselineRefOverride)
}

& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "PLONDS publish command failed."
}

foreach ($config in $supportedPlatforms) {
    $platform = $config.Platform
    $state = $platformStates[$platform]
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
    $distributionPath = Join-Path $publishedRoot "meta/distributions/$distributionId.json"
    $latestPath = Join-Path $publishedRoot "meta/channels/$Channel/$platform/latest.json"
    $payloadSnapshotPath = Join-Path $publishedRoot "payloads/$platform/$Version/app-payload.zip"
    New-ZipFromDirectory -SourceDirectory $currentAppDir -DestinationPath $payloadSnapshotPath

    $changeSummary = Get-FileMapChangeSummary -Path $manifestPath
    $changeCount = $changeSummary.Add + $changeSummary.Replace + $changeSummary.Delete
    $commitVerificationAdjusted = $false
    if ($IncrementalStrategy -eq "commit-range" -and -not $state.CommitInfo.HasPotentialPayloadImpact -and $changeCount -gt 0) {
        $commitVerificationAdjusted = $true
        Write-Warning "Commit range for $platform predicted no payload impact, but payload diff found $changeCount changes. Keeping payload diff as source of truth."
    }

    $metadata = @{
        baselineVersion = $state.BaselineVersion
        baselineRef = $state.BaselineRef
        baselineSource = $state.BaselineSource
        sourceCommit = $sourceCommit
        incrementalStrategy = $IncrementalStrategy
        isFullPayloadRelease = $isFullPayloadRelease.ToString().ToLowerInvariant()
        commitRangeStart = $state.CommitInfo.Start
        commitRangeEnd = $state.CommitInfo.End
        commitChangeCount = [string]$state.CommitInfo.ChangeCount
        commitHasPotentialPayloadImpact = [string]$state.CommitInfo.HasPotentialPayloadImpact
        commitRequiresComponentExpansion = [string]$state.CommitInfo.RequiresComponentExpansion
        commitVerificationAdjusted = [string]$commitVerificationAdjusted
        commitSamplePaths = $state.CommitInfo.SamplePaths
        payloadSnapshotPath = "lanmountain/update/payloads/$platform/$Version/app-payload.zip"
        installerMirrorMode = $installerMirrorMode
        installerMirrorBaseUrl = $installerBaseUrl
    }

    Update-JsonMetadata -Path $manifestPath -Metadata $metadata
    Update-JsonMetadata -Path $distributionPath -Metadata $metadata

    & (Join-Path $PSScriptRoot "Sign-FileMap.ps1") `
        -FilesJsonPath $manifestPath `
        -PrivateKeyPath $PrivateKeyPath `
        -OutputPath $manifestSignaturePath
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to re-sign PLONDS manifest for $platform"
    }

    $legacyOutputDir = Join-Path $legacyRoot $platform
    New-Item -ItemType Directory -Path $legacyOutputDir -Force | Out-Null

    & (Join-Path $PSScriptRoot "Generate-DeltaPackage.ps1") `
        -PreviousVersion $state.BaselineVersion `
        -CurrentVersion $Version `
        -PreviousDir $state.LegacyPreviousDir `
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
    Copy-Item -LiteralPath $distributionPath -Destination (Join-Path $releaseAssetsRoot "plonds-distribution-$platform.json") -Force
    Copy-Item -LiteralPath $latestPath -Destination (Join-Path $releaseAssetsRoot "plonds-latest-$platform.json") -Force
    Copy-Item -LiteralPath $payloadSnapshotPath -Destination (Join-Path $releaseAssetsRoot "plonds-payload-$platform.zip") -Force

    Copy-Item -LiteralPath $legacyManifestPath -Destination (Join-Path $releaseAssetsRoot "files-$platform.json") -Force
    Copy-Item -LiteralPath $legacySignaturePath -Destination (Join-Path $releaseAssetsRoot "files-$platform.json.sig") -Force
    Copy-Item -LiteralPath (Join-Path $legacyOutputDir "update.zip") -Destination (Join-Path $releaseAssetsRoot "update-$platform.zip") -Force
}

if (-not [string]::IsNullOrWhiteSpace($S3Endpoint) -and -not [string]::IsNullOrWhiteSpace($S3Bucket)) {
    Upload-DirectoryToS3 -LocalRoot (Join-Path $publishedRoot "payloads") -RemotePrefix "lanmountain/update/payloads" -SkipExisting
    Upload-DirectoryToS3 -LocalRoot (Join-Path $publishedRoot "repo") -RemotePrefix "lanmountain/update/repo" -SkipExisting
    if ($mirrorInstallers) {
        Upload-InstallerDirectoryToS3 -LocalRoot (Join-Path $publishedRoot "installers") -RemotePrefix "lanmountain/update/installers"
    }
    else {
        Write-Host "Skipping blocking S3 installer mirror upload. Installer mirrors will resolve via $installerMirrorMode."
    }
    Upload-DirectoryToS3 -LocalRoot (Join-Path $publishedRoot "manifests") -RemotePrefix "lanmountain/update/manifests"
    if ($uploadMetaToS3) {
        Upload-DirectoryToS3 -LocalRoot (Join-Path $publishedRoot "meta") -RemotePrefix "lanmountain/update/meta"
    }
    else {
        Write-Host "Deferring S3 meta upload until after GitHub Release is published."
    }
}

Write-Host "PLONDS publish staging completed."
Write-Host "Published root: $publishedRoot"
Write-Host "Release assets: $releaseAssetsRoot"

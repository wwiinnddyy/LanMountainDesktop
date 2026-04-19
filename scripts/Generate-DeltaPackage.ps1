param(
    [Parameter(Mandatory = $true)]
    [string]$PreviousVersion,

    [Parameter(Mandatory = $true)]
    [string]$CurrentVersion,

    [Parameter(Mandatory = $true)]
    [string]$PreviousDir,

    [Parameter(Mandatory = $true)]
    [string]$CurrentDir,

    [Parameter(Mandatory = $true)]
    [string]$OutputDir
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-NormalizedRelativePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootDir,

        [Parameter(Mandatory = $true)]
        [string]$FullPath
    )

    $root = [System.IO.Path]::GetFullPath($RootDir)
    $path = [System.IO.Path]::GetFullPath($FullPath)

    if (-not $root.EndsWith([System.IO.Path]::DirectorySeparatorChar.ToString()) -and
        -not $root.EndsWith([System.IO.Path]::AltDirectorySeparatorChar.ToString())) {
        $root += [System.IO.Path]::DirectorySeparatorChar
    }

    $rootUri = [System.Uri]$root
    $pathUri = [System.Uri]$path
    $relative = [System.Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString())

    return $relative.Replace('\', '/')
}

function Get-FileSha256Hex {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-FileManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootDir
    )

    if (-not (Test-Path -LiteralPath $RootDir)) {
        throw "Directory does not exist: $RootDir"
    }

    $resolvedRoot = (Resolve-Path -LiteralPath $RootDir).Path
    $manifest = @{}
    $files = Get-ChildItem -LiteralPath $resolvedRoot -Recurse -File

    foreach ($file in $files) {
        $relativePath = Get-NormalizedRelativePath -RootDir $resolvedRoot -FullPath $file.FullName
        $manifest[$relativePath] = [ordered]@{
            Path = $relativePath
            Sha256 = Get-FileSha256Hex -Path $file.FullName
            Size = [long]$file.Length
        }
    }

    return $manifest
}

function New-DeltaArchive {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ZipPath,

        [Parameter(Mandatory = $true)]
        [string]$CurrentRoot,

        [Parameter(Mandatory = $true)]
        [object[]]$ChangedFiles
    )

    if (Test-Path -LiteralPath $ZipPath) {
        Remove-Item -LiteralPath $ZipPath -Force
    }

    $zip = [System.IO.Compression.ZipFile]::Open($ZipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $ChangedFiles) {
            $sourcePath = Join-Path $CurrentRoot $file.Path
            if (-not (Test-Path -LiteralPath $sourcePath)) {
                throw "Changed file was not found while building archive: $sourcePath"
            }

            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip,
                $sourcePath,
                $file.Path,
                [System.IO.Compression.CompressionLevel]::Optimal
            ) | Out-Null
        }
    }
    finally {
        $zip.Dispose()
    }
}

Write-Host "Generating incremental package..."
Write-Host "From: $PreviousVersion"
Write-Host "To:   $CurrentVersion"
Write-Host "Prev: $PreviousDir"
Write-Host "Curr: $CurrentDir"
Write-Host "Out:  $OutputDir"

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$previousManifest = Get-FileManifest -RootDir $PreviousDir
$currentManifest = Get-FileManifest -RootDir $CurrentDir

$changedFiles = @()
$reusedFiles = @()
$deletedFiles = @()

foreach ($path in ($currentManifest.Keys | Sort-Object)) {
    $currentFile = $currentManifest[$path]

    if ($previousManifest.ContainsKey($path)) {
        $previousFile = $previousManifest[$path]
        if ($currentFile.Sha256 -eq $previousFile.Sha256) {
            $reusedFiles += [ordered]@{
                Path = $path
                Action = "reuse"
                Sha256 = $currentFile.Sha256
                Size = $currentFile.Size
            }
        }
        else {
            $changedFiles += [ordered]@{
                Path = $path
                Action = "replace"
                Sha256 = $currentFile.Sha256
                Size = $currentFile.Size
                ArchivePath = $path
            }
        }
    }
    else {
        $changedFiles += [ordered]@{
            Path = $path
            Action = "add"
            Sha256 = $currentFile.Sha256
            Size = $currentFile.Size
            ArchivePath = $path
        }
    }
}

foreach ($path in ($previousManifest.Keys | Sort-Object)) {
    if (-not $currentManifest.ContainsKey($path)) {
        $deletedFiles += [ordered]@{
            Path = $path
            Action = "delete"
        }
    }
}

Write-Host "Changed: $($changedFiles.Count)"
Write-Host "Reused:  $($reusedFiles.Count)"
Write-Host "Deleted: $($deletedFiles.Count)"

$resolvedCurrentDir = (Resolve-Path -LiteralPath $CurrentDir).Path
$updateZipPath = Join-Path $OutputDir "update.zip"
New-DeltaArchive -ZipPath $updateZipPath -CurrentRoot $resolvedCurrentDir -ChangedFiles $changedFiles

$deltaZipPath = Join-Path $OutputDir ("delta-{0}-to-{1}.zip" -f $PreviousVersion, $CurrentVersion)
Copy-Item -LiteralPath $updateZipPath -Destination $deltaZipPath -Force

$allEntries = @($changedFiles + $reusedFiles + $deletedFiles)
$filesJson = [ordered]@{
    FromVersion = $PreviousVersion
    ToVersion = $CurrentVersion
    GeneratedAt = [DateTimeOffset]::UtcNow.ToString("o")
    Files = $allEntries
}

$jsonText = $filesJson | ConvertTo-Json -Depth 10
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

$filesJsonPath = Join-Path $OutputDir "files.json"
[System.IO.File]::WriteAllText($filesJsonPath, $jsonText, $utf8NoBom)

$versionedFilesJsonPath = Join-Path $OutputDir ("files-{0}.json" -f $CurrentVersion)
Copy-Item -LiteralPath $filesJsonPath -Destination $versionedFilesJsonPath -Force

$updateSizeBytes = (Get-Item -LiteralPath $updateZipPath).Length
$updateSizeMb = [Math]::Round($updateSizeBytes / 1MB, 2)

Write-Host ""
Write-Host "Done."
Write-Host "update.zip size: $updateSizeMb MB"
Write-Host "Generated:"
Write-Host "  $updateZipPath"
Write-Host "  $filesJsonPath"
Write-Host "  $deltaZipPath"
Write-Host "  $versionedFilesJsonPath"

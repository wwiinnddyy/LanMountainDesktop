[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDir,

    [Parameter(Mandatory = $true)]
    [string]$RuntimeIdentifier,

    [switch]$KeepSymbols,

    [switch]$AssertClean
)

$ErrorActionPreference = "Stop"

function Format-Size {
    param([long]$Bytes)

    if ($Bytes -ge 1GB) {
        return "{0:N2} GB" -f ($Bytes / 1GB)
    }

    if ($Bytes -ge 1MB) {
        return "{0:N2} MB" -f ($Bytes / 1MB)
    }

    return "{0:N2} KB" -f ($Bytes / 1KB)
}

function Get-DirectorySize {
    param([Parameter(Mandatory = $true)][string]$Path)

    $sum = (Get-ChildItem -LiteralPath $Path -Recurse -File -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum).Sum
    if ($null -eq $sum) {
        return 0
    }

    return [long]$sum
}

function Get-RelativePathCompat {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $rootPath = [System.IO.Path]::GetFullPath($Root)
    if (-not $rootPath.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $rootPath += [System.IO.Path]::DirectorySeparatorChar
    }

    $targetPath = [System.IO.Path]::GetFullPath($Path)
    $rootUri = [System.Uri]::new($rootPath)
    $targetUri = [System.Uri]::new($targetPath)
    return [System.Uri]::UnescapeDataString($rootUri.MakeRelativeUri($targetUri).ToString()).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
}

function Write-PayloadAudit {
    param([Parameter(Mandatory = $true)][string]$Root)

    $files = @(Get-ChildItem -LiteralPath $Root -Recurse -File -ErrorAction SilentlyContinue)
    $totalBytes = ($files | Measure-Object -Property Length -Sum).Sum
    if ($null -eq $totalBytes) {
        $totalBytes = 0
    }

    Write-Host "Publish payload audit"
    Write-Host "  Root: $Root"
    Write-Host "  Files: $($files.Count)"
    Write-Host "  Total: $(Format-Size -Bytes $totalBytes)"

    Write-Host "Largest files:"
    $files |
        Sort-Object Length -Descending |
        Select-Object -First 30 |
        ForEach-Object {
            $relative = Get-RelativePathCompat -Root $Root -Path $_.FullName
            Write-Host ("  {0,10}  {1}" -f (Format-Size -Bytes $_.Length), $relative)
        }

    Write-Host "By extension:"
    $extensionGroups = @($files | Group-Object Extension)
    $extensionRows = foreach ($group in $extensionGroups) {
        $bytes = ($group.Group | Measure-Object -Property Length -Sum).Sum
        if ($null -eq $bytes) {
            $bytes = 0
        }

        [PSCustomObject]@{
            Extension = if ([string]::IsNullOrWhiteSpace($group.Name)) { "<none>" } else { $group.Name }
            Count = $group.Count
            Bytes = [long]$bytes
        }
    }

    foreach ($row in @($extensionRows | Sort-Object Bytes -Descending)) {
        Write-Host ("  {0,10}  {1,5}  {2}" -f (Format-Size -Bytes $row.Bytes), $row.Count, $row.Extension)
    }

    $runtimeRoots = @(Get-ChildItem -LiteralPath $Root -Recurse -Directory -Filter "runtimes" -ErrorAction SilentlyContinue)
    if ($runtimeRoots.Count -gt 0) {
        Write-Host "Runtime directories:"
        foreach ($runtimeRoot in $runtimeRoots) {
            Get-ChildItem -LiteralPath $runtimeRoot.FullName -Directory -ErrorAction SilentlyContinue |
                Sort-Object Name |
                ForEach-Object {
                    $relative = Get-RelativePathCompat -Root $Root -Path $_.FullName
                    Write-Host ("  {0,10}  {1}" -f (Format-Size -Bytes (Get-DirectorySize -Path $_.FullName)), $relative)
                }
        }
    }
}

function Remove-PdbFiles {
    param([Parameter(Mandatory = $true)][string]$Root)

    if ($KeepSymbols) {
        Write-Host "Keeping PDB files because -KeepSymbols was specified."
        return
    }

    $pdbFiles = @(Get-ChildItem -LiteralPath $Root -Recurse -File -Filter "*.pdb" -ErrorAction SilentlyContinue)
    foreach ($file in $pdbFiles) {
        Remove-Item -LiteralPath $file.FullName -Force -ErrorAction Stop
    }

    Write-Host "Removed PDB files: $($pdbFiles.Count)"
}

function Remove-NonTargetRuntimeDirectories {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Rid
    )

    if ($Rid -notlike "win-*") {
        return
    }

    $runtimeRoots = @(Get-ChildItem -LiteralPath $Root -Recurse -Directory -Filter "runtimes" -ErrorAction SilentlyContinue)
    $removed = 0
    foreach ($runtimeRoot in $runtimeRoots) {
        Get-ChildItem -LiteralPath $runtimeRoot.FullName -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -ne $Rid } |
            ForEach-Object {
                Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction Stop
                $removed++
            }
    }

    Write-Host "Removed non-target runtime directories: $removed"
}

function Assert-WindowsPayloadClean {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Rid
    )

    if ($Rid -notlike "win-*") {
        return
    }

    $violations = [System.Collections.Generic.List[string]]::new()
    $forbiddenExtensions = @(".pdb", ".so", ".dylib", ".a")

    Get-ChildItem -LiteralPath $Root -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $forbiddenExtensions -contains $_.Extension.ToLowerInvariant() } |
        ForEach-Object {
            $violations.Add((Get-RelativePathCompat -Root $Root -Path $_.FullName))
        }

    Get-ChildItem -LiteralPath $Root -Recurse -Directory -Filter "runtimes" -ErrorAction SilentlyContinue |
        ForEach-Object {
            Get-ChildItem -LiteralPath $_.FullName -Directory -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -ne $Rid } |
                ForEach-Object {
                    $violations.Add((Get-RelativePathCompat -Root $Root -Path $_.FullName))
                }
        }

    if ($violations.Count -gt 0) {
        $sample = ($violations | Select-Object -First 50) -join [Environment]::NewLine
        throw "Windows publish payload contains forbidden files or runtime directories for ${Rid}:$([Environment]::NewLine)$sample"
    }

    Write-Host "Windows payload guard passed for $Rid."
}

$resolvedPublishDir = [System.IO.Path]::GetFullPath($PublishDir)
if (-not (Test-Path -LiteralPath $resolvedPublishDir)) {
    throw "Publish directory not found: $resolvedPublishDir"
}

Write-Host "Optimizing publish payload for $RuntimeIdentifier..."
Remove-PdbFiles -Root $resolvedPublishDir
Remove-NonTargetRuntimeDirectories -Root $resolvedPublishDir -Rid $RuntimeIdentifier
Write-PayloadAudit -Root $resolvedPublishDir

if ($AssertClean) {
    Assert-WindowsPayloadClean -Root $resolvedPublishDir -Rid $RuntimeIdentifier
}

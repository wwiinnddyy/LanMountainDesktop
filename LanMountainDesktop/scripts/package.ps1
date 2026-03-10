[CmdletBinding()]
param(
    [string]$Project = "LanMountainDesktop.csproj",
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$Version = "",
    [string]$PublishDir = "",
    [string]$InstallerOutputDir = "",
    [string]$ArchiveOutputDir = "",
    [string]$InnoScript = "",
    [string]$InnoCompiler = "",
    [switch]$SkipInstaller,
    [switch]$SkipArchive,
    [switch]$KeepSymbols
)

$ErrorActionPreference = "Stop"

function Resolve-ExistingPath {
    param([Parameter(Mandatory = $true)][string]$PathValue)
    $resolved = Resolve-Path -LiteralPath $PathValue -ErrorAction Stop
    return $resolved.Path
}

function Is-WindowsRuntimeIdentifier {
    param([Parameter(Mandatory = $true)][string]$Rid)
    return $Rid -like "win-*"
}

function Find-InnoCompiler {
    param([string]$ExplicitPath = "")

    if ($ExplicitPath) {
        if (Test-Path -LiteralPath $ExplicitPath) {
            return (Resolve-ExistingPath -PathValue $ExplicitPath)
        }
        throw "Inno compiler not found at explicit path: $ExplicitPath"
    }

    $fromPath = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($fromPath -and (Test-Path -LiteralPath $fromPath.Source)) {
        return (Resolve-ExistingPath -PathValue $fromPath.Source)
    }

    $candidates = @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return (Resolve-ExistingPath -PathValue $candidate)
        }
    }

    throw "ISCC.exe not found. Install Inno Setup 6 or pass -InnoCompiler."
}

function Read-VersionFromProject {
    param([Parameter(Mandatory = $true)][string]$ProjectFile)

    [xml]$xml = Get-Content -LiteralPath $ProjectFile -Raw
    $versionNode = $xml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
    if ($versionNode) {
        return $versionNode.Trim()
    }
    return "0.1.0"
}

function Remove-LibVlcForOtherArch {
    param(
        [Parameter(Mandatory = $true)][string]$PublishedDirectory,
        [Parameter(Mandatory = $true)][string]$Rid
    )

    $libVlcRoot = Join-Path $PublishedDirectory "libvlc"
    if (-not (Test-Path -LiteralPath $libVlcRoot)) {
        return
    }

    $dirsToDelete = @()
    if ($Rid -eq "win-x64") {
        $dirsToDelete += (Join-Path $libVlcRoot "win-x86")
    } elseif ($Rid -eq "win-x86") {
        $dirsToDelete += (Join-Path $libVlcRoot "win-x64")
    } elseif (-not (Is-WindowsRuntimeIdentifier -Rid $Rid)) {
        $dirsToDelete += (Join-Path $libVlcRoot "win-x64")
        $dirsToDelete += (Join-Path $libVlcRoot "win-x86")
    }

    foreach ($dir in $dirsToDelete) {
        if (-not (Test-Path -LiteralPath $dir)) {
            continue
        }

        $pruned = $false
        try {
            [System.IO.Directory]::Delete($dir, $true)
            $pruned = $true
        } catch {
            if (-not (Test-Path -LiteralPath $dir)) {
                $pruned = $true
            } else {
                Write-Warning "Prune retry for '$dir': $($_.Exception.Message)"
                try {
                    Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction Stop
                    $pruned = $true
                } catch {
                    if (-not (Test-Path -LiteralPath $dir)) {
                        $pruned = $true
                    } else {
                        throw "Failed to prune '$dir': $($_.Exception.Message)"
                    }
                }
            }
        }

        if ($pruned) {
            Write-Host "Pruned: $dir"
        }
    }
}

function Create-PackageArchive {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDirectory,
        [Parameter(Mandatory = $true)][string]$DestinationDirectory,
        [Parameter(Mandatory = $true)][string]$VersionValue,
        [Parameter(Mandatory = $true)][string]$Rid
    )

    [System.IO.Directory]::CreateDirectory($DestinationDirectory) | Out-Null

    $archiveName = "LanMountainDesktop-$VersionValue-$Rid.zip"
    $archivePath = Join-Path $DestinationDirectory $archiveName
    if (Test-Path -LiteralPath $archivePath) {
        [System.IO.File]::Delete($archivePath)
    }

    Compress-Archive -Path (Join-Path $SourceDirectory "*") -DestinationPath $archivePath -Force
    return $archivePath
}

function Clear-DirectoryContents {
    param([Parameter(Mandatory = $true)][string]$TargetDirectory)

    [System.IO.Directory]::CreateDirectory($TargetDirectory) | Out-Null
    Get-ChildItem -LiteralPath $TargetDirectory -Force -ErrorAction SilentlyContinue | ForEach-Object {
        Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction Stop
    }
}

function Remove-LegacyOutputArtifacts {
    param([Parameter(Mandatory = $true)][string]$TargetDirectory)

    $legacyArtifacts = @(
        "LanMontainDesktop.exe",
        "LanMontainDesktop.dll",
        "LanMontainDesktop.deps.json",
        "LanMontainDesktop.runtimeconfig.json",
        "LanMontainDesktop.pdb"
    )

    foreach ($artifactName in $legacyArtifacts) {
        $artifactPath = Join-Path $TargetDirectory $artifactName
        if (-not (Test-Path -LiteralPath $artifactPath)) {
            continue
        }

        Remove-Item -LiteralPath $artifactPath -Force -ErrorAction Stop
        Write-Host "Removed legacy artifact: $artifactPath"
    }
}

function Add-LinuxDesktopAssets {
    param(
        [Parameter(Mandatory = $true)][string]$PublishedDirectory,
        [Parameter(Mandatory = $true)][string]$RepoRoot
    )

    $resourcesRoot = Join-Path $RepoRoot "packaging/linux"
    $desktopTemplate = Join-Path $resourcesRoot "LanMountainDesktop.desktop"
    $iconSource = Join-Path $resourcesRoot "lanmountaindesktop.png"
    $installScriptSource = Join-Path $resourcesRoot "install.sh"

    foreach ($requiredPath in @($desktopTemplate, $iconSource, $installScriptSource)) {
        if (-not (Test-Path -LiteralPath $requiredPath)) {
            throw "Linux packaging resource is missing: $requiredPath"
        }
    }

    $applicationsDir = Join-Path $PublishedDirectory "share/applications"
    $iconsDir = Join-Path $PublishedDirectory "share/icons/hicolor/256x256/apps"
    [System.IO.Directory]::CreateDirectory($applicationsDir) | Out-Null
    [System.IO.Directory]::CreateDirectory($iconsDir) | Out-Null

    Copy-Item -LiteralPath $desktopTemplate -Destination (Join-Path $applicationsDir "LanMountainDesktop.desktop") -Force
    Copy-Item -LiteralPath $iconSource -Destination (Join-Path $iconsDir "lanmountaindesktop.png") -Force
    Copy-Item -LiteralPath $installScriptSource -Destination (Join-Path $PublishedDirectory "install.sh") -Force
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-ExistingPath -PathValue (Join-Path $scriptRoot "..")

$projectPath = $Project
if (-not [System.IO.Path]::IsPathRooted($projectPath)) {
    $projectPath = Join-Path $repoRoot $projectPath
}
$projectPath = Resolve-ExistingPath -PathValue $projectPath

if (-not $Version) {
    $Version = Read-VersionFromProject -ProjectFile $projectPath
}

if (-not $PublishDir) {
    $PublishDir = Join-Path $repoRoot "artifacts/publish/$RuntimeIdentifier"
}
if (-not [System.IO.Path]::IsPathRooted($PublishDir)) {
    $PublishDir = Join-Path $repoRoot $PublishDir
}
Clear-DirectoryContents -TargetDirectory $PublishDir

Write-Host "Publishing project..."
$publishArgs = @(
    "publish",
    $projectPath,
    "-c", $Configuration,
    "-r", $RuntimeIdentifier,
    "--self-contained", "true",
    "-p:PublishSingleFile=false",
    "-p:PublishTrimmed=false",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-p:Version=$Version",
    "-o", $PublishDir
)

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Remove-LibVlcForOtherArch -PublishedDirectory $PublishDir -Rid $RuntimeIdentifier
Remove-LegacyOutputArtifacts -TargetDirectory $PublishDir

if ($RuntimeIdentifier -like "linux-*") {
    Add-LinuxDesktopAssets -PublishedDirectory $PublishDir -RepoRoot $repoRoot
}

if (-not $KeepSymbols) {
    Get-ChildItem -Path $PublishDir -Recurse -File -Filter "*.pdb" | ForEach-Object {
        [System.IO.File]::Delete($_.FullName)
    }
}

if (Is-WindowsRuntimeIdentifier -Rid $RuntimeIdentifier) {
    if (-not $InstallerOutputDir) {
        $InstallerOutputDir = Join-Path $repoRoot "artifacts/installer"
    }
    if (-not [System.IO.Path]::IsPathRooted($InstallerOutputDir)) {
        $InstallerOutputDir = Join-Path $repoRoot $InstallerOutputDir
    }
    [System.IO.Directory]::CreateDirectory($InstallerOutputDir) | Out-Null

    if ($SkipInstaller) {
        Write-Host "Publish completed. Installer step skipped."
        Write-Host "Published files: $PublishDir"
        exit 0
    }

    if (-not $InnoScript) {
        $InnoScript = Join-Path $repoRoot "installer/LanMountainDesktop.iss"
    }
    if (-not [System.IO.Path]::IsPathRooted($InnoScript)) {
        $InnoScript = Join-Path $repoRoot $InnoScript
    }
    $InnoScript = Resolve-ExistingPath -PathValue $InnoScript

    $archForInstaller = "x64"
    if ($RuntimeIdentifier -like "*x86*") {
        $archForInstaller = "x86"
    }

    $isccPath = Find-InnoCompiler -ExplicitPath $InnoCompiler

    Write-Host "Building installer..."
    $isccArgs = @(
        "/DMyAppVersion=$Version",
        "/DPublishDir=$PublishDir",
        "/DMyOutputDir=$InstallerOutputDir",
        "/DMyAppArch=$archForInstaller",
        $InnoScript
    )

    & $isccPath @isccArgs
    if ($LASTEXITCODE -ne 0) {
        throw "ISCC failed with exit code $LASTEXITCODE."
    }

    $installer = Get-ChildItem -Path $InstallerOutputDir -File -Filter "*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -ne $installer) {
        Write-Host "Installer created: $($installer.FullName)"
    } else {
        Write-Host "Installer build finished, but no .exe was found in $InstallerOutputDir"
    }

    exit 0
}

if ($SkipArchive) {
    Write-Host "Publish completed. Archive step skipped."
    Write-Host "Published files: $PublishDir"
    exit 0
}

if (-not $ArchiveOutputDir) {
    $ArchiveOutputDir = Join-Path $repoRoot "artifacts/packages"
}
if (-not [System.IO.Path]::IsPathRooted($ArchiveOutputDir)) {
    $ArchiveOutputDir = Join-Path $repoRoot $ArchiveOutputDir
}

$archivePath = Create-PackageArchive `
    -SourceDirectory $PublishDir `
    -DestinationDirectory $ArchiveOutputDir `
    -VersionValue $Version `
    -Rid $RuntimeIdentifier

Write-Host "Archive created: $archivePath"

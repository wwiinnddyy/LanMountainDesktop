# Generate-DeltaPackage.ps1
# 生成增量更新包 (delta.zip + files.json)

param(
    [Parameter(Mandatory=$true)]
    [string]$PreviousVersion,
    
    [Parameter(Mandatory=$true)]
    [string]$CurrentVersion,
    
    [Parameter(Mandatory=$true)]
    [string]$PreviousDir,
    
    [Parameter(Mandatory=$true)]
    [string]$CurrentDir,
    
    [Parameter(Mandatory=$true)]
    [string]$OutputDir
)

$ErrorActionPreference = "Stop"

Write-Host "=== 生成增量更新包 ===" -ForegroundColor Cyan
Write-Host "从版本: $PreviousVersion"
Write-Host "到版本: $CurrentVersion"
Write-Host "上一版本目录: $PreviousDir"
Write-Host "当前版本目录: $CurrentDir"
Write-Host "输出目录: $OutputDir"
Write-Host ""

# 确保输出目录存在
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

# 计算文件 SHA256
function Get-FileSha256 {
    param([string]$Path)
    $hash = Get-FileHash -Path $Path -Algorithm SHA256
    return $hash.Hash.ToLower()
}

# 获取目录中所有文件的相对路径和哈希
function Get-FileManifest {
    param([string]$RootDir)
    
    $manifest = @{}
    $files = Get-ChildItem -Path $RootDir -Recurse -File
    
    foreach ($file in $files) {
        $relativePath = $file.FullName.Substring($RootDir.Length).TrimStart('\', '/')
        $relativePath = $relativePath.Replace('\', '/')
        
        $manifest[$relativePath] = @{
            Path = $relativePath
            Sha256 = Get-FileSha256 -Path $file.FullName
            Size = $file.Length
        }
    }
    
    return $manifest
}

Write-Host "扫描上一版本文件..." -ForegroundColor Yellow
$previousManifest = Get-FileManifest -RootDir $PreviousDir

Write-Host "扫描当前版本文件..." -ForegroundColor Yellow
$currentManifest = Get-FileManifest -RootDir $CurrentDir

# 分析文件变更
$changedFiles = @()
$reusedFiles = @()
$deletedFiles = @()

Write-Host "分析文件变更..." -ForegroundColor Yellow

# 检查新增和修改的文件
foreach ($path in $currentManifest.Keys) {
    $currentFile = $currentManifest[$path]
    
    if ($previousManifest.ContainsKey($path)) {
        $previousFile = $previousManifest[$path]
        
        if ($currentFile.Sha256 -eq $previousFile.Sha256) {
            # 文件未变更,可以复用
            $reusedFiles += @{
                Path = $path
                Action = "reuse"
                Sha256 = $currentFile.Sha256
                Size = $currentFile.Size
            }
        } else {
            # 文件已修改
            $changedFiles += @{
                Path = $path
                Action = "replace"
                Sha256 = $currentFile.Sha256
                Size = $currentFile.Size
                ArchivePath = $path
            }
        }
    } else {
        # 新增文件
        $changedFiles += @{
            Path = $path
            Action = "add"
            Sha256 = $currentFile.Sha256
            Size = $currentFile.Size
            ArchivePath = $path
        }
    }
}

# 检查删除的文件
foreach ($path in $previousManifest.Keys) {
    if (-not $currentManifest.ContainsKey($path)) {
        $deletedFiles += @{
            Path = $path
            Action = "delete"
        }
    }
}

Write-Host "变更统计:" -ForegroundColor Green
Write-Host "  新增/修改: $($changedFiles.Count) 个文件"
Write-Host "  复用: $($reusedFiles.Count) 个文件"
Write-Host "  删除: $($deletedFiles.Count) 个文件"
Write-Host ""

# 创建临时目录用于打包
$tempDir = Join-Path $OutputDir "temp_delta"
if (Test-Path $tempDir) {
    Remove-Item -Path $tempDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $tempDir | Out-Null

# 复制变更的文件到临时目录
Write-Host "复制变更文件..." -ForegroundColor Yellow
foreach ($file in $changedFiles) {
    $sourcePath = Join-Path $CurrentDir $file.Path
    $destPath = Join-Path $tempDir $file.Path
    $destDir = Split-Path -Parent $destPath
    
    if (-not (Test-Path $destDir)) {
        New-Item -ItemType Directory -Force -Path $destDir | Out-Null
    }
    
    Copy-Item -Path $sourcePath -Destination $destPath -Force
}

# 创建 delta.zip
$deltaZipPath = Join-Path $OutputDir "delta-$PreviousVersion-to-$CurrentVersion.zip"
Write-Host "创建增量包: $deltaZipPath" -ForegroundColor Yellow

if (Test-Path $deltaZipPath) {
    Remove-Item -Path $deltaZipPath -Force
}

Compress-Archive -Path "$tempDir\*" -DestinationPath $deltaZipPath -CompressionLevel Optimal

# 清理临时目录
Remove-Item -Path $tempDir -Recurse -Force

# 生成 files.json
$filesJson = @{
    FromVersion = $PreviousVersion
    ToVersion = $CurrentVersion
    GeneratedAt = (Get-Date).ToUniversalTime().ToString("o")
    Files = @($changedFiles + $reusedFiles + $deletedFiles)
}

$filesJsonPath = Join-Path $OutputDir "files-$CurrentVersion.json"
Write-Host "生成文件清单: $filesJsonPath" -ForegroundColor Yellow

$filesJson | ConvertTo-Json -Depth 10 | Set-Content -Path $filesJsonPath -Encoding UTF8

# 计算增量包大小
$deltaSize = (Get-Item $deltaZipPath).Length
$deltaSizeMB = [math]::Round($deltaSize / 1MB, 2)

Write-Host ""
Write-Host "=== 完成 ===" -ForegroundColor Green
Write-Host "增量包大小: $deltaSizeMB MB"
Write-Host "输出文件:"
Write-Host "  - $deltaZipPath"
Write-Host "  - $filesJsonPath"

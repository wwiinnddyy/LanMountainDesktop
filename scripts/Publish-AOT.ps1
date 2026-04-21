# Launcher AOT 单文件发布脚本
param(
    [Parameter(Mandatory=$false)]
    [string]$Configuration = "Release",
    
    [Parameter(Mandatory=$false)]
    [string]$RuntimeIdentifier = "win-x64",
    
    [Parameter(Mandatory=$false)]
    [string]$OutputDir = "",
    
    [Parameter(Mandatory=$false)]
    [switch]$SelfContained = $true,
    
    [Parameter(Mandatory=$false)]
    [switch]$SingleFile = $true,
    
    [Parameter(Mandatory=$false)]
    [switch]$Compress = $true
)

$ErrorActionPreference = "Stop"

# 设置默认输出目录
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = "..\publish\aot\$RuntimeIdentifier"
}

$projectPath = "..\LanMountainDesktop.Launcher\LanMountainDesktop.Launcher.csproj"
$absoluteOutputDir = Resolve-Path $OutputDir -ErrorAction SilentlyContinue
if (-not $absoluteOutputDir) {
    $absoluteOutputDir = Join-Path (Get-Location) $OutputDir
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Launcher AOT 单文件发布" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "配置信息:" -ForegroundColor Yellow
Write-Host "  项目: $projectPath"
Write-Host "  配置: $Configuration"
Write-Host "  运行时: $RuntimeIdentifier"
Write-Host "  输出目录: $absoluteOutputDir"
Write-Host "  自包含: $SelfContained"
Write-Host "  单文件: $SingleFile"
Write-Host "  压缩: $Compress"
Write-Host ""

# 清理输出目录
if (Test-Path $absoluteOutputDir) {
    Write-Host "清理旧输出目录..." -ForegroundColor Yellow
    Remove-Item -Path $absoluteOutputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $absoluteOutputDir -Force | Out-Null

# 构建发布参数
$publishArgs = @(
    "publish",
    $projectPath,
    "-c", $Configuration,
    "-r", $RuntimeIdentifier,
    "-o", $absoluteOutputDir,
    "-p:PublishAot=true",
    "-p:PublishTrimmed=true",
    "-p:TrimMode=partial"
)

if ($SelfContained) {
    $publishArgs += "--self-contained"
}

if ($SingleFile) {
    $publishArgs += "-p:PublishSingleFile=true"
    $publishArgs += "-p:IncludeNativeLibrariesForSelfExtract=true"
}

if ($Compress) {
    $publishArgs += "-p:EnableCompressionInSingleFile=true"
}

Write-Host "开始发布..." -ForegroundColor Green
Write-Host "命令: dotnet $([string]::Join(' ', $publishArgs))" -ForegroundColor Gray
Write-Host ""

try {
    & dotnet @publishArgs
    
    if ($LASTEXITCODE -ne 0) {
        throw "发布失败，退出代码: $LASTEXITCODE"
    }
    
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "  发布成功!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host ""
    
    # 显示输出文件
    $outputFiles = Get-ChildItem -Path $absoluteOutputDir -File
    Write-Host "输出文件:" -ForegroundColor Yellow
    foreach ($file in $outputFiles) {
        $size = if ($file.Length -gt 1MB) { 
            "{0:N2} MB" -f ($file.Length / 1MB) 
        } else { 
            "{0:N2} KB" -f ($file.Length / 1KB) 
        }
        Write-Host "  $($file.Name) - $size"
    }
    
    # 验证单文件
    $exeFile = Get-ChildItem -Path $absoluteOutputDir -Filter "*.exe" | Select-Object -First 1
    if ($exeFile) {
        Write-Host ""
        Write-Host "可执行文件: $($exeFile.FullName)" -ForegroundColor Green
        
        # 检查是否为单文件
        if ($SingleFile -and $outputFiles.Count -eq 1) {
            Write-Host "✓ 单文件发布成功！" -ForegroundColor Green
        } elseif ($SingleFile) {
            Write-Host "⚠ 警告: 发现 $($outputFiles.Count) 个文件，可能不是完全的单文件" -ForegroundColor Yellow
        }
    }
    
    Write-Host ""
    Write-Host "使用说明:" -ForegroundColor Cyan
    Write-Host "  1. 将 $($exeFile.Name) 复制到目标机器"
    Write-Host "  2. 确保目录结构包含 app-* 文件夹"
    Write-Host "  3. 直接运行即可，无需安装 .NET Runtime"
    
} catch {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "  发布失败!" -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "错误: $_" -ForegroundColor Red
    exit 1
}

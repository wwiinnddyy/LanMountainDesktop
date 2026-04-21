# 开发环境设置脚本
# 创建模拟的生产目录结构，方便测试 Launcher

param(
    [string]$Configuration = "Debug",
    [string]$Version = "1.0.0-dev"
)

$ErrorActionPreference = "Stop"

# 获取项目根目录
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$LauncherOutput = Join-Path $ProjectRoot "LanMountainDesktop.Launcher\bin\$Configuration\net10.0"
$MainAppOutput = Join-Path $ProjectRoot "LanMountainDesktop\bin\$Configuration\net10.0"
$DevRoot = Join-Path $ProjectRoot "dev-test"

Write-Host "Setting up development environment..." -ForegroundColor Cyan
Write-Host "Project Root: $ProjectRoot"
Write-Host "Launcher Output: $LauncherOutput"
Write-Host "Main App Output: $MainAppOutput"
Write-Host "Dev Root: $DevRoot"
Write-Host ""

# 检查主程序是否已构建
if (-not (Test-Path (Join-Path $MainAppOutput "LanMountainDesktop.exe"))) {
    Write-Host "Main application not found. Building..." -ForegroundColor Yellow
    dotnet build "$ProjectRoot\LanMountainDesktop.slnx" -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed!"
        exit 1
    }
}

# 清理旧的开发环境
if (Test-Path $DevRoot) {
    Write-Host "Cleaning old dev environment..." -ForegroundColor Yellow
    Remove-Item -Path $DevRoot -Recurse -Force
}

# 创建目录结构
$AppDir = Join-Path $DevRoot "app-$Version"
New-Item -ItemType Directory -Path $AppDir -Force | Out-Null

# 复制主程序到 app-{version} 目录
Write-Host "Copying main application to app-$Version..." -ForegroundColor Green
Copy-Item -Path "$MainAppOutput\*" -Destination $AppDir -Recurse -Force

# 创建 .current 标记文件
New-Item -ItemType File -Path (Join-Path $AppDir ".current") -Force | Out-Null

# 复制 Launcher
Write-Host "Copying Launcher..." -ForegroundColor Green
Copy-Item -Path "$LauncherOutput\LanMountainDesktop.Launcher.exe" -Destination (Join-Path $DevRoot "LanMountainDesktop.exe") -Force

# 复制 Launcher 依赖
$LauncherDeps = Get-ChildItem -Path $LauncherOutput -Filter "*.dll" -File
foreach ($dep in $LauncherDeps) {
    Copy-Item -Path $dep.FullName -Destination $DevRoot -Force
}

# 复制 Avalonia 主题文件
$ThemeFiles = Get-ChildItem -Path $LauncherOutput -Filter "*.xaml" -File
foreach ($theme in $ThemeFiles) {
    Copy-Item -Path $theme.FullName -Destination $DevRoot -Force
}

Write-Host ""
Write-Host "Development environment setup complete!" -ForegroundColor Green
Write-Host "Run the Launcher from: $DevRoot\LanMountainDesktop.exe" -ForegroundColor Cyan
Write-Host ""
Write-Host "Directory structure:" -ForegroundColor Gray
Get-ChildItem -Path $DevRoot | Format-Table Name, @{Label="Type"; Expression={if($_.PSIsContainer){"Directory"}else{"File"}}}

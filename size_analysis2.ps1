$ErrorActionPreference = 'Continue'
$base = 'd:\github\LanMountainDesktop\LanMountainDesktop\bin\Debug\net10.0'

Write-Output "=== CATEGORY BREAKDOWN ==="

$categories = @(
    @{ Name = "SkiaSharp native (all platforms)"; Pattern = "runtimes\*\native\libSkiaSharp.*" },
    @{ Name = "SkiaSharp PDB (all platforms)"; Pattern = "runtimes\*\native\libSkiaSharp.pdb" },
    @{ Name = "HarfBuzzSharp native (all platforms)"; Pattern = "runtimes\*\native\libHarfBuzzSharp.*" },
    @{ Name = "HarfBuzzSharp PDB (all platforms)"; Pattern = "runtimes\*\native\libHarfBuzzSharp.pdb" },
    @{ Name = "SQLite native (all platforms)"; Pattern = "runtimes\*\native\*sqlite3*" },
    @{ Name = "WebView2 native"; Pattern = "runtimes\*\native\*WebView2*" },
    @{ Name = "Avalonia DLLs"; Pattern = "Avalonia*.dll" },
    @{ Name = "FluentAvalonia DLLs"; Pattern = "Fluent*.dll" },
    @{ Name = "Material DLLs"; Pattern = "Material*.dll" },
    @{ Name = "Sentry DLLs"; Pattern = "Sentry*.dll" },
    @{ Name = "PostHog DLLs"; Pattern = "PostHog*.dll" },
    @{ Name = "Microsoft.Extensions DLLs"; Pattern = "Microsoft.Extensions*.dll" },
    @{ Name = "Microsoft.Data.Sqlite DLLs"; Pattern = "Microsoft.Data*.dll" },
    @{ Name = "MudTools DLLs"; Pattern = "MudTools*.dll" },
    @{ Name = "PortAudioSharp DLLs"; Pattern = "PortAudio*.dll" },
    @{ Name = "Harmony DLLs"; Pattern = "*Harmony*.dll" },
    @{ Name = "InkCanvas DLLs"; Pattern = "*InkCanvas*.dll" },
    @{ Name = "InkCore DLLs"; Pattern = "*InkCore*.dll" },
    @{ Name = "dotnetCampus DLLs"; Pattern = "dotnetCampus*.dll" },
    @{ Name = "ClassIsland DLLs"; Pattern = "ClassIsland*.dll" },
    @{ Name = "App DLLs (LanMountainDesktop)"; Pattern = "LanMountainDesktop*.dll" }
)

foreach ($cat in $categories) {
    $files = Get-ChildItem $base -Recurse -File | Where-Object { $_.Name -like $cat.Pattern -or $_.FullName -like "*\$($cat.Pattern)" }
    if (-not $files) {
        $files = Get-ChildItem $base -Recurse -File | Where-Object { $_.FullName -like "*$($cat.Pattern)*" }
    }
    if ($files) {
        $totalMB = [math]::Round(($files | Measure-Object -Property Length -Sum).Sum/1MB, 2)
        Write-Output "$($cat.Name): $totalMB MB ($($files.Count) files)"
    }
}

Write-Output ""
Write-Output "=== RUNTIME RID SUBFOLDERS ==="
Get-ChildItem "$base\runtimes" -Directory | ForEach-Object {
    $sizeMB = [math]::Round((Get-ChildItem $_.FullName -Recurse -File | Measure-Object -Property Length -Sum).Sum/1MB, 2)
    Write-Output "$sizeMB MB  $($_.Name)"
}

Write-Output ""
Write-Output "=== AIRAPPHOST RUNTIME RID SUBFOLDERS ==="
$airBase = "$base\AirAppHost\runtimes"
if (Test-Path $airBase) {
    Get-ChildItem $airBase -Directory | ForEach-Object {
        $sizeMB = [math]::Round((Get-ChildItem $_.FullName -Recurse -File | Measure-Object -Property Length -Sum).Sum/1MB, 2)
        Write-Output "$sizeMB MB  $($_.Name)"
    }
}

Write-Output ""
Write-Output "=== TOP-LEVEL DLLs (not in runtimes/) ==="
Get-ChildItem $base -File -Filter "*.dll" | Sort-Object Length -Descending | Select-Object -First 30 | ForEach-Object {
    $sizeKB = [math]::Round($_.Length/1KB, 1)
    Write-Output "$sizeKB KB  $($_.Name)"
}

Write-Output ""
Write-Output "=== TOTAL SIZE BY EXTENSION ==="
Get-ChildItem $base -Recurse -File | Group-Object Extension | Sort-Object Count -Descending | ForEach-Object {
    $totalMB = [math]::Round(($_.Group | Measure-Object -Property Length -Sum).Sum/1MB, 2)
    Write-Output "$totalMB MB  $($_.Count) files  $($_.Name)"
}

Write-Output ""
Write-Output "=== AirAppHost duplicate check ==="
$airHostPath = "$base\AirAppHost"
if (Test-Path $airHostPath) {
    $airHostMB = [math]::Round((Get-ChildItem $airHostPath -Recurse -File | Measure-Object -Property Length -Sum).Sum/1MB, 2)
    Write-Output "AirAppHost folder total: $airHostMB MB"
    
    $duplicateFiles = Get-ChildItem $airHostPath -Recurse -File | Where-Object {
        $originalPath = Join-Path $base $_.FullName.Substring($airHostPath.Length+1)
        (Test-Path $originalPath) -and ((Get-Item $originalPath).Length -eq $_.Length)
    }
    $dupMB = [math]::Round(($duplicateFiles | Measure-Object -Property Length -Sum).Sum/1MB, 2)
    Write-Output "Duplicate files (same name+size as main output): $dupMB MB ($($duplicateFiles.Count) files)"
}

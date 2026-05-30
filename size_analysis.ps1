$ErrorActionPreference = 'Continue'

Write-Output "=== FONT FILES ==="
Get-ChildItem 'd:\github\LanMountainDesktop\LanMountainDesktop\Assets\Fonts' -File | Sort-Object Length -Descending | ForEach-Object {
    $sizeKB = [math]::Round($_.Length/1KB, 1)
    Write-Output "$sizeKB KB  $($_.Name)"
}
$totalFontMB = [math]::Round((Get-ChildItem 'd:\github\LanMountainDesktop\LanMountainDesktop\Assets\Fonts' -File | Measure-Object -Property Length -Sum).Sum/1MB, 2)
Write-Output "TOTAL FONTS: $totalFontMB MB"

Write-Output ""
Write-Output "=== ROOT ASSET FILES ==="
Get-ChildItem 'd:\github\LanMountainDesktop\LanMountainDesktop\Assets' -File | Sort-Object Length -Descending | ForEach-Object {
    $sizeKB = [math]::Round($_.Length/1KB, 1)
    Write-Output "$sizeKB KB  $($_.Name)"
}

Write-Output ""
Write-Output "=== MATERIAL WEATHER ICONS ==="
$weatherStats = Get-ChildItem 'd:\github\LanMountainDesktop\LanMountainDesktop\Assets\MaterialWeatherIcons' -Recurse -File | Measure-Object -Property Length -Sum
$weatherMB = [math]::Round($weatherStats.Sum/1MB, 2)
Write-Output "$weatherMB MB total, $($weatherStats.Count) files"

Write-Output ""
Write-Output "=== BUILD OUTPUT (Debug) ==="
$debugPath = 'd:\github\LanMountainDesktop\LanMountainDesktop\bin\Debug'
if (Test-Path $debugPath) {
    $debugStats = Get-ChildItem $debugPath -Recurse -File | Measure-Object -Property Length -Sum
    $debugMB = [math]::Round($debugStats.Sum/1MB, 2)
    Write-Output "$debugMB MB total, $($debugStats.Count) files"
    
    $tfmPath = Get-ChildItem $debugPath -Directory | Select-Object -First 1
    if ($tfmPath) {
        Write-Output ""
        Write-Output "=== TOP 50 LARGEST FILES IN BUILD OUTPUT ==="
        Get-ChildItem $debugPath -Recurse -File | Sort-Object Length -Descending | Select-Object -First 50 | ForEach-Object {
            $sizeKB = [math]::Round($_.Length/1KB, 1)
            $rel = $_.FullName.Substring($debugPath.Length+1)
            Write-Output "$sizeKB KB  $rel"
        }
    }
} else {
    Write-Output "Debug output not found"
}

Write-Output ""
Write-Output "=== BUILD OUTPUT (Release) ==="
$releasePath = 'd:\github\LanMountainDesktop\LanMountainDesktop\bin\Release'
if (Test-Path $releasePath) {
    $releaseStats = Get-ChildItem $releasePath -Recurse -File | Measure-Object -Property Length -Sum
    $releaseMB = [math]::Round($releaseStats.Sum/1MB, 2)
    Write-Output "$releaseMB MB total, $($releaseStats.Count) files"
} else {
    Write-Output "Release output not found"
}

Write-Output ""
Write-Output "=== NUGET CACHE - LARGEST PACKAGES ==="
$nugetPath = 'd:\github\LanMountainDesktop\LanMountainDesktop\obj\project.assets.json'
if (Test-Path $nugetPath) {
    $assets = Get-Content $nugetPath -Raw | ConvertFrom-Json
    $libs = $assets.Libraries
    $libSizes = @()
    foreach ($prop in $libs.PSObject.Properties) {
        $libSizes += [PSCustomObject]@{
            Name = $prop.Name
            Type = $prop.Value.type
        }
    }
    $libSizes | Where-Object { $_.Type -eq 'package' } | Select-Object -First 30 Name | ForEach-Object { Write-Output $_.Name }
}

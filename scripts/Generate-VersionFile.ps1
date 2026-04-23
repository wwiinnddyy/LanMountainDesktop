param(
    [Parameter(Mandatory=$true)]
    [string]$OutputPath,

    [Parameter(Mandatory=$true)]
    [string]$Version,

    [Parameter(Mandatory=$false)]
    [string]$Codename = "Administrate"
)

$ErrorActionPreference = "Stop"

function Normalize-ArgumentValue {
    param(
        [Parameter(Mandatory=$true)]
        [AllowEmptyString()]
        [string]$Value
    )

    $trimmed = $Value.Trim()
    if ($trimmed.Length -ge 2) {
        $first = $trimmed[0]
        $last = $trimmed[$trimmed.Length - 1]
        if (($first -eq "'" -and $last -eq "'") -or ($first -eq '"' -and $last -eq '"')) {
            return $trimmed.Substring(1, $trimmed.Length - 2).Trim()
        }
    }

    return $trimmed
}

$OutputPath = Normalize-ArgumentValue -Value $OutputPath
$Version = Normalize-ArgumentValue -Value $Version
$Codename = Normalize-ArgumentValue -Value $Codename

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    throw "OutputPath is required."
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Version is required."
}

$versionInfo = @{
    Version = $Version
    Codename = $Codename
}

$json = $versionInfo | ConvertTo-Json -Compress
$dir = Split-Path -Parent $OutputPath

if ([string]::IsNullOrWhiteSpace($dir)) {
    throw "OutputPath must include a directory: $OutputPath"
}

if (!(Test-Path -LiteralPath $dir)) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}

Set-Content -LiteralPath $OutputPath -Value $json -Encoding UTF8
Write-Host "Generated version file: $OutputPath" -ForegroundColor Green
Write-Host "  Version: $Version" -ForegroundColor Gray
Write-Host "  Codename: $Codename" -ForegroundColor Gray

param(
    [Parameter(Mandatory = $true)]
    [string]$FilesJsonPath,

    [Parameter(Mandatory = $true)]
    [string]$PrivateKeyPath,

    [Parameter(Mandatory = $false)]
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = "$FilesJsonPath.sig"
}

$toolProject = Join-Path $PSScriptRoot "..\PenguinLogisticsOnlineNetworkDistributionSystem\src\Plonds.Tool\Plonds.Tool.csproj"
if (-not (Test-Path -LiteralPath $toolProject)) {
    throw "PLONDS tool project not found: $toolProject"
}

& dotnet run --project $toolProject -- sign --manifest $FilesJsonPath --private-key $PrivateKeyPath --output $OutputPath
if ($LASTEXITCODE -ne 0) {
    throw "PLONDS sign command failed."
}

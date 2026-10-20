param()

$ErrorActionPreference = 'Stop'
$updateAuditRepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$updateAuditProbeDir = Join-Path $updateAuditRepoRoot 'artifacts/update-chain-audit/probe'
$updateAuditEvidenceDir = Join-Path $updateAuditRepoRoot 'artifacts/update-chain-audit/evidence'
New-Item -ItemType Directory -Path $updateAuditProbeDir -Force | Out-Null
New-Item -ItemType Directory -Path $updateAuditEvidenceDir -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'audit-fixtures/update-chain-probe.cs.txt') -Destination (Join-Path $updateAuditProbeDir 'Program.cs') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'audit-fixtures/update-chain-probe.csproj.txt') -Destination (Join-Path $updateAuditProbeDir 'UpdateAuditProbe.csproj') -Force

# Each scenario uses synthetic HTTP responses, in-memory preferences, and new temporary directories.
# Production project references are built so the probe always runs against the current source.
$updateAuditProbeLog = Join-Path $updateAuditRepoRoot 'artifacts/update-chain-audit/probe.log'
$updateAuditProbeResult = Join-Path $updateAuditEvidenceDir 'local-probes.json'
& dotnet run --project (Join-Path $updateAuditProbeDir 'UpdateAuditProbe.csproj') -c Debug -p:SkipAirAppHostBuild=true -- $updateAuditProbeResult *> $updateAuditProbeLog
$updateAuditProbeExit = $LASTEXITCODE
if ($updateAuditProbeExit -ne 0) {
    Get-Content -LiteralPath $updateAuditProbeLog -Tail 45
    throw "Update audit probe failed with exit code $updateAuditProbeExit."
}
Get-Content -LiteralPath $updateAuditProbeResult -Raw

param(
    [Parameter(Mandatory = $true)]
    [string] $SourcePath,

    [Parameter(Mandatory = $true)]
    [string] $DestinationPath
)

$ErrorActionPreference = 'Stop'

$source = Get-Item -LiteralPath $SourcePath
$destinationDirectory = Split-Path -Parent $DestinationPath
New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null

$existing = Get-Item -LiteralPath $DestinationPath -ErrorAction SilentlyContinue
if ($existing -and $existing.LastWriteTimeUtc -ge $source.LastWriteTimeUtc -and $existing.Length -gt 0) {
    return
}

$temporaryPath = "$DestinationPath.$PID.tmp"
if (Test-Path -LiteralPath $temporaryPath) {
    Remove-Item -LiteralPath $temporaryPath -Force
}

$inputStream = [System.IO.File]::OpenRead($source.FullName)
try {
    $outputStream = [System.IO.File]::Create($temporaryPath)
    try {
        $gzipStream = New-Object System.IO.Compression.GZipStream($outputStream, [System.IO.Compression.CompressionMode]::Compress)
        try {
            $inputStream.CopyTo($gzipStream)
        }
        finally {
            $gzipStream.Dispose()
        }
    }
    finally {
        $outputStream.Dispose()
    }
}
finally {
    $inputStream.Dispose()
}

Move-Item -LiteralPath $temporaryPath -Destination $DestinationPath -Force

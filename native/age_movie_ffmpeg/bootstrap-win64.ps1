param(
    [string]$Destination
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($Destination)) {
    $Destination = Join-Path $repoRoot 'build\ffmpeg-sdk'
}
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'dependency-win64.json') -Raw | ConvertFrom-Json
$downloadDir = Join-Path $repoRoot 'build\downloads'
$archivePath = Join-Path $downloadDir $manifest.archive
$partialArchivePath = "$archivePath.part"
$extractRoot = Join-Path $Destination ([IO.Path]::GetFileNameWithoutExtension($manifest.archive))
New-Item -ItemType Directory -Force -Path $downloadDir | Out-Null

if (-not (Test-Path -LiteralPath $archivePath)) {
    try {
        Invoke-WebRequest -Uri $manifest.url -OutFile $partialArchivePath
        Move-Item -LiteralPath $partialArchivePath -Destination $archivePath
    }
    finally {
        if (Test-Path -LiteralPath $partialArchivePath) {
            Remove-Item -LiteralPath $partialArchivePath -Force
        }
    }
}
$actualSize = (Get-Item -LiteralPath $archivePath).Length
if ($actualSize -ne $manifest.size) {
    throw "FFmpeg archive size mismatch: expected $($manifest.size), got $actualSize"
}
$actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archivePath).Hash.ToLowerInvariant()
if ($actualHash -ne $manifest.sha256) {
    throw "FFmpeg archive SHA-256 mismatch: expected $($manifest.sha256), got $actualHash"
}
if (-not (Test-Path -LiteralPath $extractRoot)) {
    Expand-Archive -LiteralPath $archivePath -DestinationPath $Destination
}
$sdkRoot = $extractRoot
if (-not (Test-Path -LiteralPath (Join-Path $sdkRoot 'include\libavformat\avformat.h'))) {
    throw "Pinned FFmpeg SDK was not found at $sdkRoot"
}
$reported = & (Join-Path $sdkRoot 'bin\ffmpeg.exe') -version | Select-Object -First 1
$expectedVersion = $manifest.ffmpeg_version -replace '-\d{8}$', ''
if ($reported -notlike "*$expectedVersion*" ) {
    throw "Unexpected FFmpeg build: $reported"
}
Write-Output $sdkRoot

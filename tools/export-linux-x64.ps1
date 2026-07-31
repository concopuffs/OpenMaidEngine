param(
    [Parameter(Mandatory = $true)]
    [string]$GodotConsole
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectRoot = Join-Path $repoRoot 'godot'
$godotConsolePath = (Resolve-Path -LiteralPath $GodotConsole).Path
$outputDirectory = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'build\export\linux-x64'))
$expectedOutputDirectory = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'build\export\linux-x64'))
$executable = Join-Path $outputDirectory 'Himegari.x86_64'

if (-not (Test-Path -LiteralPath $godotConsolePath -PathType Leaf)) {
    throw "Godot console executable was not found: $godotConsolePath"
}
foreach ($required in @(
    (Join-Path $projectRoot 'Himegari.sln'),
    (Join-Path $projectRoot 'export_presets.cfg'),
    (Join-Path $repoRoot 'build\native\linux-x64\libage_movie_ffmpeg.so')
)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Linux export prerequisite was not found: $required"
    }
}

if (-not $outputDirectory.Equals($expectedOutputDirectory, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean unexpected export directory: $outputDirectory"
}
if (Test-Path -LiteralPath $outputDirectory) {
    Remove-Item -LiteralPath $outputDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $outputDirectory | Out-Null

& $godotConsolePath --headless --quit-after 120 --path $projectRoot --export-release 'Linux x86_64' $executable
if ($LASTEXITCODE -ne 0) {
    throw "Godot Linux x64 export failed with exit code $LASTEXITCODE. Install the Godot 4.7 .NET export templates and retry."
}

$managedDirectory = Join-Path $outputDirectory 'data_Himegari_linuxbsd_x86_64'
foreach ($required in @(
    $executable,
    (Join-Path $outputDirectory 'Himegari.pck'),
    (Join-Path $managedDirectory 'Himegari.dll'),
    (Join-Path $managedDirectory 'Age.Engine.dll'),
    (Join-Path $managedDirectory 'libage_movie_ffmpeg.so'),
    (Join-Path $managedDirectory 'libavformat.so.62'),
    (Join-Path $managedDirectory 'libavcodec.so.62'),
    (Join-Path $managedDirectory 'libavutil.so.60'),
    (Join-Path $managedDirectory 'libswscale.so.9'),
    (Join-Path $managedDirectory 'libswresample.so.6'),
    (Join-Path $managedDirectory 'FFmpeg-LICENSE.txt')
)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Linux export is incomplete; expected artifact was not found: $required"
    }
}
foreach ($forbidden in @(
    (Join-Path $managedDirectory 'Age.Engine.Text.Windows.dll'),
    (Join-Path $managedDirectory 'age_movie_ffmpeg.dll'),
    (Join-Path $managedDirectory 'avformat-62.dll')
)) {
    if (Test-Path -LiteralPath $forbidden) {
        throw "Linux export contains a Windows-only artifact: $forbidden"
    }
}

Write-Output "Linux x64 export: $outputDirectory"

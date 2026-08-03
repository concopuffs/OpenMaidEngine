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

py -3.11 -X utf8 (Join-Path $repoRoot 'tools\package_linux_x64.py') verify $outputDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Linux export payload verification failed with exit code $LASTEXITCODE."
}

Write-Output "Linux x64 export: $outputDirectory"

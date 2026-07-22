param(
    [Parameter(Mandatory = $true)]
    [string]$SdkRoot,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$SdkRoot = (Resolve-Path $SdkRoot).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot 'build\native\win-x64'
}
$include = Join-Path $SdkRoot 'include'
$lib = Join-Path $SdkRoot 'lib'
$bin = Join-Path $SdkRoot 'bin'
foreach ($required in @(
    (Join-Path $include 'libavformat\avformat.h'),
    (Join-Path $lib 'avformat.lib'),
    (Join-Path $bin 'avformat-62.dll')
)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Missing FFmpeg SDK file: $required" }
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw "Visual Studio Installer vswhere.exe was not found" }
$vsInstall = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vsInstall) { throw "A Visual C++ x64 build environment was not found" }
$vcvars = Join-Path $vsInstall 'VC\Auxiliary\Build\vcvars64.bat'
if (-not (Test-Path -LiteralPath $vcvars)) { throw "Visual C++ x64 environment not found: $vcvars" }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$source = Join-Path $PSScriptRoot 'age_movie.c'
$object = Join-Path $OutputDirectory 'age_movie.obj'
$dll = Join-Path $OutputDirectory 'age_movie_ffmpeg.dll'
$importLibrary = Join-Path $OutputDirectory 'age_movie_ffmpeg.lib'
$compile = 'call "{0}" && cl.exe /nologo /std:c11 /utf-8 /O2 /MD /W4 /external:I"{1}" /external:W0 /LD /Fo"{2}" "{3}" /link /OUT:"{4}" /IMPLIB:"{5}" /LIBPATH:"{6}" avformat.lib avcodec.lib avutil.lib swscale.lib' -f
    $vcvars, $include, $object, $source, $dll, $importLibrary, $lib
& cmd.exe /d /s /c $compile
if ($LASTEXITCODE -ne 0) { throw "age_movie_ffmpeg compilation failed with exit code $LASTEXITCODE" }
foreach ($runtime in @('avformat-62.dll', 'avcodec-62.dll', 'avutil-60.dll', 'swscale-9.dll', 'swresample-6.dll')) {
    Copy-Item -LiteralPath (Join-Path $bin $runtime) -Destination $OutputDirectory -Force
}
Copy-Item -LiteralPath (Join-Path $SdkRoot 'LICENSE.txt') -Destination (Join-Path $OutputDirectory 'FFmpeg-LICENSE.txt') -Force
Write-Output $dll

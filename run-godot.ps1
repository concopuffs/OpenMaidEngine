# run-godot.ps1 -- build the C# and launch the OME Godot project against an AGE game root.
#
#   .\run-godot.ps1            build, then run the persistent SYSTEM4 root windowed
#   .\run-godot.ps1 -SelfTest  build, then headless self-test (asserts the dialogue trace vs vm0)
#   .\run-godot.ps1 -Import    also (re)import the project first (needed after adding assets)
#   .\run-godot.ps1 -NoBuild   skip the dotnet build, just launch
#   .\run-godot.ps1 -NativeDebugMenu  expose TITLE's unreachable shipped developer menu
#   .\run-godot.ps1 -StartupDiagnostics  capture the natural TITLE -> Game Start -> SC0000 route
#   .\run-godot.ps1 -PerfLog   write a timestamped frame/compositor CSV under build/perf
#   .\run-godot.ps1 -SoftwareRenderer  use the retained software correctness oracle
#   .\run-godot.ps1 -Kamidori  launch the conventional ../Kamidori install with its profile
#   .\run-godot.ps1 -Kamidori -TranslationPatch  enable its patch/ English overlay and BMP assets
#   .\run-godot.ps1 -OverlayRoot <dir> -AllowBmpAsAgf  select generic asset compatibility options
#   .\run-godot.ps1 -Profile kamidori -GameRoot <install>  explicitly select a game profile
#   .\run-godot.ps1 -Profile himegari -Probe -GameRoot <install>  force a read-only diagnostic run
#   .\run-godot.ps1 -Doctor     resolve and print prerequisites without building or launching
#
# Uses the *console* Godot build so GD.Print / DRAW logs / the selftest result show in the terminal.
# Resolution order is explicit parameter, AGE_* environment variable, then a portable fallback.
param(
    [string]$GodotConsole,
    [string]$GameRoot,
    [string]$Profile,
    [switch]$Kamidori,
    [string[]]$OverlayRoot,
    [switch]$AllowBmpAsAgf,
    [switch]$TranslationPatch,
    [switch]$Probe,
    [switch]$SelfTest,
    [switch]$Import,
    [switch]$NoBuild,
    [switch]$NativeDebugMenu,
    [switch]$StartupDiagnostics,
    [switch]$PerfLog,
    [switch]$SoftwareRenderer,
    [switch]$Doctor
)

$ErrorActionPreference = 'Stop'
$repo    = $PSScriptRoot                          # age-reimpl/
$project = Join-Path $repo 'godot'
$csproj  = Join-Path $project 'OME.csproj'

function Resolve-ConfiguredFile {
    param(
        [string]$ExplicitValue,
        [string]$EnvironmentValue,
        [string[]]$PathCommands,
        [string]$Description
    )

    $configured = if ($ExplicitValue) { $ExplicitValue } elseif ($EnvironmentValue) { $EnvironmentValue } else { $null }
    if ($configured) {
        if (-not (Test-Path -LiteralPath $configured -PathType Leaf)) {
            throw "$Description not found: $configured"
        }
        return (Resolve-Path -LiteralPath $configured).Path
    }

    foreach ($commandName in $PathCommands) {
        $command = Get-Command $commandName -CommandType Application -ErrorAction SilentlyContinue |
                   Select-Object -First 1
        if ($command) { return $command.Source }
    }

    throw "$Description not found. Pass -GodotConsole, set AGE_GODOT_CONSOLE, or add Godot to PATH."
}

function Resolve-ConfiguredGameRoot {
    param(
        [string]$ExplicitValue,
        [string]$EnvironmentValue,
        [string]$ConventionalValue
    )

    $configured = if ($ExplicitValue) {
        $ExplicitValue
    } elseif ($EnvironmentValue) {
        $EnvironmentValue
    } else {
        $ConventionalValue
    }
    if (-not (Test-Path -LiteralPath $configured -PathType Container)) {
        throw "Game root not found: $configured. Pass -GameRoot or set AGE_GAME_ROOT."
    }
    $resolved = (Resolve-Path -LiteralPath $configured).Path
    if (-not (Test-Path -LiteralPath (Join-Path $resolved 'SYS4INI.BIN') -PathType Leaf)) {
        throw "Game root does not contain SYS4INI.BIN: $resolved"
    }
    return $resolved
}

$godotArguments = @{
    ExplicitValue = $GodotConsole
    EnvironmentValue = $env:AGE_GODOT_CONSOLE
    PathCommands = @('godot4', 'godot', 'godot-mono')
    Description = 'Godot .NET console executable'
}
$godot = Resolve-ConfiguredFile @godotArguments
if ($Kamidori) {
    if ($Profile -and $Profile -ne 'kamidori') {
        throw "-Kamidori conflicts with -Profile $Profile."
    }
    $Profile = 'kamidori'
    if (-not $PSBoundParameters.ContainsKey('GameRoot')) {
        $GameRoot = Join-Path (Split-Path $repo -Parent) 'Kamidori'
    }
}
$conventionalInstallName = if ($Profile -eq 'kamidori') { 'Kamidori' } else { 'Himegari_Game' }
$gameRootArguments = @{
    ExplicitValue = $GameRoot
    EnvironmentValue = $env:AGE_GAME_ROOT
    ConventionalValue = Join-Path (Split-Path $repo -Parent) $conventionalInstallName
}
$resolvedGameRoot = Resolve-ConfiguredGameRoot @gameRootArguments
$requestedOverlayRoots = @()
if ($TranslationPatch) {
    $requestedOverlayRoots += 'patch'
    $AllowBmpAsAgf = $true
}
if ($OverlayRoot) { $requestedOverlayRoots += $OverlayRoot }
$resolvedOverlayRoots = @()
foreach ($requestedOverlayRoot in $requestedOverlayRoots) {
    $candidate = if ([IO.Path]::IsPathRooted($requestedOverlayRoot)) {
        $requestedOverlayRoot
    } else {
        Join-Path $resolvedGameRoot $requestedOverlayRoot
    }
    if (-not (Test-Path -LiteralPath $candidate -PathType Container)) {
        throw "Overlay root not found: $candidate"
    }
    $resolvedOverlayRoot = (Resolve-Path -LiteralPath $candidate).Path
    if ($resolvedOverlayRoots -notcontains $resolvedOverlayRoot) {
        $resolvedOverlayRoots += $resolvedOverlayRoot
    }
}
$assetArguments = @()
foreach ($resolvedOverlayRoot in $resolvedOverlayRoots) {
    $assetArguments += @('--overlay-root', $resolvedOverlayRoot)
}
if ($AllowBmpAsAgf) { $assetArguments += '--allow-bmp-as-agf' }
if ($Probe -and -not $Profile) {
    throw '-Probe requires -Profile so the runtime knows which engine ABI to use.'
}
$profileArguments = @()
if ($Profile) { $profileArguments += @('--profile', $Profile) }
if ($Probe) { $profileArguments += '--probe' }

if ($Doctor) {
    $dotnet = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue |
              Select-Object -First 1
    $python = Get-Command py -CommandType Application -ErrorAction SilentlyContinue |
              Select-Object -First 1
    if (-not $dotnet) { throw '.NET SDK not found on PATH.' }
    if (-not $python) { throw 'Python launcher py.exe not found on PATH.' }
    Write-Host "Repository : $repo"
    Write-Host "Godot      : $godot"
    Write-Host "Game root  : $resolvedGameRoot"
    Write-Host "Profile    : $(if ($Profile) { $Profile } else { '<automatic>' })"
    Write-Host "Probe      : $Probe"
    Write-Host "Overlays   : $(if ($resolvedOverlayRoots.Count) { $resolvedOverlayRoots -join ' -> ' } else { '<none>' })"
    Write-Host "BMP as AGF : $AllowBmpAsAgf"
    Write-Host "dotnet     : $($dotnet.Source)"
    Write-Host "Python     : $($python.Source)"
    exit 0
}

if ($SelfTest -and $StartupDiagnostics) {
    throw '-SelfTest and -StartupDiagnostics are mutually exclusive.'
}

$diagnosticFiles = @()
if ($StartupDiagnostics) {
    $diagnosticDir = Join-Path $repo 'build\validation\title-newgame'
    New-Item -ItemType Directory -Force -Path $diagnosticDir | Out-Null
    $diagnosticFiles = @(
        (Join-Path $diagnosticDir 'godot.log'),
        (Join-Path $diagnosticDir 'timeline.jsonl'),
        (Join-Path $diagnosticDir 'histogram.txt'),
        (Join-Path $diagnosticDir 'page-map.jsonl')
    )
}

$perfLogFile = $null
if ($PerfLog -and -not $SelfTest) {
    $perfLogDir = Join-Path $repo 'build\perf'
    New-Item -ItemType Directory -Force -Path $perfLogDir | Out-Null
    $perfLogFile = Join-Path $perfLogDir ("run-{0}.csv" -f (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
}

if (-not $NoBuild) {
    Write-Host "==> dotnet build $csproj" -ForegroundColor Cyan
    dotnet build $csproj -c Debug --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "build failed" }
}

if ($Import) {
    Write-Host "==> importing project" -ForegroundColor Cyan
    & $godot --headless --path $project --import
}

if ($SelfTest) {
    Write-Host "==> headless self-test" -ForegroundColor Cyan
    $userArgs = @('--selftest', '--game-root', $resolvedGameRoot) + $profileArguments + $assetArguments
    & $godot --headless --path $project -- @userArgs
} else {
    # The selected profile owns its natural boot script (currently SYSTEM4 for both built-ins).
    # Do not add --boot or SC0000 seeds here: those belong only to explicit direct-scene diagnostics.
    Write-Host "==> launching windowed from SYSTEM4" -ForegroundColor Cyan
    $userArgs = @('--game-root', $resolvedGameRoot) + $profileArguments + $assetArguments
    if ($perfLogFile) {
        Write-Host "==> performance log: $perfLogFile" -ForegroundColor Cyan
        $userArgs += @('--perf-log', $perfLogFile)
    }
    if ($SoftwareRenderer) { $userArgs += @('--render-backend', 'software') }
    if ($NativeDebugMenu -and -not $StartupDiagnostics) { $userArgs += '--native-debug-menu' }
    if ($StartupDiagnostics) {
        Write-Host "==> startup diagnostics enabled (native exit semantics)" -ForegroundColor Cyan
        $userArgs += @(
            '--timeline-log', $diagnosticFiles[1],
            '--trace-histogram', $diagnosticFiles[2],
            '--page-map', $diagnosticFiles[3]
        )
        & $godot --log-file $diagnosticFiles[0] --path $project -- @userArgs
    } else {
        & $godot --path $project -- @userArgs
    }
}

$godotExitCode = $LASTEXITCODE
if ($StartupDiagnostics) {
    Write-Host "==> startup diagnostic artifacts" -ForegroundColor Cyan
    foreach ($file in $diagnosticFiles) {
        if (Test-Path -LiteralPath $file) {
            $length = (Get-Item -LiteralPath $file).Length
            Write-Host ("    {0} ({1:N0} bytes)" -f $file, $length)
        } else {
            Write-Warning "diagnostic artifact was not written: $file"
        }
    }
}
if ($godotExitCode -ne 0) { exit $godotExitCode }

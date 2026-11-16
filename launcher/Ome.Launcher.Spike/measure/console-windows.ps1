# M0 question 5 (docs/superpowers/plans/2026-11-15-launcher.md §2.2): what a windowed program's `--check` output
# looks like from cmd, PowerShell and a captured .NET process, and whether each shell waits for it. Every case runs in
# its own new, minimized console; console-dump.ps1 then reads that console's text. "EXIT=" is what the shell saw:
# 1 when it waited for the program, something else when it did not. Output: build/launcher-m0/console/*.txt and
# build/launcher-m0/results-console-windows.json.
param([string]$Spike = '')
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
$m0 = Join-Path $repo 'build\launcher-m0'
if (-not $Spike) { $Spike = Join-Path $m0 'publish\win-x64-folder\Ome.Launcher.Spike.exe' }
$dir = Join-Path $m0 'console'
if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
New-Item -ItemType Directory $dir | Out-Null
$dump = Join-Path $PSScriptRoot 'console-dump.ps1'

function In-NewConsole([string]$name, [string]$program, [string]$arguments) {
    $out = Join-Path $dir "$name.txt"
    Start-Process $program -ArgumentList $arguments -WindowStyle Minimized -Wait
    [ordered]@{ case = $name; console = @(if (Test-Path $out) { Get-Content $out -Encoding UTF8 } else { '(no dump)' }) }
}
function DumpCmd([string]$name) { "powershell -NoProfile -ExecutionPolicy Bypass -File `"$dump`" `"$(Join-Path $dir "$name.txt")`"" }
function DumpPs([string]$name) { "& '$dump' '$(Join-Path $dir "$name.txt")'" }

$cases = @()
# cmd: a command string, start /wait, a batch file. On one line cmd expands %ERRORLEVEL% before running anything, so
# these read the exit status with && and || instead: "EXIT=0" then means the shell saw success without waiting.
$cases += In-NewConsole 'cmd-c' 'cmd.exe' "/c `"(`"$Spike`" --check-like && echo EXIT=0 || echo EXIT=non-zero) & $(DumpCmd 'cmd-c')`""
$cases += In-NewConsole 'cmd-start-wait' 'cmd.exe' "/c `"(start `"`" /wait `"$Spike`" --check-like && echo EXIT=0 || echo EXIT=non-zero) & $(DumpCmd 'cmd-start-wait')`""
$bat = Join-Path $dir 'check.bat'
Set-Content -Encoding ascii $bat "@echo off`r`n`"$Spike`" --check-like`r`necho EXIT=%ERRORLEVEL%`r`n$(DumpCmd 'cmd-batch')`r`n"
$cases += In-NewConsole 'cmd-batch' 'cmd.exe' "/c `"$bat`""
# cmd at its prompt: commands typed one by one, here read from a file by an inner cmd that owns the new console.
$typed = Join-Path $dir 'typed.txt'
Set-Content -Encoding ascii $typed "`"$Spike`" --check-like`r`necho EXIT=%ERRORLEVEL%`r`n$(DumpCmd 'cmd-prompt')`r`nexit`r`n"
$cases += In-NewConsole 'cmd-prompt' 'cmd.exe' "/c `"cmd /q /k < `"$typed`"`""

# PowerShell: plain, Start-Process -Wait (with and without -NoNewWindow), piped.
function PsCase([string]$name, [string]$body) {
    $script = Join-Path $dir "$name.ps1"
    Set-Content -Encoding utf8 $script "$body`r`n$(DumpPs $name)`r`n"
    In-NewConsole $name 'powershell.exe' "-NoProfile -ExecutionPolicy Bypass -File `"$script`""
}
$cases += PsCase 'ps-plain' "& '$Spike' --check-like; `"EXIT=`$LASTEXITCODE`""
$cases += PsCase 'ps-start-process-wait' "`$p = Start-Process '$Spike' -ArgumentList '--check-like' -Wait -PassThru; `"EXIT=`$(`$p.ExitCode)`""
$cases += PsCase 'ps-start-process-nonewwindow' "`$p = Start-Process '$Spike' -ArgumentList '--check-like' -Wait -PassThru -NoNewWindow; `"EXIT=`$(`$p.ExitCode)`""
$cases += PsCase 'ps-piped' "& '$Spike' --check-like | Out-Host; `"EXIT=`$LASTEXITCODE`""

# A captured .NET process (as the real launcher's tests and another program would run it), and --output.
$log = Join-Path $dir 'captured.log'
& $Spike --capture --log $log --timeout 30 -- $Spike --check-like | Out-Null
$cases += [ordered]@{ case = 'captured-dotnet'; console = @(Get-Content $log -Encoding UTF8) }
$file = Join-Path $dir 'output-file.txt'
Start-Process cmd.exe -ArgumentList "/c `"`"$Spike`" --check-like --output `"$file`"`"" -WindowStyle Minimized -Wait
Start-Sleep -Milliseconds 2500
$cases += [ordered]@{ case = 'output-file-from-cmd-c'; console = @(if (Test-Path $file) { Get-Content $file -Encoding UTF8 } else { '(no file)' }) }

$cases | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 (Join-Path $m0 'results-console-windows.json')
foreach ($c in $cases) { "=== $($c.case)"; $c.console | ForEach-Object { "  $_" } }

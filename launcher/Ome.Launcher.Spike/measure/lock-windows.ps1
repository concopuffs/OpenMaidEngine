# M0 question 8 on Windows (docs/superpowers/plans/2026-11-15-launcher.md §2.2): the run lock's exclusive open across
# two processes, with a readiness file and no sleep. Also what else touches a held run.lock: reading its time, reading
# it, copying it. Output: build/launcher-m0/results-lock-windows.json.
param([string]$Spike = '')
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
$m0 = Join-Path $repo 'build\launcher-m0'
if (-not $Spike) { $Spike = Join-Path $m0 'publish\win-x64-folder\Ome.Launcher.Spike.exe' }
$dir = Join-Path $m0 'lock-windows'
if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
New-Item -ItemType Directory $dir | Out-Null
$lock = Join-Path $dir 'run.lock'
$ready = Join-Path $dir 'ready'

function Probe([string]$label) {
    $out = & $Spike --lock $lock | Out-String
    [ordered]@{ step = $label; exit = $LASTEXITCODE; output = $out.Trim() }
}

$steps = @()
$steps += Probe 'first open, nothing holds it'
$holder = Start-Process $Spike -ArgumentList "--lock `"$lock`" --hold-until-killed --ready `"$ready`"" -PassThru `
    -WindowStyle Hidden -RedirectStandardOutput (Join-Path $dir 'holder.out')
$deadline = (Get-Date).AddSeconds(20)
while (-not (Test-Path $ready)) {
    if ((Get-Date) -gt $deadline -or $holder.HasExited) { throw 'the holder did not become ready' }
    Start-Sleep -Milliseconds 20
}
$heldTime = (Get-Item $lock).LastWriteTimeUtc.ToString('HH:mm:ss.fff')
$steps += Probe 'second open while the holder runs'
$other = [ordered]@{ step = 'other access while held'; read_time = $heldTime }
try { [IO.File]::ReadAllBytes($lock) | Out-Null; $other.read = 'ok' } catch { $other.read = $_.Exception.InnerException.Message }
try { Copy-Item $lock (Join-Path $dir 'copy.lock'); $other.copy = 'ok' } catch { $other.copy = $_.Exception.Message }
try {
    $s = [IO.File]::Open($lock, 'Open', 'Read', 'ReadWrite'); $s.Dispose(); $other.open_shared_read = 'ok'
} catch { $other.open_shared_read = $_.Exception.InnerException.Message }
$steps += $other
Stop-Process -Id $holder.Id -Force
$holder.WaitForExit()
$steps += [ordered]@{ step = 'holder killed'; holder_output = (Get-Content (Join-Path $dir 'holder.out') -Raw).Trim(); file_left = (Test-Path $lock) }
$steps += Probe 'open after the holder was killed'
$steps | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 (Join-Path $m0 'results-lock-windows.json')
$steps | ConvertTo-Json -Depth 4

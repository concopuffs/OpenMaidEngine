# M0 questions 1 and 3 on Windows (docs/superpowers/plans/2026-11-15-launcher.md §2.2): publishes the spike in each
# variant, records the folder size, the zip size, where the native libraries are, and the time to the first frame
# over one first run and five more. Output: build/launcher-m0/publish/win-x64-<variant>/ and
# build/launcher-m0/results-windows.json. Needs the .NET 10 SDK (DOTNET_ROOT or %LOCALAPPDATA%\Microsoft\dotnet).
param(
    [string]$Dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe",
    [int]$Runs = 5
)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$project = Split-Path $PSScriptRoot -Parent
$repo = Resolve-Path (Join-Path $project '..\..')
$m0 = Join-Path $repo 'build\launcher-m0'
$publishRoot = Join-Path $m0 'publish'
New-Item -ItemType Directory -Force $publishRoot | Out-Null
$icon = Join-Path $m0 'spike.ico'
& py -3.11 (Join-Path $PSScriptRoot 'make-icon.py') $icon
if ($LASTEXITCODE -ne 0) { throw 'make-icon.py failed' }

$variants = [ordered]@{
    # Question 2's reference: the resources a program built on Windows carries.
    'single-icon'     = @('-p:PublishSingleFile=true', "-p:SpikeIcon=$icon")
    'folder'          = @()
    'single'          = @('-p:PublishSingleFile=true')
    'single-extract'  = @('-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true')
    'single-compress' = @('-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true')
    'single-r2r'      = @('-p:PublishSingleFile=true', '-p:PublishReadyToRun=true')
}

# Where a self-extracting bundle unpacks: the default base and a private one for this measurement.
$extractBase = Join-Path $m0 'extract-base'
$results = [ordered]@{}
Push-Location $project
try {
    foreach ($name in $variants.Keys) {
        $out = Join-Path $publishRoot "win-x64-$name"
        if (Test-Path $out) { Remove-Item -Recurse -Force $out }
        $publishArgs = @('publish', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', $out,
            '-p:DebugType=none', '-p:DebugSymbols=false') + $variants[$name]
        & $Dotnet @publishArgs | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "publish $name failed" }

        $files = Get-ChildItem $out -Recurse -File
        $zip = Join-Path $publishRoot "win-x64-$name.zip"
        if (Test-Path $zip) { Remove-Item -Force $zip }
        Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip -CompressionLevel Optimal
        $natives = $files | Where-Object { $_.Name -match '^(libSkiaSharp|libHarfBuzzSharp|av_libGLESv2)' } |
            ForEach-Object { $_.FullName.Substring($out.Length + 1) }

        $exe = Join-Path $out 'Ome.Launcher.Spike.exe'
        $times = @()
        $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $extractBase
        if (Test-Path $extractBase) { Remove-Item -Recurse -Force $extractBase }
        $modules = Join-Path $m0 "modules-win-x64-$name.txt"
        for ($i = 0; $i -le $Runs; $i++) {
            $ff = Join-Path $m0 "ff-win-x64-$name.txt"
            if (Test-Path $ff) { Remove-Item -Force $ff }
            $p = Start-Process $exe -ArgumentList "--first-frame-file `"$ff`" --modules-file `"$modules`" --exit-after-first-frame" -PassThru -Wait
            if ($p.ExitCode -ne 0) { throw "run $i of $name exited $($p.ExitCode)" }
            $times += [int](Get-Content $ff)
        }
        Remove-Item Env:\DOTNET_BUNDLE_EXTRACT_BASE_DIR
        $extracted = if (Test-Path $extractBase) {
            Get-ChildItem $extractBase -Recurse -File | ForEach-Object { $_.FullName.Substring($extractBase.Length + 1) }
        } else { @() }
        # One more start without the private base: where a player's copy unpacks by default.
        $defaultExtract = @()
        if ($extracted.Count -gt 0) {
            $p = Start-Process $exe -ArgumentList '--exit-after-first-frame' -PassThru -Wait
            $defaultBase = Join-Path $env:TEMP '.net\Ome.Launcher.Spike'
            if (Test-Path $defaultBase) {
                $defaultExtract = @(Get-ChildItem $defaultBase -Recurse -File | ForEach-Object { [string]$_.FullName })
            }
        }
        $warm = $times[1..$Runs] | Sort-Object
        $results[$name] = [ordered]@{
            files            = $files.Count
            folder_bytes     = ($files | Measure-Object Length -Sum).Sum
            exe_bytes        = (Get-Item $exe).Length
            zip_bytes        = (Get-Item $zip).Length
            natives_beside   = @($natives | ForEach-Object { [string]$_ })
            natives_loaded   = @(Get-Content $modules | Where-Object { $_ -match 'libSkiaSharp|libHarfBuzzSharp|av_libGLESv2' } | ForEach-Object { [string]$_ })
            extracted_files  = @($extracted | ForEach-Object { [string]$_ })
            default_extract  = $defaultExtract
            first_run_ms     = $times[0]
            warm_ms          = @($times[1..$Runs])
            warm_median_ms   = $warm[[int][math]::Floor($warm.Count / 2)]
        }
        Write-Host ("{0,-16} files={1,3} folder={2,7:N1} MiB zip={3,6:N1} MiB first={4,5} ms warm-median={5,5} ms" -f $name,
            $files.Count, ($results[$name].folder_bytes / 1MB), ($results[$name].zip_bytes / 1MB), $times[0], $results[$name].warm_median_ms)
    }
} finally {
    Pop-Location
}
$results | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 (Join-Path $m0 'results-windows.json')

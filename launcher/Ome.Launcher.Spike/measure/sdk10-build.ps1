# M0 question 11, second half (docs/superpowers/plans/2026-11-15-launcher.md §2.2): does the repository build under
# the .NET 10 SDK with its net8.0 targets unchanged? Builds engine/OmeEngine.sln and godot/OME.csproj in a detached
# worktree of HEAD, first with the pinned SDK 8 (global.json as committed), then with SDK 10 (global.json swapped in
# the worktree only), and compares errors and warnings. The real working tree is not touched.
# Usage: sdk10-build.ps1 -Worktree <empty folder outside the repository>
param(
    [Parameter(Mandatory)][string]$Worktree,
    [string]$Dotnet10 = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$m0 = Join-Path $repo 'build\launcher-m0'
git -C $repo worktree add --detach $Worktree HEAD | Out-Null
try {
    New-Item -ItemType Directory -Force (Join-Path $Worktree 'build') | Out-Null
    Copy-Item (Join-Path $repo 'build\opcodes.json') (Join-Path $Worktree 'build\opcodes.json')
    $flags = @('--nologo', '--verbosity', 'minimal', '-m:1', '-p:UseSharedCompilation=false', '-p:BuildInParallel=false')
    $results = [ordered]@{}
    foreach ($sdk in 'sdk8', 'sdk10') {
        if ($sdk -eq 'sdk10') {
            Set-Content -Encoding ascii (Join-Path $Worktree 'global.json') '{ "sdk": { "version": "10.0.404", "rollForward": "latestFeature", "allowPrerelease": false } }'
            Get-ChildItem $Worktree -Recurse -Directory -Include bin, obj | Remove-Item -Recurse -Force
            $dotnet = $Dotnet10
        } else {
            $dotnet = 'dotnet'
        }
        foreach ($target in 'engine/OmeEngine.sln', 'godot/OME.csproj') {
            $log = Join-Path $m0 ("build-{0}-{1}.log" -f $sdk, ($target -replace '[/\\.]', '-'))
            Push-Location $Worktree
            try {
                $version = & $dotnet --version
                & $dotnet build $target @flags *> $log
                $exit = $LASTEXITCODE
            } finally { Pop-Location }
            $text = Get-Content $log
            $warnings = $text | Select-String -Pattern 'warning [A-Z]+\d+' | ForEach-Object {
                if ($_.Line -match 'warning ([A-Z]+\d+)') { $Matches[1] } } | Group-Object | Sort-Object Name |
                ForEach-Object { "$($_.Name) x$($_.Count)" }
            $errors = @($text | Select-String -Pattern ' error [A-Z]+\d+' | ForEach-Object { $_.Line.Trim() } | Select-Object -Unique -First 10)
            $results["$sdk $target"] = [ordered]@{ sdk = $version; exit = $exit; warnings = @($warnings); errors = $errors }
            Write-Host ("{0,-6} {1,-22} sdk={2} exit={3} warnings=[{4}] errors={5}" -f $sdk, $target, $version, $exit, ($warnings -join ', '), $errors.Count)
        }
    }
    $results | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 (Join-Path $m0 'results-sdk10-build.json')
} finally {
    git -C $repo worktree remove --force $Worktree
}

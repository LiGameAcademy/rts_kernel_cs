param(
    [Parameter(Mandatory)][string]$Godot,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$probe = Join-Path $PSScriptRoot 'godot_probe'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$destination = (Resolve-Path $OutputDirectory).Path
$oldRollForward = $env:DOTNET_ROLL_FORWARD
$records = @()
try {
    dotnet build (Join-Path $root 'tests/Rts.Kernel.Tests/Rts.Kernel.Tests.csproj') -c Release > "$destination/cli-build.log" 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'CLI build failed' }
    dotnet build (Join-Path $probe 'StationaryCombat.Probe.csproj') -c Debug > "$destination/godot-build.log" 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Godot build failed' }
    $assembly = Join-Path $root 'tests/Rts.Kernel.Tests/bin/Release/net10.0/Rts.Kernel.Tests.dll'
    foreach ($mode in @('cli', 'godot')) {
        foreach ($count in @(24, 300, 500)) {
            $name = "$mode-$count"
            if ($mode -eq 'cli') {
                $file = 'dotnet'
                $arguments = @(('"' + $assembly + '"'), '--combat-profile', $count)
            } else {
                $file = $Godot
                $arguments = @('--headless', '--path', ('"' + $probe + '"'), '--', '--combat-profile', $count)
            }
            $env:DOTNET_ROLL_FORWARD = 'LatestMajor'
            $started = [DateTime]::UtcNow
            $launch = @{
                FilePath = $file; ArgumentList = $arguments; WindowStyle = 'Hidden'; PassThru = $true
                RedirectStandardOutput = "$destination/$name.log"
                RedirectStandardError = "$destination/$name.err"
            }
            $process = Start-Process @launch
            if (!$process.WaitForExit(120000)) {
                # This handle identifies only the helper launched above.
                $process.Kill()
                $process.WaitForExit()
                throw "$name timed out"
            }
            $process.WaitForExit()
            $text = Get-Content "$destination/$name.log" -Raw
            $errors = [string](Get-Content "$destination/$name.err" -Raw)
            $json = @($text -split '\r?\n' | Where-Object { $_.StartsWith('{"scenario":') })
            if ($json.Count -ne 1) { throw "$name did not emit exactly one profile" }
            $result = $json[0] | ConvertFrom-Json
            $valid = $process.ExitCode -eq 0 -and $result.over_budget -eq 0
            if ($mode -eq 'godot') {
                $valid = $valid -and $text.Contains('COMBAT_PROBE_DONE') -and !$text.Contains('COMBAT_PROBE_FAILED') -and $errors -notmatch 'SCRIPT ERROR|ERROR:'
            }
            $records += [pscustomobject]@{
                mode = $mode; count = $count; pid = $process.Id; started_utc = $started
                ended_utc = [DateTime]::UtcNow; exit_code = $process.ExitCode; valid = $valid
                first_ms = $result.first_ms; max_ms = $result.max_ms; p95_ms = $result.p95_ms
                over_budget = $result.over_budget; state_hash = $result.state_trace_sha256
                event_hash = $result.event_trace_sha256
            }
            $records | ConvertTo-Json -Depth 4 | Set-Content "$destination/results.json" -Encoding utf8
        }
    }
    foreach ($count in @(24, 300, 500)) {
        $pair = @($records | Where-Object count -eq $count)
        if ($pair[0].state_hash -ne $pair[1].state_hash -or $pair[0].event_hash -ne $pair[1].event_hash) {
            throw "$count state/event fingerprint mismatch"
        }
    }
    $records | Format-Table mode,count,first_ms,max_ms,p95_ms,over_budget,valid -AutoSize
    if (@($records | Where-Object { !$_.valid }).Count -gt 0) { throw 'Runtime/performance gate failed; retain all samples' }
} finally {
    $env:DOTNET_ROLL_FORWARD = $oldRollForward
}

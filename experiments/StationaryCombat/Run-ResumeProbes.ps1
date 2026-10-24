param(
    [Parameter(Mandatory)][string]$Godot,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$probe = Join-Path $PSScriptRoot 'godot_probe'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$destination = (Resolve-Path $OutputDirectory).Path
if (Test-Path "$destination/results.json") { throw 'Choose a fresh destination; preserve earlier first-run evidence.' }
$frozen = Join-Path $destination 'frozen'
New-Item -ItemType Directory -Force -Path $frozen | Out-Null
$oldRollForward = $env:DOTNET_ROLL_FORWARD
$records = @()

function Invoke-Child([string]$File, [string[]]$Arguments, [string]$Name) {
    $launch = @{
        FilePath = $File; ArgumentList = $Arguments; WindowStyle = 'Hidden'; PassThru = $true
        RedirectStandardOutput = "$destination/$Name.log"
        RedirectStandardError = "$destination/$Name.err"
    }
    $child = Start-Process @launch
    if (!$child.WaitForExit(60000)) {
        $child.Kill()
        $child.WaitForExit()
        throw "$Name timed out"
    }
    $child.WaitForExit()
    return $child
}
try {
    dotnet build (Join-Path $root 'tests/Rts.Kernel.Tests/Rts.Kernel.Tests.csproj') -c Release > "$destination/cli-build.log" 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'CLI build failed' }
    dotnet build (Join-Path $probe 'StationaryCombat.Probe.csproj') -c Debug > "$destination/godot-build.log" 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Godot build failed' }
    $assembly = Join-Path $root 'tests/Rts.Kernel.Tests/bin/Release/net10.0/Rts.Kernel.Tests.dll'
    foreach ($count in @(24, 300, 500)) {
        $child = Invoke-Child 'dotnet' @(('"' + $assembly + '"'), '--combat-create-resume', $count, ('"' + $frozen + '"')) "create-$count"
        if ($child.ExitCode -ne 0) { throw "Fixture $count creation failed" }
    }
    foreach ($mode in @('cli', 'godot')) {
        foreach ($count in @(24, 300, 500)) {
            $name = "$mode-$count"
            $path = Join-Path $frozen "resume-$count.json"
            $env:DOTNET_ROLL_FORWARD = 'LatestMajor'
            if ($mode -eq 'cli') {
                $file = 'dotnet'
                $arguments = @(('"' + $assembly + '"'), '--combat-resume', $count, ('"' + $path + '"'))
            } else {
                $file = $Godot
                $arguments = @('--headless', '--path', ('"' + $probe + '"'), '--', '--combat-resume', $count, ('"' + $path + '"'))
            }
            $started = [DateTime]::UtcNow
            $child = Invoke-Child $file $arguments $name
            $text = [string](Get-Content "$destination/$name.log" -Raw)
            $errors = [string](Get-Content "$destination/$name.err" -Raw)
            $lines = @($text -split '\r?\n' | Where-Object { $_.StartsWith('{"scenario":"stationary_resume"') })
            if ($lines.Count -ne 1) { throw "$name did not emit a resume result" }
            $result = $lines[0] | ConvertFrom-Json
            $valid = $child.ExitCode -eq 0 -and $result.expected_match -and $result.over_budget -eq 0
            if ($mode -eq 'godot') {
                $valid = $valid -and $text.Contains('COMBAT_PROBE_DONE') -and !$text.Contains('COMBAT_PROBE_FAILED') -and $errors -notmatch 'SCRIPT ERROR|ERROR:'
            }
            $records += [pscustomobject]@{
                mode = $mode; count = $count; pid = $child.Id; started_utc = $started; ended_utc = [DateTime]::UtcNow
                exit_code = $child.ExitCode; valid = $valid; restore_ms = $result.restore_ms; first_ms = $result.first_ms
                max_ms = $result.max_ms; p95_ms = $result.p95_ms; over_budget = $result.over_budget
                expected_match = $result.expected_match; state_hash = $result.state_trace_sha256; event_hash = $result.event_trace_sha256
            }
            $records | ConvertTo-Json -Depth 4 | Set-Content "$destination/results.json" -Encoding utf8
        }
    }
    foreach ($count in @(24, 300, 500)) {
        $pair = @($records | Where-Object count -eq $count)
        if ($pair[0].state_hash -ne $pair[1].state_hash -or $pair[0].event_hash -ne $pair[1].event_hash) { throw "$count cross-runtime mismatch" }
    }
    $records | Format-Table mode,count,restore_ms,first_ms,max_ms,over_budget,valid -AutoSize
    if (@($records | Where-Object { !$_.valid }).Count) { throw 'Resume gate failed; all first samples retained.' }
} finally {
    $env:DOTNET_ROLL_FORWARD = $oldRollForward
}

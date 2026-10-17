param(
    [Parameter(Mandatory)][ValidateSet("cli", "godot")][string]$Runtime,
    [Parameter(Mandatory)][ValidateRange(1, 3)][int]$Round,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$GodotPath
)
$ErrorActionPreference = "Stop"
$kernelRoot = (Resolve-Path "$PSScriptRoot/../..").Path
$outputRoot = (New-Item -ItemType Directory -Force -Path $OutputDirectory).FullName
$prefix = Join-Path $outputRoot "$Runtime-trace-$Round"
if (Test-Path "$prefix.log") { throw "Refusing to overwrite prior process evidence: $prefix" }
if ($Runtime -eq "godot" -and !(Test-Path -LiteralPath $GodotPath)) { throw "Pass an existing Godot mono console executable." }
$session = "RtsScheduling" + [guid]::NewGuid().ToString("N")
$status = & wpr -status 2>&1
$status | Set-Content -Encoding utf8 "$prefix-status.txt"
if ($LASTEXITCODE -ne 0 -or ($status -join "`n") -notmatch "WPR is not recording") {
    throw "Existing/unknown WPR recording state; will not start or cancel another recording."
}
$owned = $false
Push-Location $kernelRoot
try {
    & wpr -start CPU.Verbose -instancename $session > "$prefix-wpr-start.txt" 2>&1
    if ($LASTEXITCODE -ne 0) { throw "WPR start failed; see $prefix-wpr-start.txt" }
    $owned = $true
    $started = [DateTime]::UtcNow
    if ($Runtime -eq "cli") {
        & dotnet tests/Rts.Kernel.Tests/bin/Release/net10.0/Rts.Kernel.Tests.dll --prepared-multi-party > "$prefix.log" 2>&1
    } else {
        & $GodotPath --headless --path experiments/MovementProfile/godot_probe -- --prepared-multi-party > "$prefix.log" 2>&1
    }
    $probeExit = $LASTEXITCODE
    & wpr -stop "$prefix.etl" "RTS cold $Runtime command and restore" -skipPdbGen -compress -instancename $session > "$prefix-wpr-stop.txt" 2>&1
    if ($LASTEXITCODE -ne 0) { throw "WPR stop failed; see $prefix-wpr-stop.txt" }
    $owned = $false
    @{ runtime = $Runtime; round = $Round; session = $session; profile = "CPU.Verbose.Memory"
       start_utc = $started.ToString("O"); stop_utc = [DateTime]::UtcNow.ToString("O")
       exit_code = $probeExit; trace = "$prefix.etl"; log = "$prefix.log"
       notes = "New process; no frames removed; trace overhead is diagnostic, wall gate unchanged."
    } | ConvertTo-Json | Set-Content -Encoding utf8 "$prefix-capture.json"
    if ($probeExit -ne 0) { throw "Cold probe failed with exit $probeExit" }
    Write-Output "Captured $prefix"
} finally {
    if ($owned) {
        # Only cancel the uniquely named recording that this invocation successfully started.
        & wpr -cancel -instancename $session > "$prefix-wpr-cleanup.txt" 2>&1
    }
    Pop-Location
}

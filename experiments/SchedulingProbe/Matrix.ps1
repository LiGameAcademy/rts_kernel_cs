param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$ProfilesDirectory,
    [Parameter(Mandatory)][string]$CalibrationResult,
    [string]$DotnetPath = (Get-Command dotnet.exe -ErrorAction Stop).Source
)
$ErrorActionPreference = 'Stop'
$taskKernel = (Resolve-Path "$PSScriptRoot/../..").Path
$taskProbe = Join-Path $taskKernel 'tests/Rts.Kernel.Tests/bin/Release/net10.0/Rts.Kernel.Tests.dll'
$taskProfiles = (Resolve-Path -LiteralPath $ProfilesDirectory).Path
$taskCalibration = Get-Content -Raw -LiteralPath $CalibrationResult | ConvertFrom-Json
if (!$taskCalibration.passed) { throw 'Known-method calibration has not passed.' }
$taskManifest = Get-Content -Raw -LiteralPath "$taskProfiles/profile-verification.json" | ConvertFrom-Json
if (!$taskManifest.otherwise_identical -or $taskManifest.logging_mode -ne 'File') { throw 'Matched file-mode profiles required.' }
foreach ($taskCondition in @('B','C')) {
    if ((Get-FileHash "$taskProfiles/$taskCondition.wprp" -Algorithm SHA256).Hash -ne $taskManifest.profiles.$taskCondition) {
        throw 'Profile fingerprint changed after comparison.'
    }
}
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Refusing to overwrite a matrix run.' }
$taskOutput = (New-Item -ItemType Directory -Path $OutputDirectory).FullName
$taskBinaryPaths = @(Get-ChildItem -LiteralPath (Split-Path $taskProbe) -File | Where-Object Extension -In '.dll','.json' | Sort-Object Name | Select-Object -ExpandProperty FullName)
$taskBinaries = @($taskBinaryPaths | ForEach-Object { @{path=$_;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash} })
$taskRows = [Collections.Generic.List[object]]::new()
$taskMatrixFailure = $null
try {
    $taskOrdinal = 0
    foreach ($taskCondition in @('A','B','C','C','B','A')) {
        $taskOrdinal++
        $taskPrefix = 'cli-{0:D2}-{1}-{2}' -f $taskOrdinal,$taskCondition, $(if($taskCondition -eq 'A'){'control'}else{'trace-1'})
        $taskStem = Join-Path $taskOutput $taskPrefix
        $taskSession = 'RtsMatrix' + [guid]::NewGuid().ToString('N')
        $taskOwned = $false
        $taskExit = $null
        $taskFailure = $null
        $taskStarted = [DateTime]::UtcNow
        try {
            $taskStatus = & wpr -status 2>&1
            $taskStatus | Set-Content -Encoding utf8 "$taskStem-status-before.txt"
            if ($LASTEXITCODE -ne 0 -or ($taskStatus -join "`n") -notmatch 'WPR is not recording') {
                throw 'Existing/unknown WPR session; not starting this process.'
            }
            foreach ($taskBinary in $taskBinaries) {
                if ((Get-FileHash -LiteralPath $taskBinary.path -Algorithm SHA256).Hash -ne $taskBinary.sha256) { throw 'Frozen binary changed.' }
            }
            & wpr -profint > "$taskStem-profile-interval.txt" 2>&1
            if ($LASTEXITCODE -ne 0) { throw 'Cannot record sampling interval.' }
            if ($taskCondition -ne 'A') {
                & wpr -start "$taskProfiles/$taskCondition.wprp!RtsOverhead" -filemode -recordtempto $taskOutput -instancename $taskSession > "$taskStem-start.txt" 2>&1
                if ($LASTEXITCODE -ne 0) { throw 'WPR file recording start failed.' }
                $taskOwned = $true
                & wpr -status collectors -instancename $taskSession > "$taskStem-collectors.txt" 2>&1
                if ($LASTEXITCODE -ne 0) { throw 'Cannot record live collector configuration.' }
            }
            # One invocation per condition, no additional match warmup or retry.
            & $DotnetPath $taskProbe --prepared-multi-party > "$taskStem.log" 2>&1
            $taskExit = $LASTEXITCODE
            if ($taskCondition -ne 'A') {
                & wpr -stop "$taskStem.etl" "RTS six-process matrix $taskOrdinal $taskCondition" -skipPdbGen -compress -instancename $taskSession > "$taskStem-stop.txt" 2>&1
                if ($LASTEXITCODE -ne 0) { throw 'WPR file recording save failed.' }
                $taskOwned = $false
            }
            if ($taskExit -ne 0) { throw "Probe failed: $taskExit" }
        } catch {
            $taskFailure = $_.ToString()
        } finally {
            if ($taskOwned) { & wpr -cancel -instancename $taskSession > "$taskStem-cleanup.txt" 2>&1 }
            $taskRows.Add(@{ordinal=$taskOrdinal;condition=$taskCondition;prefix=$taskPrefix;session=$taskSession;
                started_utc=$taskStarted.ToString('O');stopped_utc=[DateTime]::UtcNow.ToString('O');probe_exit=$taskExit;error=$taskFailure})
            $taskRows | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 "$taskOutput/processes.json"
        }
        if ($null -ne $taskFailure) { throw $taskFailure }
    }
} catch {
    $taskMatrixFailure = $_.ToString()
} finally {
    @{order=@('A','B','C','C','B','A');attempts=$taskRows.Count;error=$taskMatrixFailure;binaries=$taskBinaries;
      calibration_result=(Resolve-Path -LiteralPath $CalibrationResult).Path;profile_verification=$taskManifest;
      admin=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator);
      notes='Six cold processes maximum; no retries, builds, extra warmup, affinity/priority/JIT changes or discarded first frames.'
    } | ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 "$taskOutput/matrix.json"
}
if ($null -ne $taskMatrixFailure) { throw $taskMatrixFailure }
Write-Output 'Six A-B-C-C-B-A cold CLI processes completed; analysis and original wall gate remain required.'

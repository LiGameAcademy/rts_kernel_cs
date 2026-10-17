param(
    [Parameter(Mandatory)][string]$ToolPath,
    [Parameter(Mandatory)][string]$ProfilePath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$DotnetPath = (Get-Command dotnet.exe -ErrorAction Stop).Source
)
$ErrorActionPreference = 'Stop'
$taskTool = (Resolve-Path -LiteralPath $ToolPath).Path
$taskInput = (Resolve-Path -LiteralPath $ProfilePath).Path
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Refusing to overwrite prior calibration evidence.' }
$taskOutput = (New-Item -ItemType Directory -Path $OutputDirectory).FullName
$taskSession = 'RtsMetadata' + [guid]::NewGuid().ToString('N')
$taskOwned = $false
$taskChild = $null
$taskErrorTask = $null
$taskFailure = $null
$taskSaved = $false
$taskStarted = [DateTime]::UtcNow
function Read-FixtureLine([string]$ExpectedPrefix) {
    $taskLine = $taskChild.StandardOutput.ReadLineAsync().WaitAsync([TimeSpan]::FromSeconds(60)).GetAwaiter().GetResult()
    if ($null -eq $taskLine) { throw "Fixture exited before $ExpectedPrefix" }
    Add-Content -LiteralPath "$taskOutput/fixture.log" -Encoding utf8 -Value $taskLine
    if (!$taskLine.StartsWith($ExpectedPrefix, [StringComparison]::Ordinal)) { throw "Unexpected fixture output: $taskLine" }
    return $taskLine.Substring($ExpectedPrefix.Length)
}
try {
    $taskStatus = & wpr -status 2>&1
    $taskStatus | Set-Content -Encoding utf8 "$taskOutput/status-before.txt"
    if ($LASTEXITCODE -ne 0 -or ($taskStatus -join "`n") -notmatch 'WPR is not recording') {
        throw 'Existing/unknown WPR recording state; not touching another recording.'
    }
    [xml]$taskProfile = Get-Content -Raw -LiteralPath $taskInput
    $taskProfileNode = $taskProfile.SelectSingleNode('/WindowsPerformanceRecorder/Profiles/Profile')
    if ($null -eq $taskProfileNode) { throw 'Missing source profile.' }
    $taskProfileNode.SetAttribute('Id', 'RtsMetadata.Verbose.File')
    $taskProfileNode.SetAttribute('Name', 'RtsMetadata')
    $taskProfileNode.SetAttribute('LoggingMode', 'File')
    $taskProfileNode.SetAttribute('Description', 'Single metadata calibration; not an A/B/C performance condition')
    foreach ($taskCollector in $taskProfile.SelectNodes('/WindowsPerformanceRecorder/Profiles/SystemCollector | /WindowsPerformanceRecorder/Profiles/EventCollector')) {
        $taskCollector.SetAttribute('Name', $taskCollector.LocalName + '_' + $taskSession)
    }
    $taskFileProfile = Join-Path $taskOutput 'calibration-file.wprp'
    $taskProfile.Save($taskFileProfile)
    & wpr -profiledetails "$taskFileProfile!RtsMetadata" > "$taskOutput/profile-details.txt" 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'File profile validation failed.' }
    & wpr -start "$taskFileProfile!RtsMetadata" -filemode -recordtempto $taskOutput -instancename $taskSession > "$taskOutput/wpr-start.txt" 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'WPR start failed.' }
    $taskOwned = $true
    $taskInfo = [Diagnostics.ProcessStartInfo]::new($DotnetPath)
    $taskInfo.UseShellExecute = $false
    $taskInfo.CreateNoWindow = $true
    $taskInfo.RedirectStandardInput = $true
    $taskInfo.RedirectStandardOutput = $true
    $taskInfo.RedirectStandardError = $true
    $taskInfo.ArgumentList.Add($taskTool)
    $taskInfo.ArgumentList.Add('--metadata-fixture-handshake')
    $taskChild = [Diagnostics.Process]::new()
    $taskChild.StartInfo = $taskInfo
    if (!$taskChild.Start()) { throw 'Fixture did not start.' }
    $taskErrorTask = $taskChild.StandardError.ReadToEndAsync()
    $taskReady = (Read-FixtureLine 'metadata_ready=') | ConvertFrom-Json
    if ($taskReady.process_id -ne $taskChild.Id) { throw 'Fixture PID does not match the child process.' }
    & $DotnetPath $taskTool --providers $taskChild.Id $taskSession "$taskOutput/providers-before-work.json" > "$taskOutput/providers-before-work.txt" 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Target CLR provider is not registered/enabled for this capture.' }
    $taskChild.StandardInput.WriteLine('run')
    $taskChild.StandardInput.Flush()
    $taskFixture = (Read-FixtureLine 'metadata_fixture=') | ConvertFrom-Json
    if ($taskFixture.process_id -ne $taskChild.Id -or $taskFixture.native_thread_id -ne $taskReady.native_thread_id) {
        throw 'Fixture process/thread identity changed.'
    }
    & $DotnetPath $taskTool --providers $taskChild.Id $taskSession "$taskOutput/providers-after-work.json" > "$taskOutput/providers-after-work.txt" 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Target CLR enable state missing after work.' }
    & wpr -status collectors -instancename $taskSession > "$taskOutput/status-collectors.txt" 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Cannot query owned recording.' }
    & wpr -stop "$taskOutput/calibration.etl" 'Single file-mode CLR method calibration' -skipPdbGen -compress -instancename $taskSession > "$taskOutput/wpr-stop.txt" 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'WPR save failed.' }
    $taskOwned = $false
    $taskSaved = $true
    if ($taskChild.HasExited) { throw 'Fixture exited before save completed.' }
    $taskChild.StandardInput.WriteLine('release')
    $taskChild.StandardInput.Flush()
    $taskReleased = Read-FixtureLine 'metadata_released='
    if ($taskReleased -ne 'true' -or !$taskChild.WaitForExit(10000) -or $taskChild.ExitCode -ne 0) {
        throw 'Fixture release failed.'
    }
    & $DotnetPath $taskTool --calibrate "$taskOutput/calibration.etl" "$taskOutput/fixture.log" "$taskOutput/calibration.json" > "$taskOutput/calibration-check.txt" 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Metadata/address calibration failed; six-run gate remains closed.' }
} catch {
    $taskFailure = $_.ToString()
} finally {
    if ($taskOwned) {
        & wpr -cancel -instancename $taskSession > "$taskOutput/wpr-cleanup.txt" 2>&1
    }
    if ($null -ne $taskChild) {
        if (!$taskChild.HasExited) {
            $taskChild.StandardInput.Close()
            if (!$taskChild.WaitForExit(5000)) { $taskChild.Kill(); $taskChild.WaitForExit() }
        }
        if ($null -ne $taskErrorTask) { $taskErrorTask.GetAwaiter().GetResult() | Set-Content -Encoding utf8 "$taskOutput/fixture-stderr.txt" }
        $taskChild.Dispose()
    }
    @{ passed = ($null -eq $taskFailure); error = $taskFailure; saved = $taskSaved; session = $taskSession
       started_utc = $taskStarted.ToString('O'); stopped_utc = [DateTime]::UtcNow.ToString('O')
       logging_mode = 'File'; fixture_handshake = 'ready -> provider snapshot -> run -> provider snapshot -> save -> release'
       six_runs_executed = 0; source_profile_sha256 = (Get-FileHash -LiteralPath $taskInput -Algorithm SHA256).Hash
       tool_sha256 = (Get-FileHash -LiteralPath $taskTool -Algorithm SHA256).Hash
    } | ConvertTo-Json | Set-Content -Encoding utf8 "$taskOutput/capture.json"
}
if ($null -ne $taskFailure) { throw $taskFailure }
Write-Output "Single file-mode metadata calibration PASS: $taskOutput"

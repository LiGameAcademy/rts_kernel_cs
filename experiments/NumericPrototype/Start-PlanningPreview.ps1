param(
    [Parameter(Mandatory = $true)][string]$GodotConsole,
    [Parameter(Mandatory = $true)][string]$AssetsDirectory,
    [switch]$SelfTest,
    [string]$CapturePath = ""
)
$ErrorActionPreference = 'Stop'
$godot = (Resolve-Path -LiteralPath $GodotConsole).Path
$assets = (Resolve-Path -LiteralPath $AssetsDirectory).Path
$directory = Join-Path $PSScriptRoot 'godot_probe'
$project = Join-Path $directory 'NumericProbe.csproj'
$packages = Join-Path (Split-Path $godot -Parent) 'GodotSharp/Tools/nupkgs'
$logs = Join-Path ([IO.Path]::GetTempPath()) ("rts-planning-preview-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $logs | Out-Null
if ($CapturePath -and -not $SelfTest) { throw 'CapturePath requires SelfTest.' }
if ($CapturePath -and -not [IO.Path]::IsPathFullyQualified($CapturePath)) { throw 'CapturePath must be absolute.' }
dotnet restore $project --source $packages --ignore-failed-sources -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Preview restore failed.' }
dotnet build $project --no-restore -c Debug -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Preview build failed.' }
& $godot --headless --path $directory --editor --import *> (Join-Path $logs 'import.log')
$importExit = $LASTEXITCODE
$importText = [IO.File]::ReadAllText((Join-Path $logs 'import.log'))
$errors = 'SCRIPT ERROR|Parse Error|Failed to load|Failed loading|(?m)^ERROR:'
if ($importExit -ne 0 -or $importText -match $errors) { throw "Preview import failed ($importExit): $logs" }
$arguments = @('--path', $directory, 'res://numeric_planning_preview.tscn', '--', '--assets', $assets)
if ($SelfTest) { $arguments += '--preview-selftest' }
if ($CapturePath) { $arguments += @('--preview-capture', $CapturePath) }
if ($SelfTest -and -not $CapturePath) {
    $arguments = @('--headless') + $arguments
    & $godot @arguments *> (Join-Path $logs 'selftest.log')
    $code = $LASTEXITCODE
    $output = [IO.File]::ReadAllText((Join-Path $logs 'selftest.log'))
    if ($code -ne 0 -or $output -match $errors -or $output -notmatch 'PLANNING_PREVIEW_SELFTEST_PASS') {
        throw "Preview self-test failed ($code): $logs"
    }
    Write-Output $output
}
else {
    # All arguments are fixed switches or filesystem paths. Quote each for the Windows native argument list.
    if ($arguments | Where-Object { $_.Contains('"') -or $_.EndsWith('\') }) { throw 'Unsupported quote/trailing slash in launch path.' }
    $quoted = ($arguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $windowEngine = $godot.Replace('_console.exe', '.exe')
    if (-not (Test-Path -LiteralPath $windowEngine)) { $windowEngine = $godot }
    $process = Start-Process -FilePath $windowEngine -ArgumentList $quoted -WindowStyle Normal -PassThru -RedirectStandardOutput (Join-Path $logs 'runtime.log') -RedirectStandardError (Join-Path $logs 'runtime-errors.log')
    if ($SelfTest) {
        if (-not $process.WaitForExit(600000)) { $process.Kill($true); throw "Preview capture timed out: $logs" }
        $output = [IO.File]::ReadAllText((Join-Path $logs 'runtime.log')) + [IO.File]::ReadAllText((Join-Path $logs 'runtime-errors.log'))
        if ($process.ExitCode -ne 0 -or $output -match $errors -or $output -notmatch 'PLANNING_PREVIEW_CAPTURE_PASS') {
            throw "Preview capture failed: $logs"
        }
        Write-Output $output
    }
    else { Write-Output "PLANNING_PREVIEW_LAUNCHED pid=$($process.Id) logs=$logs" }
}

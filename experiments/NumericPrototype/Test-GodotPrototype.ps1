param(
    [Parameter(Mandatory = $true)][string]$GodotConsole,
    [string]$AssetsDirectory = "",
    [string]$ReportPath = ""
)
$ErrorActionPreference = 'Stop'
$godot = (Resolve-Path -LiteralPath $GodotConsole).Path
$packages = Join-Path (Split-Path $godot -Parent) 'GodotSharp/Tools/nupkgs'
if (-not (Test-Path -LiteralPath $packages)) { throw "Godot Mono packages missing: $packages" }
if ($AssetsDirectory) { $AssetsDirectory = (Resolve-Path -LiteralPath $AssetsDirectory).Path }
$logs = Join-Path ([IO.Path]::GetTempPath()) ("rts-numeric-godot-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $logs | Out-Null
$projectDirectory = Join-Path $PSScriptRoot 'godot_probe'
$project = Join-Path $projectDirectory 'NumericProbe.csproj'
$cli = Join-Path $PSScriptRoot 'NumericPrototype.csproj'

# ArgumentList avoids shell quoting/interpolation. Capture both pipes asynchronously; bound each owned process.
function Invoke-Captured([string]$Executable, [string[]]$Arguments, [string]$LogPath) {
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $Executable
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    try {
        $process.Start() | Out-Null
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(600000)) {
            $process.Kill($true)
            throw "Numeric probe timed out; logs directory: $logs"
        }
        $output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
        [IO.File]::WriteAllText($LogPath, $output)
        if ($process.ExitCode -ne 0) { throw "Probe exited $($process.ExitCode): $LogPath" }
        return $output
    }
    finally { $process.Dispose() }
}

$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
Invoke-Captured $dotnet @('build', $cli, '-c', 'Debug', '-p:NuGetAudit=false') (Join-Path $logs 'cli-build.log') | Out-Null
Invoke-Captured $dotnet @('restore', $project, '--source', $packages, '--ignore-failed-sources', '-p:NuGetAudit=false') (Join-Path $logs 'godot-restore.log') | Out-Null
Invoke-Captured $dotnet @('build', $project, '--no-restore', '-c', 'Debug', '-p:NuGetAudit=false') (Join-Path $logs 'godot-build.log') | Out-Null
$errors = '(?m)^ERROR:|SCRIPT ERROR|Parse Error|Failed to load|Failed loading|Unhandled exception|Could not instantiate'
$import = Invoke-Captured $godot @('--headless', '--path', $projectDirectory, '--editor', '--import', '--quit') (Join-Path $logs 'import.log')
if ($import -match $errors) { throw "Godot import reported errors: $logs" }
$cliAssembly = Join-Path $PSScriptRoot 'bin/Debug/net10.0/NumericPrototype.dll'
$cliCore = Join-Path $PSScriptRoot 'bin/Debug/net10.0/NumericPrototype.Core.dll'
$coreHash = (Get-FileHash -LiteralPath $cliCore -Algorithm SHA256).Hash.ToLowerInvariant()
$loadContext = [Runtime.Loader.AssemblyLoadContext]::new(("numeric-parity-" + [Guid]::NewGuid().ToString('N')), $true)
try { $coreModule = $loadContext.LoadFromAssemblyPath($cliCore).ManifestModule.ModuleVersionId.ToString('D') }
finally { $loadContext.Unload() }
$cases = [ordered]@{ checks = [string[]]@() }
if ($AssetsDirectory) {
    $cases['preview_restore'] = [string[]]@('--planning-verify', $AssetsDirectory)
    $cases['survey'] = [string[]]@('--survey', $AssetsDirectory)
    $cases['movement'] = [string[]]@('--movement-assets', $AssetsDirectory)
    $cases['group'] = [string[]]@('--group-assets', $AssetsDirectory)
    $cases['placement'] = [string[]]@('--placement-assets', $AssetsDirectory)
}
$results = @()
foreach ($caseName in $cases.Keys) {
    $cliArguments = [string[]](@($cliAssembly) + $cases[$caseName])
    $expected = Invoke-Captured $dotnet $cliArguments (Join-Path $logs "$caseName-cli.json")
    $expected = $expected.Trim()
    $expected | ConvertFrom-Json | Out-Null
    $godotReport = Join-Path $logs "$caseName-godot.json"
    $godotArguments = [string[]](@('--headless', '--path', $projectDirectory, '--', '--output', $godotReport) + $cases[$caseName])
    $actualLog = Invoke-Captured $godot $godotArguments (Join-Path $logs "$caseName-godot.log")
    if ($actualLog -match $errors -or $actualLog -notmatch 'NUMERIC_PROBE_PASS runtime=(.+) core_sha256=([a-f0-9]{64}) core_mvid=([a-f0-9-]{36})') {
        throw "Godot probe missing successful completion or reported errors: $caseName/$logs"
    }
    $runtime = $Matches[1]
    if ($Matches[2] -ne $coreHash -or $Matches[3] -ne $coreModule) { throw "Godot loaded a different core assembly: $caseName" }
    if (-not (Test-Path -LiteralPath $godotReport)) { throw "Godot output missing: $caseName" }
    $actual = [IO.File]::ReadAllText($godotReport).Trim()
    if ($actual -cne $expected) { throw "CLI/Godot full JSON report differs: $caseName/$logs" }
    $results += [pscustomobject]@{
        case = $caseName
        runtime = $runtime
        coreSha256 = $coreHash
        coreMvid = $coreModule
        result = ($actual | ConvertFrom-Json)
    }
}
$report = [pscustomobject]@{
    godotVersion = (Invoke-Captured $godot @('--version') (Join-Path $logs 'version.log')).Trim()
    coreTarget = 'net8.0'
    cliTarget = 'net10.0'
    godotAssemblyConfiguration = 'Debug'
    logsDirectory = $logs
    results = $results
}
$serialized = $report | ConvertTo-Json -Depth 12
if ($ReportPath) { [IO.File]::WriteAllText($ReportPath, $serialized) }
$serialized

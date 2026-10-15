param([string]$ReportPath = "")
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'TrafficPrototype.csproj'
$expectedDigest = 'ab2057caad06d97a8517a6801da1084b429a568823f68640675a086a183e1fcc'
$reports = @()
foreach ($configuration in @('Debug', 'Release')) {
    dotnet build $project -c $configuration -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw "Traffic reference build failed: $configuration" }
    $assembly = Join-Path $PSScriptRoot "bin/$configuration/net10.0/TrafficPrototype.dll"
    for ($process = 1; $process -le 2; $process++) {
        $output = & dotnet $assembly
        if ($LASTEXITCODE -ne 0) { throw "Traffic reference process failed: $configuration/$process" }
        $report = ($output -join "`n") | ConvertFrom-Json
        if ($report.digest -ne $expectedDigest) { throw "Traffic trace mismatch: $configuration/$process" }
        if ($report.productionChanged -ne $false -or $report.bigIntegerReference -ne $true) {
            throw 'Wrong traffic probe scope.'
        }
        $reports += [pscustomobject]@{ configuration = $configuration; process = $process; result = $report }
        Write-Output "Traffic reference PASS: $configuration/$process checks=$($report.checks) digest=$($report.digest)"
    }
}
if ($ReportPath) {
    [IO.File]::WriteAllText($ReportPath, ($reports | ConvertTo-Json -Depth 12))
}

param([string]$AssetsDirectory = "", [string]$ReportPath = "")
$ErrorActionPreference = 'Stop'
$prototype = Join-Path $PSScriptRoot 'NumericPrototype.csproj'
$expectedHash = '9f4c05d906c21313a8e2d615b87b1a0a54140d84a56792caece87ec1db938975'
$vendor = Join-Path $PSScriptRoot 'vendor/FixPointCS'
$manifest = Get-Content (Join-Path $vendor 'SOURCE.json') -Raw | ConvertFrom-Json
foreach ($entry in $manifest.files.PSObject.Properties) {
    $source = Join-Path $vendor (Split-Path $entry.Name -Leaf)
    $actual = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $entry.Value.sha256) { throw "Pinned vendor file mismatch: $($entry.Name)" }
}
$expectedInputHash = '6d8de2d1136af0129063576981e9caa3f3cb1c68398d8ca07e8cc959dbe95621'
$expectedWideHash = '4227d7416bf64430462b07089e0ae94bcca4d3a0daa8c98774c9729d17d30ba2'
$expectedMovementHash = '4cf7cd8ca7c2ab68272da6963cb44e53243082a84b97e267b18e0ffe0ad6a531'
$realMovementBaseline = $null
$reports = @()
foreach ($configuration in @('Debug', 'Release')) {
    dotnet build $prototype -c $configuration -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw "Prototype build failed: $configuration" }
    $assembly = Join-Path $PSScriptRoot "bin/$configuration/net10.0/NumericPrototype.dll"
    for ($process = 1; $process -le 2; $process++) {
        $output = & dotnet $assembly
        if ($LASTEXITCODE -ne 0) { throw "Prototype process failed: $configuration/$process" }
        $report = ($output -join "`n") | ConvertFrom-Json
        if ($report.rawTraceHash -ne $expectedHash) { throw "Raw math trace mismatch: $configuration/$process" }
        if ($report.inputTraceHash -ne $expectedInputHash) { throw "Input quantization trace mismatch: $configuration/$process" }
        if ($report.wideCostTraceHash -ne $expectedWideHash) { throw "Wide-cost trace mismatch: $configuration/$process" }
        if ($report.movementTraceHash -ne $expectedMovementHash) { throw "Movement state/event trace mismatch: $configuration/$process" }
        $realMovement = $null
        if ($AssetsDirectory) {
            $realOutput = & dotnet $assembly --movement-assets $AssetsDirectory
            if ($LASTEXITCODE -ne 0) { throw "Real-map movement failed: $configuration/$process" }
            $canonical = $realOutput -join "`n"
            if ($null -eq $realMovementBaseline) { $realMovementBaseline = $canonical }
            if ($canonical -ne $realMovementBaseline) { throw "Real-map state/event trace differs: $configuration/$process" }
            $realMovement = $canonical | ConvertFrom-Json
        }
        $reports += [pscustomobject]@{
            configuration = $configuration
            process = $process
            checks = $report.checks
            rawTraceHash = $report.rawTraceHash
            inputTraceHash = $report.inputTraceHash
            wideCostTraceHash = $report.wideCostTraceHash
            movementTraceHash = $report.movementTraceHash
            realMovement = $realMovement
            maxSinError = $report.maxSinError
            maxAtanError = $report.maxAtanError
            maxSqrtError = $report.maxSqrtError
        }
    }
}
$serializedReport = $reports | ConvertTo-Json -Depth 8
if ($ReportPath) { [System.IO.File]::WriteAllText($ReportPath, $serializedReport) }
$serializedReport

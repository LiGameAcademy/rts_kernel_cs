$ErrorActionPreference = 'Stop'
$vendor = Join-Path $PSScriptRoot '../third_party/FixPointCS'
$manifest = Get-Content (Join-Path $vendor 'SOURCE.json') -Raw | ConvertFrom-Json
foreach ($entry in $manifest.files.PSObject.Properties) {
    $source = Join-Path $vendor (Split-Path $entry.Name -Leaf)
    $actual = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $entry.Value.sha256) { throw "Pinned dependency mismatch: $($entry.Name)" }
}

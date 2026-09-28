param([string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot
try {
    dotnet restore RtsKernel.sln -m:1
    if ($LASTEXITCODE -ne 0) { throw "Restore failed" }
    dotnet build RtsKernel.sln --no-restore -m:1 -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Build failed" }
    dotnet run --project tests/Rts.Kernel.Tests --no-build -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Kernel checks failed" }
    dotnet run --project samples/Rts.Kernel.Cli --no-build -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "CLI sample failed" }
}
finally { Pop-Location }

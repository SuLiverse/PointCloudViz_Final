param([switch]$Smoke)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$localDotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
$dotnet = if (Test-Path $localDotnet) { $localDotnet } else { 'dotnet' }
Push-Location $root
try {
    & $dotnet restore PointCloudViz_Final.sln --locked-mode
    if ($LASTEXITCODE) { throw 'Restore failed.' }
    & $dotnet build PointCloudViz_Final.sln -c Release --no-restore -warnaserror
    if ($LASTEXITCODE) { throw 'Build failed.' }
    & $dotnet test PointCloudViz_Final.Tests -c Release --no-build --logger 'trx;LogFileName=regression.trx'
    if ($LASTEXITCODE) { throw 'Tests failed.' }
    if ($Smoke) {
        & $dotnet run --project PointCloudViz_Final.Smoke -c Release --no-build -- artifacts/smoke
        if ($LASTEXITCODE) { throw 'Native GPU smoke test failed.' }
    }
}
finally { Pop-Location }

param([string]$File)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$localDotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
$dotnet = if (Test-Path $localDotnet) { $localDotnet } else { 'dotnet' }
if ($File) { $File = (Resolve-Path -LiteralPath $File).Path }
Push-Location $root
try {
    if ($File) {
        & $dotnet run --project PointCloudViz_Final -c Release -- $File
    }
    else { & $dotnet run --project PointCloudViz_Final -c Release }
    if ($LASTEXITCODE) { throw 'Application exited with an error.' }
}
finally { Pop-Location }

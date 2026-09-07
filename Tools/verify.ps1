param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $Dotnet run --project (Join-Path $projectRoot 'Tools/Checks/Framework.Checks.csproj') -- $projectRoot
if ($LASTEXITCODE -ne 0) { throw "Framework checks failed with exit code $LASTEXITCODE" }

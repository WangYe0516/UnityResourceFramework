param(
    [Parameter(Mandatory=$true)][string]$UnityEditor,
    [ValidateSet('All', 'Import', 'PlayMode', 'Player')][string]$Mode = 'All'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$logRoot = Join-Path $projectRoot 'Logs/Verification'
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
if (-not (Test-Path -LiteralPath $UnityEditor -PathType Leaf)) { throw "Unity Editor not found: $UnityEditor" }

function Invoke-CheckedProcess([string]$Executable, [string[]]$Arguments, [string]$Log, [string]$Marker) {
    if (Test-Path -LiteralPath $Log) { Remove-Item -LiteralPath $Log }
    $process = Start-Process -FilePath $Executable -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(900000)) {
        Stop-Process -Id $process.Id
        throw "Verification timed out; inspect $Log"
    }
    $process.Refresh()
    if ($process.ExitCode -ne 0) { throw "Exit code $($process.ExitCode); inspect $Log" }
    if (-not (Test-Path -LiteralPath $Log) -or -not (Select-String -LiteralPath $Log -SimpleMatch $Marker -Quiet)) {
        throw "Expected success marker missing; inspect $Log"
    }
    Write-Output $Marker
}

$common = @('-batchmode', '-nographics', '-projectPath', ('"' + $projectRoot + '"'))
if ($Mode -in @('All','Import')) {
    $log = Join-Path $logRoot 'import.log'
    Invoke-CheckedProcess $UnityEditor ($common + @('-quit','-executeMethod','ResourceFramework.ConfigImportMenu.BatchValidateAndRunDemo','-logFile',('"'+$log+'"'))) $log '[Batch Validation] PASS:'
}
if ($Mode -in @('All','PlayMode')) {
    $log = Join-Path $logRoot 'playmode.log'
    Invoke-CheckedProcess $UnityEditor ($common + @('-executeMethod','ResourceFramework.DemoPlayModeCheck.Run','-logFile',('"'+$log+'"'))) $log '[Play Mode Validation] PASS:'
}
if ($Mode -in @('All','Player')) {
    $log = Join-Path $logRoot 'build.log'
    Invoke-CheckedProcess $UnityEditor ($common + @('-quit','-buildTarget','Win64','-executeMethod','ResourceFramework.ConfigImportMenu.BatchBuildDemoWindows','-logFile',('"'+$log+'"'))) $log '[Batch Build] PASS:'
    $player = Join-Path $projectRoot 'Builds/Windows/ResourceFrameworkDemo.exe'
    $log = Join-Path $logRoot 'player.log'
    Invoke-CheckedProcess $player @('-batchmode','-nographics','--smoke-test','-logFile',('"'+$log+'"')) $log 'PASS: herb=9, water=3, potion=3, ticket=1, actionPoints=6.'
}
Write-Output "Unity verification completed: $Mode. Logs: $logRoot"

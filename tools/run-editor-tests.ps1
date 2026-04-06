#!/usr/bin/env pwsh
# run-editor-tests.ps1 - Runs EditMode tests via Unity batchmode
param(
    [int]$TimeoutSeconds = 1200
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot "UnityBatchTools.ps1")

$UnityExe = "C:\Program Files\Unity\Hub\Editor\6000.0.72f1\Editor\Unity.exe"
$ProjectPath = Join-Path $ProjectRoot "MazeChase"
$ReportsDir = Join-Path $ProjectPath "BuildOutput\Reports"
$LogDir = Join-Path $ProjectPath "BuildOutput\Logs"

foreach ($d in @($ReportsDir, $LogDir)) {
    if (-not (Test-Path $d)) {
        New-Item -ItemType Directory -Path $d -Force | Out-Null
    }
}

$Timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$ResultsFile = Join-Path $ReportsDir "test-results-editmode.xml"
$UnityLogFile = Join-Path $LogDir "unity-editmode-tests-$Timestamp.log"

Write-Host "=== EditMode Tests ==="
Write-Host "Project: $ProjectPath"
Write-Host "Timeout: ${TimeoutSeconds}s"

$run = Invoke-UnityBatchCommand -UnityExe $UnityExe -ProjectPath $ProjectPath -UnityLogFile $UnityLogFile -TimeoutSeconds $TimeoutSeconds -DisplayName "EditModeTests" -ArgumentList @(
    "-batchmode", "-quit", "-nographics",
    "-projectPath", $ProjectPath,
    "-runTests",
    "-testPlatform", "EditMode",
    "-testResults", $ResultsFile,
    "-logFile", $UnityLogFile
)

Write-Host "Exit code: $($run.ExitCode)"
Write-Host "Results: $ResultsFile"
Write-Host "Unity log: $UnityLogFile"

if ($run.KilledForFailFast) {
    Write-Host "Fail-fast reason: $($run.FailFastReason)"
}

if ($run.TimedOut) {
    Write-Host "Timed out after ${TimeoutSeconds}s"
}

if (Test-Path $ResultsFile) {
    [xml]$results = Get-Content $ResultsFile
    $total = $results.'test-run'.total
    $passed = $results.'test-run'.passed
    $failed = $results.'test-run'.failed
    Write-Host "Tests: $total total, $passed passed, $failed failed"
}

if ($run.TimedOut) {
    exit 1
}

exit $run.ExitCode

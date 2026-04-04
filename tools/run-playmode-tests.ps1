#!/usr/bin/env pwsh
# run-playmode-tests.ps1 — Runs PlayMode tests via Unity batchmode
$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path $PSScriptRoot -Parent
$UnityExe = "C:\Program Files\Unity\Hub\Editor\6000.0.72f1\Editor\Unity.exe"
$ProjectPath = Join-Path $ProjectRoot "MazeChase"
$ReportsDir = Join-Path $ProjectRoot "BuildOutput\Reports"
$LogDir = Join-Path $ProjectRoot "BuildOutput\Logs"
foreach ($d in @($ReportsDir, $LogDir)) {
    if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
}

$Timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$ResultsFile = Join-Path $ReportsDir "test-results-playmode.xml"
$UnityLogFile = Join-Path $LogDir "unity-playmode-tests-$Timestamp.log"

Write-Host "=== PlayMode Tests ==="
Write-Host "Project: $ProjectPath"

$proc = Start-Process -FilePath $UnityExe -ArgumentList @(
    "-batchmode", "-quit", "-nographics",
    "-projectPath", $ProjectPath,
    "-runTests",
    "-testPlatform", "PlayMode",
    "-testResults", $ResultsFile,
    "-logFile", $UnityLogFile
) -Wait -PassThru -NoNewWindow

Write-Host "Exit code: $($proc.ExitCode)"
Write-Host "Results: $ResultsFile"
Write-Host "Unity log: $UnityLogFile"

if (Test-Path $ResultsFile) {
    [xml]$results = Get-Content $ResultsFile
    $total = $results.'test-run'.total
    $passed = $results.'test-run'.passed
    $failed = $results.'test-run'.failed
    Write-Host "Tests: $total total, $passed passed, $failed failed"
}

exit $proc.ExitCode

#!/usr/bin/env pwsh
# build-win64.ps1 - Builds the game for Windows x64 via Unity batchmode
param(
    [int]$TimeoutSeconds = 1800
)

$ErrorActionPreference = 'Stop'
$Script:StartTime = Get-Date
$ProjectRoot = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot "UnityBatchTools.ps1")

$UnityExe = "C:\Program Files\Unity\Hub\Editor\6000.0.72f1\Editor\Unity.exe"
$ProjectPath = Join-Path $ProjectRoot "MazeChase"
$BuildOutputDir = Join-Path $ProjectPath "BuildOutput"
$LogDir = Join-Path $BuildOutputDir "Logs"
$ReportsDir = Join-Path $BuildOutputDir "Reports"

foreach ($d in @($LogDir, $ReportsDir, (Join-Path $BuildOutputDir "Win64"))) {
    if (-not (Test-Path $d)) {
        New-Item -ItemType Directory -Path $d -Force | Out-Null
    }
}

$Timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$LogFile = Join-Path $LogDir "build-$Timestamp.log"
$UnityLogFile = Join-Path $LogDir "unity-build-$Timestamp.log"

function Log($msg) {
    $line = "[$(Get-Date -Format 'HH:mm:ss')] $msg"
    Write-Host $line
    $line | Out-File -FilePath $LogFile -Append
}

Log "=== Win64 Build ==="
Log "Unity: $UnityExe"
Log "Project: $ProjectPath"
Log "Build Output: $BuildOutputDir\Win64"
Log "Timeout: ${TimeoutSeconds}s"

if (-not (Test-Path $UnityExe)) {
    Log "ERROR: Unity editor not found"
    exit 1
}

if (-not (Test-Path (Join-Path $ProjectPath "Assets"))) {
    Log "ERROR: Unity project not found at $ProjectPath"
    exit 1
}

Log "Starting Unity build..."
$run = Invoke-UnityBatchCommand -UnityExe $UnityExe -ProjectPath $ProjectPath -UnityLogFile $UnityLogFile -TimeoutSeconds $TimeoutSeconds -DisplayName "BuildWin64" -ArgumentList @(
    "-batchmode", "-quit", "-nographics",
    "-projectPath", $ProjectPath,
    "-executeMethod", "BuildSystem.BuildScript.BuildWin64",
    "-logFile", $UnityLogFile,
    "-buildTarget", "Win64"
)

Log "Unity PID: $($run.ProcessId)"
Log "Unity exit code: $($run.ExitCode)"
Log "Unity duration: $($run.DurationSeconds)s"
Log "Unity log: $UnityLogFile"

if ($run.KilledForFailFast) {
    Log "Fail-fast reason: $($run.FailFastReason)"
}

if ($run.TimedOut) {
    Log "Unity timed out after ${TimeoutSeconds}s"
}

$buildResultPath = Join-Path $ReportsDir "build-result.json"
$elapsed = (Get-Date) - $Script:StartTime

if ($run.ExitCode -ne 0 -or $run.TimedOut) {
    Log "BUILD FAILED"
    if (Test-Path $UnityLogFile) {
        Log "Last 50 lines of Unity log:"
        Get-Content $UnityLogFile -Tail 50 | ForEach-Object { Log "  $_" }
    }

    @{
        success = $false
        exitCode = $run.ExitCode
        timestamp = (Get-Date -Format 'o')
        unityVersion = "6000.0.72f1"
        buildTarget = "Win64"
        unityLogPath = $UnityLogFile
        buildLogPath = $LogFile
        timedOut = $run.TimedOut
        failFastReason = $run.FailFastReason
        elapsedSeconds = [math]::Round($elapsed.TotalSeconds, 1)
    } | ConvertTo-Json | Out-File $buildResultPath -Encoding UTF8

    exit 1
}

$exePath = Join-Path $BuildOutputDir "Win64\MazeChase.exe"
if (Test-Path $exePath) {
    Log "BUILD SUCCESS"
    Log "Executable: $exePath"
} else {
    Log "WARNING: Build reported success but executable not found at $exePath"
}

$gitCommit = ""
try {
    $gitCommit = (git -C $ProjectRoot rev-parse HEAD 2>$null)
} catch {
}

@{
    success = $true
    exitCode = $run.ExitCode
    timestamp = (Get-Date -Format 'o')
    unityVersion = "6000.0.72f1"
    buildTarget = "Win64"
    playerPath = $exePath
    unityLogPath = $UnityLogFile
    buildLogPath = $LogFile
    gitCommit = $gitCommit
    timedOut = $run.TimedOut
    failFastReason = $run.FailFastReason
    elapsedSeconds = [math]::Round($elapsed.TotalSeconds, 1)
} | ConvertTo-Json | Out-File $buildResultPath -Encoding UTF8

Log "Build report: $buildResultPath"
Log "Elapsed: $([math]::Round($elapsed.TotalSeconds, 1))s"
exit 0

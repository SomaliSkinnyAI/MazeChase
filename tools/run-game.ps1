#!/usr/bin/env pwsh
# run-game.ps1 — Launches the built game executable and monitors it
param(
    [int]$TimeoutSeconds = 30,
    [switch]$SmokeTest
)
$ErrorActionPreference = 'Stop'
$Script:StartTime = Get-Date
$ProjectRoot = Split-Path $PSScriptRoot -Parent
$ProjectPath = Join-Path $ProjectRoot "MazeChase"
$ExePath = Join-Path $ProjectPath "BuildOutput\Win64\MazeChase.exe"
$LogDir = Join-Path $ProjectPath "BuildOutput\Logs"
if (-not (Test-Path $LogDir)) { New-Item -ItemType Directory -Path $LogDir -Force | Out-Null }
$LogFile = Join-Path $LogDir "run-game-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"

function Log($msg) {
    $line = "[$(Get-Date -Format 'HH:mm:ss')] $msg"
    Write-Host $line
    $line | Out-File -FilePath $LogFile -Append
}

Log "=== Run Game ==="
Log "Executable: $ExePath"
Log "Timeout: ${TimeoutSeconds}s"
Log "Smoke Test: $SmokeTest"

if (-not (Test-Path $ExePath)) {
    Log "ERROR: Executable not found at $ExePath"
    Log "Run build-win64.ps1 first."
    exit 1
}

$gameArgs = @()
if ($SmokeTest) {
    $gameArgs += "--smoke-test"
}

Log "Launching game..."
if ($gameArgs.Count -gt 0) {
    $proc = Start-Process -FilePath $ExePath -ArgumentList $gameArgs -PassThru
} else {
    $proc = Start-Process -FilePath $ExePath -PassThru
}

$exited = $proc.WaitForExit($TimeoutSeconds * 1000)
if (-not $exited) {
    Log "WARNING: Process did not exit within ${TimeoutSeconds}s - killing"
    $proc.Kill()
    $proc.WaitForExit(5000)
    Log "Process killed. Exit code: $($proc.ExitCode)"
    $timedOut = $true
} else {
    $timedOut = $false
}

$exitCode = $proc.ExitCode
Log "Exit code: $exitCode"

# Check Player.log
$playerLog = Join-Path $env:LOCALAPPDATA "..\LocalLow\IndieArcade\MazeChase\Player.log"
if (Test-Path $playerLog) {
    Log "Player log found: $playerLog"
    $errors = Get-Content $playerLog | Select-String -Pattern "(?i)(fatal|exception|crash|error)" | Select-Object -First 10
    if ($errors) {
        Log "Errors found in Player.log:"
        $errors | ForEach-Object { Log "  $_" }
    } else {
        Log "No fatal errors in Player.log"
    }
} else {
    Log "Player.log not found at $playerLog"
}

$elapsed = (Get-Date) - $Script:StartTime
Log ""
if ($timedOut) {
    Log "=== RESULT: TIMEOUT (killed after ${TimeoutSeconds}s) ==="
    exit 2
} elseif ($exitCode -ne 0) {
    Log "=== RESULT: FAILED (exit code $exitCode) ==="
    exit 1
} else {
    Log "=== RESULT: SUCCESS ==="
    Log "Elapsed: $([math]::Round($elapsed.TotalSeconds, 1))s"
    exit 0
}

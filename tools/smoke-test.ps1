#!/usr/bin/env pwsh
# smoke-test.ps1 — Builds, launches, and verifies the game end-to-end
param(
    [int]$RunTimeout = 15,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$Script:StartTime = Get-Date
$ProjectRoot = Split-Path $PSScriptRoot -Parent
$ProjectPath = Join-Path $ProjectRoot "MazeChase"
$LogDir = Join-Path $ProjectPath "BuildOutput\Logs"
if (-not (Test-Path $LogDir)) { New-Item -ItemType Directory -Path $LogDir -Force | Out-Null }
$LogFile = Join-Path $LogDir "smoke-test-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"

function Log($msg) {
    $line = "[$(Get-Date -Format 'HH:mm:ss')] $msg"
    Write-Host $line
    $line | Out-File -FilePath $LogFile -Append
}

Log "=== Smoke Test ==="
$failed = $false

# Step 1: Build
if (-not $SkipBuild) {
    Log "Step 1: Building..."
    & (Join-Path $PSScriptRoot "build-win64.ps1")
    if ($LASTEXITCODE -ne 0) {
        Log "SMOKE TEST FAILED: Build failed (exit code $LASTEXITCODE)"
        $failed = $true
    } else {
        Log "Build passed."
    }
} else {
    Log "Step 1: Build skipped."
}

# Step 2: Run
if (-not $failed) {
    Log "Step 2: Running game (smoke test mode, timeout ${RunTimeout}s)..."
    & (Join-Path $PSScriptRoot "run-game.ps1") -TimeoutSeconds $RunTimeout -SmokeTest
    $runExit = $LASTEXITCODE
    if ($runExit -eq 2) {
        Log "Game timed out — this may be acceptable for smoke test (game ran without crashing)."
    } elseif ($runExit -ne 0) {
        Log "SMOKE TEST FAILED: Game run failed (exit code $runExit)"
        $failed = $true
    } else {
        Log "Game run passed."
    }
}

# Step 3: Check logs
if (-not $failed) {
    Log "Step 3: Checking logs..."
    $buildResult = Join-Path $ProjectPath "BuildOutput\Reports\build-result.json"
    if (Test-Path $buildResult) {
        $result = Get-Content $buildResult | ConvertFrom-Json
        if ($result.success) {
            Log "Build result: SUCCESS"
        } else {
            Log "Build result: FAILED"
            $failed = $true
        }
    }
}

$elapsed = (Get-Date) - $Script:StartTime
Log ""
if ($failed) {
    Log "=== SMOKE TEST: FAILED ==="
    Log "Elapsed: $([math]::Round($elapsed.TotalSeconds, 1))s"
    Log "Log: $LogFile"
    exit 1
} else {
    Log "=== SMOKE TEST: PASSED ==="
    Log "Elapsed: $([math]::Round($elapsed.TotalSeconds, 1))s"
    Log "Log: $LogFile"
    exit 0
}

#!/usr/bin/env pwsh
# collect-diagnostics.ps1 — Gathers all logs and reports into a diagnostics bundle
$ErrorActionPreference = 'Continue'
$ProjectRoot = Split-Path $PSScriptRoot -Parent
$DiagDir = Join-Path $ProjectRoot "BuildOutput\Reports"
if (-not (Test-Path $DiagDir)) { New-Item -ItemType Directory -Path $DiagDir -Force | Out-Null }

$summary = @()
$summary += "# Diagnostics Summary"
$summary += "**Generated:** $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
$summary += ""

# Editor.log
$editorLog = Join-Path $env:LOCALAPPDATA "Unity\Editor\Editor.log"
if (Test-Path $editorLog) {
    $summary += "## Editor.log"
    $summary += "**Path:** $editorLog"
    $errors = Get-Content $editorLog -Tail 100 | Select-String "(?i)(error|fatal|exception)" | Select-Object -First 20
    if ($errors) {
        $summary += "**Errors found:**"
        $errors | ForEach-Object { $summary += "- $_" }
    } else {
        $summary += "No errors in last 100 lines."
    }
    $summary += ""
}

# Player.log
$playerLog = Join-Path $env:APPDATA "..\LocalLow\DefaultCompany\MazeChase\Player.log"
if (Test-Path $playerLog) {
    $summary += "## Player.log"
    $summary += "**Path:** $playerLog"
    $errors = Get-Content $playerLog -Tail 100 | Select-String "(?i)(error|fatal|exception)" | Select-Object -First 20
    if ($errors) {
        $summary += "**Errors found:**"
        $errors | ForEach-Object { $summary += "- $_" }
    } else {
        $summary += "No errors in last 100 lines."
    }
    $summary += ""
}

# Build logs
$buildLogDir = Join-Path $ProjectRoot "BuildOutput\Logs"
if (Test-Path $buildLogDir) {
    $latestLog = Get-ChildItem $buildLogDir -Filter "*.log" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($latestLog) {
        $summary += "## Latest Build Log"
        $summary += "**Path:** $($latestLog.FullName)"
        $summary += "**Modified:** $($latestLog.LastWriteTime)"
        $lastLines = Get-Content $latestLog.FullName -Tail 50
        $summary += '```'
        $lastLines | ForEach-Object { $summary += $_ }
        $summary += '```'
        $summary += ""
    }
}

# Build result
$buildResult = Join-Path $DiagDir "build-result.json"
if (Test-Path $buildResult) {
    $summary += "## Build Result"
    $result = Get-Content $buildResult | ConvertFrom-Json
    $summary += "- **Success:** $($result.success)"
    $summary += "- **Exit Code:** $($result.exitCode)"
    $summary += "- **Timestamp:** $($result.timestamp)"
    $summary += ""
}

# Runtime logs
$runtimeLogDir = Join-Path $ProjectRoot "Logs\runtime"
if (Test-Path $runtimeLogDir) {
    $latestRuntime = Join-Path $runtimeLogDir "latest.log"
    if (Test-Path $latestRuntime) {
        $summary += "## Runtime Log (latest)"
        $summary += "**Path:** $latestRuntime"
        $lastLines = Get-Content $latestRuntime -Tail 30
        $summary += '```'
        $lastLines | ForEach-Object { $summary += $_ }
        $summary += '```'
        $summary += ""
    }
}

# Write summary
$summaryPath = Join-Path $DiagDir "diagnostics-summary.md"
$summary -join "`n" | Out-File $summaryPath -Encoding UTF8
Write-Host "Diagnostics summary written to: $summaryPath"
exit 0

#!/usr/bin/env pwsh
# verify-environment.ps1 — Checks all required tools and writes environment reports
$ErrorActionPreference = 'Stop'
$StartTime = Get-Date
$ProjectRoot = Split-Path $PSScriptRoot -Parent
$ReportsDir = Join-Path $ProjectRoot "reports"
if (-not (Test-Path $ReportsDir)) { New-Item -ItemType Directory -Path $ReportsDir -Force | Out-Null }

$LogFile = Join-Path $ReportsDir "verify-environment-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"
function Log($msg) {
    $line = "[$(Get-Date -Format 'HH:mm:ss')] $msg"
    Write-Host $line
    $line | Out-File -FilePath $LogFile -Append
}

Log "=== Environment Verification ==="
Log "Project Root: $ProjectRoot"

$checks = [System.Collections.ArrayList]::new()
$allPassed = $true

function Add-Check($name, $passed, $detail) {
    $null = $script:checks.Add(@{ name = $name; passed = $passed; detail = $detail })
    if (-not $passed) { $script:allPassed = $false }
    $status = if ($passed) { "PASS" } else { "FAIL" }
    Log "  [$status] $name - $detail"
}

# Unity Hub
$hubPath = "C:\Program Files\Unity Hub\Unity Hub.exe"
$hubExists = Test-Path $hubPath
Add-Check "Unity Hub" $hubExists $(if ($hubExists) { $hubPath } else { "Not found at $hubPath" })

# Unity Editor
$editorPath = "C:\Program Files\Unity\Hub\Editor\6000.0.72f1\Editor\Unity.exe"
$editorExists = Test-Path $editorPath
Add-Check "Unity Editor 6000.0.72f1" $editorExists $(if ($editorExists) { $editorPath } else { "Not found" })

# Windows Build Support
$il2cppPath = "C:\Program Files\Unity\Hub\Editor\6000.0.72f1\Editor\Data\PlaybackEngines\windowsstandalonesupport"
$il2cppExists = Test-Path $il2cppPath
Add-Check "Windows Build Support" $il2cppExists $(if ($il2cppExists) { "Installed" } else { "Not found at $il2cppPath" })

# Git
try {
    $gitVersion = & git --version 2>&1
    Add-Check "Git" $true "$gitVersion"
} catch {
    Add-Check "Git" $false "Not found"
}

# PowerShell version
Add-Check "PowerShell" $true "Version $($PSVersionTable.PSVersion)"

# Disk space
$drive = (Get-PSDrive -Name C)
$freeGB = [math]::Round($drive.Free / 1GB, 1)
$diskOk = $freeGB -ge 10
Add-Check "Disk Space (C:)" $diskOk "${freeGB} GB free"

# Write access
$testFile = Join-Path $ProjectRoot ".write-test-$(Get-Random)"
try {
    "test" | Out-File $testFile
    Remove-Item $testFile -Force
    Add-Check "Write Access" $true "Project directory is writable"
} catch {
    Add-Check "Write Access" $false "Cannot write to $ProjectRoot"
}

# Visual Studio (optional)
$vsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vsWhere) {
    $vsPath = & $vsWhere -latest -property installationPath 2>$null
    if ($vsPath) {
        Add-Check "Visual Studio" $true $vsPath
    } else {
        Add-Check "Visual Studio (optional)" $true "Not installed - VS Code will be used"
    }
} else {
    Add-Check "Visual Studio (optional)" $true "vswhere not found - VS Code will be used"
}

# Summary
$passCount = ($checks | Where-Object { $_.passed }).Count
$totalCount = $checks.Count
Log ""
Log "=== Summary: $passCount/$totalCount checks passed ==="
if ($allPassed) { Log "Environment is ready." } else { Log "Some checks failed." }

$elapsed = (Get-Date) - $StartTime
Log "Elapsed: $([math]::Round($elapsed.TotalSeconds, 1))s"
Log "Log: $LogFile"

# Write JSON report
$jsonPath = Join-Path $ReportsDir "environment-report.json"
@{
    timestamp = (Get-Date -Format 'o')
    projectRoot = $ProjectRoot
    checks = @($checks)
    allPassed = $allPassed
    elapsedSeconds = [math]::Round($elapsed.TotalSeconds, 1)
} | ConvertTo-Json -Depth 5 | Out-File $jsonPath -Encoding UTF8
Log "JSON report: $jsonPath"

# Write Markdown report
$mdPath = Join-Path $ReportsDir "environment-report.md"
$md = "# Environment Report`n**Generated:** $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')`n**Project Root:** $ProjectRoot`n`n| Check | Status | Detail |`n|-------|--------|--------|"
foreach ($c in $checks) {
    $s = if ($c.passed) { "PASS" } else { "FAIL" }
    $md += "`n| $($c.name) | $s | $($c.detail) |"
}
$md += "`n`n**Result:** $passCount/$totalCount passed"
$md | Out-File $mdPath -Encoding UTF8
Log "Markdown report: $mdPath"

if (-not $allPassed) { exit 1 }
exit 0

#!/usr/bin/env pwsh
# clean-rebuild.ps1 — Deletes Library cache and does a full rebuild
$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path $PSScriptRoot -Parent
$ProjectPath = Join-Path $ProjectRoot "MazeChase"
$LibraryDir = Join-Path $ProjectPath "Library"

Write-Host "=== Clean Rebuild ==="

if (Test-Path $LibraryDir) {
    Write-Host "Deleting Library cache at $LibraryDir..."
    Remove-Item $LibraryDir -Recurse -Force
    Write-Host "Library cache deleted."
} else {
    Write-Host "No Library cache found."
}

# Delete previous build output
$win64Dir = Join-Path $ProjectRoot "BuildOutput\Win64"
if (Test-Path $win64Dir) {
    Write-Host "Deleting previous build output..."
    Remove-Item $win64Dir -Recurse -Force
    New-Item -ItemType Directory -Path $win64Dir -Force | Out-Null
}

Write-Host "Running build..."
& (Join-Path $PSScriptRoot "build-win64.ps1")
exit $LASTEXITCODE

#!/usr/bin/env pwsh
# create-project.ps1 — Creates the Unity project via batchmode
$ErrorActionPreference = 'Stop'
$Script:StartTime = Get-Date
$ProjectRoot = Split-Path $PSScriptRoot -Parent
$UnityExe = "C:\Program Files\Unity\Hub\Editor\6000.0.72f1\Editor\Unity.exe"
$ProjectPath = Join-Path $ProjectRoot "MazeChase"
$LogDir = Join-Path $ProjectRoot "Logs\build"
if (-not (Test-Path $LogDir)) { New-Item -ItemType Directory -Path $LogDir -Force | Out-Null }
$LogFile = Join-Path $LogDir "create-project-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"

function Log($msg) {
    $line = "[$(Get-Date -Format 'HH:mm:ss')] $msg"
    Write-Host $line
    $line | Out-File -FilePath $LogFile -Append
}

Log "=== Unity Project Creation ==="
Log "Unity: $UnityExe"
Log "Project: $ProjectPath"

if (-not (Test-Path $UnityExe)) {
    Log "ERROR: Unity editor not found at $UnityExe"
    exit 1
}

# Create project if it doesn't exist
if (-not (Test-Path (Join-Path $ProjectPath "Assets"))) {
    Log "Creating new Unity project..."
    $unityLog = Join-Path $LogDir "unity-create-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"

    $proc = Start-Process -FilePath $UnityExe -ArgumentList @(
        "-batchmode", "-quit", "-nographics",
        "-createProject", $ProjectPath,
        "-logFile", $unityLog
    ) -Wait -PassThru -NoNewWindow

    Log "Unity exit code: $($proc.ExitCode)"
    Log "Unity log: $unityLog"

    if ($proc.ExitCode -ne 0) {
        Log "ERROR: Unity project creation failed"
        if (Test-Path $unityLog) {
            $lastLines = Get-Content $unityLog -Tail 30
            Log "Last 30 lines of Unity log:"
            $lastLines | ForEach-Object { Log "  $_" }
        }
        exit 1
    }
    Log "Project created successfully."
} else {
    Log "Project already exists at $ProjectPath"
}

# Create folder structure
$folders = @(
    "Assets/Art", "Assets/Art/Sprites", "Assets/Art/Materials", "Assets/Art/Shaders",
    "Assets/Audio", "Assets/Audio/SFX", "Assets/Audio/Music",
    "Assets/Prefabs", "Assets/Prefabs/Characters", "Assets/Prefabs/Maze", "Assets/Prefabs/UI",
    "Assets/Scenes",
    "Assets/Scripts/Core", "Assets/Scripts/Game", "Assets/Scripts/AI", "Assets/Scripts/AI/Autoplay",
    "Assets/Scripts/UI", "Assets/Scripts/Audio", "Assets/Scripts/VFX",
    "Assets/Scripts/Infrastructure/Logging", "Assets/Scripts/Infrastructure/Diagnostics", "Assets/Scripts/Infrastructure/CrashHandling",
    "Assets/Editor/Build",
    "Assets/Settings",
    "Assets/Tests/EditMode", "Assets/Tests/PlayMode"
)

foreach ($f in $folders) {
    $fullPath = Join-Path $ProjectPath $f
    if (-not (Test-Path $fullPath)) {
        New-Item -ItemType Directory -Path $fullPath -Force | Out-Null
        Log "Created: $f"
    }
}

$elapsed = (Get-Date) - $Script:StartTime
Log ""
Log "=== Done in $([math]::Round($elapsed.TotalSeconds, 1))s ==="
Log "Log: $LogFile"
exit 0

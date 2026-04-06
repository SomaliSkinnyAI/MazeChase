#!/usr/bin/env pwsh
# UnityBatchTools.ps1 - Shared helpers for watchdog-driven Unity batch commands
Set-StrictMode -Version Latest

function Get-UnityLogChunk {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [ref]$ByteOffset
    )

    if (-not (Test-Path $Path)) {
        return @()
    }

    $file = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    try {
        $file.Seek($ByteOffset.Value, [System.IO.SeekOrigin]::Begin) | Out-Null
        $reader = New-Object System.IO.StreamReader($file, [System.Text.Encoding]::UTF8, $true, 4096, $true)
        try {
            $text = $reader.ReadToEnd()
            $ByteOffset.Value = $file.Position
        } finally {
            $reader.Dispose()
        }
    } finally {
        $file.Dispose()
    }

    if ([string]::IsNullOrEmpty($text)) {
        return @()
    }

    return ($text -split "\r?\n")
}

function Stop-UnityProcessTree {
    param(
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$Process,

        [Parameter(Mandatory = $true)]
        [string]$DisplayName
    )

    if ($Process.HasExited) {
        return
    }

    try {
        & "$env:SystemRoot\System32\taskkill.exe" /PID $Process.Id /T /F | Out-Null
    } catch {
        Write-Host "[$(Get-Date -Format 'HH:mm:ss')] [$DisplayName] taskkill failed: $($_.Exception.Message)"
        try {
            $Process.Kill()
        } catch {
            Write-Host "[$(Get-Date -Format 'HH:mm:ss')] [$DisplayName] Kill failed: $($_.Exception.Message)"
        }
    }
}

function Invoke-UnityBatchCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$UnityExe,

        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,

        [Parameter(Mandatory = $true)]
        [string[]]$ArgumentList,

        [Parameter(Mandatory = $true)]
        [string]$UnityLogFile,

        [int]$TimeoutSeconds = 1800,
        [int]$PollSeconds = 2,
        [int]$HeartbeatSeconds = 15,
        [string]$DisplayName = "UnityBatch",

        [string[]]$FailFastPatterns = @(
            "(?i)\berror CS\d+\b",
            "(?i)\bBuildFailedException\b",
            "(?i)\bScripts have compiler errors\b",
            "(?i)\bCompilation failed\b",
            "(?i)\bAborting batchmode due to failure\b",
            "(?i)\ball compiler errors have to be fixed before you can enter playmode\b"
        )
    )

    if (-not (Test-Path $UnityExe)) {
        throw "Unity editor not found at $UnityExe"
    }

    $logParent = Split-Path -Parent $UnityLogFile
    if ($logParent -and -not (Test-Path $logParent)) {
        New-Item -ItemType Directory -Path $logParent -Force | Out-Null
    }

    if (Test-Path $UnityLogFile) {
        Remove-Item -LiteralPath $UnityLogFile -Force
    }

    $startedAt = Get-Date
    $proc = Start-Process -FilePath $UnityExe -ArgumentList $ArgumentList -PassThru
    $logOffset = 0L
    $lastHeartbeat = Get-Date
    $timedOut = $false
    $killedForFailFast = $false
    $failFastReason = $null

    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] [$DisplayName] PID $($proc.Id) started"
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] [$DisplayName] Log: $UnityLogFile"

    while (-not $proc.HasExited) {
        Start-Sleep -Seconds $PollSeconds
        $proc.Refresh()

        if ($proc.HasExited) {
            break
        }

        $now = Get-Date
        if (($now - $startedAt).TotalSeconds -ge $TimeoutSeconds) {
            $timedOut = $true
            Write-Host "[$(Get-Date -Format 'HH:mm:ss')] [$DisplayName] Timeout after ${TimeoutSeconds}s"
            Stop-UnityProcessTree -Process $proc -DisplayName $DisplayName
            break
        }

        foreach ($line in (Get-UnityLogChunk -Path $UnityLogFile -ByteOffset ([ref]$logOffset))) {
            if ([string]::IsNullOrWhiteSpace($line)) {
                continue
            }

            Write-Host "[$(Get-Date -Format 'HH:mm:ss')] [$DisplayName] $line"

            foreach ($pattern in $FailFastPatterns) {
                if ($line -match $pattern) {
                    $killedForFailFast = $true
                    $failFastReason = $line.Trim()
                    break
                }
            }

            if ($killedForFailFast) {
                break
            }
        }

        if ($killedForFailFast) {
            Write-Host "[$(Get-Date -Format 'HH:mm:ss')] [$DisplayName] Fail-fast triggered: $failFastReason"
            Stop-UnityProcessTree -Process $proc -DisplayName $DisplayName
            break
        }

        $proc.Refresh()
        if ($proc.HasExited) {
            break
        }

        $now = Get-Date
        if (($now - $lastHeartbeat).TotalSeconds -ge $HeartbeatSeconds) {
            $elapsed = [math]::Round(($now - $startedAt).TotalSeconds, 1)
            Write-Host "[$(Get-Date -Format 'HH:mm:ss')] [$DisplayName] Still running after ${elapsed}s"
            $lastHeartbeat = $now
        }
    }

    if (-not $proc.HasExited) {
        $null = $proc.WaitForExit(5000)
        $proc.Refresh()
    }

    foreach ($line in (Get-UnityLogChunk -Path $UnityLogFile -ByteOffset ([ref]$logOffset))) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        Write-Host "[$(Get-Date -Format 'HH:mm:ss')] [$DisplayName] $line"
    }

    return [pscustomobject]@{
        ExitCode          = $proc.ExitCode
        ProcessId         = $proc.Id
        UnityLogFile      = $UnityLogFile
        TimedOut          = $timedOut
        KilledForFailFast = $killedForFailFast
        FailFastReason    = $failFastReason
        DurationSeconds   = [math]::Round(((Get-Date) - $startedAt).TotalSeconds, 1)
    }
}

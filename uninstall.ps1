<#
.SYNOPSIS
    Removes notepad-tidy. Your notes are left exactly as they are.

.DESCRIPTION
    Stops the service, removes the scheduled task and deletes the binaries.
    Backups are kept unless -RemoveBackups is given, since they are the only
    way back if a merge turns out to be unwanted.
#>
[CmdletBinding()]
param(
    [string]$TaskName = 'NotepadTidy',
    [switch]$RemoveBackups
)

$ErrorActionPreference = 'Continue'

try { Stop-ScheduledTask -TaskName $TaskName -ErrorAction Stop; Write-Host "Service stopped." } catch { }
Get-Process -Name nptidyd -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800

try {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction Stop
    Write-Host "Scheduled task removed."
} catch { Write-Host "No scheduled task to remove." }

$root = Join-Path $env:LOCALAPPDATA 'notepad-tidy'
$bin = Join-Path $root 'bin'
if (Test-Path $bin) {
    Remove-Item $bin -Recurse -Force
    Write-Host "Binaries removed."
}

$backups = Join-Path $root 'backups'
if ($RemoveBackups) {
    if (Test-Path $backups) {
        Remove-Item $backups -Recurse -Force
        Write-Host "Backups removed."
    }
} elseif (Test-Path $backups) {
    Write-Host ""
    Write-Host "Backups kept at $backups"
    Write-Host "They are the only way to undo a merge. Pass -RemoveBackups to delete them too."
}

Write-Host ""
Write-Host "Done. Your notes were not touched."

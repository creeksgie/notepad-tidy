<#
.SYNOPSIS
    Installs notepad-tidy: copies the binaries and registers the background
    service to start at logon.

.DESCRIPTION
    No administrator rights are needed. The service only ever touches the
    current user's own Notepad folder.

    Run from a folder containing nptidy.exe and nptidyd.exe, or point -Source
    at one.

.EXAMPLE
    .\install.ps1

.EXAMPLE
    .\install.ps1 -Source .\dist
#>
[CmdletBinding()]
param(
    [string]$Source = $PSScriptRoot,
    [string]$TaskName = 'NotepadTidy'
)

$ErrorActionPreference = 'Stop'

$dest = Join-Path $env:LOCALAPPDATA 'notepad-tidy\bin'
$required = @('nptidy.exe', 'nptidyd.exe')

foreach ($file in $required) {
    if (-not (Test-Path (Join-Path $Source $file))) {
        throw "$file not found in $Source. Point -Source at the folder holding the binaries."
    }
}

# The Notepad 11 state folder. Its absence means this is not Windows 11, or
# the Store version of Notepad is not installed.
$tabState = Join-Path $env:LOCALAPPDATA `
    'Packages\Microsoft.WindowsNotepad_8wekyb3d8bbwe\LocalState\TabState'
if (-not (Test-Path $tabState)) {
    throw "Notepad's TabState folder was not found at $tabState. This tool targets the Windows 11 Store version of Notepad."
}

Write-Host "Installing to $dest"
New-Item -ItemType Directory -Path $dest -Force | Out-Null

# Stop anything already running, otherwise the executable is locked.
try { Stop-ScheduledTask -TaskName $TaskName -ErrorAction Stop } catch { }
Get-Process -Name nptidyd -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800

foreach ($file in $required) {
    Copy-Item (Join-Path $Source $file) (Join-Path $dest $file) -Force
    Write-Host "  $file"
}

$exe = Join-Path $dest 'nptidyd.exe'

try { Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction Stop } catch { }

Register-ScheduledTask -TaskName $TaskName `
    -Action    (New-ScheduledTaskAction -Execute $exe) `
    -Trigger   (New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME) `
    -Settings  (New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries `
                    -DontStopIfGoingOnBatteries `
                    -ExecutionTimeLimit ([TimeSpan]::Zero) `
                    -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)) `
    -Principal (New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" `
                    -LogonType Interactive -RunLevel Limited) `
    -Description 'Tidies Notepad tabs by theme when Notepad closes' | Out-Null

Write-Host "Scheduled task '$TaskName' registered for logon."

Start-ScheduledTask -TaskName $TaskName
Start-Sleep -Seconds 3

$process = Get-Process -Name nptidyd -ErrorAction SilentlyContinue
if ($process) {
    $ram = [math]::Round($process.WorkingSet64 / 1MB, 1)
    Write-Host "Service running (pid $($process.Id), $ram MB, no window)." -ForegroundColor Green
} else {
    Write-Warning "The service did not start. Check the log below."
}

Write-Host ""
Write-Host "Log:      $env:LOCALAPPDATA\notepad-tidy\service.log"
Write-Host "Backups:  $env:LOCALAPPDATA\notepad-tidy\backups"
Write-Host ""
Write-Host "Write '#theme' on the first line of a note, close Notepad, and it gets filed."
Write-Host "Nothing is written while Notepad is running, and every run is backed up first."
Write-Host ""
Write-Host "Inspect without changing anything:  $dest\nptidy.exe tidy"
Write-Host "Undo a run:                         $dest\nptidy.exe restore <backup folder>"
Write-Host "Uninstall:                          .\uninstall.ps1"

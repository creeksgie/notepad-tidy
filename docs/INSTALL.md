# Installing

## Requirements

- Windows 11 (Notepad 11)
- .NET 10 SDK to build

For a NativeAOT build you also need the Visual Studio C++ workload. If the
publish fails with `'vswhere.exe' is not recognized`, the tool exists but is
not on `PATH` — add it for the build:

```powershell
$env:Path += ";${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
```

## Build and install

```powershell
$dest = "$env:LOCALAPPDATA\notepad-tidy\bin"
dotnet publish src/NotepadTidy.Cli/NotepadTidy.Cli.csproj -c Release -r win-x64 -p:PublishAot=true -o $dest
dotnet publish src/NotepadTidy.Service/NotepadTidy.Service.csproj -c Release -r win-x64 -p:PublishAot=true -o $dest
```

NativeAOT gives a self-contained 2 MB executable with no .NET runtime
dependency, and roughly 15 MB of RAM at rest instead of 34 MB.

## Run at logon

```powershell
$exe = "$env:LOCALAPPDATA\notepad-tidy\bin\nptidyd.exe"
Register-ScheduledTask -TaskName NotepadTidy `
  -Action  (New-ScheduledTaskAction -Execute $exe) `
  -Trigger (New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME) `
  -Settings (New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries `
             -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) `
             -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)) `
  -Principal (New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" `
              -LogonType Interactive -RunLevel Limited)
Start-ScheduledTask -TaskName NotepadTidy
```

No administrator rights are needed: the service only ever touches the current
user's own Notepad folder.

## No console window

The service is built as `WinExe`, not `Exe`. A console application makes
Windows allocate a terminal, and closing that terminal kills the process — so
the tool would only work for as long as a black window stayed open on screen.
`WinExe` allocates nothing.

Verify it on the produced binary: the PE subsystem must be `2` (windows) for
`nptidyd.exe`, and `3` (console) for `nptidy.exe`, which is interactive and
should keep its output.

```powershell
$b = [IO.File]::ReadAllBytes("$env:LOCALAPPDATA\notepad-tidy\bin\nptidyd.exe")
[BitConverter]::ToUInt16($b, [BitConverter]::ToInt32($b, 0x3C) + 0x5C)   # 2
```

The service logs to `%LOCALAPPDATA%\notepad-tidy\service.log` instead.

## Checking on it

```powershell
Get-Content "$env:LOCALAPPDATA\notepad-tidy\service.log" -Tail 10
Get-Process nptidyd | Select-Object Id, WorkingSet64
```

Measured at rest: **0 s of CPU over 8 seconds, ~15 MB of RAM**. The service
waits on a kernel handle and is woken when Notepad exits; it never polls.

## Undoing a run

Backups are written before every write, three kept, under
`%LOCALAPPDATA%\notepad-tidy\backups`.

```powershell
nptidy restore "$env:LOCALAPPDATA\notepad-tidy\backups\<folder>"
```

Notepad must be closed. The current state is itself backed up first, and the
backup is parsed before being trusted — restoring a corrupted backup over live
notes would turn a bad day into a disaster.

## Removing it

```powershell
Stop-ScheduledTask -TaskName NotepadTidy
Unregister-ScheduledTask -TaskName NotepadTidy -Confirm:$false
Remove-Item "$env:LOCALAPPDATA\notepad-tidy" -Recurse
```

Your notes are left exactly as they are.

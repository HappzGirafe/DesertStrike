# Makes the Low Strike online server start by itself, without any window: a Windows scheduled task runs the API
# (which starts the Unity game hosts) when the PC starts, starts it again within a minute if it stops, and checks
# every 5 minutes. With PLAYIT_EXE in .env, a second task does the same for the playit agent, and with
# CLOUDFLARED_EXE and CLOUDFLARED_CONFIG one for the Cloudflare tunnel (unless cloudflared is a Windows service).
#
#   powershell -ExecutionPolicy Bypass -File scripts\install-autostart.ps1
#       in a PowerShell opened "as administrator": starts with Windows, before anyone logs in (best for a server PC)
#       in a normal PowerShell: starts when you log in
#
# To undo: scripts\uninstall-autostart.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$pythonw = Join-Path $root ".venv\Scripts\pythonw.exe"
if (-not (Test-Path $pythonw) -or -not (Test-Path (Join-Path $root ".env"))) {
    Write-Host "Run scripts\setup.ps1 first."
    exit 1
}
$settingsFile = @{}
foreach ($line in Get-Content (Join-Path $root ".env")) {
    if ($line -match '^\s*([A-Z_]+)\s*=\s*(.*)$') { $settingsFile[$Matches[1]] = $Matches[2].Trim().Trim('"') }
}

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if ($admin) {
    $trigger = New-ScheduledTaskTrigger -AtStartup
    $principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
    $when = "when Windows starts"
}
else {
    $user = "$env:USERDOMAIN\$env:USERNAME"
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
    $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
    $when = "when $env:USERNAME logs in (run this as administrator to start with Windows instead)"
}
# Every 5 minutes as well: if it is not running then, it starts (a running one is left alone).
$trigger.Repetition = (New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes 5)).Repetition
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable `
    -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1)

function Register([string]$name, [string]$program, [string]$arguments, [string]$folder) {
    $action = New-ScheduledTaskAction -Execute $program -Argument $arguments -WorkingDirectory $folder
    Register-ScheduledTask -TaskName $name -Action $action -Trigger $trigger -Principal $principal -Settings $settings `
        -Description "Low Strike online server (online-server\scripts\install-autostart.ps1)" -Force | Out-Null
    Start-ScheduledTask -TaskName $name
    Write-Host "Task '$name': starts $when; running now."
}

Register "Low Strike online server" $pythonw "`"$(Join-Path $root 'lowstrike_server.py')`" --log `"$(Join-Path $root 'logs\api.log')`"" $root
$playit = $settingsFile["PLAYIT_EXE"]
if ($playit -and (Test-Path $playit)) {
    Register "Low Strike playit agent" $playit "" (Split-Path -Parent $playit)
}
$cloudflared = $settingsFile["CLOUDFLARED_EXE"]
$tunnelConfig = $settingsFile["CLOUDFLARED_CONFIG"]
if ($cloudflared -and $tunnelConfig -and (Test-Path $cloudflared) -and (Test-Path $tunnelConfig)) {
    $tunnelLog = Join-Path (Split-Path -Parent $tunnelConfig) "cloudflared.log"
    Register "Low Strike cloudflared" $cloudflared "tunnel --no-autoupdate --config `"$tunnelConfig`" --logfile `"$tunnelLog`" run" (Split-Path -Parent $tunnelConfig)
}
Write-Host "Check it with scripts\status.ps1 (in a minute the game hosts are up)."

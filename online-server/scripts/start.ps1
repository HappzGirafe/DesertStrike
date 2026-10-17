# Starts the Low Strike online server on this PC, without any windows:
#   - the API (lowstrike_server.py), which starts the Unity game hosts itself and restarts any that stops;
#   - cloudflared, when it does not run already: its autostart task, or with CLOUDFLARED_CONFIG (a tunnel made with
#     "cloudflared tunnel create") or CLOUDFLARED_TUNNEL_TOKEN (one made in the dashboard) in .env;
#   - the playit agent, when PLAYIT_EXE is in .env and it does not run already.
# Safe to run again (the autostart task runs it every 5 minutes): whatever runs already is left alone.
#
#   powershell -ExecutionPolicy Bypass -File online-server\scripts\start.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$logs = Join-Path $root "logs"
New-Item -ItemType Directory -Force $logs | Out-Null

$envFile = Join-Path $root ".env"
if (-not (Test-Path $envFile)) {
    Write-Host "online-server\.env is missing: run scripts\setup.ps1 first."
    exit 1
}
$settings = @{}
foreach ($line in Get-Content $envFile) {
    if ($line -match '^\s*([A-Z_]+)\s*=\s*(.*)$') { $settings[$Matches[1]] = $Matches[2].Trim().Trim('"') }
}

function Find-Running([string]$name, [string]$commandLike) {
    Get-CimInstance Win32_Process -Filter "Name = '$name'" | Where-Object { $_.CommandLine -like $commandLike }
}

# 1. The API, which starts the game hosts.
$pythonw = Join-Path $root ".venv\Scripts\pythonw.exe"
if (-not (Test-Path $pythonw)) {
    Write-Host "The Python environment is missing: run scripts\setup.ps1 first."
    exit 1
}
$task = Get-ScheduledTask -TaskName "Low Strike online server" -ErrorAction SilentlyContinue
if (Find-Running "pythonw.exe" "*lowstrike_server.py*") {
    Write-Host "API: running"
}
elseif ($task) {
    # Installed with install-autostart.ps1: the task runs it (and was perhaps paused by stop.ps1).
    try {
        Enable-ScheduledTask -TaskName $task.TaskName -ErrorAction Stop | Out-Null
        Start-ScheduledTask -TaskName $task.TaskName -ErrorAction Stop
        Write-Host "API: started by the autostart task (log: online-server\logs\api.log)"
    }
    catch { Write-Host "API: could not start the autostart task (installed as administrator: run this as administrator too)" }
}
else {
    Start-Process -FilePath $pythonw -WorkingDirectory $root `
        -ArgumentList "`"$(Join-Path $root 'lowstrike_server.py')`"", "--log", "`"$(Join-Path $logs 'api.log')`""
    Write-Host "API: started (log: online-server\logs\api.log; game hosts: logs\unity-<slot>.log)"
}

# 2. Cloudflare Tunnel (only when it is not installed as a Windows service).
$token = $settings["CLOUDFLARED_TUNNEL_TOKEN"]
$cloudflared = $settings["CLOUDFLARED_EXE"]
$tunnelConfig = $settings["CLOUDFLARED_CONFIG"]
$tunnelTask = Get-ScheduledTask -TaskName "Low Strike cloudflared" -ErrorAction SilentlyContinue
if (Get-Process cloudflared -ErrorAction SilentlyContinue) {
    Write-Host "cloudflared: running"
}
elseif ($tunnelTask) {
    Start-ScheduledTask -TaskName $tunnelTask.TaskName
    Write-Host "cloudflared: started by its autostart task"
}
elseif ($tunnelConfig -and (Test-Path $tunnelConfig)) {
    if (-not $cloudflared) { $cloudflared = "cloudflared" }
    Start-Process -FilePath $cloudflared -ArgumentList "tunnel", "--no-autoupdate", "--config", "`"$tunnelConfig`"", "run" -WindowStyle Hidden `
        -RedirectStandardError (Join-Path $logs "cloudflared.log")
    Write-Host "cloudflared: started"
}
elseif ($token) {
    if (-not $cloudflared) { $cloudflared = "cloudflared" }
    $env:TUNNEL_TOKEN = $token   # in its environment, not on its command line
    Start-Process -FilePath $cloudflared -ArgumentList "tunnel", "--no-autoupdate", "run" -WindowStyle Hidden `
        -RedirectStandardError (Join-Path $logs "cloudflared.log")
    Remove-Item Env:TUNNEL_TOKEN
    Write-Host "cloudflared: started"
}
else {
    Write-Host "cloudflared: not running (install it as a service, see README step 3)"
}

# 3. The playit agent (when it does not run as a service already).
$playit = $settings["PLAYIT_EXE"]
if (Get-Process "playit*" -ErrorAction SilentlyContinue) {
    Write-Host "playit: running"
}
elseif ($playit -and (Test-Path $playit)) {
    Start-Process -FilePath $playit -WindowStyle Minimized
    Write-Host "playit: started"
}
else {
    Write-Host "playit: not running (start the playit program, or set PLAYIT_EXE in .env; README step 2)"
}

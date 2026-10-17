# Shows whether the Low Strike online server runs: the API, the game hosts, cloudflared and playit, the autostart
# task, and what the API answers here and (through the tunnel) from the internet.
#
#   powershell -ExecutionPolicy Bypass -File scripts\status.ps1

$root = Split-Path -Parent $PSScriptRoot
$settings = @{}
if (Test-Path (Join-Path $root ".env")) {
    foreach ($line in Get-Content (Join-Path $root ".env")) {
        if ($line -match '^\s*([A-Z_]+)\s*=\s*(.*)$') { $settings[$Matches[1]] = $Matches[2].Trim().Trim('"') }
    }
}
$processes = Get-CimInstance Win32_Process
function Show([string]$what, $found) { Write-Host ("{0,-14} {1}" -f $what, $(if ($found) { "running" } else { "NOT running" })) }

Show "API" ($processes | Where-Object { $_.Name -like "python*.exe" -and $_.CommandLine -like "*lowstrike_server.py*" })
$hosts = @($processes | Where-Object { $_.Name -like "LowStrike*.exe" -and $_.CommandLine -like "*-ds-server*" })
Write-Host ("{0,-14} {1} running" -f "Game hosts", $hosts.Count)
Show "cloudflared" ($processes | Where-Object { $_.Name -like "cloudflared*" })
Show "playit" ($processes | Where-Object { $_.Name -like "playit*" })
$task = Get-ScheduledTask -TaskName "Low Strike online server" -ErrorAction SilentlyContinue
Write-Host ("{0,-14} {1}" -f "Autostart", $(if ($task) { "installed ($($task.State))" } else { "not installed" }))

$port = if ($settings["API_PORT"]) { $settings["API_PORT"] } else { "8000" }
try {
    $local = Invoke-RestMethod -Uri "http://127.0.0.1:$port/health" -TimeoutSec 5
    Write-Host ("{0,-14} {1} game host(s) up, {2} room(s) free, {3} player(s) online, game version {4}" -f "This PC", $local.servers, $local.rooms_free, $local.players_online, $local.game_version)
}
catch { Write-Host ("{0,-14} the API does not answer on port {1}" -f "This PC", $port) }

$public = $settings["PUBLIC_API_URL"]
if ($public -and $public -notmatch "example\.com") {
    try {
        $remote = Invoke-RestMethod -Uri ($public.TrimEnd('/') + "/health") -TimeoutSec 10
        Write-Host ("{0,-14} {1} answers ({2} game host(s) up)" -f "Internet", $public, $remote.servers)
    }
    catch { Write-Host ("{0,-14} {1} does not answer: is the tunnel (cloudflared) running?" -f "Internet", $public) }
}
Write-Host ""
Write-Host "Logs: online-server\logs\ (api.log, unity-<slot>.log)"

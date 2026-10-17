# Stops the Low Strike online server on this PC: the API and the Unity game hosts it started. With the autostart
# task installed, the task is paused too (scripts\start.ps1 turns it back on).
# (cloudflared and playit, when they run as Windows services, are left running.)
#
#   powershell -ExecutionPolicy Bypass -File scripts\stop.ps1

$task = Get-ScheduledTask -TaskName "Low Strike online server" -ErrorAction SilentlyContinue
if ($task) {
    try {
        Disable-ScheduledTask -TaskName $task.TaskName -ErrorAction Stop | Out-Null
        Stop-ScheduledTask -TaskName $task.TaskName -ErrorAction SilentlyContinue
        Write-Host "Autostart task paused"
    }
    catch {
        Write-Host "Could not pause the autostart task (it was installed as administrator: run this as administrator too)."
        Write-Host "Otherwise it starts the server again within 5 minutes."
    }
}
$processes = Get-CimInstance Win32_Process | Where-Object {
    ($_.Name -like "python*.exe" -and $_.CommandLine -like "*lowstrike_server.py*") -or
    ($_.Name -like "LowStrike*.exe" -and $_.CommandLine -like "*-ds-server*")
}
foreach ($process in $processes) {
    Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
    Write-Host "Stopped $($process.Name) ($($process.ProcessId))"
}
if (-not $processes) { Write-Host "The server was not running." }

# Removes the scheduled tasks made by install-autostart.ps1 and stops the server (scripts\stop.ps1).
# (Run as administrator if they were installed as administrator.)
#
#   powershell -ExecutionPolicy Bypass -File scripts\uninstall-autostart.ps1

foreach ($name in @("Low Strike online server", "Low Strike playit agent", "Low Strike cloudflared")) {
    if (Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue) {
        Stop-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue
        Unregister-ScheduledTask -TaskName $name -Confirm:$false
        Write-Host "Removed task '$name'"
    }
}
& (Join-Path $PSScriptRoot "stop.ps1")

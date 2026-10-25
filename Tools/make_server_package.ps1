# Makes Builds\LowStrike-Server.zip: everything the online server PC needs, in one file to copy over.
#   LowStrike-Server\game\            the Windows build of the game (Builds\Windows), which runs the rooms
#   LowStrike-Server\online-server\   the API, its scripts and README (without .venv, .env, data or logs)
#   LowStrike-Server\VERSION.txt      the game version (setup.ps1 puts it into .env)
# Build Windows first (DesertStrikeSetup.BuildWindows).
# -Linux makes Builds\LowStrike-Server-Linux.tar.gz instead, for a Linux server: game\ holds the Linux dedicated
# server build (Builds\LinuxServer, DesertStrikeSetup.BuildLinuxServer), and scripts\linux\setup.sh sets it up.
#
#   powershell -ExecutionPolicy Bypass -File Tools\make_server_package.ps1 [-Linux]
param([switch]$Linux)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$build = Join-Path $repo $(if ($Linux) { "Builds\LinuxServer" } else { "Builds\Windows" })
$gameFile = if ($Linux) { "LowStrikeServer.x86_64" } else { "LowStrike.exe" }
if (-not (Test-Path (Join-Path $build $gameFile))) { throw "No build in $($build): build it first." }
$version = [regex]::Match((Get-Content (Join-Path $repo "Assets\Editor\DesertStrikeSetup.cs") -Raw), 'GameVersion = "([^"]+)"').Groups[1].Value

$stage = Join-Path $env:TEMP ("LowStrike-Server-" + [guid]::NewGuid().ToString("N"))
$top = Join-Path $stage "LowStrike-Server"
New-Item -ItemType Directory -Force (Join-Path $top "game"), (Join-Path $top "online-server") | Out-Null
Get-ChildItem $build | Where-Object { $_.Name -notlike "*DoNotShip*" } | Copy-Item -Destination (Join-Path $top "game") -Recurse
$server = Join-Path $repo "online-server"
# Every module of the server (so a new one is never left out: the API would not start), and what goes with them.
$items = @(Get-ChildItem $server -Filter "*.py" | ForEach-Object { $_.Name }) + @("world_rules.json", "requirements.txt", ".env.example",
                                                                                   "README.md", "scripts", "tests")
foreach ($item in $items) {
    Copy-Item (Join-Path $server $item) -Destination (Join-Path $top "online-server") -Recurse
}
Get-ChildItem (Join-Path $top "online-server") -Recurse -Directory -Filter "__pycache__" | Remove-Item -Recurse -Force
Set-Content -Path (Join-Path $top "VERSION.txt") -Value $version -Encoding ASCII
if ($Linux) {
    # Shell scripts must keep LF line ends on Linux; the tar keeps the game host runnable (setup.sh also chmods it).
    Get-ChildItem (Join-Path $top "online-server") -Recurse -Include *.sh, *.service, *.py, .env.example, requirements.txt |
        ForEach-Object { [IO.File]::WriteAllText($_.FullName, ([IO.File]::ReadAllText($_.FullName) -replace "`r`n", "`n")) }
    [IO.File]::WriteAllText((Join-Path $top "START HERE.txt"), ("Low Strike $version online server (Linux)`n`n" +
        "1. Unpack this folder somewhere that stays, e.g. /opt/lowstrike`n" +
        "2. Read online-server/README.md (the steps are the same; the commands are in online-server/scripts/linux).`n" +
        "   First command:  sh /opt/lowstrike/online-server/scripts/linux/setup.sh`n" +
        "3. Start it:       sh /opt/lowstrike/online-server/scripts/linux/start.sh`n" +
        "   or as a service that starts with the machine: online-server/scripts/linux/lowstrike.service`n"))
    $tar = Join-Path $repo "Builds\LowStrike-Server-Linux.tar.gz"
    if (Test-Path $tar) { Remove-Item $tar -Force }
    & (Join-Path $env:SystemRoot "System32\tar.exe") -czf $tar -C $stage "LowStrike-Server"   # Windows' own (Git's GNU tar takes C: for a host)
    if ($LASTEXITCODE -ne 0) { throw "tar failed" }
    Remove-Item $stage -Recurse -Force
    "{0} ({1:N0} MB, Low Strike {2}, Linux)" -f $tar, ((Get-Item $tar).Length / 1MB), $version
    return
}
Set-Content -Path (Join-Path $top "START HERE.txt") -Encoding UTF8 -Value @"
Low Strike $version online server

1. Put this folder somewhere that stays, e.g. C:\LowStrike-Server
2. Read online-server\README.md and follow steps 1 to 6.
   First command (PowerShell):
   powershell -ExecutionPolicy Bypass -File C:\LowStrike-Server\online-server\scripts\setup.ps1
"@

$zip = Join-Path $repo "Builds\LowStrike-Server.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $top -DestinationPath $zip -CompressionLevel Optimal
Remove-Item $stage -Recurse -Force
"{0} ({1:N0} MB, Low Strike {2})" -f $zip, ((Get-Item $zip).Length / 1MB), $version

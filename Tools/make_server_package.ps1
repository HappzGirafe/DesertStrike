# Makes Builds\LowStrike-Server.zip: everything the online server PC needs, in one file to copy over.
#   LowStrike-Server\game\            the Windows build of the game (Builds\Windows), which runs the rooms
#   LowStrike-Server\online-server\   the API, its scripts and README (without .venv, .env, data or logs)
#   LowStrike-Server\VERSION.txt      the game version (setup.ps1 puts it into .env)
# Build Windows first (DesertStrikeSetup.BuildWindows).
#
#   powershell -ExecutionPolicy Bypass -File Tools\make_server_package.ps1

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$build = Join-Path $repo "Builds\Windows"
if (-not (Test-Path (Join-Path $build "LowStrike.exe"))) { throw "No Windows build in Builds\Windows: build it first." }
$version = [regex]::Match((Get-Content (Join-Path $repo "Assets\Editor\DesertStrikeSetup.cs") -Raw), 'GameVersion = "([^"]+)"').Groups[1].Value

$stage = Join-Path $env:TEMP ("LowStrike-Server-" + [guid]::NewGuid().ToString("N"))
$top = Join-Path $stage "LowStrike-Server"
New-Item -ItemType Directory -Force (Join-Path $top "game"), (Join-Path $top "online-server") | Out-Null
Get-ChildItem $build | Where-Object { $_.Name -notlike "*DoNotShip*" } | Copy-Item -Destination (Join-Path $top "game") -Recurse
$server = Join-Path $repo "online-server"
foreach ($item in @("lowstrike_server.py", "living_world.py", "world_rules.json", "bot_knowledge.py", "reviews.py",
                    "requirements.txt", ".env.example", "README.md", "scripts", "tests")) {
    Copy-Item (Join-Path $server $item) -Destination (Join-Path $top "online-server") -Recurse
}
Get-ChildItem (Join-Path $top "online-server") -Recurse -Directory -Filter "__pycache__" | Remove-Item -Recurse -Force
Set-Content -Path (Join-Path $top "VERSION.txt") -Value $version -Encoding ASCII
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

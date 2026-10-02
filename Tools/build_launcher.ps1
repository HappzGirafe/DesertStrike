# Builds Builds/DesertStrike-Windows.exe: one file that carries the whole Windows game (Builds/Windows)
# and starts it. Run after "Desert Strike > Build Windows Game". Uses the C# compiler that ships with Windows.
#   powershell -ExecutionPolicy Bypass -File Tools/build_launcher.ps1
$ErrorActionPreference = "Stop"
$project = Split-Path $PSScriptRoot -Parent
$game = Join-Path $project "Builds\Windows"
$work = Join-Path $project "Logs\launcher"
$output = Join-Path $project "Builds\DesertStrike-Windows.exe"
if (-not (Test-Path (Join-Path $game "DesertStrike.exe"))) { throw "Build the Windows game first (Builds\Windows\DesertStrike.exe is missing)." }

New-Item -ItemType Directory -Force $work | Out-Null
$zip = Join-Path $work "game.zip"
$icon = Join-Path $work "game.ico"
Compress-Archive -Path (Get-ChildItem $game).FullName -DestinationPath $zip -Force

# Reuse the game's own icon for the launcher.
Add-Type -AssemblyName System.Drawing
$file = [System.IO.File]::Create($icon)
[System.Drawing.Icon]::ExtractAssociatedIcon((Join-Path $game "DesertStrike.exe")).Save($file)
$file.Close()

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /target:winexe /optimize "/out:$output" "/win32icon:$icon" "/resource:$zip,game.zip" `
    /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll `
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll `
    (Join-Path $PSScriptRoot "Launcher\Launcher.cs")
if ($LASTEXITCODE -ne 0) { throw "Compiling the launcher failed." }
"Built {0} ({1:N1} MB)" -f $output, ((Get-Item $output).Length / 1MB)

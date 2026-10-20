# Makes Builds/Closed-Maps/<map>_closed.blend for every map: the map with the walls that close each half of it in
# small matches (collections "Closed A" and "Closed B"). Needs the Windows game (Builds/Windows) and Blender 5.2.
#   powershell -ExecutionPolicy Bypass -File Tools/closed_maps.ps1
$project = Split-Path $PSScriptRoot -Parent
$game = Join-Path $project "Builds\Windows\LowStrike.exe"
$blender = "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
$work = Join-Path $project "Logs\closed-maps"
$out = Join-Path $project "Builds\Closed-Maps"
# Map id -> its .blend in Art/Blender (de_dune is built from blocks: the game writes it as an .obj).
$maps = [ordered]@{ dune = ""; village = "village_map.blend"; halloween = "halloween_map.blend"; industrial = "industrial_map.blend" }

foreach ($map in $maps.Keys) {
    $folder = Join-Path $work $map
    New-Item -ItemType Directory -Force $folder | Out-Null
    Start-Process -Wait -FilePath $game -ArgumentList @("-batchmode", "-logFile", "`"$folder\game.log`"",
        "-ds-map", $map, "-ds-export-closed", "`"$folder`"")
    Select-String -Path "$folder\game.log" -Pattern "\[Map\] (Exported|The . side|Site . closed)" | ForEach-Object { $_.Line }
    $json = Join-Path $folder "$map.json"
    if (-not (Test-Path $json)) { Write-Warning "$map was not exported (see $folder\game.log)"; continue }

    $target = Join-Path $out "${map}_closed.blend"
    $script = Join-Path $PSScriptRoot "closed_maps_blend.py"
    if ($maps[$map]) {
        & $blender -b -Y (Join-Path $project "Art\Blender\$($maps[$map])") --python $script -- $json $target 2>&1 |
            ForEach-Object { "$_" } | Select-String "closed maps|Error|Traceback" | ForEach-Object { $_.Line }
    } else {
        & $blender -b -Y --factory-startup --python $script -- $json $target (Join-Path $folder "$map.obj") 2>&1 |
            ForEach-Object { "$_" } | Select-String "closed maps|Error|Traceback" | ForEach-Object { $_.Line }
    }
}

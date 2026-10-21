# Re-exports the Blender maps to the game (needs Blender 5.2): Assets/Resources/Maps/<id>, and each map's own
# versions for small matches with one site's half shut by hand (Maps/<id>/closedA, closedB; a map without one gets
# walls from the game). Only the maps named on the command line, or all of them:
#   powershell -ExecutionPolicy Bypass -File Tools/export_maps.ps1 [dune bone ...]
param([Parameter(ValueFromRemainingArguments = $true)][string[]] $only)
$project = Split-Path $PSScriptRoot -Parent
$blender = "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
# Map id, folder under Maps/<id> ("" for the map itself, closedA / closedB), its .blend in Art/Blender, and the
# markers Tools/maps/<name>.json adds (spawns and sites for a map made without them; "" for none).
$maps = @(
    @("village", "", "village_map.blend", ""),
    @("halloween", "", "halloween_map.blend", ""),
    @("halloween", "closedA", "halloween_closedA.blend", ""),
    @("halloween", "closedB", "halloween_closedB.blend", ""),
    @("industrial", "", "industrialnorm.blend", ""),
    @("industrial", "closedB", "industrial_closedB.blend", ""),
    @("dune", "", "dunenorm.blend", "dune"),
    @("dune", "closedA", "dune_closedA.blend", "dune_closed"),
    @("dune", "closedB", "dune_closedB.blend", "dune_closed"),
    @("bone", "", "bone_map.blend", "bone")
)
foreach ($map in $maps) {
    $id, $version, $blend, $markers = $map
    if ($only -and $only -notcontains $id) { continue }
    $out = Join-Path $project "Assets\Resources\Maps\$id"
    $extra = @()
    if ($version) {
        $extra += @("--reuse-textures", $out)   # the map's own textures stay in Maps/<id>
        $out = Join-Path $out $version
    }
    if ($markers) { $extra += @("--markers", (Join-Path $PSScriptRoot "maps\$markers.json")) }
    # -Y: scripts inside the .blend do not run.
    & $blender -b -Y (Join-Path $project "Art\Blender\$blend") --python (Join-Path $PSScriptRoot "map_markers.py") `
        --python (Join-Path $PSScriptRoot "export_map_fbx.py") -- $out @extra 2>&1 |
        ForEach-Object { "$_" } | Select-String "Exported|map markers|Split|not exported|found by name|turned|Missing|Error|Traceback" | ForEach-Object { $_.Line }
}

# Re-exports every weapon model from Art/Blender to the game (needs Blender 5.2).
#   powershell -ExecutionPolicy Bypass -File Tools/export_models.ps1
# Add a line here for each new model. --roles says which objects are the weapon's body (Main) and grip/stock
# (Grip); every other object is a Detail. --copy-textures makes the model's own textures its default look.
$project = Split-Path $PSScriptRoot -Parent
$blender = "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
$models = @(
    # Default models of the weapons (Assets/Resources/Models/<weapon name>/model.fbx)
    @("Glock18.blend", "Assets/Resources/Models/Glock-18/model.fbx"),
    @("USP.blend", "Assets/Resources/Models/USP/model.fbx"),
    @("Tec9.blend", "Assets/Resources/Models/TEC-DC9/model.fbx", "--roles", "Main=Cube.002;Grip=Cube", "--copy-textures"),
    @("m1911.blend", "Assets/Resources/Models/M1911/model.fbx", "--roles", "Main=Cube.002;Grip=Cube", "--copy-textures"),
    @("mp5.blend", "Assets/Resources/Models/MP5/model.fbx", "--roles", "Main=Cube.006,Cube.003,Cube.005;Grip=Cube.008,Cube.004", "--copy-textures"),
    @("M4A1.blend", "Assets/Resources/Models/M4A1/model.fbx", "--roles", "Main=Cube.002,Cube.001;Grip=Cube.006,Cube.008", "--copy-textures"),
    @("ak 47.blend", "Assets/Resources/Models/AK-47/model.fbx", "--roles", "Main=Cube,Cylinder,Cylinder.001;Grip=Cube.002,Cube.001,Cube.007", "--copy-textures"),
    @("AWP.blend", "Assets/Resources/Models/AWP/model.fbx", "--roles", "Main=Cube,Cube.001", "--copy-textures"),
    @("digle_default.blend", "Assets/Resources/Models/Desert Eagle/model.fbx", "--roles", "Main=Cube.001;Grip=Cube", "--copy-textures"),
    # Skins that bring their own model (Assets/Resources/Skins/<weapon name>/<skin name>/model.fbx)
    @("nogektestskin.blend", "Assets/Resources/Skins/Knife/nogektestskin/model.fbx"),
    @("nogenkerambit.blend", "Assets/Resources/Skins/Knife/karambit/model.fbx", "--roles", "Blade=Cube.008;Grip=Cube.001"),
    @("digle.blend", "Assets/Resources/Skins/Desert Eagle/digle/model.fbx")
)
foreach ($model in $models) {
    $blend = Join-Path $project "Art\Blender\$($model[0])"
    $output = Join-Path $project $model[1]
    $extra = @($model | Select-Object -Skip 2)
    & $blender -b $blend --python (Join-Path $PSScriptRoot "export_gun_fbx.py") -- $output @extra 2>&1 |
        ForEach-Object { "$_" } | Select-String "Exported|Wrote parts|Error|Traceback" | ForEach-Object { $_.Line }
}

# Re-exports every weapon model from Art/Blender to the game (needs Blender 5.2).
#   powershell -ExecutionPolicy Bypass -File Tools/export_models.ps1
# Add a line here for each new model. --roles says which objects are the weapon's body (Main) and grip/stock
# (Grip); every other object is a Detail. --copy-textures makes the model's own textures its default look.
$project = Split-Path $PSScriptRoot -Parent
$blender = "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
$models = @(
    # Default models of the weapons (Assets/Resources/Models/<weapon name>/model.fbx)
    @("Glock18.blend", "Assets/Resources/Models/Glock-18/model.fbx", "--roles", "Main=Cube.001,Cube.008;Grip=Cube.005", "--copy-textures"),
    @("USP.blend", "Assets/Resources/Models/USP/model.fbx", "--roles", "Main=Cube.002,Cube.003;Grip=Cube.006", "--copy-textures"),
    @("Tec9.blend", "Assets/Resources/Models/TEC-DC9/model.fbx", "--roles", "Main=Cube.002;Grip=Cube", "--copy-textures"),
    @("m1911.blend", "Assets/Resources/Models/M1911/model.fbx", "--roles", "Main=Cube.002;Grip=Cube", "--copy-textures"),
    @("mp5.blend", "Assets/Resources/Models/MP5/model.fbx", "--roles", "Main=Cube.006,Cube.003,Cube.005;Grip=Cube.008,Cube.004", "--copy-textures"),
    @("M4A1.blend", "Assets/Resources/Models/M4A1/model.fbx", "--roles", "Main=Cube.002,Cube.001;Grip=Cube.006,Cube.008", "--copy-textures"),
    @("ak47.blend", "Assets/Resources/Models/AK-47/model.fbx", "--roles", "Main=Cube.003,Cylinder.001;Grip=Cube.001,Cube.002,Cube.004", "--copy-textures"),
    @("AWP.blend", "Assets/Resources/Models/AWP/model.fbx", "--roles", "Main=Cube,Cube.001", "--copy-textures"),
    @("digle.blend", "Assets/Resources/Models/Desert Eagle/model.fbx", "--roles", "Main=Cube.007,Cube.001;Grip=Cube.003,Cube.004", "--copy-textures"),
    @("pumpshotgun.blend", "Assets/Resources/Models/Pump Shotgun/model.fbx", "--roles", "Main=Cube.002,Cylinder,Cylinder.001;Grip=Cube.010,Cube.001,Cube.004", "--copy-textures"),
    @("Usi.blend", "Assets/Resources/Models/Uzi/model.fbx", "--roles", "Main=Cube.002;Grip=Cube", "--copy-textures"),
    @("SVD.blend", "Assets/Resources/Models/SVD/model.fbx", "--roles", "Main=Cube,Cube.004,Cube.009;Grip=Cube.001,Cube.002,Cube.003,Cube.014", "--copy-textures"),
    # Skins that bring their own model (Assets/Resources/Skins/<weapon name>/<skin name>/model.fbx)
    @("nogektestskin.blend", "Assets/Resources/Skins/Knife/nogektestskin/model.fbx"),
    @("nogenkerambit.blend", "Assets/Resources/Skins/Knife/karambit/model.fbx", "--roles", "Blade=Cube.008;Grip=Cube.001"),
    @("digle.blend", "Assets/Resources/Skins/Desert Eagle/digle/model.fbx"),
    # The RPG's Web skin and the net it fires (projectile.fbx: what the skin shoots instead of the rocket)
    @("rpg_web_launcher.blend", "Assets/Resources/Skins/RPG/Web/model.fbx", "--roles", "Main=RPG_Web_tube,RPG_Web_black;Grip=RPG_Web_wood", "--copy-textures"),
    @("rpg_web_net.blend", "Assets/Resources/Skins/RPG/Web/projectile.fbx", "--copy-textures"),
    # Halloween skins and the classic knife, made on another computer (their pictures were in a folder "текстури"):
    # "--stand-ins" exports a copy whose pictures are found by name in Art/Textures, and any picture still missing is
    # a plain colour (Tools/stand_in_textures.py).
    @("ak47hellovin.blend", "Assets/Resources/Skins/AK-47/Halloween/model.fbx", "--roles", "Main=Cube.003,Cube.004;Grip=Cube.001,Cube.005", "--copy-textures", "--stand-ins"),
    @("M4A1hellovin.blend", "Assets/Resources/Skins/M4A1/Halloween/model.fbx", "--roles", "Main=Cube.002,Cube.001;Grip=Cube.008,Cube.006", "--copy-textures", "--stand-ins"),
    @("klasiknogen.blend", "Assets/Resources/Skins/Knife/Classic/model.fbx", "--roles", "Blade=Cube.001;Grip=Cube.002", "--copy-textures", "--stand-ins"),
    @("hellovinklasiknogen.blend", "Assets/Resources/Skins/Knife/Classic Halloween/model.fbx", "--roles", "Blade=Cube.001;Grip=Cube.002", "--copy-textures", "--stand-ins")
)
foreach ($model in $models) {
    $blend = Join-Path $project "Art\Blender\$($model[0])"
    $output = Join-Path $project $model[1]
    $extra = @($model | Select-Object -Skip 2 | Where-Object { $_ -ne "--stand-ins" })
    if ($model -contains "--stand-ins") {
        $copies = Join-Path $project "Logs\export-copies"
        New-Item -ItemType Directory -Force $copies | Out-Null
        $copy = Join-Path $copies $model[0]
        & $blender -b -Y $blend --python (Join-Path $PSScriptRoot "stand_in_textures.py") -- $copy 2>&1 |
            ForEach-Object { "$_" } | Select-String "stand-ins" | ForEach-Object { $_.Line }
        $blend = $copy
    }
    & $blender -b $blend --python (Join-Path $PSScriptRoot "export_gun_fbx.py") -- $output @extra 2>&1 |
        ForEach-Object { "$_" } | Select-String "Exported|Wrote parts|Error|Traceback" | ForEach-Object { $_.Line }
}

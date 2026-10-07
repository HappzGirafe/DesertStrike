using UnityEditor;

/// <summary>
/// Import settings for the Blender maps (Assets/Resources/Maps/&lt;id&gt;/model.fbx): the meshes stay readable, because
/// the game gives them colliders, reads the marked areas from them and merges them for drawing at runtime; no
/// materials are imported, because the game makes them from parts.json.
/// </summary>
public class MapImportSettings : AssetPostprocessor
{
    void OnPreprocessModel()
    {
        if (!assetPath.StartsWith("Assets/Resources/Maps/")) return;
        var importer = (ModelImporter)assetImporter;
        importer.isReadable = true;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importAnimation = false;
        importer.importCameras = false;
        importer.importLights = false;
    }
}

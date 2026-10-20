using UnityEditor;
using UnityEngine;

/// <summary>
/// Import settings for the Blender maps (Assets/Resources/Maps/&lt;id&gt;/model.fbx): the meshes stay readable, because
/// the game gives them colliders, reads the marked areas from them and merges them for drawing at runtime; no
/// materials are imported, because the game makes them from parts.json. A colour palette (a texture named
/// "...Palette...", where each part's UVs point at one pixel, as in de_dune) keeps its exact pixels: no blurring, no
/// smaller copies and no compression.
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

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith("Assets/Resources/Maps/") || !System.IO.Path.GetFileName(assetPath).Contains("Palette")) return;
        var importer = (TextureImporter)assetImporter;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.wrapMode = TextureWrapMode.Clamp;
    }
}

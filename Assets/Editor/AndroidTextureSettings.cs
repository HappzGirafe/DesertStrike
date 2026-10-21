using UnityEditor;

/// <summary>
/// Every compressed picture of the game gets ASTC 6x6 on Android, named per picture rather than left to the build's
/// texture compression setting: after building for Linux that setting once made every picture go into the .apk raw
/// (RGB24: 128 MB instead of 48). Pictures kept uncompressed on purpose (the maps' palettes, MapImportSettings) stay so.
/// </summary>
public class AndroidTextureSettings : AssetPostprocessor
{
    public override uint GetVersion() => 1;   // a new version imports the pictures again

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith("Assets/Resources/")) return;
        var importer = (TextureImporter)assetImporter;
        if (importer.textureCompression == TextureImporterCompression.Uncompressed) return;
        var android = importer.GetPlatformTextureSettings("Android");
        android.overridden = true;
        android.format = TextureImporterFormat.ASTC_6x6;
        importer.SetPlatformTextureSettings(android);
    }
}

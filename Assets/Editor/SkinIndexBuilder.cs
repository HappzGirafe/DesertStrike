using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes Assets/Resources/Skins/index.json from the skin folders (Skins/&lt;weapon name&gt;/&lt;skin name&gt;/) and the
/// default model folders (Models/&lt;weapon name&gt;/model.fbx), so the game knows what exists. It runs by itself
/// whenever something in those folders changes, and before every build.
/// </summary>
public class SkinIndexBuilder : AssetPostprocessor
{
    const string SkinsFolder = "Assets/Resources/Skins";
    const string ModelsFolder = "Assets/Resources/Models";
    const string IndexPath = SkinsFolder + "/index.json";

    [MenuItem("Low Strike/Rebuild Skin Index")]
    public static void Build()
    {
        var index = new SkinIndex();
        if (Directory.Exists(ModelsFolder))
        {
            foreach (var modelDir in Directory.GetDirectories(ModelsFolder).OrderBy(d => d))
            {
                string weaponFolder = Path.GetFileName(modelDir);
                var weapon = WeaponData.Find(weaponFolder);
                if (weapon == null)
                {
                    Debug.LogWarning($"[Skins] Models folder '{weaponFolder}' is not a weapon name; skipping it.");
                    continue;
                }
                if (HasFile(modelDir, "model", ".fbx", ".obj"))
                    index.models.Add(new ModelIndexEntry { weapon = weapon.Id, path = $"Models/{weaponFolder}/model" });
            }
        }
        if (Directory.Exists(SkinsFolder))
        {
            foreach (var weaponDir in Directory.GetDirectories(SkinsFolder).OrderBy(d => d))
            {
                string weaponFolder = Path.GetFileName(weaponDir);
                var weapon = WeaponData.Find(weaponFolder);
                if (weapon == null)
                {
                    Debug.LogWarning($"[Skins] Folder '{weaponFolder}' is not a weapon name (e.g. 'AK-47', 'Glock-18', 'Knife'); skipping it.");
                    continue;
                }

                // "Default" first (it is the weapon's default skin), then alphabetical.
                var skinDirs = Directory.GetDirectories(weaponDir)
                    .OrderBy(d => Path.GetFileName(d) == "Default" ? "" : Path.GetFileName(d));
                foreach (var skinDir in skinDirs)
                {
                    string id = Path.GetFileName(skinDir);
                    var info = new SkinInfo();
                    string json = Path.Combine(skinDir, "skin.json");
                    if (File.Exists(json)) JsonUtility.FromJsonOverwrite(File.ReadAllText(json), info);
                    if (string.IsNullOrEmpty(info.name)) info.name = id;

                    index.skins.Add(new SkinIndexEntry
                    {
                        weapon = weapon.Id,
                        id = id,
                        folder = $"Skins/{weaponFolder}/{id}",
                        hasModel = HasFile(skinDir, "model", ".fbx", ".obj"),
                        hasMain = HasFile(skinDir, "main", ".png", ".jpg", ".jpeg"),
                        hasGrip = HasFile(skinDir, "grip", ".png", ".jpg", ".jpeg"),
                        hasDetail = HasFile(skinDir, "detail", ".png", ".jpg", ".jpeg"),
                        hasProjectile = HasFile(skinDir, "projectile", ".fbx", ".obj"),
                        info = info,
                    });
                }
            }
        }

        string text = JsonUtility.ToJson(index, true);
        if (File.Exists(IndexPath) && File.ReadAllText(IndexPath) == text) return;
        File.WriteAllText(IndexPath, text);
        AssetDatabase.ImportAsset(IndexPath);
        Debug.Log($"[Skins] Skin index updated: {index.skins.Count} skins, {index.models.Count} default models.");
    }

    static bool HasFile(string folder, string name, params string[] extensions) =>
        extensions.Any(extension => File.Exists(Path.Combine(folder, name + extension)));

    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        bool skinsChanged = imported.Concat(deleted).Concat(moved).Concat(movedFrom)
            .Any(path => (path.StartsWith(SkinsFolder + "/") || path.StartsWith(ModelsFolder + "/")) && path != IndexPath);
        if (skinsChanged) EditorApplication.delayCall += Build;
    }
}

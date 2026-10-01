using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>The optional skin.json in a skin folder. Every field is optional.</summary>
[Serializable]
public class SkinInfo
{
    public string name;               // display name (default: the folder name)
    public string main;               // "#RRGGBB"; tints the texture when the part has one
    public string grip;
    public string detail;
    public float smoothness = 0.2f;
    public float metallic;
}

/// <summary>One skin folder, as listed in Skins/index.json (written by the editor, see SkinIndexBuilder).</summary>
[Serializable]
public class SkinIndexEntry
{
    public string weapon;             // weapon id
    public string id;                 // skin folder name
    public string folder;             // Resources path of the skin folder
    public bool hasModel, hasMain, hasGrip, hasDetail;
    public SkinInfo info;
}

[Serializable]
public class SkinIndex
{
    public List<SkinIndexEntry> skins = new List<SkinIndexEntry>();
}

/// <summary>Parts a skin can paint: main = slide/body/blade, grip = grip/stock/handle, detail = barrel/mag/scope/guard.</summary>
public enum SkinPart { Main, Grip, Detail }

public class WeaponSkin
{
    public string Id;
    public string Name;
    public string WeaponId;
    public string Model;                                   // Resources path of a replacement model, or null
    public readonly string[] Textures = new string[3];     // per SkinPart: Resources path, or null
    public readonly Color?[] Colors = new Color?[3];       // per SkinPart: color (tint), or null
    public float Smoothness = 0.2f;
    public float Metallic;
}

/// <summary>
/// The weapon skins and which one the player has equipped on each weapon (saved between sessions).
///
/// Skins are folders: Assets/Resources/Skins/&lt;weapon name&gt;/&lt;skin name&gt;/ holding any of
/// main.png, grip.png, detail.png, model.fbx and skin.json. The editor lists them in Skins/index.json;
/// the "Default" folder comes first and is the weapon's default skin.
/// </summary>
public static class WeaponSkins
{
    const string Root = "Skins";

    // Weapons whose own model comes from Blender (weapon id -> Resources path). Others use box models.
    static readonly Dictionary<string, string> ModelPaths = new Dictionary<string, string>
    {
        { "glock", "Models/Glock18" },
        { "usp", "Models/USP" },
    };

    static Dictionary<string, List<WeaponSkin>> byWeapon;
    static WeaponData[] skinnable;
    static readonly Dictionary<string, GameObject> models = new Dictionary<string, GameObject>();
    static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

    /// <summary>Weapons that have at least one skin, in shop order.</summary>
    public static WeaponData[] SkinnableWeapons
    {
        get
        {
            if (skinnable == null)
                skinnable = Array.FindAll(WeaponData.All, weapon => For(weapon).Count > 0);
            return skinnable;
        }
    }

    public static List<WeaponSkin> For(WeaponData weapon)
    {
        if (byWeapon == null) LoadIndex();
        return byWeapon.TryGetValue(weapon.Id, out var skins) ? skins : new List<WeaponSkin>();
    }

    /// <summary>The weapon's default skin, or null when it has no skins.</summary>
    public static WeaponSkin DefaultFor(WeaponData weapon)
    {
        var skins = For(weapon);
        return skins.Count > 0 ? skins[0] : null;
    }

    public static WeaponSkin Equipped(WeaponData weapon)
    {
        string id = PlayerPrefs.GetString(PrefsKey(weapon), "");
        foreach (var skin in For(weapon))
            if (string.Equals(skin.Id, id, StringComparison.OrdinalIgnoreCase)) return skin;
        return DefaultFor(weapon);
    }

    public static void Equip(WeaponData weapon, WeaponSkin skin)
    {
        PlayerPrefs.SetString(PrefsKey(weapon), skin.Id);
        PlayerPrefs.Save();
    }

    /// <summary>A skin chosen by <paramref name="seed"/>, so a bot keeps the same look all match.</summary>
    public static WeaponSkin Pick(WeaponData weapon, int seed)
    {
        var skins = For(weapon);
        return skins.Count > 0 ? skins[seed % skins.Count] : null;
    }

    /// <summary>The model to build for this weapon and skin, or null to use the box model.</summary>
    public static GameObject LoadModel(WeaponData weapon, WeaponSkin skin)
    {
        string path = skin?.Model;
        if (path == null && !ModelPaths.TryGetValue(weapon.Id, out path)) return null;
        if (!models.TryGetValue(path, out var model))
        {
            model = Resources.Load<GameObject>(path);
            models[path] = model;
        }
        return model;
    }

    /// <summary>The skin's material for a part, or null when the skin leaves that part as it is.</summary>
    public static Material MaterialFor(WeaponSkin skin, SkinPart part)
    {
        if (skin == null) return null;
        string key = $"{skin.WeaponId}/{skin.Id}/{part}";
        if (!materials.TryGetValue(key, out var material))
        {
            string texturePath = skin.Textures[(int)part];
            var texture = texturePath != null ? Resources.Load<Texture2D>(texturePath) : null;
            Color? color = skin.Colors[(int)part];
            material = texture == null && color == null
                ? null
                : Effects.SkinMaterial(color ?? Color.white, texture, skin.Smoothness, skin.Metallic);
            materials[key] = material;
        }
        return material;
    }

    static void LoadIndex()
    {
        byWeapon = new Dictionary<string, List<WeaponSkin>>();
        var asset = Resources.Load<TextAsset>(Root + "/index");
        if (asset == null)
        {
            Debug.LogWarning("[Skins] Skins/index.json is missing; use Desert Strike > Rebuild Skin Index in the editor.");
            return;
        }

        foreach (var entry in JsonUtility.FromJson<SkinIndex>(asset.text).skins)
        {
            var info = entry.info ?? new SkinInfo();
            var skin = new WeaponSkin
            {
                Id = entry.id,
                Name = string.IsNullOrEmpty(info.name) ? entry.id : info.name,
                WeaponId = entry.weapon,
                Model = entry.hasModel ? entry.folder + "/model" : null,
                Smoothness = info.smoothness,
                Metallic = info.metallic,
            };
            if (entry.hasMain) skin.Textures[(int)SkinPart.Main] = entry.folder + "/main";
            if (entry.hasGrip) skin.Textures[(int)SkinPart.Grip] = entry.folder + "/grip";
            if (entry.hasDetail) skin.Textures[(int)SkinPart.Detail] = entry.folder + "/detail";
            skin.Colors[(int)SkinPart.Main] = ParseColor(info.main);
            skin.Colors[(int)SkinPart.Grip] = ParseColor(info.grip);
            skin.Colors[(int)SkinPart.Detail] = ParseColor(info.detail);

            if (!byWeapon.TryGetValue(entry.weapon, out var list)) byWeapon[entry.weapon] = list = new List<WeaponSkin>();
            list.Add(skin);
        }
    }

    static Color? ParseColor(string html) =>
        !string.IsNullOrEmpty(html) && ColorUtility.TryParseHtmlString(html, out var color) ? color : (Color?)null;

    static string PrefsKey(WeaponData weapon) => "DesertStrike.skin." + weapon.Id;
}

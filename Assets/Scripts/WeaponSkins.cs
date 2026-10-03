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
    public float[] rotation;          // [x, y, z] degrees: turns a skin's model in the hand, around the grip
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

/// <summary>A weapon's default model: Resources path of Models/&lt;weapon name&gt;/model.fbx.</summary>
[Serializable]
public class ModelIndexEntry
{
    public string weapon;
    public string path;
}

[Serializable]
public class SkinIndex
{
    public List<SkinIndexEntry> skins = new List<SkinIndexEntry>();
    public List<ModelIndexEntry> models = new List<ModelIndexEntry>();
}

/// <summary>One part's own look from Blender, as Tools/export_gun_fbx.py writes it to parts.json.</summary>
[Serializable]
public class PartLook
{
    public string name;
    public string texture;
    public string color;
    public float metallic;
    public float smoothness = 0.2f;
}

[Serializable]
class PartLooks
{
    public List<PartLook> parts = new List<PartLook>();
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
    public Vector3 Rotation;                               // extra turn of the model in the hand (degrees)
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

    static Dictionary<string, List<WeaponSkin>> byWeapon;
    static readonly Dictionary<string, string> defaultModels = new Dictionary<string, string>();   // weapon id -> Resources path
    static readonly Dictionary<string, Dictionary<string, PartLook>> partLooks = new Dictionary<string, Dictionary<string, PartLook>>();
    static WeaponData[] skinnable;
    static readonly Dictionary<string, GameObject> models = new Dictionary<string, GameObject>();
    static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
    static readonly Dictionary<string, string> runChoices = new Dictionary<string, string>();   // weapon id -> skin id

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
        if (runChoices.TryGetValue(weapon.Id, out var chosen) && Find(weapon, chosen) is WeaponSkin forRun) return forRun;
        string id = PlayerPrefs.GetString(PrefsKey(weapon), "");
        foreach (var skin in For(weapon))
            if (string.Equals(skin.Id, id, StringComparison.OrdinalIgnoreCase)) return skin;
        return DefaultFor(weapon);
    }

    /// <summary>Uses this skin for this run only, without saving (the -ds-equip test option).</summary>
    public static void EquipThisRun(WeaponData weapon, string skinId) => runChoices[weapon.Id] = skinId;

    public static void Equip(WeaponData weapon, WeaponSkin skin)
    {
        PlayerPrefs.SetString(PrefsKey(weapon), skin.Id);
        PlayerPrefs.Save();
    }

    public static WeaponSkin Find(WeaponData weapon, string id)
    {
        foreach (var skin in For(weapon))
            if (string.Equals(skin.Id, id, StringComparison.OrdinalIgnoreCase)) return skin;
        return null;
    }

    /// <summary>The skin the player has equipped on every weapon: weapon id -> skin id.</summary>
    public static Dictionary<string, string> EquippedChoices()
    {
        var choices = new Dictionary<string, string>();
        foreach (var weapon in WeaponData.All)
        {
            var skin = Equipped(weapon);
            if (skin != null) choices[weapon.Id] = skin.Id;
        }
        return choices;
    }

    /// <summary>"glock=Pink Scribble;knife=nogektestskin", to send the choices to the host.</summary>
    public static string EncodeChoices(Dictionary<string, string> choices)
    {
        var parts = new List<string>();
        foreach (var pair in choices) parts.Add(pair.Key + "=" + pair.Value);
        return string.Join(";", parts);
    }

    public static Dictionary<string, string> DecodeChoices(string text)
    {
        var choices = new Dictionary<string, string>();
        foreach (var part in (text ?? "").Split(';'))
        {
            int split = part.IndexOf('=');
            if (split > 0) choices[part.Substring(0, split)] = part.Substring(split + 1);
        }
        return choices;
    }

    /// <summary>A skin chosen by <paramref name="seed"/>, so a bot keeps the same look all match.</summary>
    public static WeaponSkin Pick(WeaponData weapon, int seed)
    {
        var skins = For(weapon);
        return skins.Count > 0 ? skins[seed % skins.Count] : null;
    }

    /// <summary>
    /// Resources path of the model to build: the skin's own model, else the weapon's default model
    /// (Models/&lt;weapon name&gt;/model), else null for the box model.
    /// </summary>
    public static string ModelPath(WeaponData weapon, WeaponSkin skin)
    {
        if (byWeapon == null) LoadIndex();
        if (skin?.Model != null) return skin.Model;
        return defaultModels.TryGetValue(weapon.Id, out var path) ? path : null;
    }

    public static GameObject LoadModel(WeaponData weapon, WeaponSkin skin)
    {
        string path = ModelPath(weapon, skin);
        if (path == null) return null;
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

    /// <summary>
    /// A part's own look from Blender (its texture or colour, listed in parts.json next to the model), used where
    /// the skin leaves the part alone. Null when the model has no parts.json or does not list the part.
    /// </summary>
    public static Material OwnLook(string modelPath, string partName)
    {
        if (string.IsNullOrEmpty(modelPath)) return null;
        string folder = modelPath.Substring(0, Math.Max(0, modelPath.LastIndexOf('/')));
        if (!partLooks.TryGetValue(folder, out var parts))
        {
            parts = new Dictionary<string, PartLook>();
            var asset = Resources.Load<TextAsset>(folder + "/parts");
            if (asset != null)
                foreach (var look in JsonUtility.FromJson<PartLooks>(asset.text).parts) parts[look.name] = look;
            partLooks[folder] = parts;
        }
        if (!parts.TryGetValue(partName, out var part)) return null;

        string key = folder + "/" + partName;
        if (!materials.TryGetValue(key, out var material))
        {
            var texture = string.IsNullOrEmpty(part.texture) ? null : Resources.Load<Texture2D>(folder + "/" + part.texture);
            Color? color = texture != null ? Color.white : ParseColor(part.color);
            material = color == null ? null : Effects.SkinMaterial(color.Value, texture, part.smoothness, part.metallic);
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

        var index = JsonUtility.FromJson<SkinIndex>(asset.text);
        foreach (var model in index.models) defaultModels[model.weapon] = model.path;
        foreach (var entry in index.skins)
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
            if (info.rotation != null && info.rotation.Length == 3) skin.Rotation = new Vector3(info.rotation[0], info.rotation[1], info.rotation[2]);
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

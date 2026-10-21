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
    public float[] rotation;          // [x, y, z] degrees: turns a skin's model (a pistol or knife around its grip)
    public float[] projectileRotation; // [x, y, z] degrees: turns the skin's projectile (projectile.fbx) in flight
    public bool locked;               // true: only a logged-in player can use it (see Accounts)
    public int tokens;                // its price in Halloween Tokens (online-server/tokens.py has the real one); 0: free
}

/// <summary>One skin folder, as listed in Skins/index.json (written by the editor, see SkinIndexBuilder).</summary>
[Serializable]
public class SkinIndexEntry
{
    public string weapon;             // weapon id
    public string id;                 // skin folder name
    public string folder;             // Resources path of the skin folder
    public bool hasModel, hasMain, hasGrip, hasDetail, hasProjectile;
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

/// <summary>
/// One part's own look from Blender, as Tools/export_gun_fbx.py and Tools/export_map_fbx.py write it to parts.json:
/// texture or colour, glow (colour and/or glow texture) and transparency ("OPAQUE", "MASK" cut-out, "BLEND").
/// </summary>
[Serializable]
public class PartLook
{
    public string name;
    public string texture;
    public string color;
    public float metallic;
    public float smoothness = 0.2f;
    public string emission;
    public string emissionTexture;
    public string alpha = "OPAQUE";
    public float cutoff = 0.5f;

    /// <summary>A material with this look (textures from the Resources folder next to the model), or null when the
    /// part has neither texture nor colour.</summary>
    public Material CreateMaterial(string folder) => CreateMaterial(name => Resources.Load<Texture2D>(folder + "/" + name));

    /// <summary>The same, with the textures found by <paramref name="picture"/> (by name).</summary>
    public Material CreateMaterial(Func<string, Texture2D> picture)
    {
        var map = string.IsNullOrEmpty(texture) ? null : picture(texture);
        Color? tint = map != null ? Color.white : WeaponSkins.ParseColor(color);
        if (tint == null) return null;
        var glowMap = string.IsNullOrEmpty(emissionTexture) ? null : picture(emissionTexture);
        return Effects.SurfaceMaterial(tint.Value, map, smoothness, metallic, WeaponSkins.ParseColor(emission), glowMap, alpha, cutoff);
    }
}

[Serializable]
public class PartLooks
{
    public List<PartLook> parts = new List<PartLook>();

    /// <summary>The looks in Resources/&lt;path&gt;.json by part name (empty when there is no such file).</summary>
    public static Dictionary<string, PartLook> Load(string path)
    {
        var looks = new Dictionary<string, PartLook>();
        var asset = Resources.Load<TextAsset>(path);
        if (asset != null)
            foreach (var look in JsonUtility.FromJson<PartLooks>(asset.text).parts) looks[look.name] = look;
        return looks;
    }
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
    public string Projectile;                              // Resources path of the skin's own projectile model, or null
    public Vector3 ProjectileRotation;
    public bool Locked;                                    // only with an account (skin.json "locked": true)
    public int TokenPrice;                                 // bought with Halloween Tokens (skin.json "tokens"); 0: free
    public string Folder;                                  // Resources path of the skin folder ("Skins/AK-47/Jungle")

    // A skin another player's game sent (see SkinPack): its pictures and models, made at runtime. It only shows on
    // their guns; it is not in this game's skin menu.
    public bool Shared;
    public Texture2D[] SharedTextures;                     // per SkinPart, or null
    public GameObject SharedModel, SharedProjectile;
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

    /// <summary>The skin equipped on a weapon: the logged-in account's choice, else the guest's (saved on this
    /// computer); a skin that needs an account falls back to the default for a guest.</summary>
    public static WeaponSkin Equipped(WeaponData weapon)
    {
        if (runChoices.TryGetValue(weapon.Id, out var chosen) && Find(weapon, chosen) is WeaponSkin forRun) return forRun;
        string id = Accounts.LoggedIn ? Accounts.EquippedSkin(weapon.Id) : PlayerPrefs.GetString(PrefsKey(weapon), "");
        var skin = Find(weapon, id ?? "");
        return skin != null && CanUse(skin) ? skin : DefaultFor(weapon);
    }

    /// <summary>False for a skin that needs an account while nobody is logged in, or one sold for Halloween Tokens
    /// that the online account has not bought.</summary>
    public static bool CanUse(WeaponSkin skin) =>
        skin == null || ((!skin.Locked || Accounts.LoggedIn) && (skin.TokenPrice <= 0 || (Bought != null && Bought(skin))));

    /// <summary>Whether the online account has bought a skin sold for Halloween Tokens (set by OnlineClient).</summary>
    public static Func<WeaponSkin, bool> Bought;

    /// <summary>The guest's equipped skins (weapon id -> skin id), which a new account starts with.</summary>
    public static Dictionary<string, string> GuestChoices()
    {
        var choices = new Dictionary<string, string>();
        foreach (var weapon in WeaponData.All)
        {
            string id = PlayerPrefs.GetString(PrefsKey(weapon), "");
            if (id.Length > 0) choices[weapon.Id] = id;
        }
        return choices;
    }

    /// <summary>This run acts as if the game did not have the skin (the -ds-forget-skin test option).</summary>
    public static void Forget(WeaponData weapon, string skinId)
    {
        For(weapon).RemoveAll(skin => string.Equals(skin.Id, skinId, StringComparison.OrdinalIgnoreCase));
        skinnable = null;
    }

    /// <summary>Uses this skin for this run only, without saving (the -ds-equip test option).</summary>
    public static void EquipThisRun(WeaponData weapon, string skinId) => runChoices[weapon.Id] = skinId;

    /// <summary>Equips a skin for the logged-in account, or for the guest; false for a skin that needs an account.</summary>
    public static bool Equip(WeaponData weapon, WeaponSkin skin)
    {
        if (!CanUse(skin)) return false;
        if (Accounts.LoggedIn)
        {
            Accounts.SetEquippedSkin(weapon.Id, skin.Id);
            return true;
        }
        PlayerPrefs.SetString(PrefsKey(weapon), skin.Id);
        PlayerPrefs.Save();
        return true;
    }

    public static WeaponSkin Find(WeaponData weapon, string id)
    {
        foreach (var skin in For(weapon))
            if (string.Equals(skin.Id, id, StringComparison.OrdinalIgnoreCase)) return skin;
        if (shared.TryGetValue(weapon.Id, out var fromOthers))
            foreach (var skin in fromOthers)
                if (string.Equals(skin.Id, id, StringComparison.OrdinalIgnoreCase)) return skin;
        return null;
    }

    // ----------------------------------------------------------------- skins from other players' games

    static readonly Dictionary<string, List<WeaponSkin>> shared = new Dictionary<string, List<WeaponSkin>>();

    /// <summary>Skins (weapon id, skin id) someone in the game has and this game does not: NetSession asks the other
    /// games for them (see SkinPack). The weapon's default shows until one arrives.</summary>
    public static readonly HashSet<(string Weapon, string Skin)> Wanted = new HashSet<(string, string)>();

    /// <summary>Goes up whenever a skin arrives from another game, so the guns already built can show it.</summary>
    public static int Version { get; private set; }

    /// <summary>Adds a skin another player's game sent (built by <see cref="SkinPack"/>); the materials of its
    /// model's and projectile's parts come ready, by part name. False when this game has that skin already.</summary>
    public static bool AddShared(WeaponSkin skin, Dictionary<string, Material> modelLooks, Dictionary<string, Material> projectileLooks)
    {
        if (byWeapon == null) LoadIndex();
        var weapon = WeaponData.Find(skin.WeaponId);
        if (weapon == null || Find(weapon, skin.Id) != null) return false;
        skin.Shared = true;
        if (skin.SharedModel != null)
        {
            skin.Model = $"shared/{skin.WeaponId}/{skin.Id}/model";
            models[skin.Model] = skin.SharedModel;
            AddLooks(skin.Model.Substring(0, skin.Model.Length - "/model".Length) + "/parts", modelLooks);
        }
        if (skin.SharedProjectile != null)
        {
            skin.Projectile = $"shared/{skin.WeaponId}/{skin.Id}/projectile";
            models[skin.Projectile] = skin.SharedProjectile;
            AddLooks(skin.Projectile + "_parts", projectileLooks);
        }
        for (int part = 0; part < 3; part++)
        {
            var texture = skin.SharedTextures?[part];
            Color? color = skin.Colors[part];
            materials[$"{skin.WeaponId}/{skin.Id}/{(SkinPart)part}"] = texture == null && color == null
                ? null
                : Effects.SkinMaterial(color ?? Color.white, texture, skin.Smoothness, skin.Metallic);
        }
        if (!shared.TryGetValue(skin.WeaponId, out var list)) shared[skin.WeaponId] = list = new List<WeaponSkin>();
        list.Add(skin);
        Wanted.Remove((skin.WeaponId, skin.Id));
        Version++;
        Debug.Log($"[Skins] Got {weapon.Name} skin '{skin.Name}' from another player's game");
        return true;
    }

    // OwnLook finds these parts' materials by the model's path, as if parts.json were there.
    static void AddLooks(string partsPath, Dictionary<string, Material> looks)
    {
        var parts = new Dictionary<string, PartLook>();
        if (looks != null)
            foreach (var pair in looks)
            {
                parts[pair.Key] = new PartLook { name = pair.Key };
                materials[partsPath + "/" + pair.Key] = pair.Value;
            }
        partLooks[partsPath] = parts;
    }

    /// <summary>The skin with this id, else (while this game does not have it, and asks the others for it) the
    /// weapon's default. "" or null: the default.</summary>
    public static WeaponSkin Shown(WeaponData weapon, string id)
    {
        if (weapon == null) return null;
        if (string.IsNullOrEmpty(id)) return DefaultFor(weapon);
        var skin = Find(weapon, id);
        if (skin != null) return skin;
        Wanted.Add((weapon.Id, id));
        return DefaultFor(weapon);
    }

    /// <summary>
    /// The id of the skin a carried gun shows ("" for the plain look), whether or not this game has that skin: the
    /// one it came with when it was picked up, else its owner's choice (a bot's is picked by its seed).
    /// </summary>
    public static string IdOf(Combatant combatant, WeaponInstance weapon) =>
        weapon == null ? "" : weapon.Skin ?? ChoiceOf(combatant, weapon.Data);

    static string ChoiceOf(Combatant combatant, WeaponData weapon)
    {
        if (combatant == null || weapon == null) return "";
        if (combatant.SkinChoices != null && combatant.SkinChoices.TryGetValue(weapon.Id, out var id) && !string.IsNullOrEmpty(id)) return id;
        return combatant.IsHuman ? "" : Pick(weapon, combatant.SkinSeed)?.Id ?? "";
    }

    /// <summary>The skin a carried gun shows (see <see cref="IdOf"/>).</summary>
    public static WeaponSkin SkinOf(Combatant combatant, WeaponInstance weapon) =>
        weapon == null ? null : Shown(weapon.Data, IdOf(combatant, weapon));

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

    /// <summary>The skin this combatant shows on the weapon: the carried gun's (see <see cref="IdOf"/>), else a
    /// person's chosen one or a bot's random one.</summary>
    public static WeaponSkin SkinOf(Combatant combatant, WeaponData weapon)
    {
        if (weapon == null) return null;
        var carried = combatant != null ? combatant.Get(weapon.Slot) : null;
        return carried != null && carried.Data == weapon ? SkinOf(combatant, carried) : Shown(weapon, ChoiceOf(combatant, weapon));
    }

    /// <summary>The skin's own projectile model (like the Web RPG's net), or null for the normal rocket.</summary>
    public static GameObject LoadProjectile(WeaponSkin skin)
    {
        if (skin?.Projectile == null) return null;
        if (!models.TryGetValue(skin.Projectile, out var model))
        {
            model = Resources.Load<GameObject>(skin.Projectile);
            models[skin.Projectile] = model;
        }
        return model;
    }

    /// <summary>A skin chosen by <paramref name="seed"/>, so a bot keeps the same look all match (never one that
    /// needs an account).</summary>
    public static WeaponSkin Pick(WeaponData weapon, int seed)
    {
        var skins = For(weapon).FindAll(s => !s.Locked);
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
        // model.fbx has parts.json; another model in the folder (a skin's projectile.fbx) has <name>_parts.json.
        string partsPath = modelPath.EndsWith("/model") ? folder + "/parts" : modelPath + "_parts";
        if (!partLooks.TryGetValue(partsPath, out var parts))
        {
            parts = PartLooks.Load(partsPath);
            partLooks[partsPath] = parts;
        }
        if (!parts.TryGetValue(partName, out var part)) return null;

        string key = partsPath + "/" + partName;
        if (!materials.TryGetValue(key, out var material))
        {
            material = part.CreateMaterial(folder);
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
            Debug.LogWarning("[Skins] Skins/index.json is missing; use Low Strike > Rebuild Skin Index in the editor.");
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
                Folder = entry.folder,
                Model = entry.hasModel ? entry.folder + "/model" : null,
                Smoothness = info.smoothness,
                Metallic = info.metallic,
            };
            if (info.rotation != null && info.rotation.Length == 3) skin.Rotation = new Vector3(info.rotation[0], info.rotation[1], info.rotation[2]);
            skin.Locked = info.locked;
            skin.TokenPrice = info.tokens;
            if (entry.hasProjectile) skin.Projectile = entry.folder + "/projectile";
            if (info.projectileRotation != null && info.projectileRotation.Length == 3)
                skin.ProjectileRotation = new Vector3(info.projectileRotation[0], info.projectileRotation[1], info.projectileRotation[2]);
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

    public static Color? ParseColor(string html) =>
        !string.IsNullOrEmpty(html) && ColorUtility.TryParseHtmlString(html, out var color) ? color : (Color?)null;

    static string PrefsKey(WeaponData weapon) => "DesertStrike.skin." + weapon.Id;
}

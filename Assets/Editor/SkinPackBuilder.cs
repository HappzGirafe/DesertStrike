using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Packs every skin (but the defaults, which every game has) into Assets/Resources/SkinPacks/&lt;weapon name&gt;/&lt;skin&gt;.bytes,
/// so the game can send it to a player whose game does not have it (see SkinPack). Pictures shrink to at most 512
/// pixels on a side and go as JPEG unless they have see-through parts. It runs with the skin index (SkinIndexBuilder);
/// a pack is only made again when something in its skin folder is newer.
/// </summary>
public static class SkinPackBuilder
{
    const string PacksFolder = "Assets/Resources/SkinPacks";
    const int MaxPicture = 512;

    [MenuItem("Low Strike/Rebuild Skin Packs")]
    static void RebuildAll() => Build(SkinIndexBuilder.MakeIndex(), force: true);

    public static void Build(SkinIndex index, bool force = false)
    {
        int made = 0;
        long total = 0;
        foreach (var entry in index.skins)
        {
            if (entry.id == "Default") continue;
            string folder = "Assets/Resources/" + entry.folder;
            string weaponFolder = Path.GetFileName(Path.GetDirectoryName(folder));
            string path = $"{PacksFolder}/{weaponFolder}/{entry.id}.bytes";
            if (!force && File.Exists(path) && File.GetLastWriteTimeUtc(path) >= Newest(folder) && IsCurrentFormat(path))
            {
                total += new FileInfo(path).Length;
                continue;
            }
            byte[] data;
            try
            {
                data = Pack(entry, folder);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Skins] Could not pack {entry.folder} to share: {e.Message}");
                continue;
            }
            if (data.Length > SkinPack.MaxBytes)
            {
                Debug.LogWarning($"[Skins] {entry.folder} packs to {data.Length / 1024} KB, more than games accept from each other; it is not shared.");
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, data);
            made++;
            total += data.Length;
        }
        if (made == 0) return;
        AssetDatabase.Refresh();
        Debug.Log($"[Skins] Packed {made} skins to share with other games (all packs: {total / 1024} KB)");
    }

    static bool IsCurrentFormat(string path)
    {
        using (var file = File.OpenRead(path))
        {
            var start = new byte[4];
            return file.Read(start, 0, 4) == 4 && System.Text.Encoding.ASCII.GetString(start) == SkinPack.Magic;
        }
    }

    static DateTime Newest(string folder) =>
        Directory.GetFiles(folder).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max();

    static byte[] Pack(SkinIndexEntry entry, string folder)
    {
        var pictures = new List<SkinPack.Picture>();
        string AddPicture(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (pictures.Exists(p => p.Name == name)) return name;
            string file = new[] { ".png", ".jpg", ".jpeg" }.Select(e => $"{folder}/{name}{e}").FirstOrDefault(File.Exists);
            if (file == null) return null;
            pictures.Add(MakePicture(name, file));
            return name;
        }

        var partPictures = new[]
        {
            entry.hasMain ? AddPicture("main") : null,
            entry.hasGrip ? AddPicture("grip") : null,
            entry.hasDetail ? AddPicture("detail") : null,
        };
        var model = entry.hasModel ? ReadModel(folder, "model", "parts", AddPicture) : null;
        var projectile = entry.hasProjectile ? ReadModel(folder, "projectile", "projectile_parts", AddPicture) : null;
        return SkinPack.Write(entry.weapon, entry.id, entry.info ?? new SkinInfo(), partPictures, pictures, model, projectile);
    }

    static SkinPack.Model ReadModel(string folder, string name, string partsName, Func<string, string> addPicture)
    {
        string file = new[] { ".fbx", ".obj" }.Select(e => $"{folder}/{name}{e}").FirstOrDefault(File.Exists);
        var prefab = file != null ? AssetDatabase.LoadAssetAtPath<GameObject>(file) : null;
        if (prefab == null) throw new Exception($"{folder}/{name} is not imported");
        string partsFile = $"{folder}/{partsName}.json";
        var model = new SkinPack.Model { PartsJson = File.Exists(partsFile) ? File.ReadAllText(partsFile) : "" };
        if (model.PartsJson.Length > 0)
            foreach (var look in JsonUtility.FromJson<PartLooks>(model.PartsJson).parts)
            {
                addPicture(look.texture);
                addPicture(look.emissionTexture);
            }
        AddNode(model, prefab.transform, -1);
        return model;
    }

    static void AddNode(SkinPack.Model model, Transform node, int parent)
    {
        int index = model.Nodes.Count;
        model.Nodes.Add(new SkinPack.Node
        {
            Name = node.name,
            Parent = parent,
            Position = node.localPosition,
            Rotation = node.localRotation,
            Scale = node.localScale,
            Mesh = node.TryGetComponent<MeshFilter>(out var filter) ? filter.sharedMesh : null,
        });
        foreach (Transform child in node) AddNode(model, child, index);
    }

    static SkinPack.Picture MakePicture(string name, string file)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(File.ReadAllBytes(file))) throw new Exception($"{file} is not a PNG or JPEG picture");
        while (texture.width > MaxPicture || texture.height > MaxPicture) texture = Half(texture);
        bool opaque = texture.GetPixels32().All(p => p.a == 255);
        var importer = AssetImporter.GetAtPath(file) as TextureImporter;
        var picture = new SkinPack.Picture
        {
            Name = name,
            Data = opaque ? texture.EncodeToJPG(88) : texture.EncodeToPNG(),
            Clamp = importer != null && importer.wrapMode == TextureWrapMode.Clamp,
            Point = importer != null && importer.filterMode == FilterMode.Point,
        };
        UnityEngine.Object.DestroyImmediate(texture);
        return picture;
    }

    // Half the size: each pixel the average of four.
    static Texture2D Half(Texture2D source)
    {
        int sw = source.width, sh = source.height;
        int w = Math.Max(1, sw / 2), h = Math.Max(1, sh / 2);
        var from = source.GetPixels32();
        var to = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int r = 0, g = 0, b = 0, a = 0;
                for (int dy = 0; dy < 2; dy++)
                    for (int dx = 0; dx < 2; dx++)
                    {
                        var p = from[Math.Min(sh - 1, y * 2 + dy) * sw + Math.Min(sw - 1, x * 2 + dx)];
                        r += p.r;
                        g += p.g;
                        b += p.b;
                        a += p.a;
                    }
                to[y * w + x] = new Color32((byte)(r / 4), (byte)(g / 4), (byte)(b / 4), (byte)(a / 4));
            }
        var result = new Texture2D(w, h, TextureFormat.RGBA32, false);
        result.SetPixels32(to);
        result.Apply();
        UnityEngine.Object.DestroyImmediate(source);
        return result;
    }
}

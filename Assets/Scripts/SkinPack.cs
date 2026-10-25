using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A skin packed into bytes, so a game that does not have it can get it from another player's game over the network
/// (see NetSession.Skins.cs): skin.json's settings, its pictures (main/grip/detail, and the textures of its own model)
/// and its model and projectile meshes with each part's look.
///
/// The editor packs every skin into Resources/SkinPacks/&lt;weapon name&gt;/&lt;skin&gt;.bytes (SkinPackBuilder), and
/// <see cref="Load"/> turns a pack from another game back into a skin for this run. A pack comes from someone else's
/// computer, so everything in it is checked and limited, and a bad one is thrown away.
/// </summary>
public static class SkinPack
{
    public const int MaxBytes = 1536 * 1024;
    const int MaxUnpacked = 6 * 1024 * 1024;
    public const string Magic = "LSK2";   // a new number for every change of the format
    const int MaxPicture = 1024;        // pixels on a side
    const int MaxPictures = 32;
    const int MaxNodes = 256;
    const int MaxVertices = 100000;     // a model's meshes together
    const int MaxIndices = 600000;
    const int MaxSubMeshes = 32;

    public class Picture
    {
        public string Name;
        public byte[] Data;             // PNG or JPEG
        public bool Clamp, Point;       // wrap mode clamp (else repeat), point filter (else bilinear)
    }

    public class Node
    {
        public string Name;
        public int Parent = -1;
        public Vector3 Position, Scale = Vector3.one;
        public Quaternion Rotation = Quaternion.identity;
        public Mesh Mesh;
    }

    public class Model
    {
        public string PartsJson = "";   // its parts.json: each part's look
        public readonly List<Node> Nodes = new List<Node>();
    }

    static readonly Dictionary<string, byte[]> local = new Dictionary<string, byte[]>();

    /// <summary>This game's own pack of one of its skins (to send to a game that lacks it), or null.</summary>
    public static byte[] Local(WeaponData weapon, string skinId)
    {
        var skin = weapon != null ? WeaponSkins.Find(weapon, skinId) : null;
        if (skin == null || skin.Shared || string.IsNullOrEmpty(skin.Folder) || !skin.Folder.StartsWith("Skins/")) return null;
        string path = "SkinPacks/" + skin.Folder.Substring("Skins/".Length);
        if (!local.TryGetValue(path, out var data))
        {
            var asset = Resources.Load<TextAsset>(path);
            data = asset != null && asset.bytes.Length <= MaxBytes ? asset.bytes : null;
            if (asset != null) Resources.UnloadAsset(asset);
            local[path] = data;
        }
        return data;
    }

    // ----------------------------------------------------------------- writing (the editor)

    /// <summary>The pack: <see cref="Magic"/>, the unpacked length, then everything else squeezed with Deflate.</summary>
    public static byte[] Write(string weaponId, string skinId, SkinInfo info, string[] partPictures, List<Picture> pictures,
                               Model model, Model projectile)
    {
        byte[] raw = WriteRaw(weaponId, skinId, info, partPictures, pictures, model, projectile);
        using (var stream = new MemoryStream())
        {
            stream.Write(Encoding.ASCII.GetBytes(Magic), 0, 4);
            stream.Write(BitConverter.GetBytes(raw.Length), 0, 4);
            using (var deflate = new DeflateStream(stream, System.IO.Compression.CompressionLevel.Optimal, true)) deflate.Write(raw, 0, raw.Length);
            return stream.ToArray();
        }
    }

    static byte[] WriteRaw(string weaponId, string skinId, SkinInfo info, string[] partPictures, List<Picture> pictures,
                           Model model, Model projectile)
    {
        using (var stream = new MemoryStream())
        using (var w = new BinaryWriter(stream))
        {
            w.Write(weaponId);
            w.Write(skinId);
            w.Write(string.IsNullOrEmpty(info.name) ? skinId : info.name);
            w.Write(info.smoothness);
            w.Write(info.metallic);
            WriteAngles(w, info.rotation);
            WriteAngles(w, info.projectileRotation);
            w.Write(info.main ?? "");
            w.Write(info.grip ?? "");
            w.Write(info.detail ?? "");
            for (int part = 0; part < 3; part++) w.Write(partPictures?[part] ?? "");
            w.Write(pictures.Count);
            foreach (var picture in pictures)
            {
                w.Write(picture.Name);
                w.Write((byte)((picture.Clamp ? 1 : 0) | (picture.Point ? 2 : 0)));
                w.Write(picture.Data.Length);
                w.Write(picture.Data);
            }
            WriteModel(w, model);
            WriteModel(w, projectile);
            // Added in 1.21 (the 1.20.1 beta) at the end, where older games stop reading: the neon wraps' glow per part.
            w.Write(info.mainGlow ?? "");
            w.Write(info.gripGlow ?? "");
            w.Write(info.detailGlow ?? "");
            w.Flush();
            return stream.ToArray();
        }
    }

    static void WriteAngles(BinaryWriter w, float[] angles)
    {
        for (int i = 0; i < 3; i++) w.Write(angles != null && angles.Length == 3 ? angles[i] : 0f);
    }

    static void WriteModel(BinaryWriter w, Model model)
    {
        w.Write(model != null);
        if (model == null) return;
        w.Write(model.PartsJson ?? "");
        w.Write(model.Nodes.Count);
        foreach (var node in model.Nodes)
        {
            w.Write(node.Name ?? "");
            w.Write((short)node.Parent);
            w.Write(node.Position);
            w.Write(node.Rotation.x);
            w.Write(node.Rotation.y);
            w.Write(node.Rotation.z);
            w.Write(node.Rotation.w);
            w.Write(node.Scale);
            var mesh = node.Mesh;
            w.Write(mesh != null);
            if (mesh == null) continue;
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var uvs = mesh.uv;
            w.Write(vertices.Length);
            foreach (var v in vertices) w.Write(v);
            bool hasNormals = normals.Length == vertices.Length;
            w.Write(hasNormals);
            if (hasNormals) foreach (var n in normals) w.Write(n);
            bool hasUvs = uvs.Length == vertices.Length;
            w.Write(hasUvs);
            if (hasUvs)
                foreach (var uv in uvs)
                {
                    w.Write(uv.x);
                    w.Write(uv.y);
                }
            var triangles = new List<int[]>();
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
                if (mesh.GetTopology(sub) == MeshTopology.Triangles) triangles.Add(mesh.GetTriangles(sub));
            w.Write(triangles.Count);
            bool wide = vertices.Length > 65535;
            foreach (var list in triangles)
            {
                w.Write(list.Length);
                foreach (int index in list)
                {
                    if (wide) w.Write(index);
                    else w.Write((ushort)index);
                }
            }
        }
    }

    // ----------------------------------------------------------------- reading (a pack from another game)

    static Transform holder;

    /// <summary>
    /// Makes the skin in <paramref name="data"/> (which must be <paramref name="weaponId"/>'s skin
    /// <paramref name="skinId"/>) and adds it to <see cref="WeaponSkins"/> for this run. False for a bad pack.
    /// </summary>
    public static bool Load(byte[] data, string weaponId, string skinId)
    {
        var made = new List<UnityEngine.Object>();
        try
        {
            var weapon = WeaponData.Find(weaponId);
            if (weapon == null || data == null || data.Length < 8 || data.Length > MaxBytes) return false;
            if (Encoding.ASCII.GetString(data, 0, 4) != Magic) return false;
            int rawLength = BitConverter.ToInt32(data, 4);
            if (rawLength <= 0 || rawLength > MaxUnpacked) return false;
            var raw = new byte[rawLength];
            using (var deflate = new DeflateStream(new MemoryStream(data, 8, data.Length - 8, false), CompressionMode.Decompress))
            {
                int read = 0, got;
                while (read < rawLength && (got = deflate.Read(raw, read, rawLength - read)) > 0) read += got;
                if (read != rawLength) return false;
            }
            using (var r = new BinaryReader(new MemoryStream(raw, false)))
            {
                if (!string.Equals(r.ReadString(), weaponId, StringComparison.OrdinalIgnoreCase)) return false;
                if (!string.Equals(r.ReadString(), skinId, StringComparison.Ordinal)) return false;
                string name = r.ReadString();
                if (name.Length == 0 || name.Length > 40) name = skinId;
                var skin = new WeaponSkin
                {
                    Id = skinId,
                    Name = name,
                    WeaponId = weapon.Id,
                    Smoothness = Mathf.Clamp01(Finite(r.ReadSingle())),
                    Metallic = Mathf.Clamp01(Finite(r.ReadSingle())),
                    Rotation = Angles(r),
                    ProjectileRotation = Angles(r),
                };
                for (int part = 0; part < 3; part++) skin.Colors[part] = WeaponSkins.ParseColor(Short(r.ReadString()));
                var partPictures = new string[3];
                for (int part = 0; part < 3; part++) partPictures[part] = r.ReadString();

                int pictureCount = r.ReadInt32();
                if (pictureCount < 0 || pictureCount > MaxPictures) return false;
                var pictures = new Dictionary<string, Texture2D>();
                for (int i = 0; i < pictureCount; i++)
                {
                    string pictureName = r.ReadString();
                    int flags = r.ReadByte();
                    int length = r.ReadInt32();
                    if (length <= 0 || length > r.BaseStream.Length - r.BaseStream.Position) return false;
                    byte[] bytes = r.ReadBytes(length);
                    if (!PictureSize(bytes, out int width, out int height) || width > MaxPicture || height > MaxPicture) return false;
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true)
                    {
                        name = pictureName,
                        wrapMode = (flags & 1) != 0 ? TextureWrapMode.Clamp : TextureWrapMode.Repeat,
                        filterMode = (flags & 2) != 0 ? FilterMode.Point : FilterMode.Bilinear,
                    };
                    made.Add(texture);
                    if (!texture.LoadImage(bytes, true)) return false;
                    pictures[pictureName] = texture;
                }
                Texture2D PictureNamed(string pictureName) =>
                    !string.IsNullOrEmpty(pictureName) && pictures.TryGetValue(pictureName, out var t) ? t : null;

                skin.SharedTextures = new Texture2D[3];
                for (int part = 0; part < 3; part++) skin.SharedTextures[part] = PictureNamed(partPictures[part]);

                int vertexBudget = MaxVertices, indexBudget = MaxIndices;
                Dictionary<string, Material> modelLooks = null, projectileLooks = null;
                if (r.ReadBoolean())
                {
                    skin.SharedModel = ReadModel(r, $"{weapon.Name} {skinId}", made, ref vertexBudget, ref indexBudget, PictureNamed, out modelLooks);
                    if (skin.SharedModel == null) return false;
                }
                if (r.ReadBoolean())
                {
                    skin.SharedProjectile = ReadModel(r, $"{weapon.Name} {skinId} projectile", made, ref vertexBudget, ref indexBudget, PictureNamed, out projectileLooks);
                    if (skin.SharedProjectile == null) return false;
                }
                if (r.BaseStream.Position < r.BaseStream.Length)   // a pack from 1.21 on: the glow per part
                    for (int part = 0; part < 3; part++) skin.Glows[part] = WeaponSkins.ParseColor(Short(r.ReadString()));
                if (!WeaponSkins.AddShared(skin, modelLooks, projectileLooks)) return false;
                made.Clear();   // kept: the skin uses them now
                return true;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Skins] A skin from another game could not be read ({weaponId}/{skinId}): {e.Message}");
            return false;
        }
        finally
        {
            foreach (var thing in made)
                if (thing != null) UnityEngine.Object.Destroy(thing);
        }
    }

    static GameObject ReadModel(BinaryReader r, string label, List<UnityEngine.Object> made, ref int vertexBudget, ref int indexBudget,
                                Func<string, Texture2D> picture, out Dictionary<string, Material> looks)
    {
        looks = new Dictionary<string, Material>();
        string partsJson = r.ReadString();
        int nodeCount = r.ReadInt32();
        if (nodeCount < 1 || nodeCount > MaxNodes) return null;

        if (holder == null)
        {
            // The models wait here, switched off; WeaponModels makes copies of them.
            var go = new GameObject("Skins from other games");
            go.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(go);
            holder = go.transform;
        }
        var nodes = new Transform[nodeCount];
        for (int i = 0; i < nodeCount; i++)
        {
            string name = Short(r.ReadString());
            int parent = r.ReadInt16();
            Vector3 position = r.ReadVector3();
            var rotation = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            Vector3 scale = r.ReadVector3();
            if (i == 0 ? parent != -1 : parent < 0 || parent >= i) return null;
            if (!Sensible(position) || !Sensible(scale) || !Sensible(new Vector3(rotation.x, rotation.y, rotation.z)) || !float.IsFinite(rotation.w))
                return null;

            var node = new GameObject(name);
            if (i == 0)
            {
                made.Add(node);
                node.SetActive(false);
            }
            node.transform.SetParent(i == 0 ? holder : nodes[parent], false);
            node.transform.localPosition = position;
            node.transform.localRotation = rotation.normalized;
            node.transform.localScale = scale;
            nodes[i] = node.transform;
            if (i == 0) node.name = label;

            if (!r.ReadBoolean()) continue;
            var mesh = ReadMesh(r, name, ref vertexBudget, ref indexBudget);
            if (mesh == null) return null;
            made.Add(mesh);
            node.AddComponent<MeshFilter>().sharedMesh = mesh;
            node.AddComponent<MeshRenderer>();
        }

        if (!string.IsNullOrEmpty(partsJson))
            foreach (var look in JsonUtility.FromJson<PartLooks>(partsJson).parts)
            {
                if (string.IsNullOrEmpty(look.name) || looks.ContainsKey(look.name)) continue;
                var material = look.CreateMaterial(picture);
                if (material != null) made.Add(material);
                looks[look.name] = material;
            }
        return nodes[0].gameObject;
    }

    static Mesh ReadMesh(BinaryReader r, string name, ref int vertexBudget, ref int indexBudget)
    {
        int vertexCount = r.ReadInt32();
        if (vertexCount < 1 || vertexCount > vertexBudget) return null;
        vertexBudget -= vertexCount;
        var vertices = new Vector3[vertexCount];
        for (int v = 0; v < vertexCount; v++)
        {
            vertices[v] = r.ReadVector3();
            if (!Sensible(vertices[v])) return null;
        }
        Vector3[] normals = null;
        if (r.ReadBoolean())
        {
            normals = new Vector3[vertexCount];
            for (int v = 0; v < vertexCount; v++) normals[v] = r.ReadVector3();
        }
        Vector2[] uvs = null;
        if (r.ReadBoolean())
        {
            uvs = new Vector2[vertexCount];
            for (int v = 0; v < vertexCount; v++) uvs[v] = new Vector2(Finite(r.ReadSingle()), Finite(r.ReadSingle()));
        }
        int subMeshes = r.ReadInt32();
        if (subMeshes < 1 || subMeshes > MaxSubMeshes) return null;
        bool wide = vertexCount > 65535;
        var mesh = new Mesh { name = name, indexFormat = wide ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.SetVertices(vertices);
        if (normals != null) mesh.SetNormals(normals);
        if (uvs != null) mesh.SetUVs(0, uvs);
        mesh.subMeshCount = subMeshes;
        for (int sub = 0; sub < subMeshes; sub++)
        {
            int count = r.ReadInt32();
            if (count < 0 || count % 3 != 0 || count > indexBudget)
            {
                UnityEngine.Object.Destroy(mesh);
                return null;
            }
            indexBudget -= count;
            var triangles = new int[count];
            for (int i = 0; i < count; i++)
            {
                int index = wide ? r.ReadInt32() : r.ReadUInt16();
                if (index < 0 || index >= vertexCount)
                {
                    UnityEngine.Object.Destroy(mesh);
                    return null;
                }
                triangles[i] = index;
            }
            mesh.SetTriangles(triangles, sub, false);
        }
        if (normals == null) mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.UploadMeshData(false);
        return mesh;
    }

    /// <summary>The size of a PNG or JPEG picture from its header (before it is unpacked); false for anything else.</summary>
    static bool PictureSize(byte[] data, out int width, out int height)
    {
        width = height = 0;
        if (data.Length >= 24 && data[0] == 0x89 && data[1] == 'P' && data[2] == 'N' && data[3] == 'G'
            && data[12] == 'I' && data[13] == 'H' && data[14] == 'D' && data[15] == 'R')
        {
            width = data[16] << 24 | data[17] << 16 | data[18] << 8 | data[19];
            height = data[20] << 24 | data[21] << 16 | data[22] << 8 | data[23];
            return width > 0 && height > 0;
        }
        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8)
        {
            int i = 2;
            while (i + 9 < data.Length)
            {
                if (data[i] != 0xFF) return false;
                int marker = data[i + 1];
                if (marker == 0xFF)
                {
                    i++;
                    continue;
                }
                int length = data[i + 2] << 8 | data[i + 3];
                // Start of frame (not DHT 0xC4, JPG 0xC8 or DAC 0xCC): its height, then width.
                if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                {
                    height = data[i + 5] << 8 | data[i + 6];
                    width = data[i + 7] << 8 | data[i + 8];
                    return width > 0 && height > 0;
                }
                if (length < 2) return false;
                i += 2 + length;
            }
        }
        return false;
    }

    static float Finite(float value) => float.IsFinite(value) ? value : 0f;

    static bool Sensible(Vector3 v) =>
        float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z) && Mathf.Abs(v.x) < 1e4f && Mathf.Abs(v.y) < 1e4f && Mathf.Abs(v.z) < 1e4f;

    static Vector3 Angles(BinaryReader r)
    {
        var angles = new Vector3(Finite(r.ReadSingle()), Finite(r.ReadSingle()), Finite(r.ReadSingle()));
        return new Vector3(Mathf.Repeat(angles.x, 360f), Mathf.Repeat(angles.y, 360f), Mathf.Repeat(angles.z, 360f));
    }

    static string Short(string text) => text.Length > 64 ? text.Substring(0, 64) : text;
}

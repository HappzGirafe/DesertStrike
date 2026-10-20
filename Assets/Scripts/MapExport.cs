using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// -ds-export-closed &lt;folder&gt; -ds-map &lt;id&gt;: writes the map with the walls that close each half in small matches,
/// for Tools/closed_maps.ps1, which turns it into a Blender file (&lt;map&gt;_closed.blend). &lt;map&gt;.json holds the walls
/// of both closings and where every map part is (to line the walls up with the map's own .blend); a map that has no
/// .blend (de_dune, built from blocks) also goes out as &lt;map&gt;.obj with its colours.
/// </summary>
public static class MapExport
{
    [Serializable]
    class Box
    {
        public string name;
        public Vector3 center, size;
        public Color color;
    }

    [Serializable]
    class Export
    {
        public string map;
        public List<Box> parts = new List<Box>();
        public List<Box> wallsA = new List<Box>(), wallsB = new List<Box>();
    }

    public static void ClosedHalves(GameMap map, string folder)
    {
        Directory.CreateDirectory(folder);
        map.SetClosedSite(null);
        var export = new Export { map = map.Id };

        // The map's parts, by a name used once (the .blend's object names; de_dune's are numbered here).
        bool blocks = map is MapBuilder;
        var renderers = new List<MeshRenderer>(map.Root.GetComponentsInChildren<MeshRenderer>());
        var count = new Dictionary<string, int>();
        foreach (var r in renderers) count[r.name] = count.TryGetValue(r.name, out int n) ? n + 1 : 1;
        var names = new Dictionary<MeshRenderer, string>();
        for (int i = 0; i < renderers.Count; i++)
        {
            var r = renderers[i];
            string name = blocks ? $"{r.name}_{i}" : r.name;
            if (!blocks && count[r.name] > 1) continue;
            names[r] = name;
            export.parts.Add(new Box { name = name, center = r.bounds.center, size = r.bounds.size });
        }
        if (blocks) WriteObj(renderers, names, Path.Combine(folder, map.Id + ".obj"));

        foreach (string site in new[] { "A", "B" })
        {
            map.SetClosedSite(site);
            var walls = site == "A" ? export.wallsA : export.wallsB;
            foreach (var r in map.Root.GetComponentsInChildren<MeshRenderer>())
            {
                if (!UnderWalls(r.transform)) continue;
                walls.Add(new Box { name = r.name, center = r.bounds.center, size = r.bounds.size, color = r.sharedMaterial.color });
            }
        }
        map.SetClosedSite(null);
        File.WriteAllText(Path.Combine(folder, map.Id + ".json"), JsonUtility.ToJson(export, true));
        Debug.Log($"[Map] Exported {map.Name}: {export.parts.Count} parts, {export.wallsA.Count} wall pieces closing A, " +
                  $"{export.wallsB.Count} closing B ({folder})");
    }

    // The walls of a closed half sit under "Closed half A" / "Closed half B" (see GameMap).
    static bool UnderWalls(Transform t)
    {
        for (; t != null; t = t.parent)
            if (t.name.StartsWith("Closed half ")) return true;
        return false;
    }

    /// <summary>The parts as a Wavefront .obj with a .mtl (colours, and textures as .png next to it), turned from
    /// Unity's left-handed space into the right-handed one .obj files use (x mirrored).</summary>
    static void WriteObj(List<MeshRenderer> renderers, Dictionary<MeshRenderer, string> names, string path)
    {
        string folder = Path.GetDirectoryName(path), stem = Path.GetFileNameWithoutExtension(path);
        var obj = new StringBuilder();
        var mtl = new StringBuilder();
        obj.AppendLine($"mtllib {stem}.mtl");
        var materials = new Dictionary<Material, string>();
        var textures = new Dictionary<Texture, string>();
        int vBase = 1, vtBase = 1, vnBase = 1;   // .obj numbers positions, UVs and normals each from 1
        foreach (var r in renderers)
        {
            var filter = r.GetComponent<MeshFilter>();
            var mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null || !r.enabled || !r.gameObject.activeInHierarchy || !names.ContainsKey(r)) continue;
            if (!mesh.isReadable)
            {
                Debug.LogWarning($"[Map] {r.name}: its mesh cannot be read; left out of the .obj");
                continue;
            }
            Matrix4x4 m = r.transform.localToWorldMatrix, n = m.inverse.transpose;
            bool mirrored = m.determinant < 0f;
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var uvs = mesh.uv;
            obj.AppendLine($"o {names[r]}");
            foreach (var v in vertices)
            {
                Vector3 p = m.MultiplyPoint3x4(v);
                obj.AppendLine(Invariant($"v {-p.x:0.####} {p.y:0.####} {p.z:0.####}"));
            }
            bool hasNormals = normals.Length == vertices.Length, hasUvs = uvs.Length == vertices.Length;
            if (hasNormals)
                foreach (var normal in normals)
                {
                    Vector3 d = n.MultiplyVector(normal).normalized;
                    obj.AppendLine(Invariant($"vn {-d.x:0.###} {d.y:0.###} {d.z:0.###}"));
                }
            var shared = r.sharedMaterials;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var material = shared.Length > 0 ? shared[Mathf.Min(sub, shared.Length - 1)] : null;
                // Each submesh writes its own UVs, with its material's tiling in them.
                Vector2 scale = material != null ? material.mainTextureScale : Vector2.one;
                Vector2 offset = material != null ? material.mainTextureOffset : Vector2.zero;
                if (hasUvs)
                    foreach (var uv in uvs)
                    {
                        Vector2 tiled = Vector2.Scale(uv, scale) + offset;
                        obj.AppendLine(Invariant($"vt {tiled.x:0.#####} {tiled.y:0.#####}"));
                    }
                obj.AppendLine($"usemtl {MaterialName(material, materials, textures, mtl, folder)}");
                string Corner(int i) =>
                    hasUvs && hasNormals ? $"{vBase + i}/{vtBase + i}/{vnBase + i}"
                    : hasUvs ? $"{vBase + i}/{vtBase + i}"
                    : hasNormals ? $"{vBase + i}//{vnBase + i}"
                    : $"{vBase + i}";
                var triangles = mesh.GetTriangles(sub);
                for (int t = 0; t + 2 < triangles.Length; t += 3)
                {
                    // Mirroring x turns the winding round; a part that was mirrored already turns it back.
                    int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                    if (!mirrored) (b, c) = (c, b);
                    obj.AppendLine($"f {Corner(a)} {Corner(b)} {Corner(c)}");
                }
                if (hasUvs) vtBase += uvs.Length;
            }
            vBase += vertices.Length;
            if (hasNormals) vnBase += normals.Length;
        }
        File.WriteAllText(path, obj.ToString());
        File.WriteAllText(Path.Combine(folder, stem + ".mtl"), mtl.ToString());
    }

    static string MaterialName(Material material, Dictionary<Material, string> materials, Dictionary<Texture, string> textures,
                               StringBuilder mtl, string folder)
    {
        if (material == null) return "none";
        if (materials.TryGetValue(material, out var name)) return name;
        name = $"m{materials.Count}_{Clean(material.name)}";
        materials[material] = name;
        Color c = material.HasProperty("_BaseColor") || material.HasProperty("_Color") ? material.color.linear : Color.white;
        mtl.AppendLine($"newmtl {name}");
        mtl.AppendLine(Invariant($"Kd {c.r:0.####} {c.g:0.####} {c.b:0.####}"));
        if (material.mainTexture is Texture2D texture)
        {
            if (!textures.TryGetValue(texture, out var file))
            {
                file = $"tex{textures.Count}_{Clean(texture.name)}.png";
                File.WriteAllBytes(Path.Combine(folder, file), Png(texture));
                textures[texture] = file;
            }
            mtl.AppendLine($"map_Kd {file}");
        }
        return name;
    }

    // Any texture as PNG (most cannot be read directly): drawn into a picture that can.
    static byte[] Png(Texture2D texture)
    {
        var target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(texture, target);
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
        copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
        copy.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(target);
        byte[] png = copy.EncodeToPNG();
        UnityEngine.Object.Destroy(copy);
        return png;
    }

    static string Clean(string name)
    {
        var clean = new StringBuilder();
        foreach (char ch in name ?? "") clean.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        return clean.Length > 0 ? clean.ToString() : "x";
    }

    static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}

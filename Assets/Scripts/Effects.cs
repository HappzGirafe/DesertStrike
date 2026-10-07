using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Primitive-based visuals: shared materials, tracers, muzzle flashes, bullet holes and blood.</summary>
public static class Effects
{
    const int MaxBulletHoles = 150;

    static Material template, glowTemplate;
    static readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
    static readonly Dictionary<Color, Material> glowMaterials = new Dictionary<Color, Material>();
    static readonly Queue<GameObject> bulletHoles = new Queue<GameObject>();
    static readonly Dictionary<string, Material> savedTemplates = new Dictionary<string, Material>();

    const int PaletteSize = 32;
    static Texture2D palette;
    static Material paletteMaterial;
    static readonly Dictionary<Color, Vector2> paletteSlots = new Dictionary<Color, Vector2>();
    static readonly Dictionary<(PrimitiveType, Color), Mesh> paletteMeshes = new Dictionary<(PrimitiveType, Color), Mesh>();
    static readonly Dictionary<PrimitiveType, Mesh> primitiveMeshes = new Dictionary<PrimitiveType, Mesh>();

    public static Material Mat(Color color)
    {
        if (!materials.TryGetValue(color, out var material))
        {
            material = new Material(Template()) { color = color };
            material.SetFloat("_Glossiness", 0.12f);
            materials[color] = material;
        }
        return material;
    }

    public static Material SkinMaterial(Color tint, Texture2D texture, float smoothness, float metallic) =>
        SurfaceMaterial(tint, texture, smoothness, metallic);

    /// <summary>
    /// A material for a look from Blender: texture (tinted) or colour, smoothness, metallic, an optional glow
    /// (colour, and a glow texture) and transparency: "MASK" is cut out where the texture's alpha is below
    /// <paramref name="cutoff"/> (webs, the crane's lattice), "BLEND" is see-through (letters painted on the ground).
    /// Each kind starts from a material saved in Resources/DesertStrike, so builds keep its shader variant.
    /// </summary>
    public static Material SurfaceMaterial(Color tint, Texture2D texture, float smoothness, float metallic,
                                           Color? glow = null, Texture2D glowMap = null, string alpha = "OPAQUE", float cutoff = 0.5f)
    {
        Material template;
        if (alpha == "MASK") template = SavedTemplate(glow.HasValue ? "CutoutGlow" : "Cutout");
        else if (alpha == "BLEND") template = SavedTemplate("Fade");
        else template = glow.HasValue ? GlowTemplate() : Template();

        var material = new Material(template) { color = tint, mainTexture = texture };
        material.SetFloat("_Glossiness", smoothness);
        material.SetFloat("_Metallic", metallic);
        if (alpha == "MASK") material.SetFloat("_Cutoff", cutoff);
        if (glow.HasValue)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", glow.Value);
            if (glowMap != null) material.SetTexture("_EmissionMap", glowMap);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        return material;
    }

    static Material GlowTemplate()
    {
        if (glowTemplate == null) glowTemplate = Resources.Load<Material>("DesertStrike/Glow") ?? Template();
        return glowTemplate;
    }

    static Material SavedTemplate(string name)
    {
        if (!savedTemplates.TryGetValue(name, out var template) || template == null)
        {
            template = Resources.Load<Material>("DesertStrike/" + name) ?? Template();
            savedTemplates[name] = template;
        }
        return template;
    }

    public static Material Glow(Color color)
    {
        if (!glowMaterials.TryGetValue(color, out var material))
        {
            material = new Material(GlowTemplate()) { color = color };
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 1.2f);
            glowMaterials[color] = material;
        }
        return material;
    }

    /// <summary>
    /// One material for every plain-coloured block of the map, the bodies and the decals: each colour is a pixel
    /// of a small palette texture and the block's mesh points all its UVs at that pixel. Objects sharing a
    /// material are drawn without switching shader state, and the static map merges into a few large batches.
    /// </summary>
    public static Material PaletteMaterial
    {
        get
        {
            if (paletteMaterial == null)
            {
                palette = new Texture2D(PaletteSize, PaletteSize, TextureFormat.RGBA32, false)
                {
                    name = "Palette",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    ignoreMipmapLimit = true,
                };
                paletteMaterial = new Material(Template()) { name = "Palette", color = Color.white, mainTexture = palette };
                paletteMaterial.SetFloat("_Glossiness", 0.12f);
            }
            return paletteMaterial;
        }
    }

    /// <summary>A copy of the primitive's mesh whose UVs all point at <paramref name="color"/> in the palette.</summary>
    public static Mesh PaletteMesh(PrimitiveType type, Color color)
    {
        var key = (type, color);
        if (paletteMeshes.TryGetValue(key, out var mesh)) return mesh;

        var material = PaletteMaterial;
        if (!paletteSlots.TryGetValue(color, out var uv))
        {
            int slot = paletteSlots.Count;
            if (slot >= PaletteSize * PaletteSize) slot = PaletteSize * PaletteSize - 1;   // more colours than pixels: reuse the last
            int x = slot % PaletteSize, y = slot / PaletteSize;
            palette.SetPixel(x, y, color);
            palette.Apply(false);
            uv = new Vector2((x + 0.5f) / PaletteSize, (y + 0.5f) / PaletteSize);
            paletteSlots[color] = uv;
        }

        if (!primitiveMeshes.TryGetValue(type, out var source))
        {
            var probe = GameObject.CreatePrimitive(type);
            source = probe.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(probe);
            primitiveMeshes[type] = source;
        }
        mesh = Object.Instantiate(source);
        mesh.name = type + " " + ColorUtility.ToHtmlStringRGB(color);
        var uvs = new Vector2[mesh.vertexCount];
        for (int i = 0; i < uvs.Length; i++) uvs[i] = uv;
        mesh.uv = uvs;
        mesh.UploadMeshData(false);   // stays readable, for static batching and merging
        paletteMeshes[key] = mesh;
        return mesh;
    }

    /// <summary>Creates a primitive under <paramref name="parent"/> (or in world space when null).
    /// <paramref name="palette"/> draws it with the shared <see cref="PaletteMaterial"/> (not for parts that skins repaint).</summary>
    public static GameObject Shape(PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 localScale,
                                   Color color, bool collider = false, Vector3 euler = default, bool palette = false)
    {
        var go = GameObject.CreatePrimitive(type);
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = localScale;
        if (palette)
        {
            go.GetComponent<MeshFilter>().sharedMesh = PaletteMesh(type, color);
            go.GetComponent<Renderer>().sharedMaterial = PaletteMaterial;
        }
        else go.GetComponent<Renderer>().sharedMaterial = Mat(color);
        return go;
    }

    /// <summary>
    /// Merges palette-coloured parts into one mesh (drawn with <see cref="PaletteMaterial"/> in one draw call),
    /// in the space of <paramref name="root"/>. The parts are removed. For models whose parts never move
    /// against each other, like the bodies.
    /// </summary>
    public static Mesh MergePaletteParts(Transform root, IList<GameObject> parts, string name)
    {
        var combine = new CombineInstance[parts.Count];
        Matrix4x4 toRoot = root.worldToLocalMatrix;
        for (int i = 0; i < parts.Count; i++)
        {
            combine[i].mesh = parts[i].GetComponent<MeshFilter>().sharedMesh;
            combine[i].transform = toRoot * parts[i].transform.localToWorldMatrix;
        }
        var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
        mesh.CombineMeshes(combine, true, true);
        mesh.RecalculateBounds();
        mesh.UploadMeshData(true);
        foreach (var part in parts) Object.DestroyImmediate(part);
        return mesh;
    }

    public static void Tracer(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        float length = delta.magnitude;
        if (length < 1f) return;
        // Start a little ahead of the muzzle so tracers do not fill the screen of whoever fires them.
        from += delta / length * Mathf.Min(1.5f, length * 0.5f);
        delta = to - from;
        length = delta.magnitude;
        var go = EffectPool.Take("tracer", () =>
        {
            var tracer = Shape(PrimitiveType.Cube, null, Vector3.zero, Vector3.one, Color.white);
            var renderer = tracer.GetComponent<Renderer>();
            renderer.sharedMaterial = Glow(new Color(0.9f, 0.7f, 0.35f));
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return tracer;
        });
        go.transform.SetPositionAndRotation(from + delta * 0.5f, Quaternion.LookRotation(delta));
        go.transform.localScale = new Vector3(0.012f, 0.012f, length);
        EffectPool.ReturnAfter(go, "tracer", 0.04f);
    }

    public static void MuzzleFlash(Vector3 position)
    {
        var go = EffectPool.Take("flash", () =>
        {
            var root = new GameObject("MuzzleFlash");
            var light = root.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.8f, 0.5f);
            light.intensity = 3f;
            light.range = 6f;
            var sphere = Shape(PrimitiveType.Sphere, root.transform, Vector3.zero, Vector3.one * 0.1f, Color.white);
            sphere.GetComponent<Renderer>().sharedMaterial = Glow(new Color(1f, 0.75f, 0.35f));
            sphere.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            return root;
        });
        go.transform.position = position;
        // A real light makes everything near it draw again, so only High quality uses one.
        go.GetComponent<Light>().enabled = GameSettings.DynamicLights;
        EffectPool.ReturnAfter(go, "flash", 0.05f);
    }

    public static void BulletHole(Vector3 point, Vector3 normal)
    {
        // At the limit the oldest hole moves to the new spot instead of making a new object.
        GameObject go = null;
        if (bulletHoles.Count >= MaxBulletHoles) go = bulletHoles.Dequeue();
        if (go == null)
        {
            go = EffectPool.Take("hole", () =>
            {
                var hole = Shape(PrimitiveType.Cube, null, Vector3.zero, new Vector3(0.09f, 0.09f, 0.01f), new Color(0.12f, 0.1f, 0.08f), palette: true);
                hole.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                return hole;
            });
        }
        go.transform.SetPositionAndRotation(point + normal * 0.005f, Quaternion.LookRotation(normal));
        bulletHoles.Enqueue(go);
    }

    public static void ClearDecals()
    {
        while (bulletHoles.Count > 0) EffectPool.Return(bulletHoles.Dequeue(), "hole");
    }

    public static void Explosion(Vector3 position, float radius)
    {
        var fireball = Shape(PrimitiveType.Sphere, null, position, Vector3.one * radius * 0.7f, Color.white);
        var renderer = fireball.GetComponent<Renderer>();
        renderer.sharedMaterial = Glow(new Color(1f, 0.55f, 0.15f));
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        fireball.AddComponent<ShrinkAndDie>().Lifetime = 0.45f;

        var smoke = Shape(PrimitiveType.Sphere, null, position + Vector3.up * 0.6f, Vector3.one * radius * 0.55f, new Color(0.25f, 0.23f, 0.2f));
        smoke.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        smoke.AddComponent<ShrinkAndDie>().Lifetime = 1.2f;

        var flash = new GameObject("ExplosionLight");
        flash.transform.position = position;
        var light = flash.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.6f, 0.3f);
        light.intensity = 6f;
        light.range = radius * 3f;
        Object.Destroy(flash, 0.15f);
    }

    /// <summary>The C4: a small olive block with a keypad and a red light (child named "Led").</summary>
    public static Transform BombModel(Transform parent)
    {
        var root = new GameObject("Bomb").transform;
        root.SetParent(parent, false);
        Shape(PrimitiveType.Cube, root, new Vector3(0f, 0.06f, 0f), new Vector3(0.32f, 0.12f, 0.22f), new Color(0.3f, 0.33f, 0.2f));
        Shape(PrimitiveType.Cube, root, new Vector3(0.04f, 0.125f, 0f), new Vector3(0.14f, 0.01f, 0.12f), new Color(0.1f, 0.12f, 0.1f));
        var led = Shape(PrimitiveType.Sphere, root, new Vector3(-0.1f, 0.13f, 0.06f), Vector3.one * 0.03f, Color.white);
        led.name = "Led";
        led.GetComponent<Renderer>().sharedMaterial = Glow(new Color(1f, 0.1f, 0.05f));
        foreach (var renderer in root.GetComponentsInChildren<Renderer>()) renderer.shadowCastingMode = ShadowCastingMode.Off;
        return root;
    }

    public static void Blood(Vector3 point)
    {
        var go = EffectPool.Take("blood", () =>
        {
            var puff = Shape(PrimitiveType.Sphere, null, Vector3.zero, Vector3.one, new Color(0.55f, 0.03f, 0.03f), palette: true);
            puff.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            puff.AddComponent<ShrinkAndDie>().PoolKind = "blood";
            return puff;
        });
        go.transform.position = point;
        go.GetComponent<ShrinkAndDie>().Restart(Vector3.one * 0.22f);
    }

    // Builds strip shaders that no saved material uses, so the base materials live in Resources
    // (created by DesertStrikeSetup). The built-in default material is only a fallback.
    static Material Template()
    {
        if (template == null) template = Resources.Load<Material>("DesertStrike/Base");
        if (template == null)
        {
            var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            template = probe.GetComponent<Renderer>().sharedMaterial;
            Object.DestroyImmediate(probe);
        }
        return template;
    }
}

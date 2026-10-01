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

    public static Material SkinMaterial(Color tint, Texture2D texture, float smoothness, float metallic)
    {
        var material = new Material(Template()) { color = tint, mainTexture = texture };
        material.SetFloat("_Glossiness", smoothness);
        material.SetFloat("_Metallic", metallic);
        return material;
    }

    public static Material Glow(Color color)
    {
        if (!glowMaterials.TryGetValue(color, out var material))
        {
            if (glowTemplate == null) glowTemplate = Resources.Load<Material>("DesertStrike/Glow") ?? Template();
            material = new Material(glowTemplate) { color = color };
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 1.2f);
            glowMaterials[color] = material;
        }
        return material;
    }

    /// <summary>Creates a primitive under <paramref name="parent"/> (or in world space when null).</summary>
    public static GameObject Shape(PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 localScale,
                                   Color color, bool collider = false, Vector3 euler = default)
    {
        var go = GameObject.CreatePrimitive(type);
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = localScale;
        go.GetComponent<Renderer>().sharedMaterial = Mat(color);
        return go;
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
        var go = Shape(PrimitiveType.Cube, null, from + delta * 0.5f, new Vector3(0.012f, 0.012f, length), Color.white);
        go.transform.rotation = Quaternion.LookRotation(delta);
        var renderer = go.GetComponent<Renderer>();
        renderer.sharedMaterial = Glow(new Color(0.9f, 0.7f, 0.35f));
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        Object.Destroy(go, 0.04f);
    }

    public static void MuzzleFlash(Vector3 position)
    {
        var go = new GameObject("MuzzleFlash");
        go.transform.position = position;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.8f, 0.5f);
        light.intensity = 3f;
        light.range = 6f;
        var flash = Shape(PrimitiveType.Sphere, go.transform, Vector3.zero, Vector3.one * 0.1f, Color.white);
        flash.GetComponent<Renderer>().sharedMaterial = Glow(new Color(1f, 0.75f, 0.35f));
        flash.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        Object.Destroy(go, 0.05f);
    }

    public static void BulletHole(Vector3 point, Vector3 normal)
    {
        var go = Shape(PrimitiveType.Cube, null, point + normal * 0.005f, new Vector3(0.09f, 0.09f, 0.01f),
                       new Color(0.12f, 0.1f, 0.08f));
        go.transform.rotation = Quaternion.LookRotation(normal);
        go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        bulletHoles.Enqueue(go);
        while (bulletHoles.Count > MaxBulletHoles)
        {
            var oldest = bulletHoles.Dequeue();
            if (oldest != null) Object.Destroy(oldest);
        }
    }

    public static void ClearDecals()
    {
        while (bulletHoles.Count > 0)
        {
            var hole = bulletHoles.Dequeue();
            if (hole != null) Object.Destroy(hole);
        }
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

    public static void Blood(Vector3 point)
    {
        var go = Shape(PrimitiveType.Sphere, null, point, Vector3.one * 0.22f, new Color(0.55f, 0.03f, 0.03f));
        go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        go.AddComponent<ShrinkAndDie>();
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

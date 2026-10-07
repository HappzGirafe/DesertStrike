using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Weapon models, shared by the first-person view model, the bots and the inventory preview.
/// Weapons with a Blender model (Glock-18, USP, and knife skins that bring one) use it with their skin;
/// everything else is built from boxes.
/// </summary>
public static class WeaponModels
{
    // Blender models are fitted to the size and place of the box models they replace: the main part
    // (pistol slide or knife blade) gets this length and its back end sits at MainBack.
    const float MainLength = 0.2f;
    const float PistolSlideBack = -0.04f;
    const float PistolSlideTop = 0.058f;
    const float KnifeBladeBack = 0.03f;

    static readonly Color Metal = new Color(0.14f, 0.14f, 0.15f);
    static readonly Color DarkMetal = new Color(0.07f, 0.07f, 0.08f);
    static readonly Color Steel = new Color(0.68f, 0.7f, 0.73f);
    static readonly Color Wood = new Color(0.45f, 0.27f, 0.13f);
    static readonly Color Olive = new Color(0.27f, 0.33f, 0.22f);

    /// <summary>Builds the gun pointing along +Z and returns its root; <paramref name="muzzle"/> marks the barrel end.</summary>
    public static Transform Build(WeaponData weapon, Transform parent, bool castShadows, out Transform muzzle, WeaponSkin skin = null)
    {
        var root = new GameObject(weapon.Name).transform;
        root.SetParent(parent, false);
        skin ??= WeaponSkins.DefaultFor(weapon);

        if (!BuildFromModel(weapon, skin, root, out Vector3 muzzlePosition))
            muzzlePosition = new Vector3(0f, 0.02f, BuildFromBoxes(weapon, new Painter(root, skin)));

        muzzle = new GameObject("Muzzle").transform;
        muzzle.SetParent(root, false);
        muzzle.localPosition = muzzlePosition;

        if (!castShadows)
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
                renderer.shadowCastingMode = ShadowCastingMode.Off;
        return root;
    }

    /// <summary>Instantiates the Blender model, paints its parts with the skin and fits it to the box model's size.</summary>
    static bool BuildFromModel(WeaponData weapon, WeaponSkin skin, Transform root, out Vector3 muzzlePosition)
    {
        muzzlePosition = default;
        var prefab = WeaponSkins.LoadModel(weapon, skin);
        if (prefab == null) return false;
        string modelPath = WeaponSkins.ModelPath(weapon, skin);

        // Parts are named by Tools/export_gun_fbx.py: Main (also Slide, Blade), Grip, and Detail (anything else).
        // Each part gets the skin's material, else its own look from Blender (parts.json), else a plain metal colour.
        var model = Object.Instantiate(prefab, root, false).transform;
        var mains = new List<MeshFilter>();
        var grips = new List<MeshFilter>();
        var all = new List<MeshFilter>();
        foreach (var filter in model.GetComponentsInChildren<MeshFilter>())
        {
            SkinPart part;
            Color unskinned;
            if (filter.name.StartsWith("Grip"))
            {
                part = SkinPart.Grip;
                unskinned = DarkMetal;
                grips.Add(filter);
            }
            else if (filter.name.StartsWith("Main") || filter.name.StartsWith("Slide") || filter.name.StartsWith("Blade"))
            {
                part = SkinPart.Main;
                unskinned = weapon.IsMelee ? Steel : Metal;
                mains.Add(filter);
            }
            else
            {
                part = SkinPart.Detail;
                unskinned = DarkMetal;
            }
            filter.GetComponent<Renderer>().sharedMaterial = WeaponSkins.MaterialFor(skin, part)
                                                             ?? WeaponSkins.OwnLook(modelPath, filter.name)
                                                             ?? Effects.Mat(unskinned);
            all.Add(filter);
        }
        if (mains.Count == 0) mains = all;

        // Turn the model so the main part points along +Z, whatever axes it was exported with:
        // away from the grip, with the grip underneath (pistol) or the blade's wide side vertical (knife).
        Bounds main = LocalBounds(root, mains);
        if (grips.Count > 0)
        {
            Bounds grip = LocalBounds(root, grips);
            int axis = LongestAxis(main.size);
            var forward = Vector3.zero;
            forward[axis] = main.center[axis] >= grip.center[axis] ? 1f : -1f;

            Vector3 towardMain = main.center - grip.center;
            towardMain[axis] = 0f;
            var up = Vector3.zero;
            // A gun's grip and stock sit below its body, if only a little (a shotgun's stock is nearly in line);
            // a knife's handle is in line with its blade.
            if (towardMain.magnitude > main.size[axis] * (weapon.IsMelee ? 0.1f : 0.03f))
            {
                int upAxis = LongestAxis(Abs(towardMain));
                up[upAxis] = towardMain[upAxis] >= 0f ? 1f : -1f;
            }
            else
            {
                // Grip in line with the main part (a knife): the blade's wider side faces up.
                Vector3 across = main.size;
                across[axis] = 0f;
                up[LongestAxis(across)] = 1f;
            }
            model.localRotation = Quaternion.Inverse(Quaternion.LookRotation(forward, up)) * model.localRotation;
            main = LocalBounds(root, mains);
        }
        else
        {
            // No grip (e.g. a rifle): keep Blender's up, and the barrel is the side the model sticks out furthest.
            Bounds overall = LocalBounds(root, all);
            int axis = LongestAxis(main.size);
            var forward = Vector3.zero;
            forward[axis] = overall.center[axis] >= main.center[axis] ? 1f : -1f;
            Vector3 up = axis == 1 ? Vector3.forward : Vector3.up;
            model.localRotation = Quaternion.Inverse(Quaternion.LookRotation(forward, up)) * model.localRotation;
            main = LocalBounds(root, mains);
        }

        // skin.json "rotation": [x, y, z] turns the model further (degrees, Unity's order: Z, then X, then Y).
        // Rifles and launchers turn before they are fitted, so they still take the box model's place (the Web RPG
        // turns round, warhead forward); pistols and knives turn in the hand afterwards, below.
        bool turned = skin != null && skin.Rotation != Vector3.zero;
        if (turned && weapon.Slot == WeaponSlot.Primary)
            model.localRotation = Quaternion.Euler(skin.Rotation) * model.localRotation;

        if (weapon.Slot == WeaponSlot.Primary)
        {
            // Rifles and other primaries take the length and place of the weapon's box model.
            Bounds target = BoxModelBounds(weapon, root);
            Bounds whole = LocalBounds(root, all);
            model.localScale *= target.size.z / Mathf.Max(0.0001f, whole.size.z);
            whole = LocalBounds(root, all);
            model.localPosition += new Vector3(target.center.x - whole.center.x, target.center.y - whole.center.y, target.min.z - whole.min.z);
        }
        else
        {
            // Pistols and knives: the slide or blade gets the box model's length and place.
            float size = weapon.Id == "deagle" ? 1.25f : 1f;   // same scale as the box Desert Eagle
            model.localScale *= MainLength * size / Mathf.Max(0.0001f, main.size.z);
            main = LocalBounds(root, mains);
            bool knife = weapon.IsMelee;
            float y = knife ? -main.center.y : PistolSlideTop * size - main.max.y;
            float z = (knife ? KnifeBladeBack : PistolSlideBack * size) - main.min.z;
            model.localPosition += new Vector3(-main.center.x, y, z);
        }

        if (turned && weapon.Slot != WeaponSlot.Primary)
        {
            // A pistol or knife turns around the middle of its grip, so the handle stays where the hand is.
            Vector3 pivot = (grips.Count > 0 ? LocalBounds(root, grips) : LocalBounds(root, all)).center;
            Quaternion turn = Quaternion.Euler(skin.Rotation);
            model.localPosition = pivot + turn * (model.localPosition - pivot);
            model.localRotation = turn * model.localRotation;
        }

        main = LocalBounds(root, mains);
        Bounds fitted = LocalBounds(root, all);
        muzzlePosition = new Vector3(0f, main.center.y, fitted.max.z + 0.005f);
        return true;
    }

    /// <summary>
    /// A skin's own projectile (the Web RPG's net) under <paramref name="parent"/>, about 1.2 m across, flying along
    /// +Z with its flattest side first (a net flies open). skin.json "projectileRotation" can turn it further.
    /// </summary>
    public static Transform BuildProjectile(WeaponSkin skin, Transform parent)
    {
        var prefab = WeaponSkins.LoadProjectile(skin);
        if (prefab == null) return null;
        var model = Object.Instantiate(prefab, parent, false).transform;
        var filters = new List<MeshFilter>(model.GetComponentsInChildren<MeshFilter>());
        foreach (var filter in filters)
        {
            var renderer = filter.GetComponent<Renderer>();
            renderer.sharedMaterial = WeaponSkins.OwnLook(skin.Projectile, filter.name) ?? Effects.Mat(DarkMetal);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        Bounds bounds = LocalBounds(parent, filters);
        int thin = bounds.size.x <= bounds.size.y && bounds.size.x <= bounds.size.z ? 0 : bounds.size.y <= bounds.size.z ? 1 : 2;
        var forward = Vector3.zero;
        forward[thin] = 1f;
        model.localRotation = Quaternion.Inverse(Quaternion.LookRotation(forward, thin == 1 ? Vector3.forward : Vector3.up)) * model.localRotation;
        model.localRotation = Quaternion.Euler(skin.ProjectileRotation) * model.localRotation;

        bounds = LocalBounds(parent, filters);
        model.localScale *= 1.2f / Mathf.Max(0.001f, Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z));
        bounds = LocalBounds(parent, filters);
        model.localPosition -= bounds.center;
        return model;
    }

    /// <summary>Bounds of the weapon's box model, measured by building it briefly under <paramref name="root"/>.</summary>
    static Bounds BoxModelBounds(WeaponData weapon, Transform root)
    {
        var measure = new GameObject("Measure").transform;
        measure.SetParent(root, false);
        BuildFromBoxes(weapon, new Painter(measure, null));
        Bounds bounds = LocalBounds(root, new List<MeshFilter>(measure.GetComponentsInChildren<MeshFilter>()));
        Object.DestroyImmediate(measure.gameObject);
        return bounds;
    }

    static int LongestAxis(Vector3 v) => v.x >= v.y && v.x >= v.z ? 0 : v.y >= v.z ? 1 : 2;

    static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    /// <summary>Bounds of the meshes in <paramref name="space"/>'s local coordinates.</summary>
    static Bounds LocalBounds(Transform space, List<MeshFilter> filters)
    {
        var bounds = new Bounds();
        bool first = true;
        foreach (var filter in filters)
        {
            Matrix4x4 toSpace = space.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            Bounds mesh = filter.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = mesh.center + Vector3.Scale(mesh.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                Vector3 point = toSpace.MultiplyPoint3x4(corner);
                if (first)
                {
                    bounds = new Bounds(point, Vector3.zero);
                    first = false;
                }
                else bounds.Encapsulate(point);
            }
        }
        return bounds;
    }

    /// <summary>Builds the box model and returns the barrel length. Each box is a main, grip or detail part for skins.</summary>
    static float BuildFromBoxes(WeaponData weapon, Painter p)
    {
        const SkinPart Main = SkinPart.Main, Grip = SkinPart.Grip, Detail = SkinPart.Detail;
        switch (weapon.Id)
        {
            case "knife":
                p.Box(Main, new Vector3(0f, 0f, 0.13f), new Vector3(0.015f, 0.045f, 0.2f), Steel);
                p.Box(Grip, new Vector3(0f, -0.005f, -0.02f), new Vector3(0.035f, 0.05f, 0.11f), DarkMetal);
                return 0.23f;

            case "glock":
            case "usp":
            case "deagle":
                float s = weapon.Id == "deagle" ? 1.25f : 1f;
                p.Box(Main, new Vector3(0f, 0.03f, 0.06f) * s, new Vector3(0.045f, 0.055f, 0.2f) * s, weapon.Id == "deagle" ? Steel : Metal);
                p.Box(Grip, new Vector3(0f, -0.045f, 0f) * s, new Vector3(0.04f, 0.11f, 0.055f) * s, DarkMetal, new Vector3(12f, 0f, 0f));
                return 0.17f * s;

            case "tec9":
                p.Box(Main, new Vector3(0f, 0.02f, 0.07f), new Vector3(0.05f, 0.07f, 0.24f), Metal);
                p.Box(Detail, new Vector3(0f, 0.02f, 0.22f), new Vector3(0.035f, 0.035f, 0.08f), DarkMetal);
                p.Box(Detail, new Vector3(0f, -0.1f, 0.12f), new Vector3(0.035f, 0.15f, 0.04f), DarkMetal);
                p.Box(Grip, new Vector3(0f, -0.06f, -0.02f), new Vector3(0.04f, 0.1f, 0.05f), DarkMetal, new Vector3(12f, 0f, 0f));
                return 0.27f;

            case "m1911":
                p.Box(Main, new Vector3(0f, 0.03f, 0.06f), new Vector3(0.042f, 0.05f, 0.21f), Metal);
                p.Box(Grip, new Vector3(0f, -0.045f, 0f), new Vector3(0.04f, 0.11f, 0.055f), Wood, new Vector3(12f, 0f, 0f));
                return 0.17f;

            case "rpg":
                p.Box(Main, new Vector3(0f, 0f, 0.05f), new Vector3(0.09f, 0.09f, 0.95f), Olive);
                p.Box(Detail, new Vector3(0f, 0f, 0.6f), new Vector3(0.13f, 0.13f, 0.18f), new Color(0.2f, 0.24f, 0.17f));
                p.Box(Detail, new Vector3(0f, 0f, 0.72f), new Vector3(0.07f, 0.07f, 0.08f), DarkMetal);
                p.Box(Grip, new Vector3(0f, -0.09f, 0.05f), new Vector3(0.035f, 0.1f, 0.04f), Wood);
                p.Box(Grip, new Vector3(0f, -0.09f, -0.12f), new Vector3(0.035f, 0.1f, 0.04f), Wood);
                p.Box(Detail, new Vector3(-0.06f, 0.06f, 0.1f), new Vector3(0.02f, 0.05f, 0.04f), DarkMetal);
                return 0.8f;

            case "mp5":
                p.Box(Main, new Vector3(0f, 0f, 0.1f), new Vector3(0.06f, 0.09f, 0.4f), Metal);
                p.Box(Detail, new Vector3(0f, -0.11f, 0.13f), new Vector3(0.04f, 0.15f, 0.05f), DarkMetal, new Vector3(10f, 0f, 0f));
                p.Box(Grip, new Vector3(0f, -0.07f, -0.02f), new Vector3(0.045f, 0.1f, 0.05f), DarkMetal, new Vector3(15f, 0f, 0f));
                p.Box(Detail, new Vector3(0f, 0.01f, 0.35f), new Vector3(0.03f, 0.03f, 0.12f), DarkMetal);
                p.Box(Grip, new Vector3(0f, 0f, -0.16f), new Vector3(0.04f, 0.06f, 0.14f), DarkMetal);
                return 0.41f;

            case "shotgun":
                p.Box(Detail, new Vector3(0f, 0.02f, 0.28f), new Vector3(0.045f, 0.045f, 0.7f), DarkMetal);
                p.Box(Grip, new Vector3(0f, -0.03f, 0.3f), new Vector3(0.06f, 0.05f, 0.2f), Wood);
                p.Box(Main, new Vector3(0f, 0f, 0f), new Vector3(0.06f, 0.09f, 0.22f), Metal);
                p.Box(Grip, new Vector3(0f, -0.04f, -0.22f), new Vector3(0.06f, 0.1f, 0.3f), Wood, new Vector3(-6f, 0f, 0f));
                return 0.63f;

            case "ak47":
                p.Box(Main, new Vector3(0f, 0f, 0.05f), new Vector3(0.06f, 0.09f, 0.35f), Metal);
                p.Box(Grip, new Vector3(0f, -0.03f, -0.22f), new Vector3(0.055f, 0.09f, 0.26f), Wood, new Vector3(-5f, 0f, 0f));
                p.Box(Grip, new Vector3(0f, 0f, 0.31f), new Vector3(0.06f, 0.07f, 0.18f), Wood);
                p.Box(Detail, new Vector3(0f, 0.01f, 0.48f), new Vector3(0.025f, 0.025f, 0.2f), DarkMetal);
                p.Box(Detail, new Vector3(0f, -0.13f, 0.12f), new Vector3(0.045f, 0.17f, 0.07f), DarkMetal, new Vector3(18f, 0f, 0f));
                return 0.58f;

            case "m4a1":
                p.Box(Main, new Vector3(0f, 0f, 0.05f), new Vector3(0.06f, 0.1f, 0.35f), Metal);
                p.Box(Detail, new Vector3(0f, 0.075f, 0.05f), new Vector3(0.025f, 0.04f, 0.2f), DarkMetal);
                p.Box(Grip, new Vector3(0f, -0.01f, -0.22f), new Vector3(0.05f, 0.09f, 0.22f), DarkMetal);
                p.Box(Main, new Vector3(0f, 0f, 0.3f), new Vector3(0.065f, 0.07f, 0.2f), DarkMetal);
                p.Box(Detail, new Vector3(0f, 0.01f, 0.52f), new Vector3(0.04f, 0.04f, 0.26f), DarkMetal);
                p.Box(Detail, new Vector3(0f, -0.12f, 0.1f), new Vector3(0.04f, 0.15f, 0.06f), DarkMetal, new Vector3(8f, 0f, 0f));
                return 0.65f;

            default: // awp
                p.Box(Main, new Vector3(0f, 0f, 0.05f), new Vector3(0.07f, 0.1f, 0.5f), Olive);
                p.Box(Detail, new Vector3(0f, 0.09f, 0.08f), new Vector3(0.05f, 0.05f, 0.32f), DarkMetal);
                p.Box(Detail, new Vector3(0f, 0.01f, 0.52f), new Vector3(0.03f, 0.03f, 0.45f), DarkMetal);
                p.Box(Main, new Vector3(0f, -0.03f, -0.32f), new Vector3(0.07f, 0.12f, 0.3f), Olive);
                p.Box(Grip, new Vector3(0f, -0.1f, 0.05f), new Vector3(0.04f, 0.08f, 0.06f), DarkMetal);
                return 0.75f;
        }
    }

    /// <summary>Adds boxes to a gun, painted with the skin's material for their part (or their own color).</summary>
    readonly struct Painter
    {
        readonly Transform root;
        readonly WeaponSkin skin;

        public Painter(Transform root, WeaponSkin skin)
        {
            this.root = root;
            this.skin = skin;
        }

        public void Box(SkinPart part, Vector3 position, Vector3 scale, Color color, Vector3 euler = default)
        {
            var box = Effects.Shape(PrimitiveType.Cube, root, position, scale, color, false, euler);
            var material = WeaponSkins.MaterialFor(skin, part);
            if (material != null) box.GetComponent<Renderer>().sharedMaterial = material;
        }
    }
}

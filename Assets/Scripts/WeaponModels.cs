using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Box-model guns, shared by the first-person view model and the bots.</summary>
public static class WeaponModels
{
    static readonly Color Metal = new Color(0.14f, 0.14f, 0.15f);
    static readonly Color DarkMetal = new Color(0.07f, 0.07f, 0.08f);
    static readonly Color Steel = new Color(0.68f, 0.7f, 0.73f);
    static readonly Color Wood = new Color(0.45f, 0.27f, 0.13f);
    static readonly Color Olive = new Color(0.27f, 0.33f, 0.22f);

    /// <summary>Builds the gun pointing along +Z and returns its root; <paramref name="muzzle"/> marks the barrel end.</summary>
    public static Transform Build(WeaponData weapon, Transform parent, bool castShadows, out Transform muzzle)
    {
        var root = new GameObject(weapon.Name).transform;
        root.SetParent(parent, false);
        float length;

        switch (weapon.Id)
        {
            case "knife":
                Part(root, new Vector3(0f, 0f, 0.13f), new Vector3(0.015f, 0.045f, 0.2f), Steel);
                Part(root, new Vector3(0f, -0.005f, -0.02f), new Vector3(0.035f, 0.05f, 0.11f), DarkMetal);
                length = 0.23f;
                break;

            case "glock":
            case "usp":
            case "deagle":
                float s = weapon.Id == "deagle" ? 1.25f : 1f;
                Part(root, new Vector3(0f, 0.03f, 0.06f) * s, new Vector3(0.045f, 0.055f, 0.2f) * s, weapon.Id == "deagle" ? Steel : Metal);
                Part(root, new Vector3(0f, -0.045f, 0f) * s, new Vector3(0.04f, 0.11f, 0.055f) * s, DarkMetal, new Vector3(12f, 0f, 0f));
                length = 0.17f * s;
                break;

            case "tec9":
                Part(root, new Vector3(0f, 0.02f, 0.07f), new Vector3(0.05f, 0.07f, 0.24f), Metal);
                Part(root, new Vector3(0f, 0.02f, 0.22f), new Vector3(0.035f, 0.035f, 0.08f), DarkMetal);
                Part(root, new Vector3(0f, -0.1f, 0.12f), new Vector3(0.035f, 0.15f, 0.04f), DarkMetal);
                Part(root, new Vector3(0f, -0.06f, -0.02f), new Vector3(0.04f, 0.1f, 0.05f), DarkMetal, new Vector3(12f, 0f, 0f));
                length = 0.27f;
                break;

            case "m1911":
                Part(root, new Vector3(0f, 0.03f, 0.06f), new Vector3(0.042f, 0.05f, 0.21f), Metal);
                Part(root, new Vector3(0f, -0.045f, 0f), new Vector3(0.04f, 0.11f, 0.055f), Wood, new Vector3(12f, 0f, 0f));
                length = 0.17f;
                break;

            case "rpg":
                Part(root, new Vector3(0f, 0f, 0.05f), new Vector3(0.09f, 0.09f, 0.95f), Olive);
                Part(root, new Vector3(0f, 0f, 0.6f), new Vector3(0.13f, 0.13f, 0.18f), new Color(0.2f, 0.24f, 0.17f));
                Part(root, new Vector3(0f, 0f, 0.72f), new Vector3(0.07f, 0.07f, 0.08f), DarkMetal);
                Part(root, new Vector3(0f, -0.09f, 0.05f), new Vector3(0.035f, 0.1f, 0.04f), Wood);
                Part(root, new Vector3(0f, -0.09f, -0.12f), new Vector3(0.035f, 0.1f, 0.04f), Wood);
                Part(root, new Vector3(-0.06f, 0.06f, 0.1f), new Vector3(0.02f, 0.05f, 0.04f), DarkMetal);
                length = 0.8f;
                break;

            case "mp5":
                Part(root, new Vector3(0f, 0f, 0.1f), new Vector3(0.06f, 0.09f, 0.4f), Metal);
                Part(root, new Vector3(0f, -0.11f, 0.13f), new Vector3(0.04f, 0.15f, 0.05f), DarkMetal, new Vector3(10f, 0f, 0f));
                Part(root, new Vector3(0f, -0.07f, -0.02f), new Vector3(0.045f, 0.1f, 0.05f), DarkMetal, new Vector3(15f, 0f, 0f));
                Part(root, new Vector3(0f, 0.01f, 0.35f), new Vector3(0.03f, 0.03f, 0.12f), DarkMetal);
                Part(root, new Vector3(0f, 0f, -0.16f), new Vector3(0.04f, 0.06f, 0.14f), DarkMetal);
                length = 0.41f;
                break;

            case "shotgun":
                Part(root, new Vector3(0f, 0.02f, 0.28f), new Vector3(0.045f, 0.045f, 0.7f), DarkMetal);
                Part(root, new Vector3(0f, -0.03f, 0.3f), new Vector3(0.06f, 0.05f, 0.2f), Wood);
                Part(root, new Vector3(0f, 0f, 0f), new Vector3(0.06f, 0.09f, 0.22f), Metal);
                Part(root, new Vector3(0f, -0.04f, -0.22f), new Vector3(0.06f, 0.1f, 0.3f), Wood, new Vector3(-6f, 0f, 0f));
                length = 0.63f;
                break;

            case "ak47":
                Part(root, new Vector3(0f, 0f, 0.05f), new Vector3(0.06f, 0.09f, 0.35f), Metal);
                Part(root, new Vector3(0f, -0.03f, -0.22f), new Vector3(0.055f, 0.09f, 0.26f), Wood, new Vector3(-5f, 0f, 0f));
                Part(root, new Vector3(0f, 0f, 0.31f), new Vector3(0.06f, 0.07f, 0.18f), Wood);
                Part(root, new Vector3(0f, 0.01f, 0.48f), new Vector3(0.025f, 0.025f, 0.2f), DarkMetal);
                Part(root, new Vector3(0f, -0.13f, 0.12f), new Vector3(0.045f, 0.17f, 0.07f), DarkMetal, new Vector3(18f, 0f, 0f));
                length = 0.58f;
                break;

            case "m4a1":
                Part(root, new Vector3(0f, 0f, 0.05f), new Vector3(0.06f, 0.1f, 0.35f), Metal);
                Part(root, new Vector3(0f, 0.075f, 0.05f), new Vector3(0.025f, 0.04f, 0.2f), DarkMetal);
                Part(root, new Vector3(0f, -0.01f, -0.22f), new Vector3(0.05f, 0.09f, 0.22f), DarkMetal);
                Part(root, new Vector3(0f, 0f, 0.3f), new Vector3(0.065f, 0.07f, 0.2f), DarkMetal);
                Part(root, new Vector3(0f, 0.01f, 0.52f), new Vector3(0.04f, 0.04f, 0.26f), DarkMetal);
                Part(root, new Vector3(0f, -0.12f, 0.1f), new Vector3(0.04f, 0.15f, 0.06f), DarkMetal, new Vector3(8f, 0f, 0f));
                length = 0.65f;
                break;

            default: // awp
                Part(root, new Vector3(0f, 0f, 0.05f), new Vector3(0.07f, 0.1f, 0.5f), Olive);
                Part(root, new Vector3(0f, 0.09f, 0.08f), new Vector3(0.05f, 0.05f, 0.32f), DarkMetal);
                Part(root, new Vector3(0f, 0.01f, 0.52f), new Vector3(0.03f, 0.03f, 0.45f), DarkMetal);
                Part(root, new Vector3(0f, -0.03f, -0.32f), new Vector3(0.07f, 0.12f, 0.3f), Olive);
                Part(root, new Vector3(0f, -0.1f, 0.05f), new Vector3(0.04f, 0.08f, 0.06f), DarkMetal);
                length = 0.75f;
                break;
        }

        muzzle = new GameObject("Muzzle").transform;
        muzzle.SetParent(root, false);
        muzzle.localPosition = new Vector3(0f, 0.02f, length);

        if (!castShadows)
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
                renderer.shadowCastingMode = ShadowCastingMode.Off;
        return root;
    }

    static void Part(Transform root, Vector3 position, Vector3 scale, Color color, Vector3 euler = default) =>
        Effects.Shape(PrimitiveType.Cube, root, position, scale, color, false, euler);
}

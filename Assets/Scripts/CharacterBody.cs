using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The third-person body of a combatant (bots, and other people's players in multiplayer): the primitive
/// model, the gun in hand with its skin, the hit capsule, crouching, the death fall, footsteps and the
/// bomb on the back of whoever carries it.
/// </summary>
[RequireComponent(typeof(Combatant))]
public class CharacterBody : MonoBehaviour
{
    /// <summary>Every body in the scene, for the <see cref="VisibilityCuller"/>.</summary>
    public static readonly List<CharacterBody> All = new List<CharacterBody>();

    static readonly Dictionary<Team, Mesh> BodyMeshes = new Dictionary<Team, Mesh>();

    public Combatant Self { get; private set; }

    CapsuleCollider capsule;
    Transform visual;
    Transform gunMount;
    Transform bombOnBack;
    float diedAt = -1f;
    Vector3 lastPosition;
    float stepTimer;
    Renderer[] renderers = new Renderer[0];
    bool renderersDirty = true;
    bool culled;

    public void Setup()
    {
        Self = GetComponent<Combatant>();
        capsule = gameObject.AddComponent<CapsuleCollider>();
        capsule.radius = 0.35f;
        Build();
        SetHeight(Self.Height);
        Self.WeaponChanged += RefreshGun;
        Self.Died += _ => OnDied();
        if (Self.Current != null) RefreshGun();
        lastPosition = transform.position;
    }

    /// <summary>Stand back up for a new round.</summary>
    public void Revive()
    {
        diedAt = -1f;
        visual.localPosition = Vector3.zero;
        visual.localRotation = Quaternion.identity;
        capsule.enabled = true;
        lastPosition = transform.position;
    }

    /// <summary>Lie down without the fall (for someone who joins a round that is already running).</summary>
    public void ForceDead()
    {
        OnDied();
        diedAt = Time.time - 10f;
    }

    public void ShowBomb(bool show)
    {
        if (bombOnBack.gameObject.activeSelf != show) bombOnBack.gameObject.SetActive(show);
    }

    /// <summary>A box around the body standing or lying, for the view test.</summary>
    public Bounds ViewBounds => new Bounds(transform.position + Vector3.up, new Vector3(4f, 2.4f, 4f));

    public bool IsLying => diedAt >= 0f;

    public Vector3 GunTip => Self.Muzzle != null ? Self.Muzzle.position : gunMount.position;

    /// <summary>Stops (or starts again) drawing the body, gun and bomb, shadows included.</summary>
    public void SetCulled(bool hide)
    {
        if (visual == null) return;
        if (hide == culled && !renderersDirty) return;
        if (renderersDirty)
        {
            renderers = visual.GetComponentsInChildren<Renderer>(true);
            renderersDirty = false;
        }
        culled = hide;
        foreach (var r in renderers)
            if (r != null) r.forceRenderingOff = hide;
    }

    void OnEnable() => All.Add(this);

    void OnDisable() => All.Remove(this);

    void OnDied()
    {
        diedAt = Time.time;
        capsule.enabled = false;
        ShowBomb(false);
    }

    void Update()
    {
        if (diedAt >= 0f)
        {
            float t = Mathf.Clamp01((Time.time - diedAt) / 0.45f);
            visual.localRotation = Quaternion.Euler(-88f * t * t, 0f, 0f);
            visual.localPosition = new Vector3(0f, 0.12f * t, 0f);
            return;
        }
        SetHeight(Self.Height);
        Footsteps();
    }

    void SetHeight(float height)
    {
        capsule.height = height;
        capsule.center = new Vector3(0f, height / 2f, 0f);
        visual.localScale = new Vector3(1f, height / Combatant.StandHeight, 1f);
    }

    // Running is loud; walking (Shift) and crouching are slow enough to stay silent.
    void Footsteps()
    {
        float dt = Time.deltaTime;
        Vector3 moved = transform.position - lastPosition;
        moved.y = 0f;
        lastPosition = transform.position;
        if (dt <= 0f || moved.magnitude > 2f || moved.magnitude / dt < 3.5f) return;
        stepTimer -= dt;
        if (stepTimer > 0f) return;
        stepTimer = 0.36f;
        SoundFX.Play(SoundFX.Step, transform.position, 0.7f, Random.Range(0.85f, 1.1f), true, 28f);
    }

    void Build()
    {
        visual = new GameObject("Body").transform;
        visual.SetParent(transform, false);

        // The whole body is one mesh (one draw call), shared by everyone on the team.
        var model = new GameObject("Model");
        model.transform.SetParent(visual, false);
        model.AddComponent<MeshFilter>().sharedMesh = BodyMesh(Self.Team);
        model.AddComponent<MeshRenderer>().sharedMaterial = Effects.PaletteMaterial;

        gunMount = new GameObject("Gun").transform;
        gunMount.SetParent(visual, false);
        gunMount.localPosition = new Vector3(0f, 1.3f, 0.36f);

        bombOnBack = Effects.BombModel(visual);
        bombOnBack.localPosition = new Vector3(0f, 1.08f, -0.2f);
        bombOnBack.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        bombOnBack.gameObject.SetActive(false);
    }

    static Mesh BodyMesh(Team team)
    {
        if (BodyMeshes.TryGetValue(team, out var mesh) && mesh != null) return mesh;

        bool terrorist = team == Team.Terrorists;
        Color outfit = terrorist ? new Color(0.55f, 0.45f, 0.3f) : new Color(0.12f, 0.16f, 0.25f);
        Color trousers = terrorist ? new Color(0.4f, 0.34f, 0.24f) : new Color(0.1f, 0.12f, 0.18f);
        Color vest = terrorist ? new Color(0.33f, 0.29f, 0.21f) : new Color(0.06f, 0.07f, 0.09f);
        Color skin = new Color(0.86f, 0.68f, 0.52f);

        var root = new GameObject("Body parts").transform;
        var parts = new List<GameObject>
        {
            Effects.Shape(PrimitiveType.Cube, root, new Vector3(-0.12f, 0.42f, 0f), new Vector3(0.18f, 0.84f, 0.22f), trousers, palette: true),
            Effects.Shape(PrimitiveType.Cube, root, new Vector3(0.12f, 0.42f, 0f), new Vector3(0.18f, 0.84f, 0.22f), trousers, palette: true),
            Effects.Shape(PrimitiveType.Cube, root, new Vector3(0f, 1.15f, 0f), new Vector3(0.5f, 0.62f, 0.3f), outfit, palette: true),
            Effects.Shape(PrimitiveType.Cube, root, new Vector3(0f, 1.18f, 0f), new Vector3(0.54f, 0.45f, 0.34f), vest, palette: true),
            Effects.Shape(PrimitiveType.Cube, root, new Vector3(-0.27f, 1.25f, 0.18f), new Vector3(0.13f, 0.13f, 0.45f), outfit, euler: new Vector3(10f, 20f, 0f), palette: true),
            Effects.Shape(PrimitiveType.Cube, root, new Vector3(0.27f, 1.25f, 0.18f), new Vector3(0.13f, 0.13f, 0.45f), outfit, euler: new Vector3(10f, -20f, 0f), palette: true),
            Effects.Shape(PrimitiveType.Sphere, root, new Vector3(0f, 1.63f, 0f), new Vector3(0.3f, 0.32f, 0.3f), skin, palette: true),
        };
        if (terrorist)
        {
            // Red bandana
            parts.Add(Effects.Shape(PrimitiveType.Sphere, root, new Vector3(0f, 1.69f, -0.01f), new Vector3(0.32f, 0.24f, 0.32f), new Color(0.62f, 0.1f, 0.08f), palette: true));
        }
        else
        {
            // Helmet and goggles
            parts.Add(Effects.Shape(PrimitiveType.Sphere, root, new Vector3(0f, 1.7f, -0.01f), new Vector3(0.36f, 0.26f, 0.36f), new Color(0.05f, 0.06f, 0.08f), palette: true));
            parts.Add(Effects.Shape(PrimitiveType.Cube, root, new Vector3(0f, 1.64f, 0.14f), new Vector3(0.24f, 0.07f, 0.04f), new Color(0.15f, 0.18f, 0.2f), palette: true));
        }
        mesh = Effects.MergePaletteParts(root, parts, terrorist ? "Terrorist body" : "SWAT body");
        Destroy(root.gameObject);
        BodyMeshes[team] = mesh;
        return mesh;
    }

    void RefreshGun()
    {
        renderersDirty = true;
        for (int i = gunMount.childCount - 1; i >= 0; i--) Destroy(gunMount.GetChild(i).gameObject);
        if (Self.Current == null) return;
        var weapon = Self.Current.Data;

        // People show their own equipped skin; bots pick one by their seed.
        WeaponSkin skin = null;
        if (Self.SkinChoices != null && Self.SkinChoices.TryGetValue(weapon.Id, out var id)) skin = WeaponSkins.Find(weapon, id);
        if (skin == null && !Self.IsHuman) skin = WeaponSkins.Pick(weapon, Self.SkinSeed);

        WeaponModels.Build(weapon, gunMount, true, out var muzzle, skin);
        Self.Muzzle = muzzle;
    }
}

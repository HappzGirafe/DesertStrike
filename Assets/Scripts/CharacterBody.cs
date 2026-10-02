using UnityEngine;

/// <summary>
/// The third-person body of a combatant (bots, and other people's players in multiplayer): the primitive
/// model, the gun in hand with its skin, the hit capsule, crouching, the death fall, footsteps and the
/// bomb on the back of whoever carries it.
/// </summary>
[RequireComponent(typeof(Combatant))]
public class CharacterBody : MonoBehaviour
{
    public Combatant Self { get; private set; }

    CapsuleCollider capsule;
    Transform visual;
    Transform gunMount;
    Transform bombOnBack;
    float diedAt = -1f;
    Vector3 lastPosition;
    float stepTimer;

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

        bool terrorist = Self.Team == Team.Terrorists;
        Color outfit = terrorist ? new Color(0.55f, 0.45f, 0.3f) : new Color(0.12f, 0.16f, 0.25f);
        Color trousers = terrorist ? new Color(0.4f, 0.34f, 0.24f) : new Color(0.1f, 0.12f, 0.18f);
        Color vest = terrorist ? new Color(0.33f, 0.29f, 0.21f) : new Color(0.06f, 0.07f, 0.09f);
        Color skin = new Color(0.86f, 0.68f, 0.52f);

        Effects.Shape(PrimitiveType.Cube, visual, new Vector3(-0.12f, 0.42f, 0f), new Vector3(0.18f, 0.84f, 0.22f), trousers);
        Effects.Shape(PrimitiveType.Cube, visual, new Vector3(0.12f, 0.42f, 0f), new Vector3(0.18f, 0.84f, 0.22f), trousers);
        Effects.Shape(PrimitiveType.Cube, visual, new Vector3(0f, 1.15f, 0f), new Vector3(0.5f, 0.62f, 0.3f), outfit);
        Effects.Shape(PrimitiveType.Cube, visual, new Vector3(0f, 1.18f, 0f), new Vector3(0.54f, 0.45f, 0.34f), vest);
        Effects.Shape(PrimitiveType.Cube, visual, new Vector3(-0.27f, 1.25f, 0.18f), new Vector3(0.13f, 0.13f, 0.45f), outfit, euler: new Vector3(10f, 20f, 0f));
        Effects.Shape(PrimitiveType.Cube, visual, new Vector3(0.27f, 1.25f, 0.18f), new Vector3(0.13f, 0.13f, 0.45f), outfit, euler: new Vector3(10f, -20f, 0f));
        Effects.Shape(PrimitiveType.Sphere, visual, new Vector3(0f, 1.63f, 0f), new Vector3(0.3f, 0.32f, 0.3f), skin);
        if (terrorist)
        {
            // Red bandana
            Effects.Shape(PrimitiveType.Sphere, visual, new Vector3(0f, 1.69f, -0.01f), new Vector3(0.32f, 0.24f, 0.32f), new Color(0.62f, 0.1f, 0.08f));
        }
        else
        {
            // Helmet and goggles
            Effects.Shape(PrimitiveType.Sphere, visual, new Vector3(0f, 1.7f, -0.01f), new Vector3(0.36f, 0.26f, 0.36f), new Color(0.05f, 0.06f, 0.08f));
            Effects.Shape(PrimitiveType.Cube, visual, new Vector3(0f, 1.64f, 0.14f), new Vector3(0.24f, 0.07f, 0.04f), new Color(0.15f, 0.18f, 0.2f));
        }

        gunMount = new GameObject("Gun").transform;
        gunMount.SetParent(visual, false);
        gunMount.localPosition = new Vector3(0f, 1.3f, 0.36f);

        bombOnBack = Effects.BombModel(visual);
        bombOnBack.localPosition = new Vector3(0f, 1.08f, -0.2f);
        bombOnBack.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        bombOnBack.gameObject.SetActive(false);
    }

    void RefreshGun()
    {
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

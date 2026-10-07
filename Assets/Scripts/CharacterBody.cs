using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The third-person body of a combatant (bots, and other people's players in multiplayer): a jointed figure
/// (hips, upper body, thighs and shins on one skinned mesh, still one draw call) that walks and runs, crouches,
/// tucks its legs in a jump and crumples when it dies, all posed by code each frame; the gun in hand with its skin,
/// the hit capsule, footsteps and the bomb on the back of whoever carries it.
/// </summary>
[RequireComponent(typeof(Combatant))]
public class CharacterBody : MonoBehaviour
{
    /// <summary>Every body in the scene, for the <see cref="VisibilityCuller"/>.</summary>
    public static readonly List<CharacterBody> All = new List<CharacterBody>();

    static readonly Dictionary<Team, Mesh> BodyMeshes = new Dictionary<Team, Mesh>();

    const float LegLength = 0.42f;                 // thigh and shin each
    const float HipHeight = 2f * LegLength;        // standing
    const float CrouchedHips = 0.3f;               // a deep squat: the head comes down to Combatant.CrouchHeight
    const float Stride = 1.7f;                     // metres for a full walk cycle (two steps)
    const float ShoulderHeight = 1.38f;

    // The joints, in the order the mesh's bone weights use, and where they sit when standing.
    static readonly Vector3[] RestPositions =
    {
        new Vector3(0f, HipHeight, 0f),            // 0 hips
        new Vector3(0f, HipHeight, 0f),            // 1 upper body
        new Vector3(-0.12f, HipHeight, 0f),        // 2 left thigh
        new Vector3(-0.12f, LegLength, 0f),        // 3 left shin
        new Vector3(0.12f, HipHeight, 0f),         // 4 right thigh
        new Vector3(0.12f, LegLength, 0f),         // 5 right shin
        new Vector3(0f, ShoulderHeight, 0f),       // 6 arms (and the gun in the hands)
    };

    public Combatant Self { get; private set; }

    CapsuleCollider capsule;
    Transform visual;                              // the whole figure: turns to fall over when dying
    Transform hips, spine, thighL, shinL, thighR, shinR, arms;
    Transform gunMount;
    Transform bombOnBack;
    float diedAt = -1f;
    Vector3 fallAxis = Vector3.right;              // the figure tips over round this (its own) axis when it dies
    Vector3 lastPosition;
    float stepTimer;
    float stridePhase, moving, inAir;
    string shownPose;                              // set by ShowPose: a still pose for the pose gallery
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
        Self.Died += OnDied;
        if (Self.Current != null) RefreshGun();
        lastPosition = transform.position;
    }

    /// <summary>Stand back up for a new round.</summary>
    public void Revive()
    {
        diedAt = -1f;
        visual.localPosition = Vector3.zero;
        visual.localRotation = Quaternion.identity;
        stridePhase = moving = inAir = 0f;
        Pose(0f, 0f, 0f, 0f);
        capsule.enabled = true;
        lastPosition = transform.position;
    }

    /// <summary>Lie down without the fall (for someone who joins a round that is already running).</summary>
    public void ForceDead()
    {
        OnDied(null);
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

    void OnDied(Combatant killer)
    {
        diedAt = Time.time;
        capsule.enabled = false;
        ShowBomb(false);
        // Fall away from whoever did it (or any way at all): tip the top of the figure that way.
        Vector3 away = killer != null ? transform.InverseTransformDirection(transform.position - killer.transform.position) : Vector3.zero;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
        fallAxis = Vector3.Cross(Vector3.up, away.normalized);
    }

    /// <summary>
    /// Holds a still pose, for the pose gallery (-ds-pose-gallery): "stand", "walk" (<paramref name="time"/> = where
    /// in the step, 0..2 pi), "crouch", "crouch walk", "jump", "dying" (<paramref name="time"/> seconds into the fall)
    /// or "dead"; a dying figure falls backwards.
    /// </summary>
    public void ShowPose(string pose, float time)
    {
        shownPose = pose;
        fallAxis = Vector3.Cross(Vector3.up, Vector3.back);
        switch (pose)
        {
            case "walk": Pose(1f, time, 0f, 0f); break;
            case "crouch": Pose(0f, 0f, 1f, 0f); break;
            case "crouch walk": Pose(1f, time, 1f, 0f); break;
            case "jump": Pose(0f, 0f, 0f, 1f); break;
            case "dying": PoseDead(time); break;
            case "dead": PoseDead(10f); break;
            default: Pose(0f, 0f, 0f, 0f); break;
        }
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (shownPose != null) return;
        if (diedAt >= 0f)
        {
            PoseDead(Time.time - diedAt);
            return;
        }
        SetHeight(Self.Height);

        Vector3 moved = transform.position - lastPosition;
        moved.y = 0f;
        lastPosition = transform.position;
        float speed = dt > 0f && moved.magnitude < 2f ? moved.magnitude / dt : 0f;
        Footsteps(speed, dt);

        // Walking: the legs swing through a cycle as far as the body moves, harder the faster it goes.
        moving = Mathf.MoveTowards(moving, Mathf.Clamp01(speed / 5f), 4f * dt);
        stridePhase = Mathf.Repeat(stridePhase + speed * dt / Stride * 2f * Mathf.PI, 2f * Mathf.PI);
        // Jumping or falling: the ground is more than a hand below the feet.
        bool airborne = !Physics.Raycast(transform.position + Vector3.up * 0.3f, Vector3.down, 0.55f,
                                         Ballistics.EnvironmentMask, QueryTriggerInteraction.Ignore);
        inAir = Mathf.MoveTowards(inAir, airborne ? 1f : 0f, 7f * dt);
        float crouch = Mathf.InverseLerp(Combatant.StandHeight, Combatant.CrouchHeight, Self.Height);
        Pose(moving, stridePhase, crouch, inAir);
    }

    /// <summary>Sets the joints: <paramref name="move"/> 0..1 how hard it walks or runs, <paramref name="phase"/>
    /// where it is in the step, <paramref name="crouch"/> 0..1 how far down, <paramref name="air"/> 0..1 how much in
    /// a jump.</summary>
    void Pose(float move, float phase, float crouch, float air)
    {
        // Crouching: thighs forward and knees bent twice as much keep the feet under the hips.
        float hipHeight = Mathf.Lerp(HipHeight, CrouchedHips, crouch);
        float squat = Mathf.Acos(Mathf.Clamp(hipHeight / HipHeight, -1f, 1f)) * Mathf.Rad2Deg;
        float walking = move * (1f - air);
        float swing = Mathf.Sin(phase) * 32f * walking * (1f - 0.45f * crouch);
        float kneeL = Mathf.Max(0f, Mathf.Cos(phase)) * 50f * walking;    // the leg swinging forward bends its knee
        float kneeR = Mathf.Max(0f, -Mathf.Cos(phase)) * 50f * walking;
        float tuck = 38f * air;                                            // in a jump both legs come up

        thighL.localRotation = Quaternion.Euler(-(squat + swing + tuck), 0f, 0f);   // a negative turn swings forward
        shinL.localRotation = Quaternion.Euler(2f * squat + kneeL + 2f * tuck, 0f, 0f);
        thighR.localRotation = Quaternion.Euler(-(squat - swing + tuck * 0.8f), 0f, 0f);
        shinR.localRotation = Quaternion.Euler(2f * squat + kneeR + 1.8f * tuck, 0f, 0f);

        float bob = walking * 0.04f * (Mathf.Abs(Mathf.Cos(phase)) - 0.5f);
        hips.localPosition = new Vector3(0f, hipHeight + bob, -0.12f * crouch);
        float lean = 7f * walking + 14f * crouch - 6f * air;
        spine.localRotation = Quaternion.Euler(lean, Mathf.Sin(phase) * 4f * walking, 0f);
        arms.localRotation = Quaternion.Euler(-lean, 0f, 0f);   // the gun stays level however the body leans
    }

    /// <summary>Dying: the knees give way, then the figure tips over (away from the killer) and lies there.</summary>
    void PoseDead(float since)
    {
        float buckle = Mathf.Clamp01(since / 0.22f);
        float fall = Mathf.Clamp01((since - 0.1f) / 0.5f);
        fall *= fall;   // faster and faster, like falling
        float squat = 40f * buckle * (1f - 0.6f * fall);
        thighL.localRotation = Quaternion.Euler(-(squat + 12f * fall), 0f, 0f);
        shinL.localRotation = Quaternion.Euler(2f * squat + 10f * fall, 0f, 0f);
        thighR.localRotation = Quaternion.Euler(-(squat * 0.6f - 8f * fall), 0f, 4f * fall);
        shinR.localRotation = Quaternion.Euler(1.4f * squat + 25f * fall, 0f, 0f);
        float hipHeight = HipHeight * Mathf.Cos(squat * Mathf.Deg2Rad);
        hips.localPosition = new Vector3(0f, hipHeight, 0f);
        spine.localRotation = Quaternion.Euler(18f * buckle * (1f - fall) - 10f * fall, 0f, 0f);
        // The gun dips as the knees go, then the arms fly up over the head and lie there with it.
        arms.localRotation = Quaternion.Euler(30f * buckle * (1f - fall) - 100f * fall, 0f, 0f);
        visual.localRotation = Quaternion.AngleAxis(86f * fall, fallAxis);
        visual.localPosition = new Vector3(0f, 0.16f * fall, 0f);
    }

    void SetHeight(float height)
    {
        capsule.height = height;
        capsule.center = new Vector3(0f, height / 2f, 0f);
    }

    // Running is loud; walking (Shift) and crouching are slow enough to stay silent.
    void Footsteps(float speed, float dt)
    {
        if (speed < 3.5f) return;
        stepTimer -= dt;
        if (stepTimer > 0f) return;
        stepTimer = 0.36f;
        SoundFX.Play(SoundFX.Step, transform.position, 0.7f, Random.Range(0.85f, 1.1f), true, 28f);
    }

    void Build()
    {
        visual = new GameObject("Body").transform;
        visual.SetParent(transform, false);
        hips = Joint("Hips", visual, RestPositions[0]);
        spine = Joint("Upper body", hips, Vector3.zero);
        thighL = Joint("Left thigh", hips, RestPositions[2] - RestPositions[0]);
        shinL = Joint("Left shin", thighL, RestPositions[3] - RestPositions[2]);
        thighR = Joint("Right thigh", hips, RestPositions[4] - RestPositions[0]);
        shinR = Joint("Right shin", thighR, RestPositions[5] - RestPositions[4]);
        arms = Joint("Arms", spine, RestPositions[6] - RestPositions[1]);

        // The whole body is one skinned mesh (one draw call), shared by everyone on the team.
        var model = new GameObject("Model");
        model.transform.SetParent(visual, false);
        var skin = model.AddComponent<SkinnedMeshRenderer>();
        skin.sharedMesh = BodyMesh(Self.Team);
        skin.sharedMaterial = Effects.PaletteMaterial;
        skin.bones = new[] { hips, spine, thighL, shinL, thighR, shinR, arms };
        skin.rootBone = hips;
        skin.quality = SkinQuality.Bone1;   // every part moves with one joint
        skin.localBounds = new Bounds(Vector3.zero, new Vector3(2.8f, 2.8f, 2.8f));   // any pose, lying included

        gunMount = new GameObject("Gun").transform;
        gunMount.SetParent(arms, false);
        gunMount.localPosition = new Vector3(0f, 1.3f - ShoulderHeight, 0.36f);

        bombOnBack = Effects.BombModel(spine);
        bombOnBack.localPosition = new Vector3(0f, 1.08f - HipHeight, -0.2f);
        bombOnBack.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        bombOnBack.gameObject.SetActive(false);
    }

    static Transform Joint(string name, Transform parent, Vector3 localPosition)
    {
        var joint = new GameObject(name).transform;
        joint.SetParent(parent, false);
        joint.localPosition = localPosition;
        return joint;
    }

    /// <summary>
    /// The team's body: primitive parts in palette colours merged into one mesh, each part weighted to the joint it
    /// moves with (0 hips, 1 upper body, 2/3 left thigh/shin, 4/5 right thigh/shin, 6 arms).
    /// </summary>
    static Mesh BodyMesh(Team team)
    {
        if (BodyMeshes.TryGetValue(team, out var mesh) && mesh != null) return mesh;

        bool terrorist = team == Team.Terrorists;
        Color outfit = terrorist ? new Color(0.55f, 0.45f, 0.3f) : new Color(0.12f, 0.16f, 0.25f);
        Color trousers = terrorist ? new Color(0.4f, 0.34f, 0.24f) : new Color(0.1f, 0.12f, 0.18f);
        Color vest = terrorist ? new Color(0.33f, 0.29f, 0.21f) : new Color(0.06f, 0.07f, 0.09f);
        Color boots = terrorist ? new Color(0.24f, 0.18f, 0.12f) : new Color(0.05f, 0.05f, 0.06f);
        Color skin = new Color(0.86f, 0.68f, 0.52f);

        var root = new GameObject("Body parts").transform;
        var parts = new List<(GameObject Part, int Joint)>();
        void Add(int joint, PrimitiveType type, Vector3 position, Vector3 size, Color color, Vector3 euler = default) =>
            parts.Add((Effects.Shape(type, root, position, size, color, euler: euler, palette: true), joint));

        Add(0, PrimitiveType.Cube, new Vector3(0f, 0.86f, 0f), new Vector3(0.42f, 0.2f, 0.24f), trousers);       // pelvis
        foreach (float side in new[] { -1f, 1f })
        {
            int thigh = side < 0f ? 2 : 4;
            Add(thigh, PrimitiveType.Cube, new Vector3(0.12f * side, 0.63f, 0f), new Vector3(0.18f, 0.46f, 0.22f), trousers);
            Add(thigh + 1, PrimitiveType.Cube, new Vector3(0.12f * side, 0.24f, 0f), new Vector3(0.17f, 0.46f, 0.2f), trousers);
            Add(thigh + 1, PrimitiveType.Cube, new Vector3(0.12f * side, 0.05f, 0.04f), new Vector3(0.19f, 0.1f, 0.3f), boots);
        }
        Add(1, PrimitiveType.Cube, new Vector3(0f, 1.15f, 0f), new Vector3(0.5f, 0.62f, 0.3f), outfit);
        Add(1, PrimitiveType.Cube, new Vector3(0f, 1.18f, 0f), new Vector3(0.54f, 0.45f, 0.34f), vest);
        Add(6, PrimitiveType.Cube, new Vector3(-0.27f, 1.25f, 0.18f), new Vector3(0.13f, 0.13f, 0.45f), outfit, new Vector3(10f, 20f, 0f));
        Add(6, PrimitiveType.Cube, new Vector3(0.27f, 1.25f, 0.18f), new Vector3(0.13f, 0.13f, 0.45f), outfit, new Vector3(10f, -20f, 0f));
        Add(1, PrimitiveType.Sphere, new Vector3(0f, 1.63f, 0f), new Vector3(0.3f, 0.32f, 0.3f), skin);
        if (terrorist)
        {
            Add(1, PrimitiveType.Sphere, new Vector3(0f, 1.69f, -0.01f), new Vector3(0.32f, 0.24f, 0.32f), new Color(0.62f, 0.1f, 0.08f));   // red bandana
        }
        else
        {
            Add(1, PrimitiveType.Sphere, new Vector3(0f, 1.7f, -0.01f), new Vector3(0.36f, 0.26f, 0.36f), new Color(0.05f, 0.06f, 0.08f));   // helmet
            Add(1, PrimitiveType.Cube, new Vector3(0f, 1.64f, 0.14f), new Vector3(0.24f, 0.07f, 0.04f), new Color(0.15f, 0.18f, 0.2f));     // goggles
        }

        var combine = new CombineInstance[parts.Count];
        var weights = new List<BoneWeight>();
        for (int i = 0; i < parts.Count; i++)
        {
            var part = parts[i].Part;
            combine[i].mesh = part.GetComponent<MeshFilter>().sharedMesh;
            combine[i].transform = root.worldToLocalMatrix * part.transform.localToWorldMatrix;
            for (int v = 0; v < combine[i].mesh.vertexCount; v++)
                weights.Add(new BoneWeight { boneIndex0 = parts[i].Joint, weight0 = 1f });
        }
        mesh = new Mesh { name = terrorist ? "Terrorist body" : "SWAT body" };
        mesh.CombineMeshes(combine, true, true);
        mesh.boneWeights = weights.ToArray();
        var bindposes = new Matrix4x4[RestPositions.Length];
        for (int i = 0; i < bindposes.Length; i++) bindposes[i] = Matrix4x4.Translate(-RestPositions[i]);
        mesh.bindposes = bindposes;
        mesh.RecalculateBounds();
        mesh.UploadMeshData(true);
        foreach (var (part, _) in parts) DestroyImmediate(part);
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
        var skin = WeaponSkins.SkinOf(Self, weapon);

        WeaponModels.Build(weapon, gunMount, true, out var muzzle, skin);
        Self.Muzzle = muzzle;
    }
}

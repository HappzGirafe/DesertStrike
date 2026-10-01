using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum BotDifficulty { Easy, Normal, Hard }

/// <summary>
/// CS-style bot. Each round it follows a plan from <see cref="PlanRound"/> (rush a site or hold an
/// angle, then hunt), reacts to what it sees, hears and gets shot by, and fights with the same
/// weapons and rules as the player.
/// </summary>
[RequireComponent(typeof(Combatant))]
public class BotController : MonoBehaviour
{
    struct Skill
    {
        public float Reaction;    // seconds before the first shot at a new target
        public float AimError;    // degrees of extra spread once settled on a target
        public float HeadChance;  // chance to aim for the head
        public float TurnSpeed;   // degrees per second
        public float SightRange;
        public bool Strafe;
    }

    static readonly string[][] TerroristRoutesA =
    {
        new[] { "OutsideLong", "LongDoors", "Long", "LongCorner", "ASite" },
        new[] { "Mid", "Catwalk", "Short", "ASite" },
    };

    static readonly string[][] TerroristRoutesB =
    {
        new[] { "OutsideTunnels", "TunnelHall", "Tunnels", "TunnelExit", "BSite" },
        new[] { "Mid", "LowerTunnels", "Tunnels", "TunnelExit", "BSite" },
    };

    static readonly (string[] Route, string LookAt)[] SwatPosts =
    {
        (new[] { "CTtoA", "AHoldLong" }, "LongCorner"),
        (new[] { "BDoors", "BHold" }, "TunnelExit"),
        (new[] { "CTMid" }, "Mid"),
        (new[] { "CTtoA", "AHoldShort" }, "Short"),
        (new[] { "BDoors", "BHold2" }, "TunnelExit"),
    };

    public Combatant Self { get; private set; }

    NavMeshAgent agent;
    CapsuleCollider body;
    Transform visual;
    Transform gunMount;
    Skill skill;
    int skinSeed;   // bots show off random (but consistent) skins

    // Round plan
    readonly List<Vector3> route = new List<Vector3>();
    int routeIndex;
    float waypointDeadline;
    Vector3 holdLook;
    float holdDuration;
    float holdUntil;
    float startMovingAt;
    bool hunting;

    // Combat and awareness
    Combatant target;
    bool targetVisible, headVisible, chestVisible, aimForHead;
    float sightingStarted;
    float lastSeenTime = -99f;
    Vector3 lastSeenPosition;
    Vector3 alertPosition;
    float alertUntil = -1f;
    float nextScan;
    int burstLeft = 4;
    float holdFireUntil;
    Vector3 strafeDirection;
    float strafeChangeAt;
    float stepTimer;
    float diedAt = -1f;

    bool HoldingAngle => routeIndex >= route.Count && !hunting;

    static Skill SkillFor(BotDifficulty difficulty)
    {
        switch (difficulty)
        {
            case BotDifficulty.Easy:
                return new Skill { Reaction = 0.75f, AimError = 4.5f, HeadChance = 0.08f, TurnSpeed = 220f, SightRange = 70f };
            case BotDifficulty.Hard:
                return new Skill { Reaction = 0.22f, AimError = 1.3f, HeadChance = 0.35f, TurnSpeed = 540f, SightRange = 140f, Strafe = true };
            default:
                return new Skill { Reaction = 0.42f, AimError = 2.4f, HeadChance = 0.18f, TurnSpeed = 360f, SightRange = 100f, Strafe = true };
        }
    }

    public void Setup(BotDifficulty difficulty)
    {
        Self = GetComponent<Combatant>();
        skill = SkillFor(difficulty);
        skinSeed = Random.Range(0, 1000);

        body = gameObject.AddComponent<CapsuleCollider>();
        body.center = new Vector3(0f, 0.9f, 0f);
        body.height = 1.8f;
        body.radius = 0.35f;

        agent = gameObject.AddComponent<NavMeshAgent>();
        agent.radius = 0.35f;
        agent.height = 1.8f;
        agent.speed = 5f;
        agent.acceleration = 30f;
        agent.angularSpeed = 600f;
        agent.stoppingDistance = 0.3f;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;

        var eye = new GameObject("Eye").transform;
        eye.SetParent(transform, false);
        eye.localPosition = new Vector3(0f, 1.62f, 0f);
        Self.Eye = eye;

        BuildBody();
        Self.WeaponChanged += RefreshGun;
        Self.Damaged += OnDamaged;
        Self.Died += OnDied;
    }

    // ----------------------------------------------------------------- round setup

    public void Respawn(Vector3 position, Quaternion rotation)
    {
        diedAt = -1f;
        visual.localPosition = Vector3.zero;
        visual.localRotation = Quaternion.identity;
        body.enabled = true;
        agent.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        agent.enabled = true;
        agent.Warp(position);

        target = null;
        targetVisible = false;
        lastSeenTime = -99f;
        alertUntil = -1f;
        route.Clear();
        routeIndex = 0;
        hunting = false;
        Halt();
    }

    public void Plan(List<Vector3> waypoints, Vector3 lookAt, float holdSeconds, float startTime)
    {
        route.Clear();
        foreach (var point in waypoints) route.Add(Jitter(point));
        routeIndex = 0;
        waypointDeadline = startTime + 30f;
        holdLook = lookAt;
        holdDuration = holdSeconds;
        startMovingAt = startTime;
        hunting = false;
    }

    /// <summary>Spends money like a simple CS bot: rifle if affordable, otherwise SMG or pistol, then armor.</summary>
    public void BuyGear()
    {
        var me = Self;
        if (me.Primary == null)
        {
            var rifle = me.Team == Team.Terrorists ? WeaponData.Ak47 : WeaponData.M4a1;
            if (me.Money >= WeaponData.Rpg.Price + WeaponData.KevlarHelmetPrice && Random.value < 0.15f) me.TryBuy(WeaponData.Rpg);
            else if (me.Money >= WeaponData.Awp.Price + WeaponData.KevlarHelmetPrice && Random.value < 0.2f) me.TryBuy(WeaponData.Awp);
            else if (me.Money >= rifle.Price + WeaponData.KevlarPrice) me.TryBuy(rifle);
            else if (me.Money >= WeaponData.Mp5.Price + WeaponData.KevlarPrice) me.TryBuy(Random.value < 0.3f ? WeaponData.Shotgun : WeaponData.Mp5);
        }
        bool starterPistol = me.Secondary.Data == WeaponData.Glock || me.Secondary.Data == WeaponData.Usp;
        if (me.Primary == null && starterPistol)
        {
            var machinePistol = me.Team == Team.Terrorists ? WeaponData.TecDc9 : WeaponData.M1911;
            if (me.Money >= WeaponData.Deagle.Price + WeaponData.KevlarPrice && Random.value < 0.4f) me.TryBuy(WeaponData.Deagle);
            else if (me.Money >= machinePistol.Price + WeaponData.KevlarPrice && Random.value < 0.6f) me.TryBuy(machinePistol);
        }

        if (me.Money >= WeaponData.KevlarHelmetPrice) me.TryBuyArmor(true);
        else if (me.Money >= WeaponData.KevlarPrice) me.TryBuyArmor(false);
        me.Equip(me.BestSlot(), instant: true);
    }

    /// <summary>Terrorists all go for one site by two routes; SWAT spread over the defensive posts.</summary>
    public static void PlanRound(List<BotController> terrorists, List<BotController> swat, MapBuilder map, float startTime)
    {
        bool attackA = Random.value < 0.5f;
        var routes = attackA ? TerroristRoutesA : TerroristRoutesB;
        Vector3 watch = map.Point(attackA ? "CTtoA" : "BDoors");
        for (int i = 0; i < terrorists.Count; i++)
            terrorists[i].Plan(map.PointsFor(routes[i % routes.Length]), watch, Random.Range(6f, 14f), startTime + Random.Range(0f, 2f));

        for (int i = 0; i < swat.Count; i++)
        {
            var post = SwatPosts[i % SwatPosts.Length];
            swat[i].Plan(map.PointsFor(post.Route), map.Point(post.LookAt), Random.Range(35f, 60f), startTime + Random.Range(0f, 0.5f));
        }
    }

    // ----------------------------------------------------------------- per-frame brain

    void Update()
    {
        if (!Self.IsAlive)
        {
            AnimateDeath();
            return;
        }
        var gm = GameManager.Instance;
        if (gm.IsPaused) return;
        if (gm.State != MatchState.Live && gm.State != MatchState.RoundEnd)
        {
            Halt();
            return;
        }

        if (Time.time >= nextScan)
        {
            nextScan = Time.time + 0.12f;
            Scan();
        }

        if (target != null && target.IsAlive && targetVisible) Fight();
        else if (target != null && target.IsAlive && Time.time - lastSeenTime < 5f) Chase();
        else if (Time.time < alertUntil) Investigate();
        else
        {
            target = null;
            FollowRoute();
        }

        ManageAmmo();
        Footsteps();
    }

    public void HearNoise(Vector3 position)
    {
        if (!Self.IsAlive || targetVisible || Time.time - lastSeenTime < 2f) return;
        alertPosition = position;
        alertUntil = Time.time + 6f;
    }

    void Scan()
    {
        Combatant best = null;
        float bestScore = float.MaxValue;
        bool bestHead = false, bestChest = false;
        Vector3 eye = Self.EyePosition;

        foreach (var other in GameManager.Instance.Combatants)
        {
            if (other == Self || !other.IsAlive || other.Team == Self.Team) continue;
            Vector3 toChest = other.ChestPosition - eye;
            float distance = toChest.magnitude;
            if (distance > skill.SightRange) continue;

            var flat = new Vector3(toChest.x, 0f, toChest.z);
            float viewAngle = HoldingAngle ? 85f : 65f;   // a bot holding an angle is watching more carefully
            bool inView = distance < 4f || Vector3.Angle(transform.forward, flat) < viewAngle
                          || (other == target && Time.time - lastSeenTime < 1f);
            if (!inView) continue;

            bool chest = Ballistics.HasLineOfSight(eye, other.ChestPosition);
            bool head = Ballistics.HasLineOfSight(eye, other.HeadPosition);
            if (!chest && !head) continue;

            float score = distance - (other == target ? 5f : 0f);
            if (score < bestScore)
            {
                best = other;
                bestScore = score;
                bestHead = head;
                bestChest = chest;
            }
        }

        bool wasVisible = targetVisible;
        targetVisible = best != null;
        if (best == null) return;

        // A new target (or one that was out of sight for a while) costs a reaction delay.
        if (best != target || (!wasVisible && Time.time - lastSeenTime > 1f))
        {
            sightingStarted = Time.time;
            aimForHead = Random.value < skill.HeadChance;
        }
        target = best;
        headVisible = bestHead;
        chestVisible = bestChest;
        lastSeenTime = Time.time;
        lastSeenPosition = best.transform.position;
        alertUntil = -1f;
    }

    void Fight()
    {
        Vector3 aimPoint = (aimForHead && headVisible) || !chestVisible ? target.HeadPosition : target.ChestPosition;
        TurnTowards(aimPoint, skill.TurnSpeed);
        var current = Self.Current;
        var weapon = current.Data;
        float distance = FlatDistance(transform.position, target.transform.position);

        // Too close for a rocket: pull the pistol instead of blowing ourselves up.
        if (weapon.Explosive && distance < 7f && Self.Secondary != null && Self.Secondary.Mag + Self.Secondary.Reserve > 0)
        {
            Self.Equip(WeaponSlot.Secondary);
            return;
        }
        if (weapon.SelectFire) current.AutoMode = distance < 15f;

        if (weapon.IsMelee) MoveTo(target.transform.position);
        else if (skill.Strafe && Time.time < holdFireUntil) Strafe();
        else Halt();

        // Holding an angle means already being aimed at it, so the first shot comes sooner.
        float reaction = HoldingAngle ? skill.Reaction * 0.6f : skill.Reaction;
        if (Time.time - sightingStarted < reaction) return;
        Vector3 flat = aimPoint - transform.position;
        flat.y = 0f;
        if (Vector3.Angle(transform.forward, flat) > 10f) return;
        if (weapon.IsMelee && distance > 1.8f) return;
        if (Time.time < holdFireUntil) return;
        if (Ballistics.FriendInLine(Self, aimPoint))
        {
            holdFireUntil = Time.time + 0.3f;
            return;
        }

        float settle = Mathf.Clamp01((Time.time - sightingStarted - reaction) / 1.5f);
        float error = skill.AimError * Mathf.Lerp(1.5f, 0.6f, settle);
        Vector3 direction = (aimPoint - Self.EyePosition).normalized;
        if (!Self.TryFire(direction, current.Spread + error)) return;

        if (current.IsAutomatic)
        {
            if (--burstLeft <= 0)
            {
                burstLeft = Random.Range(3, 7);
                holdFireUntil = Time.time + Random.Range(0.2f, 0.45f);
            }
        }
        else
        {
            holdFireUntil = Time.time + Random.Range(0.05f, 0.25f);
        }
    }

    void Strafe()
    {
        if (!agent.isOnNavMesh) return;
        if (Time.time >= strafeChangeAt)
        {
            strafeChangeAt = Time.time + Random.Range(0.3f, 0.8f);
            strafeDirection = Random.value < 0.5f ? transform.right : -transform.right;
        }
        agent.isStopped = true;
        agent.Move(strafeDirection * 3f * Time.deltaTime);
    }

    void Chase()
    {
        agent.updateRotation = true;
        MoveTo(lastSeenPosition);
        if (FlatDistance(transform.position, lastSeenPosition) < 1.5f) lastSeenTime = -99f;
    }

    void Investigate()
    {
        if (HoldingAngle && FlatDistance(transform.position, alertPosition) < 20f)
        {
            // Defenders near the fight keep their post and turn towards it; the rest rotate to help.
            Halt();
            TurnTowards(alertPosition, skill.TurnSpeed * 0.6f);
            return;
        }
        agent.updateRotation = true;
        MoveTo(alertPosition);
        if (FlatDistance(transform.position, alertPosition) < 2f) alertUntil = -1f;
    }

    void FollowRoute()
    {
        if (Time.time < startMovingAt)
        {
            Halt();
            return;
        }
        if (routeIndex < route.Count)
        {
            agent.updateRotation = true;
            MoveTo(route[routeIndex]);
            if (FlatDistance(transform.position, route[routeIndex]) < 1.6f || Time.time > waypointDeadline)
                AdvanceWaypoint();
            return;
        }

        // Holding: watch the chosen angle with a slow sweep, then go hunting.
        Halt();
        float sweep = Mathf.Sin(Time.time * 0.7f + GetInstanceID() * 0.37f) * 25f;
        TurnTowards(holdLook, 160f, sweep);
        if (Time.time >= holdUntil) StartHunting();
    }

    void AdvanceWaypoint()
    {
        routeIndex++;
        waypointDeadline = Time.time + 30f;
        if (routeIndex >= route.Count) holdUntil = Time.time + holdDuration;
    }

    void StartHunting()
    {
        var points = GameManager.Instance.Map.KeyPoints;
        route.Clear();
        route.Add(Jitter(points[Random.Range(0, points.Count)]));
        routeIndex = 0;
        waypointDeadline = Time.time + 30f;
        holdLook = points[Random.Range(0, points.Count)];
        holdDuration = Random.Range(2f, 6f);
        hunting = true;
    }

    void ManageAmmo()
    {
        var weapon = Self.Current;
        if (weapon == null || weapon.Data.IsMelee || Self.IsReloading) return;

        if (weapon.Mag == 0)
        {
            bool pistolReady = weapon.Data.Slot == WeaponSlot.Primary && Self.Secondary != null && Self.Secondary.Mag > 0;
            if (targetVisible && pistolReady) Self.Equip(WeaponSlot.Secondary);
            else if (weapon.Reserve > 0) Self.StartReload();
            else Self.Equip(Self.BestSlot());
            return;
        }

        bool calm = !targetVisible && Time.time - lastSeenTime > 2.5f;
        if (!calm) return;
        if (weapon.Data.Slot != WeaponSlot.Primary && Self.Primary != null && Self.Primary.Mag + Self.Primary.Reserve > 0)
            Self.Equip(WeaponSlot.Primary);
        else if (weapon.Mag < weapon.Data.MagSize / 2 && weapon.Reserve > 0)
            Self.StartReload();
    }

    void Footsteps()
    {
        if (!agent.isOnNavMesh || agent.velocity.sqrMagnitude < 9f) return;
        stepTimer -= Time.deltaTime;
        if (stepTimer > 0f) return;
        stepTimer = 0.36f;
        SoundFX.Play(SoundFX.Step, transform.position, 0.7f, Random.Range(0.85f, 1.1f), true, 28f);
    }

    // ----------------------------------------------------------------- events

    void OnDamaged(Combatant attacker, int amount)
    {
        if (attacker == null || attacker == Self || targetVisible) return;
        alertPosition = attacker.transform.position;
        alertUntil = Time.time + 5f;
        // Snap partly towards the shooter so being shot in the back is not a free kill.
        Vector3 flat = attacker.transform.position - transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flat), 90f);
    }

    void OnDied(Combatant killer)
    {
        diedAt = Time.time;
        Halt();
        agent.enabled = false;
        body.enabled = false;
        target = null;
        targetVisible = false;
    }

    void AnimateDeath()
    {
        float t = Mathf.Clamp01((Time.time - diedAt) / 0.45f);
        visual.localRotation = Quaternion.Euler(-88f * t * t, 0f, 0f);
        visual.localPosition = new Vector3(0f, 0.12f * t, 0f);
    }

    // ----------------------------------------------------------------- helpers

    void MoveTo(Vector3 destination)
    {
        if (!agent.isOnNavMesh) return;
        agent.isStopped = false;
        if ((!agent.hasPath && !agent.pathPending) || (agent.destination - destination).sqrMagnitude > 1f)
            agent.SetDestination(destination);
    }

    void Halt()
    {
        if (!agent.isOnNavMesh) return;
        agent.isStopped = true;
        agent.velocity = Vector3.zero;
    }

    void TurnTowards(Vector3 point, float degreesPerSecond, float yawOffset = 0f)
    {
        agent.updateRotation = false;
        Vector3 flat = point - transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.01f) return;
        Quaternion want = Quaternion.LookRotation(flat) * Quaternion.Euler(0f, yawOffset, 0f);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, want, degreesPerSecond * Time.deltaTime);
    }

    static float FlatDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

    /// <summary>Spreads bots around a waypoint so they do not all stand on the same spot.</summary>
    static Vector3 Jitter(Vector3 point)
    {
        Vector3 candidate = point + new Vector3(Random.Range(-1.2f, 1.2f), 0f, Random.Range(-1.2f, 1.2f));
        return NavMesh.SamplePosition(candidate, out var hit, 2.5f, NavMesh.AllAreas) ? hit.position : point;
    }

    void BuildBody()
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
    }

    void RefreshGun()
    {
        for (int i = gunMount.childCount - 1; i >= 0; i--) Destroy(gunMount.GetChild(i).gameObject);
        if (Self.Current == null) return;
        WeaponModels.Build(Self.Current.Data, gunMount, true, out var muzzle, WeaponSkins.Pick(Self.Current.Data, skinSeed));
        Self.Muzzle = muzzle;
        agent.speed = Self.Current.Data.MoveSpeed * 0.92f;
    }
}

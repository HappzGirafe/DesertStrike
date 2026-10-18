using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum BotDifficulty { Easy, Normal, Hard, Extreme }   // Extreme needs an account (see Accounts)

/// <summary>
/// CS-style bot. Each round it follows a plan from <see cref="PlanRound"/> (rush a site or hold an
/// angle, then hunt), reacts to what it sees, hears and gets shot by, and fights with the same
/// weapons and rules as the player.
/// </summary>
[RequireComponent(typeof(Combatant))]
public class BotController : MonoBehaviour, ICombatantController
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

    public Combatant Self { get; private set; }

    NavMeshAgent agent;
    CharacterBody body;
    Skill skill;

    // Round plan
    readonly List<Vector3> route = new List<Vector3>();
    int routeIndex;
    float waypointDeadline;
    Vector3 holdLook;
    float holdDuration;
    float holdUntil;
    float startMovingAt;
    bool hunting;
    string goingFor;   // a Terrorist's site this round ("A" or "B"): the bomb goes there
    bool plantThisRound = true;   // false: this round the Terrorists play for kills and do not plant
    bool crouchHolding;   // crouches while holding the angle at the end of its route

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

    // What real players do (BotKnowledge): the spot to have the aim on, and whether a target came out right there.
    Vector3? preAim;
    float nextPreAim;
    bool preAimedSighting;

    bool HoldingAngle => routeIndex >= route.Count && !hunting;
    static bool Learned => BotKnowledge.Instance != null && BotKnowledge.Instance.Active;

    static Skill SkillFor(BotDifficulty difficulty)
    {
        switch (difficulty)
        {
            case BotDifficulty.Easy:
                return new Skill { Reaction = 0.75f, AimError = 4.5f, HeadChance = 0.08f, TurnSpeed = 220f, SightRange = 70f };
            case BotDifficulty.Hard:
                return new Skill { Reaction = 0.22f, AimError = 1.3f, HeadChance = 0.35f, TurnSpeed = 540f, SightRange = 140f, Strafe = true };
            case BotDifficulty.Extreme:
                return new Skill { Reaction = 0.12f, AimError = 0.7f, HeadChance = 0.55f, TurnSpeed = 760f, SightRange = 170f, Strafe = true };
            default:
                return new Skill { Reaction = 0.42f, AimError = 2.4f, HeadChance = 0.18f, TurnSpeed = 360f, SightRange = 100f, Strafe = true };
        }
    }

    public void Setup(BotDifficulty difficulty)
    {
        Self = GetComponent<Combatant>();
        skill = SkillFor(difficulty);
        Self.SkinSeed = Random.Range(0, 1000);   // bots show off random (but consistent) skins

        body = gameObject.AddComponent<CharacterBody>();
        body.Setup();

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

        Self.WeaponChanged += () => agent.speed = Self.Current != null ? Self.Current.Data.MoveSpeed * 0.92f : 5f;
        Self.Damaged += OnDamaged;
        Self.Died += OnDied;
    }

    // ----------------------------------------------------------------- round setup

    public void Respawn(Vector3 position, float yaw)
    {
        body.Revive();
        crouchHolding = false;
        Self.Height = Combatant.StandHeight;
        agent.enabled = false;
        transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        agent.enabled = true;
        agent.Warp(position);

        target = null;
        targetVisible = false;
        lastSeenTime = -99f;
        alertUntil = -1f;
        preAim = null;
        preAimedSighting = false;
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
            else if (me.Money >= WeaponData.Mp5.Price + WeaponData.KevlarPrice)
            {
                float pick = Random.value;
                me.TryBuy(pick < 0.25f ? WeaponData.Shotgun : pick < 0.55f ? WeaponData.Uzi : WeaponData.Mp5);
            }
            else if (me.Money >= WeaponData.Uzi.Price + WeaponData.KevlarPrice && Random.value < 0.5f) me.TryBuy(WeaponData.Uzi);
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
        if (me.Money >= WeaponData.DefuseKitPrice + 500) me.TryBuyDefuseKit();
        me.Equip(me.BestSlot(), instant: true);
    }

    /// <summary>-ds-attack a|b: the Terrorists always go for that site (tests); null = decided every round.</summary>
    public static bool? ForcedSiteA;

    /// <summary>
    /// Once a round the Terrorist bots decide together: plant on A (45%), plant on B (45%), or not plant at all and
    /// play for kills (10%). With one site walled off (small matches): plant on the open one (95%) or not (5%).
    /// Each bot takes one of the map's routes towards the target (the bomb goes with its carrier); SWAT spread over
    /// the defensive posts, leaving out those at a closed site.
    /// </summary>
    public static void PlanRound(List<BotController> terrorists, List<BotController> swat, GameMap map, float startTime)
    {
        string target;
        if (ForcedSiteA.HasValue) target = ForcedSiteA.Value ? "A" : "B";
        else if (map.ClosedSite != null) target = Random.value < 0.95f ? (map.ClosedSite == "A" ? "B" : "A") : null;
        else
        {
            float roll = Random.value;
            target = roll < 0.45f ? "A" : roll < 0.9f ? "B" : null;
        }
        if (target == map.ClosedSite) target = null;

        var routesA = map.TerroristRoutes(true);
        var routesB = map.TerroristRoutes(false);
        for (int i = 0; i < terrorists.Count; i++)
        {
            // Without a plan to plant they still push, by the routes to the open site(s), and hunt from there.
            bool towardsA = target != null ? target == "A" : map.ClosedSite != null ? map.ClosedSite == "B" : i % 2 == 0;
            var routes = towardsA ? routesA : routesB;
            terrorists[i].Plan(routes[i % routes.Count], map.TerroristWatch(towardsA), Random.Range(6f, 14f), startTime + Random.Range(0f, 2f));
            terrorists[i].goingFor = target;
            terrorists[i].plantThisRound = target != null;
        }
        if (terrorists.Count > 0)
            Debug.Log(target != null ? $"[Bots] This round the Terrorists go for site {target}" : "[Bots] This round the Terrorists do not plant");

        var posts = map.SwatPosts();
        var knowledge = Learned ? BotKnowledge.Instance : null;
        if (map.ClosedSite != null)
        {
            // Posts that end at the closed site are no use; keep the rest (all of them if none is left).
            Bounds closed = map.SiteZone(map.ClosedSite);
            closed.Expand(new Vector3(16f, 0f, 16f));
            var open = posts.FindAll(p => p.Route.Count == 0 || !closed.Contains(new Vector3(p.Route[p.Route.Count - 1].x, closed.center.y, p.Route[p.Route.Count - 1].z)));
            if (open.Count > 0) posts = open;
        }
        for (int i = 0; i < swat.Count; i++)
        {
            // Learned: some take a spot SWAT players like to hold, instead of the map's own post.
            if (knowledge != null && Random.value < 0.35f
                && knowledge.FavouredSpot(Team.Swat, swat[i].transform.position, out var spot) && !map.InClosedSite(spot.Position))
            {
                swat[i].Plan(new List<Vector3> { spot.Position }, spot.Position + spot.Direction * 12f, Random.Range(35f, 60f),
                             startTime + Random.Range(0f, 0.5f));
                continue;
            }
            var post = posts[i % posts.Count];
            swat[i].Plan(post.Route, post.LookAt, Random.Range(35f, 60f), startTime + Random.Range(0f, 0.5f));
        }
    }

    // ----------------------------------------------------------------- per-frame brain

    void Update()
    {
        if (!Self.IsAlive || Time.timeScale == 0f) return;
        var gm = GameManager.Instance;
        if (gm.State != MatchState.Live && gm.State != MatchState.RoundEnd)
        {
            Halt();
            return;
        }

        // Crouched while holding an angle; up again to move on. Eyes (and the gun) come down with the body.
        float height = HoldingAngle && crouchHolding ? Combatant.CrouchHeight : Combatant.StandHeight;
        if (Self.Height != height)
        {
            Self.Height = Mathf.MoveTowards(Self.Height, height, 3f * Time.deltaTime);
            Self.Eye.localPosition = new Vector3(0f, Self.Height - 0.18f, 0f);
        }

        if (Time.time >= nextScan)
        {
            nextScan = Time.time + 0.12f;
            Scan();
        }

        if (target != null && target.IsAlive && targetVisible) Fight();
        else if (target != null && target.IsAlive && Time.time - lastSeenTime < 5f) Chase();
        else if (Time.time < alertUntil && !BombIsUrgent()) Investigate();
        else
        {
            target = null;
            if (!PlayBomb()) FollowRoute();
        }

        ManageAmmo();
    }

    // ----------------------------------------------------------------- the bomb

    /// <summary>Planted bomb: SWAT drop everything to defuse it, and Terrorists stay to guard it.</summary>
    bool BombIsUrgent() => GameManager.Instance.Bomb.State == BombState.Planted;

    /// <summary>Bomb jobs that override the round plan. Returns false when there is nothing to do.</summary>
    bool PlayBomb()
    {
        var gm = GameManager.Instance;
        var bomb = gm.Bomb;
        if (gm.State != MatchState.Live) return false;

        if (Self.Team == Team.Swat)
        {
            if (bomb.State != BombState.Planted) return false;
            agent.updateRotation = true;
            if (FlatDistance(transform.position, bomb.Position) > BombManager.DefuseRadius * 0.6f)
            {
                MoveTo(bomb.Position);
                return true;
            }
            Halt();
            bomb.HoldUse(Self);
            return true;
        }

        switch (bomb.State)
        {
            case BombState.Carried when bomb.Carrier == Self && plantThisRound:
                // Plant as soon as we stand on the team's site (walking over the other one on the way does not
                // count); the round plan walks us there, and after it we head for that site.
                string site = gm.Map.SiteAt(transform.position);
                if (site == null || (goingFor != null && site != goingFor))
                {
                    if (routeIndex < route.Count || Time.time < startMovingAt) return false;
                    MoveTo(gm.Map.SitePoint(goingFor != "B"));
                    return true;
                }
                Halt();
                bomb.HoldUse(Self);
                return true;

            case BombState.Dropped when IsClosestTerroristTo(bomb.Position):
                agent.updateRotation = true;
                MoveTo(bomb.Position);
                return true;

            case BombState.Planted:
                // Guard: stay within a few metres of the bomb and watch around it.
                if (FlatDistance(transform.position, bomb.Position) > 9f)
                {
                    agent.updateRotation = true;
                    MoveTo(bomb.Position);
                    return true;
                }
                Halt();
                TurnTowards(transform.position + Quaternion.Euler(0f, Time.time * 25f + GetInstanceID(), 0f) * Vector3.forward, 120f);
                return true;
        }
        return false;
    }

    bool IsClosestTerroristTo(Vector3 position)
    {
        float mine = FlatDistance(transform.position, position);
        foreach (var other in GameManager.Instance.Combatants)
            if (other != Self && other.IsAlive && other.Team == Team.Terrorists && !other.IsHuman
                && FlatDistance(other.transform.position, position) < mine)
                return false;
        return true;
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
            // Out right where it was already aiming (a spot players shoot from): it reacts sooner.
            Vector3 toTarget = best.transform.position - transform.position;
            toTarget.y = 0f;
            preAimedSighting = preAim.HasValue && Vector3.Angle(transform.forward, toTarget) < 15f;
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

        // Holding an angle (or pre-aiming the spot it came out of) means already being aimed at it: the first shot
        // comes sooner.
        float reaction = HoldingAngle || preAimedSighting ? skill.Reaction * 0.6f : skill.Reaction;
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
        // A rested gun's first shot has no spread; the bot's own aim error stays.
        float spread = current.FirstShotReady ? 0f : current.Spread;
        if (!Self.TryFire(direction, spread + error)) return;

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
            MoveTo(route[routeIndex]);
            // Learned: on the way, the gun is on the spot enemy players shoot from, as a player clears an angle.
            Vector3 heading = agent.velocity.sqrMagnitude > 0.5f ? agent.velocity : route[routeIndex] - transform.position;
            UpdatePreAim(heading, 100f);
            if (preAim.HasValue) TurnTowards(preAim.Value, skill.TurnSpeed * 0.5f);
            else agent.updateRotation = true;
            if (FlatDistance(transform.position, route[routeIndex]) < 1.6f || Time.time > waypointDeadline)
                AdvanceWaypoint();
            return;
        }

        // Holding: watch the chosen angle with a slow sweep (learned: the spot enemy players come from), then go hunting.
        Halt();
        float sweep = Mathf.Sin(Time.time * 0.7f + GetInstanceID() * 0.37f) * 25f;
        UpdatePreAim(holdLook - transform.position, 70f);
        if (preAim.HasValue) TurnTowards(preAim.Value, 160f, sweep * 0.3f);
        else TurnTowards(holdLook, 160f, sweep);
        if (Time.time >= holdUntil) StartHunting();
    }

    void UpdatePreAim(Vector3 facing, float maxAngle)
    {
        if (Time.time < nextPreAim) return;
        nextPreAim = Time.time + 0.5f;
        preAim = Learned ? BotKnowledge.Instance.PreAim(Self.Team, Self.EyePosition, facing, maxAngle) : null;
    }

    void AdvanceWaypoint()
    {
        routeIndex++;
        waypointDeadline = Time.time + 30f;
        if (routeIndex >= route.Count)
        {
            holdUntil = Time.time + holdDuration;
            crouchHolding = Random.value < 0.4f;   // some hold their angle crouched, as players do
        }
    }

    void StartHunting()
    {
        // Learned: hold a spot players of this team like, or check a place where enemy players often are.
        if (Learned && Random.value < 0.6f)
        {
            var knowledge = BotKnowledge.Instance;
            bool hold = Random.value < 0.5f;
            if (hold ? knowledge.FavouredSpot(Self.Team, transform.position, out var spot) : knowledge.EnemyHaunt(Self.Team, transform.position, out spot))
            {
                route.Clear();
                route.Add(spot.Position);
                routeIndex = 0;
                waypointDeadline = Time.time + 30f;
                holdLook = hold ? spot.Position + spot.Direction * 12f : spot.Position + (spot.Position - transform.position).normalized * 8f;
                holdDuration = hold ? Random.Range(5f, 10f) : Random.Range(2f, 5f);
                hunting = true;
                return;
            }
        }
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
        Halt();
        agent.enabled = false;
        target = null;
        targetVisible = false;
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
}

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

    // A real player's style (PlayerTactics): their ways, sites and guns. A way's places have the time the player left
    // each (seconds after the round went live): the bot does not leave one sooner, so it keeps their pace.
    public TacticStyle Style { get; private set; }
    public bool StyleChosen;          // PlayerTactics gave it one (or decided on none)
    public string StandInAccount;     // a bot playing for a person who left: their account (online), else null
    public int StandInKills;          // their kills when the bot took over (theirs, not the bot's)
    readonly List<float> routeTimes = new List<float>();
    float liveAt, waitUntil;
    bool? holdCrouched;               // the player's way: crouched or not at its end

    /// <summary>The round's site of the Terrorist bots ("A", "B", or null: no plant), for a bot that joins mid-round.</summary>
    public static string RoundTarget;

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

        // A bot taking over a person's player keeps its body.
        body = GetComponent<CharacterBody>();
        if (body == null)
        {
            body = gameObject.AddComponent<CharacterBody>();
            body.Setup();
        }

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

        Self.WeaponChanged += OnWeaponChanged;
        Self.Damaged += OnDamaged;
        Self.Died += OnDied;
    }

    void OnWeaponChanged()
    {
        if (agent != null) agent.speed = Self.Current != null ? Self.Current.Data.MoveSpeed * 0.92f : 5f;
    }

    /// <summary>Plays the way a real player does (PlayerTactics); their headshots count for its aim too.</summary>
    public void UseStyle(TacticStyle style)
    {
        Style = style;
        if (style == null) return;
        int kills = style.t.killCount + style.swat.killCount;
        if (kills >= 5)
        {
            float rate = (style.t.headshots + style.swat.headshots) / (float)kills;
            skill.HeadChance = Mathf.Clamp(Mathf.Lerp(skill.HeadChance, rate, 0.5f), 0.05f, 0.7f);
        }
    }

    /// <summary>Took over a person's player (GameManager.StandIn): onto the navmesh where it stands, if alive.</summary>
    public void TakeOver()
    {
        agent.enabled = false;
        if (!Self.IsAlive) return;   // dead: it comes back with the next round (Respawn)
        Vector3 p = transform.position;
        if (NavMesh.SamplePosition(p, out var hit, 8f, NavMesh.AllAreas)) p = hit.position;
        transform.position = p;
        agent.enabled = true;
        agent.Warp(p);
    }

    /// <summary>Gives the combatant back (its person came back): no agent or eye of its own any more.</summary>
    public void Release()
    {
        if (Self.Eye != null) Destroy(Self.Eye.gameObject);
        Self.Eye = null;
        if (agent != null) DestroyImmediate(agent);
        agent = null;
    }

    void OnDestroy()
    {
        if (Self == null) return;
        Self.WeaponChanged -= OnWeaponChanged;
        Self.Damaged -= OnDamaged;
        Self.Died -= OnDied;
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
        routeTimes.Clear();
        waitUntil = 0f;
        routeIndex = 0;
        hunting = false;
        Halt();
    }

    public void Plan(List<Vector3> waypoints, Vector3 lookAt, float holdSeconds, float startTime)
    {
        route.Clear();
        routeTimes.Clear();
        holdCrouched = null;
        waitUntil = 0f;
        foreach (var point in waypoints) route.Add(Jitter(point));
        routeIndex = 0;
        waypointDeadline = startTime + 30f;
        holdLook = lookAt;
        holdDuration = holdSeconds;
        startMovingAt = startTime;
        hunting = false;
    }

    /// <summary>
    /// A real player's way (from <paramref name="from"/> on): its places, left no sooner than the player left them
    /// (seconds after <paramref name="roundLive"/>), then holding where and how they held.
    /// </summary>
    void PlanPath(TacticPath path, int from, float roundLive)
    {
        route.Clear();
        routeTimes.Clear();
        for (int i = from; i < path.Count; i++)
        {
            route.Add(Jitter(path.Point(i)));
            routeTimes.Add(path.Time(i));
        }
        routeIndex = 0;
        liveAt = roundLive;
        waitUntil = 0f;
        startMovingAt = Mathf.Max(Time.time, roundLive);
        waypointDeadline = startMovingAt + 30f;
        Vector3 end = path.Point(path.Count - 1);
        holdLook = end + new Vector3(path.lookX, 0f, path.lookZ) * 12f;
        holdDuration = Mathf.Clamp(path.hold, 4f, 40f);
        holdCrouched = path.crouch;
        hunting = false;
    }

    /// <summary>One of the style's ways for a side (to <paramref name="site"/> when it has one), or null.</summary>
    TacticPath PickPath(GameMap map, string site)
    {
        if (Style == null) return null;
        var all = Style.Of(Self.Team).paths.FindAll(p => p.Count >= 2 && !CrossesClosedSite(p, map));
        var fitting = Self.Team == Team.Terrorists ? all.FindAll(p => p.site == (site ?? "")) : all;
        if (fitting.Count == 0 && Self.Team == Team.Terrorists && site != null)
            fitting = all.FindAll(p => p.site.Length == 0);   // ways that did not get to a site: ok, the bomb finds its site after
        if (fitting.Count == 0) return null;
        // The latest rounds a little more often.
        int index = Mathf.Min(Mathf.FloorToInt(Random.value * Random.value * fitting.Count), fitting.Count - 1);
        return fitting[index];
    }

    static bool CrossesClosedSite(TacticPath path, GameMap map)
    {
        if (map.ClosedSite == null) return false;
        for (int i = 0; i < path.Count; i++)
            if (map.InClosedSite(path.Point(i))) return true;
        return false;
    }

    /// <summary>
    /// A bot that just took over a person's player (GameManager.StandIn), any time in a round: it goes on from where
    /// it stands, the way the person plays (the rest of one of their ways), or else to the round's site (Terrorists)
    /// or the nearest post (SWAT).
    /// </summary>
    public void PlanTakeover(GameMap map, float roundLive)
    {
        target = null;
        targetVisible = false;
        if (Self.Team == Team.Terrorists)
        {
            goingFor = RoundTarget;
            if (goingFor == null && GameManager.Instance.Bomb.Carrier == Self && Style != null)
                goingFor = Style.t.siteB > Style.t.siteA ? "B" : "A";
            if (goingFor == map.ClosedSite) goingFor = null;
            plantThisRound = goingFor != null || GameManager.Instance.Bomb.Carrier == Self;
        }
        var path = PickPath(map, goingFor);
        if (path != null)
        {
            // Go on from the place nearest to here.
            int nearest = 0;
            float best = float.MaxValue;
            for (int i = 0; i < path.Count; i++)
            {
                float d = FlatDistance(transform.position, path.Point(i));
                if (d < best)
                {
                    best = d;
                    nearest = i;
                }
            }
            PlanPath(path, nearest, roundLive);
            return;
        }
        if (Self.Team == Team.Terrorists)
        {
            bool towardsA = goingFor != null ? goingFor == "A" : map.ClosedSite == "B" || (map.ClosedSite == null && Random.value < 0.5f);
            Plan(new List<Vector3> { map.SitePoint(towardsA) }, map.TerroristWatch(towardsA), Random.Range(6f, 14f), Mathf.Max(Time.time, roundLive));
            return;
        }
        var posts = map.SwatPosts().FindAll(p => p.Route.Count > 0 && !map.InClosedSite(p.Route[p.Route.Count - 1]));
        if (posts.Count == 0)
        {
            StartHunting();
            return;
        }
        var post = posts[0];
        foreach (var p in posts)
            if (FlatDistance(transform.position, p.Route[p.Route.Count - 1]) < FlatDistance(transform.position, post.Route[post.Route.Count - 1])) post = p;
        Plan(new List<Vector3> { post.Route[post.Route.Count - 1] }, post.LookAt, Random.Range(35f, 60f), Mathf.Max(Time.time, roundLive));
    }

    /// <summary>Spends money like a simple CS bot: rifle if affordable, otherwise SMG or pistol, then armor.</summary>
    public void BuyGear()
    {
        var me = Self;
        var copied = Style?.Of(me.Team);
        if (copied != null && copied.rounds < 3) copied = null;   // too few rounds to tell how they buy
        if (copied != null) BuyFavourite(copied);
        if (me.Primary == null)
        {
            var rifle = me.Team == Team.Terrorists ? WeaponData.Ak47 : WeaponData.M4a1;
            if (me.Money >= WeaponData.Rpg.Price + WeaponData.KevlarHelmetPrice && Random.value < 0.15f) me.TryBuy(WeaponData.Rpg);
            else if (me.Money >= WeaponData.Awp.Price + WeaponData.KevlarHelmetPrice && Random.value < 0.2f) me.TryBuy(WeaponData.Awp);
            else if (me.Money >= WeaponData.Svd.Price + WeaponData.KevlarPrice && Random.value < 0.15f) me.TryBuy(WeaponData.Svd);
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

        // Copying a player: armor, helmet and kit as often as they start a round with them.
        bool armor = copied == null || Random.value < copied.armor / (float)copied.rounds + 0.1f;
        bool helmet = copied == null || Random.value < copied.helmet / (float)Mathf.Max(1, copied.armor) + 0.1f;
        bool kit = copied == null || Random.value < copied.kit / (float)copied.rounds + 0.1f;
        if (armor && helmet && me.Money >= WeaponData.KevlarHelmetPrice) me.TryBuyArmor(true);
        else if (armor && me.Money >= WeaponData.KevlarPrice) me.TryBuyArmor(false);
        if (kit && me.Money >= WeaponData.DefuseKitPrice + 500) me.TryBuyDefuseKit();
        me.Equip(me.BestSlot(), instant: true);
    }

    // The guns the copied player starts rounds with, picked by how often (only those it can afford, with armor left).
    void BuyFavourite(TeamTactics copied)
    {
        var me = Self;
        bool starterPistol = me.Secondary == null || me.Secondary.Data == WeaponData.Glock || me.Secondary.Data == WeaponData.Usp;
        var choices = new List<(WeaponData Weapon, int Count)>();
        int total = 0;
        for (int i = 0; i + 1 < copied.buys.Count; i += 2)
        {
            var weapon = WeaponData.FromIndex(copied.buys[i]);
            if (weapon == null || weapon.IsMelee || !weapon.AvailableTo(me.Team) || copied.buys[i + 1] <= 0) continue;
            if (weapon.Slot == WeaponSlot.Primary ? me.Primary != null : !starterPistol) continue;
            if (me.Money < weapon.Price + WeaponData.KevlarPrice) continue;
            choices.Add((weapon, copied.buys[i + 1]));
            total += copied.buys[i + 1];
        }
        if (total == 0) return;
        int pick = Random.Range(0, total);
        foreach (var (weapon, count) in choices)
        {
            if ((pick -= count) >= 0) continue;
            me.TryBuy(weapon);
            return;
        }
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
        // Bots copying players: mostly the site those players go for.
        if (!ForcedSiteA.HasValue && map.ClosedSite == null && Random.value < 0.75f)
        {
            int a = 0, b = 0;
            foreach (var bot in terrorists)
                if (bot.Style != null)
                {
                    a += bot.Style.t.siteA;
                    b += bot.Style.t.siteB;
                }
            if (a + b >= 2) target = Random.value < a / (float)(a + b) ? "A" : "B";
        }
        if (target == map.ClosedSite) target = null;
        RoundTarget = target;

        var routesA = map.TerroristRoutes(true);
        var routesB = map.TerroristRoutes(false);
        for (int i = 0; i < terrorists.Count; i++)
        {
            terrorists[i].goingFor = target;
            terrorists[i].plantThisRound = target != null;
            // Copying a player: one of their ways to that site.
            var path = terrorists[i].PickPath(map, target);
            if (path != null)
            {
                terrorists[i].PlanPath(path, 0, startTime);
                continue;
            }
            // Without a plan to plant they still push, by the routes to the open site(s), and hunt from there.
            bool towardsA = target != null ? target == "A" : map.ClosedSite != null ? map.ClosedSite == "B" : i % 2 == 0;
            var routes = towardsA ? routesA : routesB;
            // Small matches: no waypoint on the closed side (its walls are in the way).
            var route = routes[i % routes.Count].FindAll(p => !map.InClosedSite(p));
            terrorists[i].Plan(route, map.TerroristWatch(towardsA), Random.Range(6f, 14f), startTime + Random.Range(0f, 2f));
        }
        if (terrorists.Count > 0)
            Debug.Log(target != null ? $"[Bots] This round the Terrorists go for site {target}" : "[Bots] This round the Terrorists do not plant");

        var posts = map.SwatPosts();
        var knowledge = Learned ? BotKnowledge.Instance : null;
        if (map.ClosedSite != null)
        {
            // Posts on the closed side are no use; keep the rest (all of them if none is left).
            var open = posts.FindAll(p => p.Route.Count == 0 || !map.InClosedSite(p.Route[p.Route.Count - 1]));
            if (open.Count > 0) posts = open;
        }
        for (int i = 0; i < swat.Count; i++)
        {
            // Copying a player: their way to where they hold.
            var path = swat[i].PickPath(map, null);
            if (path != null)
            {
                swat[i].PlanPath(path, 0, startTime);
                continue;
            }
            // Learned: some take a spot SWAT players like to hold, instead of the map's own post.
            if (knowledge != null && Random.value < 0.35f
                && knowledge.FavouredSpot(Team.Swat, swat[i].transform.position, out var spot) && !map.InClosedSite(spot.Position))
            {
                swat[i].Plan(new List<Vector3> { spot.Position }, spot.Position + spot.Direction * 12f, Random.Range(35f, 60f),
                             startTime + Random.Range(0f, 0.5f));
                continue;
            }
            var post = posts[i % posts.Count];
            swat[i].Plan(post.Route.FindAll(p => !map.InClosedSite(p)), post.LookAt, Random.Range(35f, 60f), startTime + Random.Range(0f, 0.5f));
        }
        int copying = terrorists.FindAll(b => b.routeTimes.Count > 0).Count + swat.FindAll(b => b.routeTimes.Count > 0).Count;
        if (copying > 0) Debug.Log($"[Bots] This round {copying} bot(s) take a real player's way");
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
            if (!PlayBomb() && !FetchWeapon()) FollowRoute();
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

    // No main gun: go for one lying near (a fallen player's), when in sight and not far.
    float nextWeaponLook;
    Vector3? weaponToFetch;

    bool FetchWeapon()
    {
        var gm = GameManager.Instance;
        if (Self.Primary != null || gm.State != MatchState.Live)
        {
            weaponToFetch = null;
            return false;
        }
        if (Time.time >= nextWeaponLook)
        {
            nextWeaponLook = Time.time + 1f;
            weaponToFetch = null;
            float best = 14f;
            foreach (var d in gm.Drops.Items)
            {
                if (d.Data.Slot != WeaponSlot.Primary) continue;
                float distance = FlatDistance(transform.position, d.Position);
                if (distance < best && Ballistics.HasLineOfSight(Self.EyePosition, d.Position + Vector3.up * 0.3f))
                {
                    best = distance;
                    weaponToFetch = d.Position;
                }
            }
        }
        if (weaponToFetch == null) return false;
        agent.updateRotation = true;
        MoveTo(weaponToFetch.Value);
        return true;
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
        if (routeIndex < route.Count && Time.time < waitUntil)
        {
            // A player's way: they waited here, so does the bot (watching where it goes next).
            Halt();
            UpdatePreAim(route[routeIndex] - transform.position, 100f);
            TurnTowards(preAim ?? route[routeIndex], skill.TurnSpeed * 0.5f);
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
        // A player's way: not off again before the time they left this place.
        bool timed = routeIndex < routeTimes.Count;
        if (timed) waitUntil = liveAt + routeTimes[routeIndex];
        routeIndex++;
        waypointDeadline = Mathf.Max(Time.time, waitUntil) + 30f;
        if (routeIndex >= route.Count)
        {
            holdUntil = timed ? Mathf.Max(waitUntil, Time.time + 4f) : Time.time + holdDuration;
            crouchHolding = holdCrouched ?? Random.value < 0.4f;   // some hold their angle crouched, as players do
        }
    }

    void StartHunting()
    {
        routeTimes.Clear();
        holdCrouched = null;
        waitUntil = 0f;
        // Copying a player: hold one of the spots they get their kills from, looking their way.
        var copied = Style?.Of(Self.Team);
        if (copied != null && copied.kills.Count >= 5 && Random.value < 0.5f)
        {
            var spots = new List<BotKnowledge.Spot>();
            for (int i = 0; i + 4 < copied.kills.Count; i += 5)
                spots.Add(new BotKnowledge.Spot
                {
                    Position = new Vector3(copied.kills[i], copied.kills[i + 1], copied.kills[i + 2]), Weight = 1f - i / (float)copied.kills.Count * 0.5f,
                    Direction = new Vector3(copied.kills[i + 3], 0f, copied.kills[i + 4]).normalized,
                });
            var gameMap = GameManager.Instance.Map;
            spots.RemoveAll(s => gameMap.InClosedSite(s.Position));
            if (BotKnowledge.PickReachable(spots, transform.position, out var own))
            {
                route.Clear();
                route.Add(own.Position);
                routeIndex = 0;
                waypointDeadline = Time.time + 30f;
                holdLook = own.Position + own.Direction * 12f;
                holdDuration = Random.Range(6f, 12f);
                hunting = true;
                return;
            }
        }
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
        var map = GameManager.Instance.Map;
        var points = map.KeyPoints.FindAll(p => !map.InClosedSite(p));
        if (points.Count == 0) points = map.KeyPoints;
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

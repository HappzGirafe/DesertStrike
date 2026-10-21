using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Host side of a player on another PC: puts their combatant where their game says it is, and carries
/// out their shots, reloads, purchases and bomb actions with the same rules as everyone else.
///
/// The other game could be changed to cheat, so what it sends is checked here: shots no faster than the weapon fires
/// and never straighter than it shoots (the spread it sends may only make them worse), and in online games every
/// move too: no faster than running, no flying (but an admin's Cheats), nothing through walls. A move that fails is not taken; after several
/// the player is put back where the host has them. Purchases, reloads and the bomb already follow the host's rules.
/// </summary>
[RequireComponent(typeof(Combatant))]
public class RemotePlayerController : MonoBehaviour, ICombatantController
{
    public Combatant Self { get; private set; }
    public NetSession.Peer Peer { get; private set; }

    CharacterBody body;
    bool useHeld;
    Vector3 lastPosition;
    float stepTimer;

    // Online move checks: a budget of metres that refills at a little over the fastest running speed (the knife's
    // 6.2 m/s) and can save up a few metres, for packets that arrive unevenly. Up and down have their own limits.
    const float MaxSpeed = 6.2f * 1.3f, MoveBurst = 2.5f, MaxRise = 8f, RiseBurst = 2f, MaxFallSpeed = 40f;
    float moveBudget = MoveBurst, riseBudget = RiseBurst, budgetAt;
    int refusedMoves;
    float measuredSpeed;   // how fast they move, as the host sees it (for the spread of their shots)
    Vector3 speedFrom;
    float speedAt;

    public void Setup(NetSession.Peer peer)
    {
        Peer = peer;
        Self = GetComponent<Combatant>();
        body = gameObject.AddComponent<CharacterBody>();
        body.Setup();

        // Lets bots path around them, like around the host's own player.
        var obstacle = gameObject.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Capsule;
        obstacle.radius = 0.35f;
        obstacle.height = Combatant.StandHeight;
        obstacle.center = new Vector3(0f, Combatant.StandHeight / 2f, 0f);
    }

    public void Respawn(Vector3 position, float yaw)
    {
        transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        lastPosition = speedFrom = position;
        useHeld = false;
        moveBudget = MoveBurst;
        riseBudget = RiseBurst;
        budgetAt = speedAt = Time.time;
        measuredSpeed = 0f;
        refusedMoves = 0;
        body.Revive();
    }

    /// <summary>Joined in the middle of a round: wait, dead, until the next one.</summary>
    public void SitOutRound()
    {
        Self.Health = 0;
        body.ForceDead();
    }

    public void ApplyInput(int spawnCount, Vector3 position, float yaw, float height, bool use, bool scoped)
    {
        // Ignore moves sent before their game saw the latest respawn.
        if (!Self.IsAlive || spawnCount != (Self.SpawnCount & 0xFF)) return;
        if (!Finite(position) || float.IsNaN(yaw) || float.IsInfinity(yaw) || float.IsNaN(height)) return;
        if (Peer.AccountId != null && !MoveAllowed(position))
        {
            // Ten refused moves in a row: back to where the host has them. Their game sees the new spawn count in
            // the next snapshot and takes that place (moves sent before it are ignored, as after a respawn).
            if (++refusedMoves >= 10)
            {
                refusedMoves = 0;
                Self.SpawnCount++;
                budgetAt = Time.time;
                Debug.Log($"[Net] {Peer.Name}: moves refused (too fast or through a wall), put back at {transform.position}");
            }
            return;
        }
        refusedMoves = 0;
        float since = Time.time - speedAt;
        if (since >= 0.1f)
        {
            Vector3 moved = position - speedFrom;
            moved.y = 0f;
            measuredSpeed = moved.magnitude / since;
            speedFrom = position;
            speedAt = Time.time;
        }
        transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        Self.Height = Mathf.Clamp(height, 1f, Combatant.StandHeight);
        Self.Scoped = scoped;
        useHeld = use;
    }

    bool MoveAllowed(Vector3 target)
    {
        float now = Time.time, passed = Mathf.Min(now - budgetAt, 1f);
        budgetAt = now;
        moveBudget = Mathf.Min(MoveBurst, moveBudget + passed * MaxSpeed);
        riseBudget = Mathf.Min(RiseBurst, riseBudget + passed * MaxRise);
        Vector3 from = transform.position, flat = target - from;
        flat.y = 0f;
        float distance = flat.magnitude, rise = target.y - from.y;
        bool flying = (Self.CheatFlags & Cheats.FlyFlag) != 0;   // an admin flying (Cheats): only walls stop them
        if (!flying && (distance > moveBudget || rise > riseBudget || -rise > MaxFallSpeed * Mathf.Max(passed, 0.1f) + 1f)) return false;
        // Through a wall: anything of the map between the two places, at half the player's height (under low
        // ceilings when crouching, over small steps).
        Vector3 lift = Vector3.up * (Self.Height * 0.5f);
        if (distance > 0.01f && Physics.Linecast(from + lift, target + lift, Ballistics.EnvironmentMask, QueryTriggerInteraction.Ignore))
            return false;
        moveBudget -= distance;
        if (rise > 0f) riseBudget -= rise;
        return true;
    }

    static bool Finite(Vector3 v) =>
        !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) && Mathf.Abs(v.x) < 5000f && Mathf.Abs(v.y) < 5000f && Mathf.Abs(v.z) < 5000f;

    public void HandleFire(Vector3 direction, float spread)
    {
        var state = GameManager.Instance.State;
        if (state != MatchState.Live && state != MatchState.RoundEnd) return;
        if (!Finite(direction) || direction.sqrMagnitude < 0.01f || float.IsNaN(spread)) return;
        // Shots may arrive up to 30% of the weapon's interval early (the network is uneven), but on average no faster
        // than it fires. The spread sent can only make a shot worse than what the host works out.
        Self.TryFire(direction.normalized, Mathf.Clamp(Mathf.Max(spread, LeastSpread()), 0f, 20f), fireRateSlack: 0.3f);
    }

    // The smallest spread the weapon can have right now, from what the host knows: the first shot after a rest is dead
    // on (as on every PC); otherwise the weapon's own spread, less crouching, more moving (counted a little leniently,
    // as the host sees the movement slightly late) and more for a sniper rifle without its scope.
    float LeastSpread()
    {
        var weapon = Self.Current;
        if (weapon == null || weapon.Data.IsMelee || weapon.FirstShotReady) return 0f;
        float spread = weapon.Spread + weapon.Data.MoveSpread * Mathf.InverseLerp(2.5f, 6f, measuredSpeed) * 0.8f;
        if (Self.Height < Combatant.StandHeight - 0.2f) spread *= 0.7f;
        if (weapon.Data.ZoomFov > 0f && !Self.Scoped) spread += 1.5f;
        return spread;
    }

    public void HandleAction(NetAction action, int argument)
    {
        var gm = GameManager.Instance;
        switch (action)
        {
            case NetAction.Reload: Self.StartReload(); break;
            case NetAction.Equip: Self.Equip((WeaponSlot)Mathf.Clamp(argument, 0, 2)); break;
            case NetAction.AutoMode: if (Self.Current != null) Self.Current.AutoMode = argument != 0; break;
            case NetAction.BuyWeapon: gm.TryBuyWeapon(Self, WeaponData.FromIndex(argument)); break;
            case NetAction.BuyArmor: gm.TryBuyArmor(Self, argument != 0); break;
            case NetAction.BuyDefuseKit: gm.TryBuyDefuseKit(Self); break;
            case NetAction.SellWeapon: gm.TrySellWeapon(Self, (WeaponSlot)Mathf.Clamp(argument, 0, 2)); break;
            case NetAction.DropBomb: gm.Bomb.Drop(Self); break;
            case NetAction.DropWeapon: gm.Drops.Drop(Self, (WeaponSlot)Mathf.Clamp(argument, 0, 2)); break;
            case NetAction.PickUp: gm.Drops.PickUp(Self, argument); break;
            case NetAction.Cheats:
                // Only an admin's ticket allows cheats (Cheats); anyone else's are ignored.
                int flags = Peer.IsAdmin ? argument & Cheats.AllFlags : 0;
                if (flags != Self.CheatFlags)
                    Debug.Log(Peer.IsAdmin ? $"[Net] {Peer.Name} (admin): cheats {flags}" : $"[Net] {Peer.Name} asked for cheats and is no admin: refused");
                Self.CheatFlags = flags;
                break;
        }
    }

    void Update()
    {
        if (!Self.IsAlive) return;
        var gm = GameManager.Instance;
        if (useHeld) gm.Bomb.HoldUse(Self);

        // Running footsteps alert the bots, as they do for the host's own player.
        Vector3 moved = transform.position - lastPosition;
        moved.y = 0f;
        lastPosition = transform.position;
        if (Time.deltaTime > 0f && moved.magnitude < 2f && moved.magnitude / Time.deltaTime > 3.5f)
        {
            stepTimer -= Time.deltaTime;
            if (stepTimer <= 0f)
            {
                stepTimer = 0.36f;
                gm.ReportNoise(transform.position, Self.Team, 15f);
            }
        }
    }
}

using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Host side of a player on another PC: puts their combatant where their game says it is, and carries
/// out their shots, reloads, purchases and bomb actions with the same rules as everyone else.
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
        lastPosition = position;
        useHeld = false;
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
        transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        Self.Height = Mathf.Clamp(height, 1f, Combatant.StandHeight);
        Self.Scoped = scoped;
        useHeld = use;
    }

    public void HandleFire(Vector3 direction, float spread)
    {
        var state = GameManager.Instance.State;
        if (state != MatchState.Live && state != MatchState.RoundEnd) return;
        Self.TryFire(direction.normalized, Mathf.Clamp(spread, 0f, 20f), ignoreFireRate: true);
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
            case NetAction.DropBomb: gm.Bomb.Drop(Self); break;
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

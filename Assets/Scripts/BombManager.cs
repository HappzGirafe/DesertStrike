using UnityEngine;

public enum BombState { None, Carried, Dropped, Planted, Defused, Exploded }

/// <summary>
/// The C4. One Terrorist carries it each round and plants it on site A or B (hold E); it explodes after
/// <see cref="FuseTime"/> seconds unless SWAT defuse it (hold E next to it; faster with a defuse kit).
/// If the carrier dies the bomb drops and any Terrorist can pick it up by walking over it.
/// On a network client this only shows the bomb; the host decides what happens to it.
/// </summary>
public class BombManager : MonoBehaviour
{
    public const float PlantTime = 3f;
    public const float DefuseTime = 10f;
    public const float KitDefuseTime = 5f;
    public const float FuseTime = 40f;
    public const float DefuseRadius = 1.8f;
    const float PickupRadius = 1.4f;
    const float BlastRadius = 22f;
    const int PlantReward = 300;
    const int DefuseReward = 300;

    public BombState State { get; private set; }
    public Combatant Carrier { get; private set; }
    public Vector3 Position { get; private set; }     // where it lies when dropped or planted
    public string Site { get; private set; }
    public Combatant User { get; private set; }       // planting or defusing right now
    public float UseProgress { get; private set; }    // 0..1
    public float TimeLeft => State == BombState.Planted ? Mathf.Max(0f, explodeAt - Time.time) : 0f;
    public bool WasPlanted => State == BombState.Planted || State == BombState.Defused || State == BombState.Exploded;

    GameManager gm;
    Transform model;
    Transform led;
    float explodeAt;
    float nextBeep;
    Combatant usedThisFrame;
    Combatant droppedBy;
    float noPickupUntil;

    void Awake()
    {
        gm = GetComponent<GameManager>();
        model = Effects.BombModel(null);
        led = model.Find("Led");
        model.gameObject.SetActive(false);
    }

    /// <summary>Give the bomb to a random living Terrorist for the new round.</summary>
    public void ResetForRound()
    {
        State = BombState.None;
        Carrier = null;
        User = null;
        UseProgress = 0f;
        Site = null;
        var terrorists = gm.Combatants.FindAll(c => c.Team == Team.Terrorists && c.IsAlive);
        if (terrorists.Count == 0) return;
        Carrier = terrorists[Random.Range(0, terrorists.Count)];
        State = BombState.Carried;
    }

    public void Clear()
    {
        State = BombState.None;
        Carrier = null;
        User = null;
    }

    /// <summary>Call every frame while someone holds the use key (E): plants or defuses when in place.</summary>
    public void HoldUse(Combatant user)
    {
        if (gm.IsClient || user == null || !user.IsAlive || gm.State != MatchState.Live) return;
        if (User != null && User != user && User.IsAlive) return;   // someone else is busy with it

        bool planting = State == BombState.Carried && Carrier == user && gm.Map.SiteAt(user.transform.position) != null;
        bool defusing = State == BombState.Planted && user.Team == Team.Swat
                        && Vector3.Distance(user.transform.position, Position) < DefuseRadius;
        if (!planting && !defusing) return;

        if (User != user)
        {
            User = user;
            UseProgress = 0f;
            if (defusing) SoundFX.Play(SoundFX.Reload, Position, 0.5f, 0.8f, true, 20f);
        }
        usedThisFrame = user;
        float duration = planting ? PlantTime : user.HasDefuseKit ? KitDefuseTime : DefuseTime;
        UseProgress += Time.deltaTime / duration;
        if (UseProgress < 1f) return;

        User = null;
        UseProgress = 0f;
        if (planting) Plant(user);
        else Defuse(user);
    }

    public void Drop(Combatant carrier)
    {
        if (gm.IsClient || State != BombState.Carried || Carrier != carrier) return;
        // Toss it a little ahead, unless that would put it inside a wall.
        Vector3 spot = carrier.transform.position + carrier.transform.forward * 1.2f;
        if (!Ballistics.HasLineOfSight(carrier.ChestPosition, spot + Vector3.up * 0.5f)) spot = carrier.transform.position;
        DropAt(spot);
        droppedBy = carrier;
        noPickupUntil = Time.time + 2f;
    }

    /// <summary>The carrier is leaving the game: leave the bomb where they stand.</summary>
    public void ReleaseFrom(Combatant leaving)
    {
        if (State == BombState.Carried && Carrier == leaving) DropAt(leaving.transform.position);
        if (User == leaving) User = null;
    }

    void DropAt(Vector3 position)
    {
        State = BombState.Dropped;
        Carrier = null;
        Position = Ground(position);
        droppedBy = null;
    }

    void Plant(Combatant planter)
    {
        State = BombState.Planted;
        Carrier = null;
        Position = Ground(planter.transform.position);
        Site = gm.Map.SiteAt(Position);
        explodeAt = Time.time + FuseTime;
        nextBeep = 0f;
        planter.Money = Mathf.Min(GameManager.MaxMoney, planter.Money + PlantReward);
        SoundFX.Play(SoundFX.RoundStart, Position, 0.8f, 0.7f, true, 80f);
        gm.OnBombPlanted(planter, Site);
    }

    void Defuse(Combatant defuser)
    {
        State = BombState.Defused;
        defuser.Money = Mathf.Min(GameManager.MaxMoney, defuser.Money + DefuseReward);
        SoundFX.Play(SoundFX.Buy, Position, 0.8f, 0.8f, true, 60f);
        gm.OnBombDefused(defuser);
    }

    void Explode()
    {
        State = BombState.Exploded;
        ExplosionEffects(Position);
        gm.OnBombExploded();   // ends the round first, so the blast's kills cannot change who won

        foreach (var target in gm.Combatants.ToArray())
        {
            if (!target.IsAlive) continue;
            float distance = Vector3.Distance(Position, target.ChestPosition);
            if (distance > BlastRadius) continue;
            float damage = WeaponData.Bomb.Damage * Mathf.Pow(1f - distance / BlastRadius, 2f);
            target.TakeExplosion(null, WeaponData.Bomb, damage);
        }
    }

    static void ExplosionEffects(Vector3 position)
    {
        Effects.Explosion(position + Vector3.up, 9f);
        SoundFX.Play(SoundFX.Explosion, position, 1f, 0.6f, true, 250f);
    }

    static Vector3 Ground(Vector3 position)
    {
        return Physics.Raycast(position + Vector3.up, Vector3.down, out var hit, 10f, Ballistics.EnvironmentMask, QueryTriggerInteraction.Ignore)
            ? hit.point
            : new Vector3(position.x, 0f, position.z);
    }

    void Update()
    {
        if (!gm.IsClient && gm.State != MatchState.Menu)
        {
            switch (State)
            {
                case BombState.Carried:
                    if (Carrier == null || !Carrier.IsAlive)
                        DropAt(Carrier != null ? Carrier.transform.position : Position);
                    break;

                case BombState.Dropped:
                    foreach (var c in gm.Combatants)
                    {
                        if (!c.IsAlive || c.Team != Team.Terrorists) continue;
                        if (c == droppedBy && Time.time < noPickupUntil) continue;
                        if (Vector3.Distance(c.transform.position, Position) > PickupRadius) continue;
                        Carrier = c;
                        State = BombState.Carried;
                        SoundFX.Play(SoundFX.DryFire, Position, 0.6f, 0.8f, true, 15f);
                        break;
                    }
                    break;

                case BombState.Planted:
                    if (Time.time >= explodeAt) Explode();
                    break;
            }
        }
        UpdateVisuals();
    }

    void LateUpdate()
    {
        if (gm.IsClient) return;
        // Letting go of E (or stepping away) resets the plant or defuse.
        if (usedThisFrame == null)
        {
            User = null;
            UseProgress = 0f;
        }
        usedThisFrame = null;
    }

    void UpdateVisuals()
    {
        bool onGround = State == BombState.Dropped || State == BombState.Planted || State == BombState.Defused;
        model.gameObject.SetActive(onGround && gm.State != MatchState.Menu);
        if (onGround) model.position = Position;

        foreach (var c in gm.Combatants)
        {
            var body = c.GetComponent<CharacterBody>();
            if (body != null) body.ShowBomb(State == BombState.Carried && Carrier == c && c.IsAlive);
        }

        // Blink and beep, faster and faster as the fuse burns down.
        bool ticking = State == BombState.Planted;
        led.gameObject.SetActive(!ticking || Time.time % 0.5f < 0.25f);
        if (!ticking || Time.time < nextBeep) return;
        float urgency = 1f - TimeLeft / FuseTime;
        nextBeep = Time.time + Mathf.Lerp(1f, 0.12f, urgency * urgency);
        SoundFX.Play(SoundFX.Beep, Position, 0.7f, 1f, true, 45f);
    }

    // ----------------------------------------------------------------- network client copy

    /// <summary>Client: take over the host's bomb state (and play what changed).</summary>
    public void ApplyNet(BombState state, Combatant carrier, Vector3 position, float timeLeft, Combatant user, float progress, string site)
    {
        if (state != State)
        {
            if (state == BombState.Planted) SoundFX.Play(SoundFX.RoundStart, position, 0.8f, 0.7f, true, 80f);
            if (state == BombState.Exploded) ExplosionEffects(position);
            if (state == BombState.Defused) SoundFX.Play(SoundFX.Buy, position, 0.8f, 0.8f, true, 60f);
        }
        State = state;
        Carrier = carrier;
        Position = position;
        explodeAt = Time.time + timeLeft;
        User = user;
        UseProgress = progress;
        Site = string.IsNullOrEmpty(site) ? null : site;
    }
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Guns on the ground. G drops the gun in hand (never the knife: with the knife in hand G drops the bomb), and a
/// player who dies drops their main gun, or their pistol when they have none. Walking over a gun picks it up when that
/// slot is free; E next to one swaps it for the gun of that kind being carried. Bots pick guns up too, and go for a
/// rifle lying near them when they have none.
///
/// The host decides all of it; a network client only shows the guns (they come in every snapshot) and asks the host
/// to swap. Everything on the ground goes at the start of each round.
/// </summary>
public class WeaponDrops : MonoBehaviour
{
    public const float PickupRadius = 1.3f;   // walking over a gun
    public const float SwapRadius = 2.4f;     // E near it
    const int MaxOnGround = 16;               // the oldest goes when there would be more (snapshots stay small)

    public class Dropped
    {
        public int Id;
        public WeaponData Data;
        public int Mag, Reserve;
        public string Skin;
        public Vector3 Position;
        public float Yaw;
        public Transform Model;
        public Combatant DroppedBy;
        public float NoPickupUntil;
    }

    public readonly List<Dropped> Items = new List<Dropped>();

    GameManager gm;
    int nextId = 1;

    void Awake() => gm = GetComponent<GameManager>();

    public void Clear()
    {
        foreach (var d in Items)
            if (d.Model != null) Destroy(d.Model.gameObject);
        Items.Clear();
    }

    /// <summary>Host: drops the gun in a slot: thrown a little ahead (G), or where the player falls.</summary>
    public bool Drop(Combatant c, WeaponSlot slot, bool thrown = true)
    {
        if (gm.IsClient || c == null || slot == WeaponSlot.Knife || gm.State == MatchState.Menu) return false;
        var weapon = c.Get(slot);
        if (weapon == null) return false;
        string skin = WeaponSkins.SkinOf(c, weapon.Data)?.Id;
        c.RemoveWeapon(slot);
        Vector3 spot = c.transform.position;
        if (thrown)
        {
            Vector3 ahead = spot + c.transform.forward * 1.3f;
            if (Ballistics.HasLineOfSight(c.ChestPosition, ahead + Vector3.up * 0.5f)) spot = ahead;
        }
        Add(weapon.Data, weapon.Mag, weapon.Reserve, skin, Ground(spot), c.transform.eulerAngles.y + Random.Range(60f, 120f), c);
        SoundFX.Play(SoundFX.DryFire, spot, 0.5f, 0.7f, true, 15f);
        return true;
    }

    /// <summary>Host: someone was killed: their main gun (or pistol) stays where they fell.</summary>
    public void OnDeath(Combatant victim)
    {
        if (gm.IsClient || victim == null) return;
        if (victim.Primary != null) Drop(victim, WeaponSlot.Primary, thrown: false);
        else if (victim.Secondary != null) Drop(victim, WeaponSlot.Secondary, thrown: false);
    }

    /// <summary>Host: E next to a gun takes it; the gun of that kind being carried goes down in its place.</summary>
    public bool PickUp(Combatant c, int id)
    {
        if (gm.IsClient || c == null || !c.IsAlive || gm.State == MatchState.Menu) return false;
        var d = Items.Find(item => item.Id == id);
        if (d == null || FlatDistance(c.transform.position, d.Position) > SwapRadius + 0.5f || Mathf.Abs(c.transform.position.y - d.Position.y) > 2f)
            return false;
        if (c.Get(d.Data.Slot) != null) Drop(c, d.Data.Slot, thrown: false);
        Take(c, d);
        return true;
    }

    /// <summary>The nearest gun within reach of a player that E would take (it swaps for the one carried), or null.</summary>
    public Dropped Nearest(Combatant c, float radius)
    {
        Dropped best = null;
        float bestDistance = radius;
        foreach (var d in Items)
        {
            float distance = FlatDistance(c.transform.position, d.Position);
            if (distance > bestDistance || Mathf.Abs(c.transform.position.y - d.Position.y) > 2f) continue;
            best = d;
            bestDistance = distance;
        }
        return best;
    }

    void Take(Combatant c, Dropped d)
    {
        // A gun from the ground sells for nothing: no money changes hands by dropping guns to each other.
        c.GiveWeapon(new WeaponInstance(d.Data) { Mag = d.Mag, Reserve = d.Reserve, SellPrice = 0 });
        Remove(d);
        SoundFX.Play(SoundFX.DryFire, d.Position, 0.6f, 0.9f, !c.IsPlayer, 15f);
        Debug.Log($"[DesertStrike] {c.DisplayName} picked up a {d.Data.Name}");
    }

    void Add(WeaponData data, int mag, int reserve, string skin, Vector3 position, float yaw, Combatant by)
    {
        if (Items.Count >= MaxOnGround) Remove(Items[0]);
        var d = new Dropped
        {
            Id = nextId, Data = data, Mag = mag, Reserve = reserve, Skin = skin, Position = position, Yaw = yaw,
            DroppedBy = by, NoPickupUntil = Time.time + 1.5f,
        };
        nextId = nextId >= 60000 ? 1 : nextId + 1;
        d.Model = BuildModel(d);
        Items.Add(d);
    }

    void Remove(Dropped d)
    {
        if (d.Model != null) Destroy(d.Model.gameObject);
        Items.Remove(d);
    }

    static Transform BuildModel(Dropped d)
    {
        var root = new GameObject("Dropped " + d.Data.Name).transform;
        root.position = d.Position + Vector3.up * 0.07f;
        root.rotation = Quaternion.Euler(0f, d.Yaw, 90f);   // lying on its side
        WeaponModels.Build(d.Data, root, true, out _, WeaponSkins.Find(d.Data, d.Skin ?? ""));
        return root;
    }

    static Vector3 Ground(Vector3 position) =>
        Physics.Raycast(position + Vector3.up, Vector3.down, out var hit, 10f, Ballistics.EnvironmentMask, QueryTriggerInteraction.Ignore)
            ? hit.point
            : position;

    static float FlatDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

    void Update()
    {
        if (gm.IsClient || gm.State == MatchState.Menu || Items.Count == 0) return;
        // Walking over a gun picks it up when that slot is free.
        foreach (var c in gm.Combatants)
        {
            if (!c.IsAlive) continue;
            foreach (var d in Items)
            {
                if (c.Get(d.Data.Slot) != null || (c == d.DroppedBy && Time.time < d.NoPickupUntil)) continue;
                if (FlatDistance(c.transform.position, d.Position) > PickupRadius || Mathf.Abs(c.transform.position.y - d.Position.y) > 1.6f) continue;
                Take(c, d);
                break;
            }
        }
    }

    // ----------------------------------------------------------------- network

    /// <summary>Host: the guns on the ground, for a snapshot (positions in 2 cm steps).</summary>
    public void Write(System.IO.BinaryWriter w)
    {
        w.Write((byte)Items.Count);
        foreach (var d in Items)
        {
            w.Write((ushort)d.Id);
            w.Write((byte)WeaponData.IndexOf(d.Data));
            w.Write((short)Mathf.Clamp(Mathf.RoundToInt(d.Position.x * 50f), short.MinValue, short.MaxValue));
            w.Write((short)Mathf.Clamp(Mathf.RoundToInt(d.Position.y * 50f), short.MinValue, short.MaxValue));
            w.Write((short)Mathf.Clamp(Mathf.RoundToInt(d.Position.z * 50f), short.MinValue, short.MaxValue));
            w.Write((byte)Mathf.RoundToInt(Mathf.Repeat(d.Yaw, 360f) / 360f * 255f));
            w.Write(d.Skin ?? "");
        }
    }

    /// <summary>Client: reads the snapshot's guns and shows them (new ones appear, picked-up ones go).</summary>
    public void Read(System.IO.BinaryReader r)
    {
        int count = r.ReadByte();
        var seen = new HashSet<int>();
        for (int i = 0; i < count; i++)
        {
            int id = r.ReadUInt16();
            var data = WeaponData.FromIndex(r.ReadByte());
            var position = new Vector3(r.ReadInt16() / 50f, r.ReadInt16() / 50f, r.ReadInt16() / 50f);
            float yaw = r.ReadByte() / 255f * 360f;
            string skin = r.ReadString();
            if (data == null) continue;
            seen.Add(id);
            if (Items.Exists(item => item.Id == id)) continue;
            var d = new Dropped { Id = id, Data = data, Position = position, Yaw = yaw, Skin = skin };
            d.Model = BuildModel(d);
            Items.Add(d);
        }
        foreach (var gone in Items.FindAll(item => !seen.Contains(item.Id))) Remove(gone);
    }
}

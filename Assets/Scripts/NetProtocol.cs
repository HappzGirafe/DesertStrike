using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>Things a client asks the host to do for its player.</summary>
public enum NetAction : byte { Reload = 1, Equip, AutoMode, BuyWeapon, BuyArmor, BuyDefuseKit, DropBomb, SellWeapon, DropWeapon, PickUp }

/// <summary>One combatant as the host describes it in a snapshot.</summary>
public struct NetCombatantState
{
    public int NetId;
    public string Name;
    public Team Team;
    public bool IsHuman, Helmet, HasKit, Scoped, AutoMode;
    public Vector3 Position;
    public float Yaw, Height, Reload;
    public int Health, Armor, Money, Kills, Deaths, Mag, Reserve, SpawnCount;
    public int PrimarySell, SecondarySell;   // what selling each gun back would give
    public WeaponData Primary, Secondary;
    public WeaponSlot Slot;
    public string PrimarySkin, SecondarySkin, KnifeSkin;   // skin ids ("" = the plain look)
}

/// <summary>The skin ids in one snapshot, written once: the guns name theirs by number (0: the plain look).</summary>
public class SkinTable
{
    readonly List<string> ids = new List<string> { "" };
    readonly Dictionary<string, int> numbers = new Dictionary<string, int> { [""] = 0 };

    public int Add(string id)
    {
        id ??= "";
        if (numbers.TryGetValue(id, out int number)) return number;
        if (ids.Count >= 255) return 0;
        numbers[id] = ids.Count;
        ids.Add(id);
        return ids.Count - 1;
    }

    public string this[int number] => number >= 0 && number < ids.Count ? ids[number] : "";

    public void Write(BinaryWriter writer)
    {
        writer.Write((byte)(ids.Count - 1));
        for (int i = 1; i < ids.Count; i++) writer.Write(ids[i]);
    }

    public static SkinTable Read(BinaryReader reader)
    {
        var table = new SkinTable();
        int count = reader.ReadByte();
        for (int i = 0; i < count; i++) table.ids.Add(reader.ReadString());
        return table;
    }
}

/// <summary>The round and score as the host describes them in a snapshot.</summary>
public struct NetMatchState
{
    public MatchState State;
    public int Round, TerroristWins, SwatWins, RoundsToWin;
    public float Remaining, BuyTimeLeft, MessageLeft;
    public string Banner, Message;
    public Team? LastWinner;
    public bool FriendlyFire;
    public string MapId;
    public string ClosedSite;   // null, "A" or "B" (small matches)
    public bool Warmup;
}

static class NetIO
{
    public static void Write(this BinaryWriter writer, Vector3 value)
    {
        writer.Write(value.x);
        writer.Write(value.y);
        writer.Write(value.z);
    }

    public static Vector3 ReadVector3(this BinaryReader reader) =>
        new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
}

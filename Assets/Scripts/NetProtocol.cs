using System.IO;
using UnityEngine;

/// <summary>Things a client asks the host to do for its player.</summary>
public enum NetAction : byte { Reload = 1, Equip, AutoMode, BuyWeapon, BuyArmor, BuyDefuseKit, DropBomb }

/// <summary>One combatant as the host describes it in a snapshot.</summary>
public struct NetCombatantState
{
    public int NetId;
    public string Name;
    public Team Team;
    public bool IsHuman, Helmet, HasKit, Scoped, AutoMode;
    public Vector3 Position;
    public float Yaw, Height, Reload;
    public int Health, Armor, Money, Kills, Deaths, Mag, Reserve, SpawnCount, SkinSeed;
    public WeaponData Primary, Secondary;
    public WeaponSlot Slot;
    public string Skin;   // skin id of the weapon in hand ("" = none / bot)
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

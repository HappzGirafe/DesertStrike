using UnityEngine;

public enum Team { Terrorists, Swat }

public enum WeaponSlot { Primary, Secondary, Knife }

/// <summary>
/// Static weapon definitions. Numbers are loosely based on classic CS 1.6 values.
/// </summary>
public class WeaponData
{
    public const int KevlarPrice = 650;
    public const int KevlarHelmetPrice = 1000;

    public string Id;
    public string Name;
    public WeaponSlot Slot;
    public int Price;
    public int Damage;
    public float FireInterval;
    public int MagSize;
    public int ReserveAmmo;
    public float ReloadTime;
    public float Spread;                  // random spread in degrees when standing still
    public float MoveSpread;              // extra spread at full running speed
    public float Recoil;                  // view kick per shot, in degrees
    public float Range = 200f;
    public bool Automatic;
    public bool SelectFire;               // right click switches between semi-auto and full-auto
    public float AutoSpread;              // extra spread while a select-fire weapon is on full-auto
    public int Pellets = 1;
    public float ZoomFov;                 // 0 = no scope
    public bool Explosive;                // fires a rocket that explodes instead of a bullet
    public float BlastRadius;
    public float ProjectileSpeed;
    public float ArmorPenetration = 0.5f; // share of damage that still reaches health through armor
    public float MoveSpeed = 5.6f;
    public int KillReward = 300;
    public Team? TeamOnly;
    public float ShotPitch = 1f;

    public bool IsMelee => Slot == WeaponSlot.Knife;
    public bool AvailableTo(Team team) => TeamOnly == null || TeamOnly == team;

    public static readonly WeaponData Knife = new WeaponData
    {
        Id = "knife", Name = "Knife", Slot = WeaponSlot.Knife, Damage = 55, FireInterval = 0.5f,
        Range = 2.2f, ArmorPenetration = 0.85f, MoveSpeed = 6.2f, KillReward = 1500,
    };

    public static readonly WeaponData Glock = new WeaponData
    {
        Id = "glock", Name = "Glock-18", Slot = WeaponSlot.Secondary, Price = 200, Damage = 25,
        FireInterval = 0.15f, MagSize = 20, ReserveAmmo = 120, ReloadTime = 2.2f, Spread = 1.0f,
        MoveSpread = 2.5f, Recoil = 0.8f, ArmorPenetration = 0.47f, MoveSpeed = 6f,
        TeamOnly = Team.Terrorists, ShotPitch = 1.3f,
    };

    public static readonly WeaponData Usp = new WeaponData
    {
        Id = "usp", Name = "USP", Slot = WeaponSlot.Secondary, Price = 200, Damage = 34,
        FireInterval = 0.17f, MagSize = 12, ReserveAmmo = 100, ReloadTime = 2.4f, Spread = 0.8f,
        MoveSpread = 2.5f, Recoil = 1.2f, ArmorPenetration = 0.5f, MoveSpeed = 6f,
        TeamOnly = Team.Swat, ShotPitch = 1.25f,
    };

    public static readonly WeaponData TecDc9 = new WeaponData
    {
        Id = "tec9", Name = "TEC-DC9", Slot = WeaponSlot.Secondary, Price = 500, Damage = 26,
        FireInterval = 0.09f, MagSize = 12, ReserveAmmo = 24, ReloadTime = 2f, Spread = 1.1f,
        MoveSpread = 2.5f, Recoil = 0.9f, SelectFire = true, AutoSpread = 1f, ArmorPenetration = 0.6f,
        MoveSpeed = 6f, TeamOnly = Team.Terrorists, ShotPitch = 1.2f,
    };

    public static readonly WeaponData M1911 = new WeaponData
    {
        Id = "m1911", Name = "M1911", Slot = WeaponSlot.Secondary, Price = 500, Damage = 34,
        FireInterval = 0.12f, MagSize = 10, ReserveAmmo = 30, ReloadTime = 2.1f, Spread = 0.9f,
        MoveSpread = 2.5f, Recoil = 1.2f, SelectFire = true, AutoSpread = 1f, ArmorPenetration = 0.55f,
        MoveSpeed = 6f, TeamOnly = Team.Swat, ShotPitch = 1.1f,
    };

    public static readonly WeaponData Deagle = new WeaponData
    {
        Id = "deagle", Name = "Desert Eagle", Slot = WeaponSlot.Secondary, Price = 700, Damage = 54,
        FireInterval = 0.3f, MagSize = 7, ReserveAmmo = 35, ReloadTime = 2.2f, Spread = 1.2f,
        MoveSpread = 4f, Recoil = 3f, ArmorPenetration = 0.93f, MoveSpeed = 5.9f, ShotPitch = 0.9f,
    };

    public static readonly WeaponData Mp5 = new WeaponData
    {
        Id = "mp5", Name = "MP5", Slot = WeaponSlot.Primary, Price = 1500, Damage = 26,
        FireInterval = 0.08f, MagSize = 30, ReserveAmmo = 120, ReloadTime = 2.6f, Spread = 1.6f,
        MoveSpread = 2f, Recoil = 0.6f, Automatic = true, ArmorPenetration = 0.6f, MoveSpeed = 5.9f,
        ShotPitch = 1.15f,
    };

    public static readonly WeaponData Shotgun = new WeaponData
    {
        Id = "shotgun", Name = "Pump Shotgun", Slot = WeaponSlot.Primary, Price = 1700, Damage = 22,
        FireInterval = 0.9f, MagSize = 8, ReserveAmmo = 32, ReloadTime = 3.2f, Spread = 4.5f,
        MoveSpread = 1f, Recoil = 4f, Range = 30f, Pellets = 9, ArmorPenetration = 0.75f,
        MoveSpeed = 5.5f, ShotPitch = 0.7f,
    };

    public static readonly WeaponData Ak47 = new WeaponData
    {
        Id = "ak47", Name = "AK-47", Slot = WeaponSlot.Primary, Price = 2500, Damage = 36,
        FireInterval = 0.1f, MagSize = 30, ReserveAmmo = 90, ReloadTime = 2.5f, Spread = 0.6f,
        MoveSpread = 4.5f, Recoil = 1.1f, Automatic = true, ArmorPenetration = 0.77f, MoveSpeed = 5.5f,
        TeamOnly = Team.Terrorists, ShotPitch = 0.95f,
    };

    public static readonly WeaponData M4a1 = new WeaponData
    {
        Id = "m4a1", Name = "M4A1", Slot = WeaponSlot.Primary, Price = 3100, Damage = 33,
        FireInterval = 0.09f, MagSize = 30, ReserveAmmo = 90, ReloadTime = 3.1f, Spread = 0.45f,
        MoveSpread = 4.5f, Recoil = 0.9f, Automatic = true, ArmorPenetration = 0.7f, MoveSpeed = 5.6f,
        TeamOnly = Team.Swat, ShotPitch = 1.05f,
    };

    public static readonly WeaponData Awp = new WeaponData
    {
        Id = "awp", Name = "AWP", Slot = WeaponSlot.Primary, Price = 4750, Damage = 115,
        FireInterval = 1.45f, MagSize = 10, ReserveAmmo = 30, ReloadTime = 3.7f, Spread = 0.05f,
        MoveSpread = 6f, Recoil = 5f, ZoomFov = 20f, ArmorPenetration = 0.97f, MoveSpeed = 5f,
        KillReward = 100, ShotPitch = 0.6f,
    };

    /// <summary>Damage is the blast damage at the center; it falls off to zero at the blast radius.</summary>
    public static readonly WeaponData Rpg = new WeaponData
    {
        Id = "rpg", Name = "RPG", Slot = WeaponSlot.Primary, Price = 10000, Damage = 220,
        FireInterval = 1f, MagSize = 1, ReserveAmmo = 14, ReloadTime = 3f, Spread = 0.6f,
        MoveSpread = 3f, Recoil = 6f, Explosive = true, BlastRadius = 6.5f, ProjectileSpeed = 40f,
        ArmorPenetration = 0.85f, MoveSpeed = 4.8f, KillReward = 100,
    };
}

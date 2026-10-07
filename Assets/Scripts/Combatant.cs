using System;
using System.Collections.Generic;
using UnityEngine;

public class WeaponInstance
{
    public readonly WeaponData Data;
    public int Mag;
    public int Reserve;
    public bool AutoMode;   // only used by select-fire weapons
    public int SellPrice;   // what selling it back gives: the full price in the round it was bought, half after that

    public bool IsAutomatic => Data.Automatic || (Data.SelectFire && AutoMode);
    public float Spread => Data.Spread + (Data.SelectFire && AutoMode ? Data.AutoSpread : 0f);

    public WeaponInstance(WeaponData data)
    {
        Data = data;
        Mag = data.MagSize;
        Reserve = data.ReserveAmmo;
    }
}

public enum HitResult { None, Hit, Kill }

/// <summary>Drives a combatant: the player on this PC, a bot, or (on the host) a player on another PC.</summary>
public interface ICombatantController
{
    void Respawn(Vector3 position, float yaw);
}

/// <summary>
/// Health, armor, money, weapons and stats for anyone who fights.
/// PlayerController, BotController and RemotePlayerController all drive one of these, so everyone plays
/// by the same rules. On a network client every combatant is a mirror: the host decides what happens to
/// it and the client only shows it (and sends its own player's shots and actions to the host).
/// </summary>
public class Combatant : MonoBehaviour
{
    public const float StandHeight = 1.8f;
    public const float CrouchHeight = 1.2f;

    public string DisplayName;
    public Team Team;
    public bool IsPlayer;          // the person playing on this PC
    public bool IsHuman;           // a person (on this PC or another), not a bot
    public bool IsMirror;          // network client copy of a combatant the host runs
    public int NetId;
    public int SpawnCount;         // goes up on every respawn, so network copies can tell
    public int SkinSeed;           // picks the skins bots carry
    public Dictionary<string, string> SkinChoices;   // weapon id -> skin id, for people
    public int Health;
    public int Armor;
    public bool Helmet;
    public bool HasDefuseKit;
    public int Money;
    public int Kills;
    public int Deaths;
    public float Height = StandHeight;
    public bool Scoped;            // looking through a scope right now (for "noscope" in the kill feed)
    public string LastResupply;    // free ammo given at the start of this round
    public Transform Eye;
    public Transform Muzzle;

    public WeaponInstance Primary { get; private set; }
    public WeaponInstance Secondary { get; private set; }
    public WeaponInstance Knife { get; private set; }
    public WeaponInstance Current { get; private set; }

    public bool IsAlive => Health > 0;
    public bool IsReloading => reloadEndTime > 0f;
    public float ReloadProgress => IsReloading ? Mathf.Clamp01(1f - (reloadEndTime - Time.time) / Current.Data.ReloadTime) : 0f;

    public Vector3 EyePosition => Eye != null ? Eye.position : transform.position + Vector3.up * (Height - 0.15f);
    public Vector3 HeadPosition => transform.position + Vector3.up * (Height - 0.17f);
    public Vector3 ChestPosition => transform.position + Vector3.up * (Height * 0.68f);

    public event Action WeaponChanged;
    public event Action<Combatant, int> Damaged;
    public event Action<Combatant> Died;

    WeaponInstance previous;
    float nextFireTime;
    float reloadEndTime;
    float predictUntil;   // client: keep our own guess of the weapon state until the host has caught up

    /// <summary>Heals for a new round. Survivors keep their gear; everyone else restarts with a pistol.</summary>
    public void ResetForRound(bool keepGear)
    {
        Health = 100;
        reloadEndTime = 0f;
        LastResupply = null;
        // Guns kept from an earlier round sell for half their price (the free starting pistol for nothing).
        foreach (var weapon in new[] { Primary, Secondary })
            if (weapon != null) weapon.SellPrice = Mathf.Min(weapon.SellPrice, weapon.Data.Price / 2);
        if (!keepGear || Knife == null)
        {
            Armor = 0;
            Helmet = false;
            HasDefuseKit = false;
            Knife = new WeaponInstance(WeaponData.Knife);
            Secondary = new WeaponInstance(Team == Team.Terrorists ? WeaponData.Glock : WeaponData.Usp);
            Primary = null;
        }
        Current = null;
        previous = null;
        Equip(BestSlot(), instant: true);
    }

    /// <summary>
    /// Free ammo at the start of a round: each carried gun gets its <see cref="WeaponData.RoundAmmoBonus"/>
    /// added to its reserve. Returns what was added, e.g. "+20 Glock-18, +40 AK-47" (empty if nothing).
    /// </summary>
    public string AddRoundAmmo()
    {
        string added = "";
        foreach (var weapon in new[] { Primary, Secondary })
        {
            if (weapon == null || weapon.Data.RoundAmmoBonus <= 0) continue;
            weapon.Reserve += weapon.Data.RoundAmmoBonus;
            added += (added.Length > 0 ? ", " : "") + $"+{weapon.Data.RoundAmmoBonus} {weapon.Data.Name}";
        }
        LastResupply = added;
        return added;
    }

    public WeaponInstance Get(WeaponSlot slot)
    {
        switch (slot)
        {
            case WeaponSlot.Primary: return Primary;
            case WeaponSlot.Secondary: return Secondary;
            default: return Knife;
        }
    }

    public WeaponSlot BestSlot()
    {
        if (Primary != null && Primary.Mag + Primary.Reserve > 0) return WeaponSlot.Primary;
        if (Secondary != null && Secondary.Mag + Secondary.Reserve > 0) return WeaponSlot.Secondary;
        return WeaponSlot.Knife;
    }

    public bool Equip(WeaponSlot slot, bool instant = false)
    {
        var weapon = Get(slot);
        if (weapon == null || weapon == Current) return false;
        SetCurrent(weapon, instant ? 0f : 0.4f);
        if (IsMirror)
        {
            HoldPrediction();
            GameManager.Instance.Net.SendAction(NetAction.Equip, (int)slot);
        }
        return true;
    }

    public bool EquipPrevious() => previous != null && Equip(previous.Data.Slot);

    public bool TryBuy(WeaponData weapon)
    {
        if (!weapon.AvailableTo(Team) || Money < weapon.Price) return false;
        if (Get(weapon.Slot)?.Data == weapon) return false;
        Money -= weapon.Price;
        var instance = new WeaponInstance(weapon) { SellPrice = weapon.Price };
        if (weapon.Slot == WeaponSlot.Primary) Primary = instance;
        else Secondary = instance;
        SetCurrent(instance, 0.4f);
        return true;
    }

    /// <summary>Sells the gun in a slot back (never the knife) for its <see cref="WeaponInstance.SellPrice"/>.</summary>
    public bool TrySell(WeaponSlot slot)
    {
        var weapon = slot == WeaponSlot.Knife ? null : Get(slot);
        if (weapon == null) return false;
        Money = Mathf.Min(GameManager.MaxMoney, Money + weapon.SellPrice);
        if (slot == WeaponSlot.Primary) Primary = null;
        else Secondary = null;
        if (Current == weapon) SetCurrent(Get(BestSlot()), 0.4f);
        if (previous == weapon) previous = null;
        return true;
    }

    public bool TryBuyArmor(bool withHelmet)
    {
        int price;
        if (withHelmet)
        {
            if (Armor >= 100 && Helmet) return false;
            price = Armor >= 100 ? WeaponData.KevlarHelmetPrice - WeaponData.KevlarPrice : WeaponData.KevlarHelmetPrice;
        }
        else
        {
            if (Armor >= 100) return false;
            price = WeaponData.KevlarPrice;
        }
        if (Money < price) return false;
        Money -= price;
        Armor = 100;
        Helmet |= withHelmet;
        return true;
    }

    public bool TryBuyDefuseKit()
    {
        if (Team != Team.Swat || HasDefuseKit || Money < WeaponData.DefuseKitPrice) return false;
        Money -= WeaponData.DefuseKitPrice;
        HasDefuseKit = true;
        return true;
    }

    public bool CanFire => IsAlive && Current != null && !IsReloading && Time.time >= nextFireTime
                           && (Current.Data.IsMelee || Current.Mag > 0);

    /// <param name="ignoreFireRate">For shots from another PC, which already paced them.</param>
    public bool TryFire(Vector3 direction, float spreadDegrees, bool ignoreFireRate = false)
    {
        if (!IsAlive || Current == null || IsReloading) return false;
        if (!ignoreFireRate && Time.time < nextFireTime) return false;
        if (!Current.Data.IsMelee && Current.Mag <= 0) return false;

        var weapon = Current.Data;
        nextFireTime = Time.time + weapon.FireInterval;
        if (!weapon.IsMelee) Current.Mag--;

        Vector3 origin = EyePosition;
        Vector3 tracerFrom = Muzzle != null ? Muzzle.position : origin;
        SoundFX.PlayShot(weapon, origin, IsPlayer);
        if (!weapon.IsMelee) Effects.MuzzleFlash(tracerFrom);

        var gm = GameManager.Instance;
        if (IsMirror)
        {
            // Network client: the host does the actual shooting.
            HoldPrediction();
            gm.Net.SendFire(direction, spreadDegrees);
            return true;
        }

        gm.Net.RecordFire(this, weapon, tracerFrom);
        if (weapon.Explosive)
            Rocket.Launch(this, origin, Ballistics.ApplySpread(direction, spreadDegrees), weapon);
        else
            for (int i = 0; i < weapon.Pellets; i++)
                Ballistics.Fire(this, origin, Ballistics.ApplySpread(direction, spreadDegrees), weapon, tracerFrom);

        gm.ReportNoise(origin, Team, weapon.IsMelee ? 6f : 60f);
        return true;
    }

    public bool StartReload()
    {
        var weapon = Current;
        if (weapon == null || weapon.Data.IsMelee || IsReloading || weapon.Mag >= weapon.Data.MagSize || weapon.Reserve <= 0)
            return false;
        reloadEndTime = Time.time + weapon.Data.ReloadTime;
        SoundFX.Play(SoundFX.Reload, EyePosition, IsPlayer ? 0.5f : 0.4f, 1f, !IsPlayer, 25f);
        if (IsMirror)
        {
            HoldPrediction();
            GameManager.Instance.Net.SendAction(NetAction.Reload, 0);
        }
        return true;
    }

    public HitResult TakeHit(Combatant attacker, WeaponData weapon, Vector3 point, out bool headshot)
    {
        headshot = false;
        if (!CanBeHurtBy(attacker)) return HitResult.None;

        float height = point.y - transform.position.y;
        headshot = !weapon.IsMelee && height > Height - 0.32f;
        float damage = weapon.Damage;
        if (headshot) damage *= 4f;
        else if (height < Height * 0.42f) damage *= 0.75f;
        return ApplyDamage(attacker, weapon, damage, headshot);
    }

    public HitResult TakeExplosion(Combatant attacker, WeaponData weapon, float damage) =>
        CanBeHurtBy(attacker) ? ApplyDamage(attacker, weapon, damage, false) : HitResult.None;

    // Teammates can only hurt each other with friendly fire on; your own rocket always can.
    bool CanBeHurtBy(Combatant attacker) =>
        IsAlive && !IsMirror
        && (attacker == null || attacker == this || attacker.Team != Team || GameManager.Instance.FriendlyFire);

    HitResult ApplyDamage(Combatant attacker, WeaponData weapon, float damage, bool headshot)
    {
        if (Armor > 0 && (!headshot || Helmet))
        {
            float toHealth = damage * weapon.ArmorPenetration;
            Armor = Mathf.Max(0, Armor - Mathf.CeilToInt((damage - toHealth) * 0.5f));
            damage = toHealth;
        }

        int amount = Mathf.Max(1, Mathf.RoundToInt(damage));
        Health = Mathf.Max(0, Health - amount);
        Damaged?.Invoke(attacker, amount);
        if (Health > 0) return HitResult.Hit;

        Deaths++;
        reloadEndTime = 0f;
        Died?.Invoke(attacker);
        GameManager.Instance.OnKilled(this, attacker, weapon, headshot);
        return HitResult.Kill;
    }

    // ----------------------------------------------------------------- network client copies

    /// <summary>Client: keep our own guess of slot, ammo and reload for a moment after acting on them.</summary>
    public void HoldPrediction() => predictUntil = Time.time + 0.35f;

    public void ApplyNetLoadout(WeaponData primary, WeaponData secondary, WeaponSlot slot, int mag, int reserve,
                                float reloadProgress, bool autoMode, int primarySell, int secondarySell)
    {
        if (Knife == null) Knife = new WeaponInstance(WeaponData.Knife);
        if (Primary?.Data != primary) Primary = primary != null ? new WeaponInstance(primary) : null;
        if (Secondary?.Data != secondary) Secondary = secondary != null ? new WeaponInstance(secondary) : null;
        if (Primary != null) Primary.SellPrice = primarySell;
        if (Secondary != null) Secondary.SellPrice = secondarySell;

        bool predicting = Time.time < predictUntil;
        var wanted = predicting && Current != null && Get(Current.Data.Slot) == Current ? Current : Get(slot) ?? Knife;
        bool changed = wanted != Current;
        if (changed)
        {
            previous = Current;
            Current = wanted;
        }
        if (!predicting && Current.Data.Slot == slot)
        {
            Current.Mag = mag;
            Current.Reserve = reserve;
            Current.AutoMode = autoMode;
            reloadEndTime = reloadProgress > 0f ? Time.time + (1f - reloadProgress) * Current.Data.ReloadTime : 0f;
        }
        if (changed) WeaponChanged?.Invoke();
    }

    public void ApplyNetHealth(int health)
    {
        int old = Health;
        Health = health;
        if (health < old) Damaged?.Invoke(null, old - health);
        if (old > 0 && health <= 0)
        {
            reloadEndTime = 0f;
            Died?.Invoke(null);
        }
    }

    void SetCurrent(WeaponInstance weapon, float drawDelay)
    {
        previous = Current;
        Current = weapon;
        reloadEndTime = 0f;
        nextFireTime = Time.time + drawDelay;
        WeaponChanged?.Invoke();
    }

    void Update()
    {
        if (reloadEndTime > 0f && Time.time >= reloadEndTime)
        {
            reloadEndTime = 0f;
            if (IsMirror) return;   // the host sends the new ammo counts
            int take = Mathf.Min(Current.Data.MagSize - Current.Mag, Current.Reserve);
            Current.Mag += take;
            Current.Reserve -= take;
        }
    }
}

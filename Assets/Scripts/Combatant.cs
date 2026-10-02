using System;
using UnityEngine;

public class WeaponInstance
{
    public readonly WeaponData Data;
    public int Mag;
    public int Reserve;
    public bool AutoMode;   // only used by select-fire weapons

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

/// <summary>
/// Health, armor, money, weapons and stats for anyone who fights.
/// PlayerController and BotController both drive one of these, so both play by the same rules.
/// </summary>
public class Combatant : MonoBehaviour
{
    public const float StandHeight = 1.8f;

    public string DisplayName;
    public Team Team;
    public bool IsPlayer;
    public int Health;
    public int Armor;
    public bool Helmet;
    public int Money;
    public int Kills;
    public int Deaths;
    public float Height = StandHeight;
    public Transform Eye;
    public Transform Muzzle;

    public WeaponInstance Primary { get; private set; }
    public WeaponInstance Secondary { get; private set; }
    public WeaponInstance Knife { get; private set; }
    public WeaponInstance Current { get; private set; }

    public bool IsAlive => Health > 0;
    public bool IsReloading => reloadEndTime > 0f;
    public float ReloadProgress => IsReloading ? 1f - (reloadEndTime - Time.time) / Current.Data.ReloadTime : 0f;

    public Vector3 EyePosition => Eye != null ? Eye.position : transform.position + Vector3.up * (Height - 0.15f);
    public Vector3 HeadPosition => transform.position + Vector3.up * (Height - 0.17f);
    public Vector3 ChestPosition => transform.position + Vector3.up * (Height * 0.68f);

    public event Action WeaponChanged;
    public event Action<Combatant, int> Damaged;
    public event Action<Combatant> Died;

    WeaponInstance previous;
    float nextFireTime;
    float reloadEndTime;

    /// <summary>Heals for a new round. Survivors keep their gear; everyone else restarts with a pistol.</summary>
    public void ResetForRound(bool keepGear)
    {
        Health = 100;
        reloadEndTime = 0f;
        if (!keepGear || Knife == null)
        {
            Armor = 0;
            Helmet = false;
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
        return true;
    }

    public bool EquipPrevious() => previous != null && Equip(previous.Data.Slot);

    public bool TryBuy(WeaponData weapon)
    {
        if (!weapon.AvailableTo(Team) || Money < weapon.Price) return false;
        if (Get(weapon.Slot)?.Data == weapon) return false;
        Money -= weapon.Price;
        var instance = new WeaponInstance(weapon);
        if (weapon.Slot == WeaponSlot.Primary) Primary = instance;
        else Secondary = instance;
        SetCurrent(instance, 0.4f);
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

    public bool CanFire => IsAlive && Current != null && !IsReloading && Time.time >= nextFireTime
                           && (Current.Data.IsMelee || Current.Mag > 0);

    public bool TryFire(Vector3 direction, float spreadDegrees)
    {
        if (!CanFire) return false;
        var weapon = Current.Data;
        nextFireTime = Time.time + weapon.FireInterval;
        if (!weapon.IsMelee) Current.Mag--;

        Vector3 origin = EyePosition;
        Vector3 tracerFrom = Muzzle != null ? Muzzle.position : origin;
        if (weapon.Explosive)
            Rocket.Launch(this, origin, Ballistics.ApplySpread(direction, spreadDegrees), weapon);
        else
            for (int i = 0; i < weapon.Pellets; i++)
                Ballistics.Fire(this, origin, Ballistics.ApplySpread(direction, spreadDegrees), weapon, tracerFrom);

        SoundFX.PlayShot(weapon, origin, IsPlayer);
        if (!weapon.IsMelee) Effects.MuzzleFlash(tracerFrom);
        GameManager.Instance.ReportNoise(origin, Team, weapon.IsMelee ? 6f : 60f);
        return true;
    }

    public bool StartReload()
    {
        var weapon = Current;
        if (weapon == null || weapon.Data.IsMelee || IsReloading || weapon.Mag >= weapon.Data.MagSize || weapon.Reserve <= 0)
            return false;
        reloadEndTime = Time.time + weapon.Data.ReloadTime;
        SoundFX.Play(SoundFX.Reload, EyePosition, IsPlayer ? 0.5f : 0.4f, 1f, !IsPlayer, 25f);
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

    // No friendly fire, but your own rocket can still hurt you.
    bool CanBeHurtBy(Combatant attacker) =>
        IsAlive && (attacker == null || attacker == this || attacker.Team != Team);

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
            int take = Mathf.Min(Current.Data.MagSize - Current.Mag, Current.Reserve);
            Current.Mag += take;
            Current.Reserve -= take;
        }
    }
}

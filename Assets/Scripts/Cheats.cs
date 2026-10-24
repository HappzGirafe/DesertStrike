using UnityEngine;

/// <summary>
/// The admins' cheats. In a match, ` (or CONSOLE in the pause menu, on a phone) opens the console; "/cheats on" there
/// turns them on and shows their switches: aimbot, fly, no clip (flying through walls), wall hack, all weapons (every
/// gun, free: the mouse wheel or the phone's weapon button goes through them all, 1 / 2 again to the next rifle /
/// pistol), infinite ammo (999), infinite money, infinite health and shop anywhere (buy and sell all round, anywhere);
/// "/cheats off" turns them all off. Only for admin accounts (the online server says who: OnlineClient.IsAdmin),
/// in every match but the tutorial: against bots, in LAN games (hosted or joined) and in online rooms.
/// An admin can also turn on "cheats for everyone" for the match ("/cheats everyone on|off", or the console's button):
/// then every player in it may use them too (GameManager.CheatsForEveryone, kept where the match runs).
///
/// Aimbot and wall hack happen in the player's own game. The others are carried out where the match runs: in the player's own game, or by the host, which takes them only from a player the online server vouched for:
/// online by the room ticket's admin mark (<see cref="JoinTicket"/>), in a LAN game by an admin's pass the host checked
/// (OnlineClient.LanPass), as <see cref="Combatant.CheatFlags"/>.
/// </summary>
public static class Cheats
{
    public const int MaxAmmo = 999;
    public const int FlyFlag = 1, AmmoFlag = 2, MoneyFlag = 4, HealthFlag = 8, NoClipFlag = 16, ShopFlag = 32, AllWeaponsFlag = 64,
                     AllFlags = 127;

    public static bool On { get; private set; }
    public static bool Aimbot, Fly, NoClip, InfiniteAmmo, WallHack, InfiniteMoney, InfiniteHealth, ShopAnywhere, AllWeapons;

    /// <summary>Whether cheats may be used in this game right now.</summary>
    public static bool Allowed(GameManager gm) => gm.Tutorial == null && (IsAdmin(gm) || gm.CheatsForEveryone);

    public static bool IsAdmin(GameManager gm) => gm.Online != null && gm.Online.IsAdmin;

    /// <summary>An admin turns cheats for everyone on or off: where the match runs, or by asking its host.</summary>
    public static string SetForEveryone(GameManager gm, bool on)
    {
        if (!IsAdmin(gm)) return "Only admins can turn cheats for everyone on or off.";
        if (gm.Tutorial != null) return "No cheats in the tutorial.";
        if (gm.IsClient)
        {
            gm.Net.SendAction(NetAction.CheatsForEveryone, on ? 1 : 0);
            return on ? "Asked the host to turn on cheats for everyone." : "Asked the host to turn off cheats for everyone.";
        }
        gm.SetCheatsForEveryone(on, gm.Online.User.nick);
        return on ? "Cheats for everyone: on." : "Cheats for everyone: off.";
    }

    public static bool AimbotOn => On && Aimbot;
    public static bool FlyOn => On && Fly;
    public static bool NoClipOn => On && NoClip;
    public static bool WallHackOn => On && WallHack;
    public static bool AllWeaponsOn => On && AllWeapons;

    /// <summary>What the host carries out (all but aimbot and wall hack), as <see cref="Combatant.CheatFlags"/>.</summary>
    public static int Flags => !On ? 0 : (Fly ? FlyFlag : 0) | (InfiniteAmmo ? AmmoFlag : 0) | (InfiniteMoney ? MoneyFlag : 0)
                                         | (InfiniteHealth ? HealthFlag : 0) | (NoClip ? NoClipFlag : 0)
                                         | (ShopAnywhere ? ShopFlag : 0) | (AllWeapons ? AllWeaponsFlag : 0);

    /// <summary>Whether a combatant has that cheat: as the host has it, or this game's own player's switches (a LAN
    /// or online player's game knows its switches before the host does).</summary>
    public static bool Has(Combatant c, int flag) => c != null && ((c.CheatFlags & flag) != 0 || (c.IsPlayer && (Flags & flag) != 0));

    /// <summary>All weapons: the next gun after <paramref name="current"/> (<paramref name="step"/> 1 or -1), among every
    /// weapon, or only those of one slot.</summary>
    public static WeaponData NextWeapon(WeaponData current, int step, WeaponSlot? slot = null)
    {
        var list = System.Array.FindAll(WeaponData.All, w => slot == null || w.Slot == slot);
        int index = System.Array.IndexOf(list, current);
        if (index < 0) return list[step > 0 ? 0 : list.Length - 1];
        return list[((index + step) % list.Length + list.Length) % list.Length];
    }

    /// <summary>All weapons: takes that gun into its slot (or the knife), from whoever runs the match.</summary>
    public static void TakeWeapon(GameManager gm, WeaponData weapon)
    {
        if (weapon == null || gm.Player == null) return;
        if (gm.IsClient) gm.Net.SendAction(NetAction.CheatWeapon, WeaponData.IndexOf(weapon));
        else gm.Player.Self.TakeCheatWeapon(weapon);
    }

    static int sentFlags = -1;
    static float sentAt;

    /// <summary>A console line: does it and answers (an English text; GameUI translates it).</summary>
    public static string Command(GameManager gm, string text)
    {
        string command = (text ?? "").Trim().ToLowerInvariant();
        while (command.Contains("  ")) command = command.Replace("  ", " ");
        switch (command)
        {
            case "":
                return null;
            case "/help":
                return "Commands: /cheats on, /cheats off; admins also /cheats everyone on, /cheats everyone off";
            case "/cheats everyone on":
                return SetForEveryone(gm, true);
            case "/cheats everyone off":
                return SetForEveryone(gm, false);
            case "/cheats on":
                if (gm.Tutorial != null) return "No cheats in the tutorial.";
                if (!Allowed(gm)) return "Only admins can turn on cheats (or everyone, once an admin turns on cheats for everyone).";
                On = true;
                Debug.Log("[Cheats] On");
                return "Cheats on: pick them below.";
            case "/cheats off":
                if (!On) return "Cheats are off.";
                TurnOff();
                return "Cheats off.";
            default:
                return "Unknown command. Try /help";
        }
    }

    public static void TurnOff()
    {
        if (On) Debug.Log("[Cheats] Off");
        On = Aimbot = Fly = NoClip = InfiniteAmmo = WallHack = InfiniteMoney = InfiniteHealth = ShopAnywhere = AllWeapons = false;
    }

    /// <summary>Every frame of a match in the player's game: cheats off where they are not allowed (a LAN guest,
    /// after logging out), and the switches handed to whoever runs the match: this game, or the host (online or LAN)
    /// (sent when they change, and again every few seconds in case a packet was lost).</summary>
    public static void Update(GameManager gm)
    {
        if (On && !Allowed(gm)) TurnOff();
        var me = gm.Player != null ? gm.Player.Self : null;
        int flags = Flags;
        if (!gm.IsClient)
        {
            if (me != null) me.CheatFlags = flags;
            sentFlags = -1;
        }
        else
        {
            if (flags != sentFlags || (flags != 0 && Time.unscaledTime - sentAt > 3f))
            {
                gm.Net.SendAction(NetAction.Cheats, flags);
                sentFlags = flags;
                sentAt = Time.unscaledTime;
            }
            // This game counts its own magazine as it fires (the host's count comes back with every snapshot), so
            // with infinite ammo it is kept full here too, or the gun would stop at 0 before the host's refill arrives.
            if (InfiniteAmmo && On && me != null && me.Current != null && !me.Current.Data.IsMelee) me.Current.Mag = me.Current.Data.MagSize;
        }
    }

    /// <summary>Where the match runs (a player's own game or an online room's host), every frame: infinite money and
    /// ammo kept topped up for whoever has them (infinite health, fly and no clip are in Combatant and
    /// RemotePlayerController, shop anywhere in GameManager.CanBuy, all weapons in Combatant.TakeCheatWeapon).</summary>
    public static void ApplyOnHost(GameManager gm)
    {
        foreach (var c in gm.Combatants)
        {
            if (c.CheatFlags == 0 || !c.IsAlive) continue;
            if ((c.CheatFlags & MoneyFlag) != 0) c.Money = GameManager.MaxMoney;
            if ((c.CheatFlags & AmmoFlag) == 0) continue;
            foreach (var weapon in new[] { c.Primary, c.Secondary })
                if (weapon != null && !weapon.Data.IsMelee)
                {
                    weapon.Mag = weapon.Data.MagSize;
                    weapon.Reserve = MaxAmmo;
                }
        }
    }

    /// <summary>Aimbot: the head of the nearest enemy in sight within <paramref name="cone"/> degrees of where the
    /// player looks, or null.</summary>
    public static Vector3? AimbotTarget(GameManager gm, Combatant me, Vector3 eye, Vector3 forward, float cone = 60f)
    {
        Vector3? best = null;
        float bestAngle = cone;
        foreach (var c in gm.Combatants)
        {
            if (c == me || !c.IsAlive || c.Team == me.Team) continue;
            Vector3 head = c.HeadPosition;
            float angle = Vector3.Angle(forward, head - eye);
            if (angle >= bestAngle || !Ballistics.HasLineOfSight(eye, head)) continue;
            bestAngle = angle;
            best = head;
        }
        return best;
    }
}

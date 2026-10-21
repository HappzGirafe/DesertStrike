using UnityEngine;

/// <summary>
/// The admins' cheats. In a match, ` (or CONSOLE in the pause menu, on a phone) opens the console; "/cheats on" there
/// turns them on and shows their switches: aimbot, fly, infinite ammo (999), wall hack, infinite money; "/cheats off"
/// turns them all off. Only for admin accounts (the online server says who: OnlineClient.IsAdmin), and only in
/// matches against bots or in a LAN game this game hosts: never in an online room or as a LAN guest, where the host
/// decides what happens (and would refuse a flying player anyway).
/// </summary>
public static class Cheats
{
    public const int MaxAmmo = 999;

    public static bool On { get; private set; }
    public static bool Aimbot, Fly, InfiniteAmmo, WallHack, InfiniteMoney;

    /// <summary>Whether cheats may be used in this game right now.</summary>
    public static bool Allowed(GameManager gm) =>
        gm.Online != null && gm.Online.IsAdmin && !gm.IsClient && !gm.Net.OnlineClient && gm.Tutorial == null;

    public static bool AimbotOn => On && Aimbot;
    public static bool FlyOn => On && Fly;
    public static bool WallHackOn => On && WallHack;

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
                return "Commands: /cheats on, /cheats off (admins only)";
            case "/cheats on":
                if (gm.Online == null || !gm.Online.IsAdmin) return "Only admins can turn on cheats.";
                if (!Allowed(gm)) return "Cheats work in matches against bots and in LAN games you host.";
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
        On = Aimbot = Fly = InfiniteAmmo = WallHack = InfiniteMoney = false;
    }

    /// <summary>Every frame of a match (GameManager): what infinite ammo and money keep topped up, and cheats switched
    /// off where they are not allowed (an online room, a LAN guest, after logging out).</summary>
    public static void Update(GameManager gm)
    {
        if (!On) return;
        if (!Allowed(gm))
        {
            TurnOff();
            return;
        }
        var me = gm.Player != null ? gm.Player.Self : null;
        if (me == null || !me.IsAlive) return;
        if (InfiniteMoney) me.Money = GameManager.MaxMoney;
        if (InfiniteAmmo)
            foreach (var weapon in new[] { me.Primary, me.Secondary })
                if (weapon != null && !weapon.Data.IsMelee)
                {
                    weapon.Mag = weapon.Data.MagSize;
                    weapon.Reserve = MaxAmmo;
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

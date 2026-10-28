using System;
using UnityEngine;

/// <summary>
/// The admin panel (ADMIN in the main menu, for the accounts on the server's ADMIN_NICKS list): find a player by
/// nickname, give or take Tokens, give or take back the shop's skins, and see what admins did lately. The
/// server decides who is an admin and checks every request (online-server/admin.py); it logs everything done here.
/// </summary>
public partial class GameUI
{
    static readonly int[] QuickAmounts = { 10, 50, 100, 500, 1000 };

    bool adminOpen;
    string adminNick = "", adminAmount = "50";
    AdminPlayer adminPlayer;
    int adminSkinPage;   // the shop's skins, a page at a time
    string adminMessage;
    bool adminProblem;

    void OpenAdmin()
    {
        adminOpen = true;
        adminMessage = null;
        if (Online.User != null) AdminFind(adminPlayer?.nick ?? Online.User.nick);   // fresh numbers for whoever was shown
        Online.RefreshAdminLog();
    }

    void AdminFind(string nick)
    {
        if (string.IsNullOrWhiteSpace(nick))
        {
            AdminSay("Type a nickname and press FIND.", true);
            return;
        }
        adminNick = nick.Trim();
        adminMessage = null;
        Online.AdminFind(adminNick, (player, problem) =>
        {
            if (problem != null) AdminSay(problem, true);
            else adminPlayer = player;
        });
    }

    void AdminChangeTokens(string nick, int amount)
    {
        Online.AdminTokens(nick, amount, (player, problem) =>
        {
            if (problem != null)
            {
                AdminSay(problem, true);
                return;
            }
            adminPlayer = player;
            AdminSay($"{player.nick} now has {player.tokens} Tokens.", false);
        });
    }

    void AdminChangeSkin(string nick, ShopItem item, bool give)
    {
        string name = $"{WeaponData.Find(item.weapon)?.Name ?? item.weapon} | {item.skin}";
        Online.AdminSkin(nick, item.weapon, item.skin, give, (player, problem) =>
        {
            if (problem != null)
            {
                AdminSay(problem, true);
                return;
            }
            adminPlayer = player;
            AdminSay(give ? $"Gave {name} to {player.nick}." : $"Took {name} back from {player.nick}.", false);
        });
    }

    void AdminSay(string text, bool problem)
    {
        adminMessage = text;
        adminProblem = problem;
    }

    void DrawAdmin()
    {
        var online = Online;
        var e = Event.current;
        if (!online.IsAdmin || (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape))
        {
            adminOpen = false;
            if (!online.IsAdmin) adminPlayer = null;   // logged out
            return;
        }
        bool enter = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                     && GUI.GetNameOfFocusedControl() == "admin-nick";
        Fill(new Rect(0f, 0f, width, RefHeight), new Color(0f, 0f, 0f, 0.55f));
        float w = Mathf.Min(1440f, width - 40f), h = 1040f, x = (width - w) / 2f, y = (RefHeight - h) / 2f;   // tall enough for the shop's skins
        Panel(new Rect(x, y, w, h));
        Label(new Rect(x, y + 18f, w, 60f), "ADMIN PANEL", 46, Gold, TextAnchor.MiddleCenter);
        Label(new Rect(x, y + 76f, w, 28f), "Only admins see this. Everything done here is kept in the server's log.", 18, Dim,
              TextAnchor.MiddleCenter);

        // Left: one player's account.
        float lx = x + 50f, lw = w * 0.56f, cy = y + 130f;
        adminNick = TextInput(new Rect(lx, cy, lw - 340f, 52f), "admin-nick", adminNick, 16);
        Label(new Rect(lx + 10f, cy, lw - 340f, 52f), adminNick.Length == 0 && !KeyboardOpenFor("admin-nick") ? "player's nickname" : "", 18,
              new Color(1f, 1f, 1f, 0.3f));
        if (Button(new Rect(lx + lw - 330f, cy, 160f, 52f), "FIND", false, !online.Busy, 22) || (enter && !online.Busy)) AdminFind(adminNick);
        if (Button(new Rect(lx + lw - 160f, cy, 160f, 52f), "ME", false, !online.Busy, 22)) AdminFind(online.User.nick);
        cy += 80f;

        var player = adminPlayer;
        if (player == null) Label(new Rect(lx, cy, lw, 40f), "Find a player to see their account.", 22, Dim);
        else
        {
            Fill(new Rect(lx, cy, lw, 132f), new Color(1f, 1f, 1f, 0.05f));
            Label(new Rect(lx + 20f, cy + 10f, lw - 40f, 44f), player.nick, 34, Color.white);
            string since = player.created_at?.Substring(0, Mathf.Min(10, player.created_at.Length));
            Label(new Rect(lx + 20f, cy + 10f, lw - 40f, 44f), $"Account since {since}  ·  Time in the game: {PlayedText(player.played)}", 18, Dim,
                  TextAnchor.MiddleRight);
            Label(new Rect(lx + 20f, cy + 66f, lw - 40f, 44f), $"Tokens: {player.tokens}", 30, Pumpkin);
            Label(new Rect(lx + 20f, cy + 66f, lw - 40f, 44f), $"Today from own matches: {player.today} / {player.day_limit}", 18, Dim,
                  TextAnchor.MiddleRight);
            cy += 144f;

            // Account recovery: a one-time code the admin passes on; the player sets a new password with it.
            if (Button(new Rect(lx, cy, 300f, 44f), "RECOVERY CODE", false, !online.Busy, 18))
            {
                string nick = player.nick;
                online.AdminRecoveryCode(nick, (code, hours, problem) =>
                    AdminSay(problem ?? $"Recovery code for {nick}: {code}  (once, {hours} hours). They press FORGOT PASSWORD? > I HAVE A CODE.",
                             problem != null));
            }
            Label(new Rect(lx + 316f, cy, lw - 316f, 44f), "for a player who cannot log in", 17, Dim);
            cy += 58f;

            // Give or take tokens: the amount typed, or one of the quick ones.
            Label(new Rect(lx, cy, 200f, 30f), "Amount", 18, Dim);
            cy += 32f;
            adminAmount = TextInput(new Rect(lx, cy, 200f, 52f), "admin-amount", adminAmount, 6, numbers: true);
            int amount = int.TryParse(adminAmount, out int typed) ? Mathf.Abs(typed) : 0;
            float bw = (lw - 220f - 20f) / 2f;
            if (Button(new Rect(lx + 220f, cy, bw, 52f), "GIVE", false, amount > 0 && !online.Busy, 22)) AdminChangeTokens(player.nick, amount);
            if (Button(new Rect(lx + 240f + bw, cy, bw, 52f), "TAKE AWAY", false, amount > 0 && !online.Busy, 22)) AdminChangeTokens(player.nick, -amount);
            cy += 62f;
            float qw = (lw - 10f * (QuickAmounts.Length - 1)) / QuickAmounts.Length;
            for (int i = 0; i < QuickAmounts.Length; i++)
                if (Button(new Rect(lx + i * (qw + 10f), cy, qw, 46f), $"+{QuickAmounts[i]}", false, !online.Busy, 20))
                    AdminChangeTokens(player.nick, QuickAmounts[i]);
            cy += 72f;

            // The shop's skins: give one, or take it back.
            Label(new Rect(lx, cy, lw, 34f), "Skins for Tokens", 24, Gold);
            cy += 42f;
            // Two columns, a page at a time (64 skins since the neon wraps).
            int rows = Mathf.Max(1, Mathf.FloorToInt((y + h - 210f - cy) / 46f)), perPage = rows * 2;
            int pages = (player.shop.Count + perPage - 1) / perPage;
            adminSkinPage = Mathf.Clamp(adminSkinPage, 0, Mathf.Max(0, pages - 1));
            if (pages > 1)
            {
                float py = y + h - 196f;
                if (Button(new Rect(lx, py, 120f, 40f), "<", false, adminSkinPage > 0, 22)) adminSkinPage--;
                Label(new Rect(lx + 130f, py, lw - 260f, 40f), $"Page {adminSkinPage + 1} / {pages}", 19, Dim, TextAnchor.MiddleCenter);
                if (Button(new Rect(lx + lw - 120f, py, 120f, 40f), ">", false, adminSkinPage < pages - 1, 22)) adminSkinPage++;
            }
            float cw = (lw - 10f) / 2f;
            for (int i = adminSkinPage * perPage; i < Mathf.Min(player.shop.Count, (adminSkinPage + 1) * perPage); i++)
            {
                var item = player.shop[i];
                bool owned = player.owned.Contains(item.weapon + "/" + item.skin);
                int cell = i - adminSkinPage * perPage;
                float cx = lx + cell / rows * (cw + 10f), cellY = cy + cell % rows * 46f;
                Fill(new Rect(cx, cellY, cw, 42f), new Color(1f, 1f, 1f, owned ? 0.08f : 0.04f));
                Label(new Rect(cx + 12f, cellY, cw - 150f, 24f), $"{WeaponData.Find(item.weapon)?.Name ?? item.weapon} | {item.skin}", 16, Color.white);
                Label(new Rect(cx + 12f, cellY + 21f, cw - 150f, 20f), owned ? "OWNED" : $"{item.price} Tokens", 14, owned ? Gold : Dim);
                if (Button(new Rect(cx + cw - 130f, cellY + 4f, 122f, 34f), owned ? "TAKE BACK" : "GIVE", false, !online.Busy, 15))
                    AdminChangeSkin(player.nick, item, !owned);
            }
        }

        // Right: what admins did lately (newest first).
        float rx = lx + lw + 40f, rw = x + w - 50f - rx, ry = y + 130f;
        Label(new Rect(rx, ry, rw, 34f), "What admins did lately", 24, Gold);
        ry += 44f;
        if (online.AdminLog.Count == 0) Label(new Rect(rx, ry, rw, 30f), "Nothing yet.", 18, Dim);
        for (int i = 0; i < Mathf.Min(12, online.AdminLog.Count); i++)
        {
            var entry = online.AdminLog[i];
            string when = DateTimeOffset.FromUnixTimeSeconds((long)entry.at).ToLocalTime().ToString("dd.MM HH:mm");
            Label(new Rect(rx, ry, rw, 22f), $"{when}   {entry.admin} > {entry.target}", 16, Dim);
            Label(new Rect(rx, ry + 22f, rw, 26f), entry.action, 19, Color.white);
            ry += 54f;
        }

        if (adminMessage != null) WrapLabel(new Rect(x + 50f, y + h - 140f, w - 100f, 50f), adminMessage, 20, adminProblem ? Danger : Gold);
        if (Button(new Rect(x + (w - 320f) / 2f, y + h - 82f, 320f, 58f), "CLOSE", false, true, 24)) adminOpen = false;
    }
}

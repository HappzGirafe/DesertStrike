using UnityEngine;

/// <summary>
/// The start menu as it was before 1.22: one panel with the match settings in two columns and every button in a row
/// below, the keys under it. Settings > Start menu > CLASSIC shows it instead of the new one (GameUI.MainMenu).
/// </summary>
public partial class GameUI
{
    const string ControlsHelp =
        "WASD move  ·  Mouse aim  ·  Left click shoot  ·  R reload  ·  Space jump\n" +
        "Right click: AWP scope  ·  TEC-DC9 / M1911 switch semi-auto and full-auto\n" +
        "1 / 2 / 3 weapons  ·  Q last weapon  ·  Ctrl crouch  ·  Shift walk (silent)  ·  V talk (LAN)\n" +
        "E plant / defuse, take a gun  ·  G drop the gun (knife in hand: the bomb)  ·  B buy (in your spawn)  ·  Tab scores  ·  Esc pause";

    // Two columns: who plays on the left, where and how on the right; the buttons and controls below.
    void DrawClassicMenu()
    {
        Fill(new Rect(0f, 0f, width, RefHeight), new Color(0f, 0f, 0f, 0.3f));
        const float top = 70f;
        float w = Mathf.Min(1480f, width - 40f), x = (width - w) / 2f;
        float column = (w - 120f) / 2f, right = x + column + 40f;
        Panel(new Rect(x, top, w, 720f));
        Label(new Rect(x, top + 20f, w, 80f), "LOW STRIKE", 66, Gold, TextAnchor.MiddleCenter);
        Label(new Rect(x, top + 95f, w, 30f), $"Terrorists vs SWAT on {gm.Map.Description}", 22, Dim, TextAnchor.MiddleCenter);
        DrawAccountBox(x + 24f, top + 22f);
        if (Accounts.IsOnline && Online.Tokens >= 0)   // between the title and EXIT
            Label(new Rect(x + w - 164f - 16f - 330f, top + 30f, 330f, 30f), $"Tokens: {Online.Tokens}", 20, Pumpkin, TextAnchor.MiddleRight);
        // iPhones close apps from the app switcher only (Apple's rules); everywhere else the game can quit itself.
        if (Application.platform != RuntimePlatform.IPhonePlayer && !WebLink.Available && Button(new Rect(x + w - 164f, top + 22f, 140f, 46f), "EXIT", false, true, 20))
        {
            Debug.Log("[DesertStrike] Exit from the main menu");
            Application.Quit();
        }

        float y = top + 150f;
        float leftEnd = TeamRow(x, y, allowSpectate: true, column);
        leftEnd = PlayersRow(x, leftEnd, column);
        leftEnd = DifficultyRow(x, leftEnd, column);
        float rightEnd = MapRow(right, y, column);
        rightEnd = RoundsRow(right, rightEnd, column);
        rightEnd = FriendlyFireAndSiteRow(right, rightEnd, column);
        y = Mathf.Max(leftEnd, rightEnd) + 6f;

        const float small = 188f, gap = 12f;
        float buttons = 300f + 5f * small + 5f * gap, bx = x + (w - buttons) / 2f;
        if (Button(new Rect(bx, y, 300f, 66f), "START MATCH", true, true, 30)) gm.StartMatch();
        bx += 300f + gap;
        if (Button(new Rect(bx, y, small, 66f), "PLAY ONLINE", false, true, 21)) OpenOnline();
        bx += small + gap;
        if (Button(new Rect(bx, y, small, 66f), "LAN GAME", false, true, 21))
        {
            if (WebLink.Available) gm.MenuNotice = BrowserNoLan;   // the browser version
            else OpenLan();
        }
        bx += small + gap;
        bool onlineFriends = Accounts.IsOnline;
        int requests = onlineFriends ? Online.RequestCount + Online.InviteCount : gm.Friends.Incoming.Count;
        int online = onlineFriends ? Online.Friends.FindAll(f => f.status == "online" || f.status == "in_match").Count
                   : Accounts.LoggedIn ? gm.Friends.OnlineCount : 0;
        string news = onlineFriends && Online.InviteCount > 0 ? $"{Online.InviteCount} invitation{(Online.InviteCount == 1 ? "" : "s")}"
                    : $"{requests} request{(requests == 1 ? "" : "s")}";
        string friends = requests > 0 ? $"FRIENDS\n{news}" : online > 0 ? $"FRIENDS\n{online} online" : "FRIENDS";
        if (Button(new Rect(bx, y, small, 66f), friends, requests > 0, true, friends.Contains("\n") ? 18 : 21))
        {
            friendsOpen = true;
            friendsLan = false;
        }
        bx += small + gap;
        if (Button(new Rect(bx, y, small, 66f), "INVENTORY", false, true, 21)) inventoryOpen = true;
        bx += small + gap;
        if (Button(new Rect(bx, y, small, 66f), "SETTINGS", false, true, 21)) settingsOpen = true;
        y += 82f;
        if (!string.IsNullOrEmpty(gm.MenuNotice))
        {
            Label(new Rect(x, y - 6f, w, 26f), gm.MenuNotice, 18, Danger, TextAnchor.MiddleCenter);
            y += 22f;
        }
        Label(new Rect(x + 20f, y, w - 40f, 100f), GameInput.Touch ? TouchHelp : GameInput.CrouchOnC ? ControlsHelp.Replace("Ctrl crouch", "C crouch") : ControlsHelp,
              16, Dim, TextAnchor.UpperCenter);
    }
}

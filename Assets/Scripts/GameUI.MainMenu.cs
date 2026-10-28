using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The main menu. A bar on the left holds the game's name, the account and where to go (online, LAN, friends,
/// inventory, settings, controls); the rest is a match against bots: the maps as pictures, the teams and the rules
/// in one panel, and START. The chosen map circles slowly behind it all. A narrow screen (a small browser window, an
/// old tablet) gets the bar's buttons in a row along the top instead.
/// </summary>
public partial class GameUI
{
    bool controlsOpen;   // CONTROLS: every key, as key caps
    readonly Dictionary<string, Texture2D> mapCards = new Dictionary<string, Texture2D>();

    static readonly Color MenuText = new Color(1f, 1f, 1f, 0.86f);
    static readonly Color Accent = new Color(0.93f, 0.58f, 0.15f);

    void DrawMainMenu()
    {
        bool narrow = width < 1320f;
        float side = narrow ? 0f : Mathf.Clamp(width * 0.23f, 340f, 430f);
        // The map behind stays in sight, darker where the text is.
        Fill(new Rect(0f, 0f, width, RefHeight), new Color(0f, 0f, 0f, 0.2f));
        Fade(new Rect(side, 0f, 640f, RefHeight), new Color(0.01f, 0.012f, 0.02f, 0.75f), toRight: true);
        Fade(new Rect(0f, RefHeight * 0.35f, width, RefHeight * 0.65f), new Color(0f, 0f, 0f, 0.55f), up: true);

        float x, y, w;
        if (narrow)
        {
            DrawLogo(28f, 20f, 44);
            DrawAccountBox(width - 450f, 18f);
            DrawNavRow(24f, 96f, width - 48f);
            x = 24f;
            w = width - 48f;
            y = 172f;
        }
        else
        {
            DrawSideBar(side);
            x = side + 52f;
            w = width - x - 44f;
            y = 54f;
            Label(new Rect(x, y, w, 56f), "PLAY VS BOTS", 46, Color.white);
            Label(new Rect(x + 2f, y + 58f, w, 30f), "Pick a map, set up the teams and start the match", 20, Dim);
            y += 114f;
        }

        y = DrawMapCards(x, y, w, RefHeight - y - 590f);
        Label(new Rect(x + 4f, y + 8f, w, 30f), $"Terrorists vs SWAT on {gm.Map.Description}", 19, Dim);
        y = DrawMatchSettings(x, y + 50f, w);
        DrawStartBar(x, y + 22f, w);

        // Enter starts too (unless a number is being typed).
        var e = Event.current;
        if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            && string.IsNullOrEmpty(GUI.GetNameOfFocusedControl()))
        {
            e.Use();
            gm.StartMatch();
        }
    }

    // ----------------------------------------------------------------- the bar on the left

    void DrawSideBar(float side)
    {
        Fill(new Rect(0f, 0f, side, RefHeight), new Color(0.02f, 0.024f, 0.034f, 0.78f));
        Fade(new Rect(0f, 0f, side, 260f), new Color(Accent.r, Accent.g, Accent.b, 0.08f));
        Fill(new Rect(side - 2f, 0f, 2f, RefHeight), new Color(Gold.r, Gold.g, Gold.b, 0.22f));
        float x = 28f, w = side - 56f;
        DrawLogo(x + 6f, 40f, 58, w - 12f);
        float y = DrawAccountCard(x, 150f, w);
        y += 26f;
        foreach (var item in NavItems())
        {
            if (NavItem(new Rect(x, y, w, 56f), item.Text, false, item.Badge, item.BadgeColor)) item.Click();
            y += 62f;
        }
        float bottom = RefHeight - 34f;
        Text(new Rect(x + 6f, bottom - 6f, w, 24f), "Low Strike " + Application.version, 15, new Color(1f, 1f, 1f, 0.35f), TextAnchor.MiddleLeft);
        if (CanQuit && NavItem(new Rect(x, bottom - 76f, w, 56f), "EXIT", false, null, Color.clear, Danger))
        {
            Debug.Log("[DesertStrike] Exit from the main menu");
            Application.Quit();
        }
    }

    // iPhones close apps from the app switcher only (Apple's rules), and a browser tab is closed by the browser.
    static bool CanQuit => Application.platform != RuntimePlatform.IPhonePlayer && !WebLink.Available;

    void DrawNavRow(float x, float y, float w)
    {
        var items = NavItems();
        int count = items.Count + (CanQuit ? 1 : 0);
        float gap = 10f, bw = (w - gap * (count - 1)) / count;
        for (int i = 0; i < items.Count; i++)
        {
            var rect = new Rect(x + i * (bw + gap), y, bw, 54f);
            bool news = !string.IsNullOrEmpty(items[i].Badge);
            if (Button(rect, news ? items[i].Text + "  (" + items[i].Badge + ")" : items[i].Text, false, true, 19)) items[i].Click();
        }
        if (CanQuit && Button(new Rect(x + items.Count * (bw + gap), y, bw, 54f), "EXIT", false, true, 19)) Application.Quit();
    }

    List<(string Text, string Badge, Color BadgeColor, Action Click)> NavItems()
    {
        bool onlineFriends = Accounts.IsOnline;
        int requests = onlineFriends ? Online.RequestCount + Online.InviteCount : gm.Friends.Incoming.Count;
        int online = onlineFriends ? Online.Friends.FindAll(f => f.status == "online" || f.status == "in_match").Count
                   : Accounts.LoggedIn ? gm.Friends.OnlineCount : 0;
        string friendsBadge = requests > 0 ? requests.ToString() : online > 0 ? $"{online} online" : null;
        return new List<(string, string, Color, Action)>
        {
            ("PLAY ONLINE", null, Color.clear, OpenOnline),
            ("LAN GAME", null, Color.clear, () =>
            {
                if (WebLink.Available) gm.MenuNotice = BrowserNoLan;   // the browser version
                else OpenLan();
            }),
            ("FRIENDS", friendsBadge, requests > 0 ? Danger : MoneyGreen, () =>
            {
                friendsOpen = true;
                friendsLan = false;
            }),
            ("INVENTORY", null, Color.clear, () => inventoryOpen = true),
            ("SETTINGS", null, Color.clear, () => settingsOpen = true),
            ("CONTROLS", null, Color.clear, () => controlsOpen = true),
        };
    }

    // A line of the bar: lights up and moves a little under the pointer; a badge (a number, "3 online") on the right.
    bool NavItem(Rect rect, string text, bool active, string badge, Color badgeColor, Color? color = null)
    {
        float h = Hover(rect);
        Round(rect, new Color(1f, 1f, 1f, active ? 0.1f : 0.07f * h), 10f);
        if (active || h > 0.01f)
            Round(new Rect(rect.x, rect.y + 12f, 4f, rect.height - 24f), new Color(Gold.r, Gold.g, Gold.b, active ? 1f : h), 2f);
        Color normal = color ?? MenuText;
        Label(new Rect(rect.x + 24f + 6f * h, rect.y, rect.width - 110f, rect.height), text, 23, Color.Lerp(normal, color ?? Color.white, h), TextAnchor.MiddleLeft);
        if (!string.IsNullOrEmpty(badge))
        {
            string shown = Loc.T(badge);
            float bw = Style(16, TextAnchor.MiddleCenter).CalcSize(new GUIContent(shown)).x + 22f;
            var pill = new Rect(rect.xMax - bw - 14f, rect.y + (rect.height - 28f) / 2f, bw, 28f);
            Round(pill, badgeColor, 14f);
            Text(pill, shown, 16, Color.white, TextAnchor.MiddleCenter);
        }
        return GUI.Button(rect, GUIContent.none, GUIStyle.none);
    }

    void DrawLogo(float x, float y, int size, float maxWidth = 1000f)
    {
        while (size > 24 && Style(size, TextAnchor.MiddleLeft).CalcSize(new GUIContent("LOW STRIKE")).x > maxWidth) size -= 2;
        float low = Style(size, TextAnchor.MiddleLeft).CalcSize(new GUIContent("LOW ")).x;
        Text(new Rect(x, y, 400f, size * 1.25f), "LOW", size, Color.white, TextAnchor.MiddleLeft);
        Text(new Rect(x + low, y, 400f, size * 1.25f), "STRIKE", size, Gold, TextAnchor.MiddleLeft);
        Round(new Rect(x + 3f, y + size * 1.25f + 2f, 70f, 5f), Gold, 2.5f);
    }

    // Who plays: a round picture with the first letter, the name and the kind of account (or Tokens), and its buttons.
    float DrawAccountCard(float x, float y, float w)
    {
        var card = new Rect(x, y, w, 86f);
        float h = Accounts.LoggedIn ? 0f : Hover(card);
        Round(card, new Color(1f, 1f, 1f, 0.06f + 0.04f * h), 14f);
        Outline(card, new Color(1f, 1f, 1f, 0.07f + 0.15f * h), 14f);
        var avatar = new Rect(card.x + 14f, card.y + 15f, 56f, 56f);
        float tx = avatar.xMax + 16f, tw = card.xMax - tx - 12f;
        if (Accounts.LoggedIn)
        {
            string name = Accounts.Current;
            Glow(avatar, new Color(Gold.r, Gold.g, Gold.b, 0.25f), 8f);
            Round(avatar, Gold, 28f);
            Text(avatar, name.Substring(0, 1).ToUpperInvariant(), 28, new Color(0.12f, 0.08f, 0.02f), TextAnchor.MiddleCenter);
            Text(new Rect(tx, card.y + 12f, tw, 34f), name, FitSize(name, 25, tw), Color.white, TextAnchor.MiddleLeft);
            if (Accounts.IsOnline && Online.Tokens >= 0) Label(new Rect(tx, card.y + 46f, tw, 26f), $"Tokens: {Online.Tokens}", 18, Pumpkin);
            else FitLabel(new Rect(tx, card.y + 46f, tw, 26f), Accounts.IsOnline ? "Online account" : "Account on this computer", 17, Dim);
        }
        else
        {
            Round(avatar, new Color(0.26f, 0.28f, 0.32f), 28f);
            Text(avatar, "?", 28, Color.white, TextAnchor.MiddleCenter);
            FitLabel(new Rect(tx, card.y + 12f, tw, 34f), "Playing as a guest", 22, Color.white);
            FitLabel(new Rect(tx, card.y + 46f, tw, 26f), "Log in to keep your skins and Tokens", 16, Dim);
            if (GUI.Button(card, GUIContent.none, GUIStyle.none)) OpenAccount();
        }
        y = card.yMax + 10f;
        if (!Accounts.LoggedIn)
        {
            if (Button(new Rect(x, y, w, 46f), "LOG IN / SIGN UP", true, true, 19)) OpenAccount();
            return y + 46f;
        }
        var buttons = new List<(string, Action)> { ("LOG OUT", () => { if (Accounts.IsOnline) Online.LogOut(); else Accounts.LogOut(); }) };
        if (Accounts.IsOnline) buttons.Add(("ACCOUNT", () => { onlineAccountOpen = true; accountPageMessage = null; }));
        if (Accounts.IsOnline && Online.IsAdmin) buttons.Add(("ADMIN", OpenAdmin));
        float gap = 8f, bw = (w - gap * (buttons.Count - 1)) / buttons.Count;
        for (int i = 0; i < buttons.Count; i++)
            if (Button(new Rect(x + i * (bw + gap), y, bw, 42f), buttons[i].Item1, false, true, 17)) buttons[i].Item2();
        return y + 42f;
    }

    // ----------------------------------------------------------------- the match against bots

    Texture2D MapCard(string id)
    {
        if (!mapCards.TryGetValue(id, out var texture))
        {
            texture = Resources.Load<Texture2D>("MapCards/" + id);
            mapCards[id] = texture;
        }
        return texture;
    }

    // Every map as a picture; choosing one loads it at once (it shows behind the menu). Locked: log in first.
    float DrawMapCards(float x, float y, float w, float room)
    {
        var maps = MapCatalog.All;
        const float gap = 18f;
        float cw = (w - gap * (maps.Length - 1)) / maps.Length, ih = Mathf.Round(Mathf.Clamp(room - 52f, cw * 0.5f, cw * 0.78f)), ch = ih + 52f;
        for (int i = 0; i < maps.Length; i++)
        {
            var map = maps[i];
            bool locked = map.AccountOnly && !Accounts.LoggedIn, on = map.Id == gm.Map.Id;
            var slot = new Rect(x + i * (cw + gap), y, cw, ch);
            float h = Hover(slot);
            var card = new Rect(slot.x, slot.y - 5f * h, slot.width, slot.height);
            if (on) Glow(card, new Color(1f, 0.62f, 0.15f, 0.4f), 20f);
            Round(card, new Color(0.05f, 0.055f, 0.07f, 0.94f), 14f);
            var image = new Rect(card.x + 6f, card.y + 6f, card.width - 12f, ih);
            var picture = MapCard(map.Id);
            if (picture != null && Repainting) GUI.DrawTexture(image, picture, ScaleMode.ScaleAndCrop);
            Fade(new Rect(image.x, image.yMax - ih * 0.5f, image.width, ih * 0.5f), new Color(0f, 0f, 0f, 0.55f), up: true);
            if (!on && !locked) Fill(image, new Color(0f, 0f, 0f, 0.22f * (1f - h)));   // the chosen one stands out
            if (locked)
            {
                Fill(image, new Color(0f, 0f, 0f, 0.42f));
                DrawLock(image.center.x - 8f, image.center.y - 26f, LockColor);
                Label(new Rect(image.x, image.center.y + 2f, image.width, 26f), "Log in to play", 17, LockColor, TextAnchor.MiddleCenter);
            }
            Label(new Rect(card.x + 16f, image.yMax + 4f, card.width - 32f, 42f), map.Name, 22, on ? Gold : Color.white);
            if (on) Round(new Rect(card.xMax - 30f, image.yMax + 19f, 12f, 12f), Gold, 6f);
            Outline(card, on ? new Color(1f, 0.7f, 0.28f, 0.95f) : new Color(1f, 1f, 1f, 0.08f + 0.3f * h), 14f);
            if (GUI.Button(slot, GUIContent.none, GUIStyle.none))
            {
                if (locked) OpenAccount($"Log in or sign up to unlock {map.Name}");
                else if (!on) gm.SelectMap(map.Id);
            }
        }
        return y + ch;
    }

    // Who plays and how, in one panel: team and bots on the first line, the number of players on the second, the
    // rules on the third.
    float DrawMatchSettings(float x, float y, float w)
    {
        const float pad = 26f, row = 104f;
        var panel = new Rect(x, y, w, pad * 2f + row * 3f - 22f);
        Panel(panel, 18f);
        float cx = x + pad, cw = w - 2f * pad, col = (cw - 40f) / 2f, right = cx + col + 40f;
        float ry = y + pad;

        SettingTitle(cx, ry, col, "Your team");
        int side = Segmented(new Rect(cx, ry + 32f, col, 54f), new[] { "Terrorists", "SWAT", "Watch bots" }, (int)gm.Side, null,
                             i => i == 0 ? TerroristColor : i == 1 ? SwatColor : new Color(0.5f, 0.56f, 0.66f));
        if (side >= 0)
        {
            gm.Side = (PlayerSide)side;
            if (gm.Side != PlayerSide.Spectate) gm.Net.PreferredTeam = gm.Side == PlayerSide.Terrorists ? Team.Terrorists : Team.Swat;
        }
        SettingTitle(right, ry, col, "Bot difficulty");
        int level = Segmented(new Rect(right, ry + 32f, col, 54f), new[] { "Easy", "Normal", "Hard", "Extreme" }, (int)gm.Difficulty,
                              i => (BotDifficulty)i == BotDifficulty.Extreme && !Accounts.LoggedIn, DifficultyColor);
        if (level >= 0) gm.Difficulty = (BotDifficulty)level;

        ry += row;
        SettingTitle(cx, ry, cw, "Players in each team");
        float count = Mathf.Min(420f, col);
        int t = CountField(new Rect(cx, ry + 34f, count, 50f), "menu-t", "Terrorists", gm.TerroristCount);
        int s = CountField(new Rect(right, ry + 34f, count, 50f), "menu-swat", "SWAT", gm.SwatCount);
        if (t != gm.TerroristCount || s != gm.SwatCount) gm.SetTeamCounts(t, s);

        ry += row;
        SettingTitle(cx, ry, col, "Rounds to win");
        int[] rounds = { 3, 5, 8, 16 };
        int chosen = Segmented(new Rect(cx, ry + 32f, col, 54f), new[] { "3", "5", "8", "16" }, Array.IndexOf(rounds, gm.RoundsToWin));
        if (chosen >= 0) gm.RoundsToWin = rounds[chosen];
        float ff = Mathf.Round(col * 0.36f), site = col - ff - 20f;
        SettingTitle(right, ry, ff, "Friendly fire");
        int fire = Segmented(new Rect(right, ry + 32f, ff, 54f), new[] { "Off", "On" }, gm.FriendlyFire ? 1 : 0);
        if (fire >= 0) gm.FriendlyFire = fire == 1;
        float sx = right + ff + 20f;
        if (MapCatalog.Find(gm.MapId)?.OneSite == true)
        {
            SettingTitle(sx, ry, site, "Site in play");
            Label(new Rect(sx, ry + 32f, site, 54f), "One bombsite, in the middle", 18, Dim);
        }
        else
        {
            // Small matches (1v1 to 3v3) play on one site with the other half shut; bigger ones always have the whole map.
            bool counts = gm.TeamSize <= 3;
            SettingTitle(sx, ry, site, counts ? "Site in play" : "Site in play (1v1-3v3)");
            string shut = gm.CloseHalf ? gm.ClosedSite : null;
            int current = shut == "B" ? 0 : shut == "A" ? 1 : 2;
            int pick = Segmented(new Rect(sx, ry + 32f, site, 54f), new[] { "Site A", "Site B", "Whole map" }, current, null,
                                 _ => counts ? Accent : new Color(0.4f, 0.42f, 0.47f), 18);
            if (pick >= 0) gm.SetClosedSite(pick == 0 ? "B" : pick == 1 ? "A" : null);
        }
        return panel.yMax;
    }

    static Color DifficultyColor(int level) =>
        level == 0 ? new Color(0.3f, 0.72f, 0.42f) : level == 1 ? new Color(0.22f, 0.62f, 0.72f)
      : level == 2 ? Accent : new Color(0.86f, 0.24f, 0.22f);

    /// <summary>A label that always fits its box (the font shrinks, to 70% at most), in English too.</summary>
    void FitLabel(Rect rect, string text, int size, Color color, TextAnchor anchor = TextAnchor.MiddleLeft)
    {
        text = Loc.T(text);
        Text(rect, text, FitSize(text, size, rect.width), color, anchor);
    }

    /// <summary>Text that fits its box: on one line (a little smaller if need be), or else on two in a smaller font.</summary>
    void FitLines(Rect rect, string text, int size, Color color)
    {
        text = Loc.T(text);
        var content = new GUIContent(text);
        if (Style(size, TextAnchor.MiddleLeft).CalcSize(content).x * 0.85f <= rect.width)
        {
            Text(rect, text, FitSize(text, size, rect.width), color, TextAnchor.MiddleLeft);
            return;
        }
        var style = WrapStyle(Mathf.Max(12, Mathf.RoundToInt(size * 0.78f)));
        float h = style.CalcHeight(content, rect.width);
        var lines = new Rect(rect.x, rect.y + (rect.height - h) / 2f, rect.width, h);
        GUI.color = new Color(0f, 0f, 0f, 0.8f * color.a);
        GUI.Label(new Rect(lines.x + 2f, lines.y + 2f, lines.width, lines.height), text, style);
        GUI.color = color;
        GUI.Label(lines, text, style);
        GUI.color = Color.white;
    }

    void SettingTitle(float x, float y, float w, string title) => Label(new Rect(x + 2f, y, w, 28f), title, 19, new Color(1f, 1f, 1f, 0.7f));

    /// <summary>
    /// Choices side by side in one dark strip; the chosen one lights up in its colour (<paramref name="colorOf"/>, gold
    /// when none). Returns the one clicked, or -1. A locked one asks to log in instead.
    /// </summary>
    int Segmented(Rect rect, string[] options, int selected, Func<int, bool> locked = null, Func<int, Color> colorOf = null, int size = 20)
    {
        int clicked = -1;
        Round(rect, new Color(0f, 0f, 0f, 0.38f), 12f);
        Outline(rect, new Color(1f, 1f, 1f, 0.07f), 12f);
        const float gap = 4f;
        float ow = (rect.width - gap * (options.Length + 1)) / options.Length;
        for (int i = 0; i < options.Length; i++)
        {
            var r = new Rect(rect.x + gap + i * (ow + gap), rect.y + gap, ow, rect.height - 2f * gap);
            bool isLocked = locked != null && locked(i), on = i == selected;
            float h = isLocked ? 0f : Hover(r);
            Color accent = colorOf != null ? colorOf(i) : Accent;
            if (on)
            {
                Glow(r, new Color(accent.r, accent.g, accent.b, 0.3f), 10f);
                Round(r, new Color(accent.r, accent.g, accent.b, 0.95f), 9f);
                Sheen(r, 0.2f);
            }
            else Round(r, new Color(1f, 1f, 1f, 0.08f * h), 9f);
            var textRect = isLocked ? new Rect(r.x, r.y, r.width - 26f, r.height) : r;
            string text = Loc.T(options[i]);
            Text(textRect, text, FitSize(text, size, textRect.width - 12f),
                 isLocked ? new Color(1f, 1f, 1f, 0.35f) : on ? Color.white : Color.Lerp(MenuText, Color.white, h), TextAnchor.MiddleCenter);
            if (isLocked) DrawLock(r.xMax - 24f, r.y + (r.height - 20f) / 2f, LockColor);
            if (GUI.Button(r, GUIContent.none, GUIStyle.none))
            {
                if (isLocked) OpenAccount($"Log in or sign up to unlock {options[i]}");
                else clicked = i;
            }
        }
        return clicked;
    }

    // What is about to be played, and START (with a soft pulse); a message from the game takes the first line.
    void DrawStartBar(float x, float y, float w)
    {
        var bar = new Rect(x, y, w, 104f);
        Panel(bar, 18f);
        float bw = Mathf.Min(400f, w * 0.36f);
        var start = new Rect(bar.xMax - bw - 16f, bar.y + 16f, bw, bar.height - 32f);
        float tw = start.x - bar.x - 50f;
        if (!string.IsNullOrEmpty(gm.MenuNotice)) Label(new Rect(bar.x + 28f, bar.y + 14f, tw, 32f), gm.MenuNotice, 18, Danger);
        else Label(new Rect(bar.x + 28f, bar.y + 14f, tw, 32f), "READY TO PLAY", 18, Gold);
        FitLines(new Rect(bar.x + 28f, bar.y + 46f, tw, 44f), gm.MatchDescription(), 22, Color.white);

        float h = Hover(start), pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.6f);
        Glow(start, new Color(1f, 0.6f, 0.12f, 0.22f + 0.14f * pulse + 0.2f * h), 22f);
        Round(start, Color.Lerp(new Color(0.92f, 0.56f, 0.12f), new Color(1f, 0.69f, 0.26f), h), 14f);
        Sheen(start, 0.25f);
        Label(start, "START MATCH", 32, Color.white, TextAnchor.MiddleCenter);
        if (GUI.Button(start, GUIContent.none, GUIStyle.none)) gm.StartMatch();
    }

    // ----------------------------------------------------------------- controls

    // Every key as a key cap with what it does (a phone: how the touch buttons work).
    void DrawControls()
    {
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
        {
            Event.current.Use();
            controlsOpen = false;
            return;
        }
        Fill(new Rect(0f, 0f, width, RefHeight), new Color(0f, 0f, 0f, 0.5f));
        float w = Mathf.Min(1240f, width - 40f), h = 860f, x = (width - w) / 2f, y = (RefHeight - h) / 2f;
        Panel(new Rect(x, y, w, h), 22f);
        Label(new Rect(x, y + 28f, w, 50f), "CONTROLS", 40, Gold, TextAnchor.MiddleCenter);
        if (GameInput.Touch) WrapLabel(new Rect(x + 70f, y + 120f, w - 140f, h - 240f), TouchHelp, 24, Color.white);
        else
        {
            var keys = new (string Key, string Action)[]
            {
                ("W A S D", "Move"), ("Mouse", "Aim"), ("Left click", "Shoot"), ("Right click", "Scope (AWP), semi or full auto (TEC-DC9, M1911)"),
                ("R", "Reload"), ("Space", "Jump"), (GameInput.CrouchOnC ? "C" : "Ctrl / C", "Crouch"), ("Shift", "Walk quietly"),
                ("1  2  3", "Weapons"), ("Q", "Last weapon"), ("E", "Plant or defuse, take a gun"), ("G", "Drop the gun (with the knife: the bomb)"),
                ("B", "Buy (in your spawn)"), ("Tab", "Scores"), ("V", "Talk (LAN)"), ("Esc", "Pause"),
            };
            float col = (w - 140f) / 2f, top = y + 112f;
            for (int i = 0; i < keys.Length; i++)
            {
                float kx = x + 60f + (i / 8) * (col + 20f), ky = top + (i % 8) * 76f;
                var cap = new Rect(kx, ky, 172f, 54f);
                Round(new Rect(cap.x, cap.y + 5f, cap.width, cap.height), new Color(0f, 0f, 0f, 0.55f), 10f);   // its side
                Round(cap, new Color(0.2f, 0.215f, 0.25f), 10f);
                Sheen(cap, 0.08f);
                Outline(cap, new Color(1f, 1f, 1f, 0.12f), 10f);
                string key = Loc.T(keys[i].Key);
                Text(cap, key, FitSize(key, 21, cap.width - 16f), Color.white, TextAnchor.MiddleCenter);
                FitLines(new Rect(cap.xMax + 22f, ky, col - 200f, 54f), keys[i].Action, 21, MenuText);
            }
        }
        if (Button(new Rect(x + w / 2f - 160f, y + h - 92f, 320f, 62f), "BACK", false, true, 24)) controlsOpen = false;
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Menus and HUD, drawn with IMGUI so the project needs no UI assets.</summary>
public class GameUI : MonoBehaviour
{
    const float RefHeight = 1080f;   // everything is laid out for 1080p and scaled to the screen

    public static readonly Color TerroristColor = new Color(0.98f, 0.66f, 0.25f);
    public static readonly Color SwatColor = new Color(0.45f, 0.72f, 1f);
    static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.65f);
    static readonly Color Gold = new Color(1f, 0.78f, 0.35f);
    static readonly Color MoneyGreen = new Color(0.45f, 0.95f, 0.45f);
    static readonly Color Danger = new Color(1f, 0.35f, 0.3f);
    static readonly Color Dim = new Color(1f, 1f, 1f, 0.6f);

    const string ControlsHelp =
        "WASD move  ·  Mouse aim  ·  Left click shoot  ·  R reload  ·  Space jump\n" +
        "Right click: AWP scope  ·  TEC-DC9 / M1911 switch semi-auto and full-auto\n" +
        "1 / 2 / 3 weapons  ·  Q last weapon  ·  Ctrl crouch  ·  Shift walk (silent)\n" +
        "B buy menu (start of round)  ·  Tab scoreboard  ·  Esc pause";

    GameManager gm;
    Texture2D scopeMask;
    readonly Dictionary<int, GUIStyle> styles = new Dictionary<int, GUIStyle>();
    float width;
    float scale;

    public static Color TeamColor(Team team) => team == Team.Terrorists ? TerroristColor : SwatColor;

    void Awake()
    {
        gm = GetComponent<GameManager>();
        scopeMask = BuildScopeMask(512);
    }

    void OnGUI()
    {
        scale = Screen.height / RefHeight;
        width = Screen.width / scale;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

        if (gm.State == MatchState.Menu)
        {
            DrawMainMenu();
            return;
        }

        DrawNameTags();
        DrawHud();
        if (gm.BuyMenuOpen) DrawBuyMenu();
        if (gm.State == MatchState.MatchOver) DrawMatchOver();
        else if (Input.GetKey(KeyCode.Tab)) DrawScoreboard(170f);
        if (gm.IsPaused) DrawPauseMenu();
    }

    // ----------------------------------------------------------------- menus

    void DrawMainMenu()
    {
        Fill(new Rect(0f, 0f, width, RefHeight), new Color(0f, 0f, 0f, 0.3f));
        float w = 780f, x = (width - w) / 2f, y = 70f;
        Fill(new Rect(x, y, w, 940f), PanelColor);
        Label(new Rect(x, y + 20f, w, 80f), "DESERT STRIKE", 66, Gold, TextAnchor.MiddleCenter);
        Label(new Rect(x, y + 95f, w, 30f), "Terrorists vs SWAT on de_dune, a Dust-style desert map", 22, Dim, TextAnchor.MiddleCenter);
        y += 150f;

        y = OptionRow(x, y, "Your team", new[] { "Terrorists", "SWAT", "Watch bots" }, (int)gm.Side, i => gm.Side = (PlayerSide)i);
        y = OptionRow(x, y, "Players per team", new[] { "1v1", "2v2", "3v3", "4v4", "5v5" }, gm.TeamSize - 1, i => gm.TeamSize = i + 1);
        y = OptionRow(x, y, "Bot difficulty", new[] { "Easy", "Normal", "Hard" }, (int)gm.Difficulty, i => gm.Difficulty = (BotDifficulty)i);
        int[] rounds = { 3, 5, 8, 16 };
        y = OptionRow(x, y, "Rounds to win", new[] { "3", "5", "8", "16" }, Array.IndexOf(rounds, gm.RoundsToWin), i => gm.RoundsToWin = rounds[i]);

        y = SensitivitySlider(x + 40f, y, w - 80f);
        if (Button(new Rect(x + 40f, y, w - 80f, 70f), "START MATCH", true, true, 32)) gm.StartMatch();
        y += 95f;
        Label(new Rect(x + 20f, y, w - 40f, 100f), ControlsHelp, 17, Dim, TextAnchor.UpperCenter);
    }

    float OptionRow(float x, float y, string title, string[] options, int selected, Action<int> onSelect)
    {
        Label(new Rect(x + 40f, y, 700f, 34f), title, 22, Color.white);
        float buttonWidth = (700f - (options.Length - 1) * 10f) / options.Length;
        for (int i = 0; i < options.Length; i++)
            if (Button(new Rect(x + 40f + i * (buttonWidth + 10f), y + 38f, buttonWidth, 48f), options[i], i == selected))
                onSelect(i);
        return y + 105f;
    }

    float SensitivitySlider(float x, float y, float w)
    {
        Label(new Rect(x, y, w, 34f), $"Mouse sensitivity   {PlayerController.MouseSensitivity:0.0}", 22, Color.white);
        PlayerController.MouseSensitivity = GUI.HorizontalSlider(new Rect(x, y + 42f, w, 24f), PlayerController.MouseSensitivity, 0.3f, 8f);
        return y + 85f;
    }

    void DrawPauseMenu()
    {
        Fill(new Rect(0f, 0f, width, RefHeight), new Color(0f, 0f, 0f, 0.55f));
        float w = 440f, x = (width - w) / 2f, y = 260f;
        Fill(new Rect(x, y, w, 520f), PanelColor);
        Label(new Rect(x, y + 15f, w, 60f), "PAUSED", 44, Gold, TextAnchor.MiddleCenter);
        y += 90f;
        if (Button(new Rect(x + 40f, y, w - 80f, 56f), "RESUME")) gm.SetPaused(false);
        y += 70f;
        if (Button(new Rect(x + 40f, y, w - 80f, 56f), "RESTART MATCH")) gm.StartMatch();
        y += 70f;
        if (Button(new Rect(x + 40f, y, w - 80f, 56f), "MAIN MENU")) gm.ReturnToMenu();
        y += 70f;
        if (Button(new Rect(x + 40f, y, w - 80f, 56f), "QUIT GAME")) gm.QuitGame();
        y += 80f;
        SensitivitySlider(x + 40f, y, w - 80f);
    }

    void DrawMatchOver()
    {
        Fill(new Rect(0f, 0f, width, RefHeight), new Color(0f, 0f, 0f, 0.4f));
        bool terroristsWon = gm.TerroristWins > gm.SwatWins;
        Label(new Rect(0f, 50f, width, 80f), terroristsWon ? "TERRORISTS WIN THE MATCH" : "SWAT WIN THE MATCH", 60,
              terroristsWon ? TerroristColor : SwatColor, TextAnchor.MiddleCenter);
        Label(new Rect(0f, 120f, width, 40f), $"{gm.TerroristWins} : {gm.SwatWins}", 36, Color.white, TextAnchor.MiddleCenter);
        DrawScoreboard(180f);
        float center = width / 2f;
        if (Button(new Rect(center - 260f, RefHeight - 130f, 240f, 60f), "PLAY AGAIN")) gm.StartMatch();
        if (Button(new Rect(center + 20f, RefHeight - 130f, 240f, 60f), "MAIN MENU")) gm.ReturnToMenu();
    }

    void DrawBuyMenu()
    {
        var me = gm.Player.Self;
        float w = 940f, h = 560f, x = (width - w) / 2f, y = (RefHeight - h) / 2f;
        Fill(new Rect(x, y, w, h), new Color(0f, 0f, 0f, 0.85f));
        Label(new Rect(x + 30f, y + 15f, 400f, 50f), "BUY MENU", 36, Gold);
        Label(new Rect(x + w - 330f, y + 15f, 300f, 50f), $"$ {me.Money}", 36, MoneyGreen, TextAnchor.MiddleRight);
        Label(new Rect(x + 30f, y + 62f, w - 60f, 28f), $"{Mathf.Max(0f, gm.BuyTimeLeft):0}s of buy time left  ·  B / Esc to close  ·  weapons carry over if you survive", 18, Dim);

        string[] titles = { "PISTOLS", "SMG / SHOTGUN", "RIFLES & HEAVY", "EQUIPMENT" };
        WeaponData[][] columns =
        {
            new[] { WeaponData.Glock, WeaponData.Usp, WeaponData.TecDc9, WeaponData.M1911, WeaponData.Deagle },
            new[] { WeaponData.Mp5, WeaponData.Shotgun },
            new[] { WeaponData.Ak47, WeaponData.M4a1, WeaponData.Awp, WeaponData.Rpg },
        };
        float columnWidth = (w - 60f - 3 * 20f) / 4f;
        for (int c = 0; c < titles.Length; c++)
        {
            float cx = x + 30f + c * (columnWidth + 20f), cy = y + 110f;
            Label(new Rect(cx, cy, columnWidth, 30f), titles[c], 20, Dim);
            cy += 40f;
            if (c < columns.Length)
            {
                foreach (var weapon in columns[c])
                {
                    if (!weapon.AvailableTo(me.Team)) continue;
                    bool owned = me.Get(weapon.Slot)?.Data == weapon;
                    string text = $"{weapon.Name}\n{(owned ? "OWNED" : "$" + weapon.Price)}";
                    if (Button(new Rect(cx, cy, columnWidth, 80f), text, false, !owned && me.Money >= weapon.Price) && me.TryBuy(weapon))
                        SoundFX.Play(SoundFX.Buy, Vector3.zero, 0.6f, 1f, false);
                    cy += 92f;
                }
            }
            else
            {
                bool hasVest = me.Armor >= 100;
                if (Button(new Rect(cx, cy, columnWidth, 80f), $"Kevlar Vest\n{(hasVest ? "OWNED" : "$" + WeaponData.KevlarPrice)}",
                           false, !hasVest && me.Money >= WeaponData.KevlarPrice) && me.TryBuyArmor(false))
                    SoundFX.Play(SoundFX.Buy, Vector3.zero, 0.6f, 1f, false);
                cy += 92f;
                bool full = hasVest && me.Helmet;
                int helmetPrice = hasVest ? WeaponData.KevlarHelmetPrice - WeaponData.KevlarPrice : WeaponData.KevlarHelmetPrice;
                if (Button(new Rect(cx, cy, columnWidth, 80f), $"Kevlar + Helmet\n{(full ? "OWNED" : "$" + helmetPrice)}",
                           false, !full && me.Money >= helmetPrice) && me.TryBuyArmor(true))
                    SoundFX.Play(SoundFX.Buy, Vector3.zero, 0.6f, 1f, false);
            }
        }
    }

    void DrawScoreboard(float top)
    {
        const float rowHeight = 34f;
        float w = 900f, x = (width - w) / 2f, y = top;
        var me = gm.Player != null ? gm.Player.Self : null;
        Fill(new Rect(x, y, w, 2 * 70f + gm.Combatants.Count * rowHeight + 10f), new Color(0f, 0f, 0f, 0.8f));

        foreach (var team in new[] { Team.Terrorists, Team.Swat })
        {
            var members = gm.Combatants.FindAll(c => c.Team == team);
            members.Sort((a, b) => a.Kills != b.Kills ? b.Kills.CompareTo(a.Kills) : a.Deaths.CompareTo(b.Deaths));
            int wins = team == Team.Terrorists ? gm.TerroristWins : gm.SwatWins;
            Label(new Rect(x + 20f, y + 10f, 400f, 36f), $"{(team == Team.Terrorists ? "TERRORISTS" : "SWAT")}   {wins}", 28, TeamColor(team));
            Label(new Rect(x + 540f, y + 14f, 80f, 30f), "Kills", 18, Dim, TextAnchor.MiddleCenter);
            Label(new Rect(x + 640f, y + 14f, 80f, 30f), "Deaths", 18, Dim, TextAnchor.MiddleCenter);
            Label(new Rect(x + 740f, y + 14f, 140f, 30f), "Money", 18, Dim, TextAnchor.MiddleCenter);
            y += 50f;
            foreach (var c in members)
            {
                if (c.IsPlayer) Fill(new Rect(x + 10f, y, w - 20f, rowHeight), new Color(1f, 1f, 1f, 0.1f));
                Color color = c.IsAlive ? Color.white : new Color(1f, 1f, 1f, 0.4f);
                Label(new Rect(x + 20f, y, 480f, rowHeight), c.DisplayName + (c.IsAlive ? "" : "   (dead)"), 22, color);
                Label(new Rect(x + 540f, y, 80f, rowHeight), c.Kills.ToString(), 22, color, TextAnchor.MiddleCenter);
                Label(new Rect(x + 640f, y, 80f, rowHeight), c.Deaths.ToString(), 22, color, TextAnchor.MiddleCenter);
                bool showMoney = me == null || me.Team == team;
                if (showMoney) Label(new Rect(x + 740f, y, 140f, rowHeight), "$" + c.Money, 22, MoneyGreen, TextAnchor.MiddleCenter);
                y += rowHeight;
            }
            y += 20f;
        }
    }

    // ----------------------------------------------------------------- HUD

    void DrawHud()
    {
        DrawRadar();
        DrawTopBar();
        DrawKillFeed();

        var player = gm.Player;
        if (player != null && player.Self.IsAlive)
        {
            if (player.IsScoped) DrawScope();
            else DrawCrosshair(player);
            DrawHitMarker();
            DrawVitals(player.Self);
            DrawAmmo(player.Self);
            DrawMoney(player.Self);
            DrawDamageFlash();
        }
        else if (gm.Spectated != null)
        {
            var s = gm.Spectated;
            string weapon = s.Current != null ? s.Current.Data.Name : "";
            Label(new Rect(0f, RefHeight - 120f, width, 40f), $"Spectating {s.DisplayName}   ({s.Health} HP, {weapon})", 26, TeamColor(s.Team), TextAnchor.MiddleCenter);
            Label(new Rect(0f, RefHeight - 82f, width, 30f), "Left / right click to switch player", 18, Dim, TextAnchor.MiddleCenter);
        }

        if (gm.State == MatchState.Freeze)
            Label(new Rect(0f, 300f, width, 60f), $"ROUND {gm.Round}   -   GET READY  {Mathf.CeilToInt(gm.StateEndsAt - Time.time)}", 40, Color.white, TextAnchor.MiddleCenter);
        if (gm.State == MatchState.RoundEnd && gm.Banner != null)
            Label(new Rect(0f, 280f, width, 80f), gm.Banner, 56, gm.LastWinner.HasValue ? TeamColor(gm.LastWinner.Value) : Color.white, TextAnchor.MiddleCenter);
    }

    void DrawTopBar()
    {
        float w = 460f, x = (width - w) / 2f;
        Fill(new Rect(x, 10f, w, 76f), PanelColor);
        Label(new Rect(x + 15f, 14f, 150f, 44f), $"T  {gm.TerroristWins}", 34, TerroristColor);
        Label(new Rect(x + w - 165f, 14f, 150f, 44f), $"{gm.SwatWins}  SWAT", 34, SwatColor, TextAnchor.MiddleRight);

        float remaining = gm.State == MatchState.Live ? Mathf.Max(0f, gm.StateEndsAt - Time.time)
                        : gm.State == MatchState.Freeze ? GameManager.RoundTime : 0f;
        Color timerColor = gm.State == MatchState.Live && remaining < 15f ? Danger : Color.white;
        Label(new Rect(x, 12f, w, 48f), $"{(int)remaining / 60}:{(int)remaining % 60:00}", 38, timerColor, TextAnchor.MiddleCenter);

        Label(new Rect(x + 15f, 54f, 200f, 26f), $"{gm.AliveCount(Team.Terrorists)} alive", 18, Dim);
        Label(new Rect(x + w - 215f, 54f, 200f, 26f), $"{gm.AliveCount(Team.Swat)} alive", 18, Dim, TextAnchor.MiddleRight);
        Label(new Rect(x, 54f, w, 26f), $"Round {gm.Round}  ·  first to {gm.RoundsToWin}", 16, Dim, TextAnchor.MiddleCenter);
    }

    void DrawRadar()
    {
        var area = new Rect(20f, 20f, 240f, 240f);
        Fill(area, new Color(0f, 0f, 0f, 0.45f));
        GUI.DrawTexture(area, gm.Map.Radar);
        var me = gm.Player != null ? gm.Player.Self : null;
        foreach (var c in gm.Combatants)
        {
            if (!c.IsAlive || (me != null && c.Team != me.Team)) continue;
            Vector2 p = MapBuilder.ToRadar(c.transform.position);
            float size = c.IsPlayer ? 10f : 8f;
            var dot = new Rect(area.x + p.x * area.width - size / 2f, area.y + (1f - p.y) * area.height - size / 2f, size, size);
            Fill(dot, c.IsPlayer ? Color.white : TeamColor(c.Team));
        }
    }

    void DrawKillFeed()
    {
        var style = Style(20, TextAnchor.MiddleLeft);
        float y = 20f;
        foreach (var k in gm.KillFeed)
        {
            string weapon = $"  [{k.Weapon}{(k.Noscope ? " NOSCOPE" : "")}{(k.Headshot ? " + HEADSHOT" : "")}]  ";
            float killerWidth = style.CalcSize(new GUIContent(k.Killer)).x;
            float weaponWidth = style.CalcSize(new GUIContent(weapon)).x;
            float victimWidth = style.CalcSize(new GUIContent(k.Victim)).x;
            float total = killerWidth + weaponWidth + victimWidth + 20f;
            float x = width - total - 20f;
            Fill(new Rect(x, y, total, 32f), new Color(0f, 0f, 0f, 0.5f));
            x += 10f;
            Label(new Rect(x, y, killerWidth + 4f, 32f), k.Killer, 20, TeamColor(k.KillerTeam));
            x += killerWidth;
            Label(new Rect(x, y, weaponWidth + 4f, 32f), weapon, 20, Color.white);
            x += weaponWidth;
            Label(new Rect(x, y, victimWidth + 4f, 32f), k.Victim, 20, TeamColor(k.VictimTeam));
            y += 36f;
        }
    }

    void DrawNameTags()
    {
        var cam = gm.MainCamera;
        var me = gm.Player != null ? gm.Player.Self : null;
        foreach (var c in gm.Combatants)
        {
            if (!c.IsAlive || c.IsPlayer || (me != null && c.Team != me.Team)) continue;
            Vector3 screen = cam.WorldToScreenPoint(c.transform.position + Vector3.up * 2.1f);
            if (screen.z < 0.5f || screen.z > 70f) continue;
            var rect = new Rect(screen.x / scale - 100f, (Screen.height - screen.y) / scale - 14f, 200f, 28f);
            Label(rect, c.DisplayName, 16, TeamColor(c.Team), TextAnchor.MiddleCenter);
        }
    }

    void DrawCrosshair(PlayerController player)
    {
        var center = new Vector2(width / 2f, RefHeight / 2f);
        var color = new Color(0.35f, 1f, 0.35f, 0.9f);
        if (player.Self.Current.Data.IsMelee)
        {
            Fill(new Rect(center.x - 2f, center.y - 2f, 4f, 4f), color);
            return;
        }
        // Gap matches the real spread cone at a 75 degree field of view.
        float gap = 3f + Mathf.Tan(player.CurrentSpread * Mathf.Deg2Rad) * RefHeight / (2f * Mathf.Tan(37.5f * Mathf.Deg2Rad));
        const float length = 10f, thickness = 2f;
        Fill(new Rect(center.x - gap - length, center.y - thickness / 2f, length, thickness), color);
        Fill(new Rect(center.x + gap, center.y - thickness / 2f, length, thickness), color);
        Fill(new Rect(center.x - thickness / 2f, center.y - gap - length, thickness, length), color);
        Fill(new Rect(center.x - thickness / 2f, center.y + gap, thickness, length), color);
    }

    void DrawScope()
    {
        float side = RefHeight, x = (width - side) / 2f;
        GUI.DrawTexture(new Rect(x, 0f, side, side), scopeMask);
        Fill(new Rect(0f, 0f, x + 1f, RefHeight), Color.black);
        Fill(new Rect(x + side - 1f, 0f, x + 1f, RefHeight), Color.black);
        Fill(new Rect(x, RefHeight / 2f - 0.75f, side, 1.5f), Color.black);
        Fill(new Rect(width / 2f - 0.75f, 0f, 1.5f, RefHeight), Color.black);
    }

    void DrawHitMarker()
    {
        float age = Time.time - gm.HitMarkerTime;
        if (gm.HitMarkerTime < 0f || age > 0.2f) return;
        Color color = gm.HitMarkerKill ? Danger : Color.white;
        color.a = 1f - age / 0.2f;
        float size = gm.HitMarkerHeadshot ? 14f : 10f;
        var center = new Vector2(width / 2f, RefHeight / 2f);
        Matrix4x4 saved = GUI.matrix;
        for (int i = 0; i < 4; i++)
        {
            GUI.matrix = saved;
            GUIUtility.RotateAroundPivot(45f + 90f * i, center * scale);
            Fill(new Rect(center.x + 6f, center.y - 1f, size, 2f), color);
        }
        GUI.matrix = saved;
    }

    void DrawDamageFlash()
    {
        float age = Time.time - gm.DamageFlashTime;
        if (gm.DamageFlashTime < 0f || age > 0.4f) return;
        Fill(new Rect(0f, 0f, width, RefHeight), new Color(0.8f, 0f, 0f, 0.3f * (1f - age / 0.4f)));
    }

    void DrawVitals(Combatant me)
    {
        float y = RefHeight - 90f;
        Fill(new Rect(20f, y, 420f, 70f), PanelColor);
        Label(new Rect(35f, y, 60f, 70f), "HP", 22, Dim);
        Label(new Rect(80f, y, 120f, 70f), me.Health.ToString(), 44, me.Health <= 25 ? Danger : Color.white);
        Label(new Rect(200f, y, 110f, 70f), me.Helmet ? "ARMOR+H" : "ARMOR", 18, Dim);
        Label(new Rect(310f, y, 120f, 70f), me.Armor.ToString(), 44, Color.white);
    }

    void DrawAmmo(Combatant me)
    {
        var weapon = me.Current;
        float x = width - 420f, y = RefHeight - 90f;
        Fill(new Rect(x, y, 400f, 70f), PanelColor);
        if (weapon.Data.SelectFire)
        {
            Label(new Rect(x + 15f, y + 2f, 200f, 40f), weapon.Data.Name, 22, Dim);
            Label(new Rect(x + 15f, y + 38f, 200f, 26f), weapon.AutoMode ? "AUTO  (right click)" : "SEMI  (right click)", 15, Gold);
        }
        else
        {
            Label(new Rect(x + 15f, y, 200f, 70f), weapon.Data.Name, 22, Dim);
        }
        string ammo = weapon.Data.IsMelee ? "-" : $"{weapon.Mag} / {weapon.Reserve}";
        Label(new Rect(x + 150f, y, 235f, 70f), ammo, 40, !weapon.Data.IsMelee && weapon.Mag == 0 ? Danger : Color.white, TextAnchor.MiddleRight);
        if (me.IsReloading)
        {
            Fill(new Rect(x, y - 6f, 400f * me.ReloadProgress, 5f), Gold);
            Label(new Rect(x, y - 40f, 400f, 30f), "RELOADING", 18, Gold, TextAnchor.MiddleCenter);
        }
    }

    void DrawMoney(Combatant me)
    {
        Label(new Rect(20f, RefHeight - 140f, 300f, 44f), $"$ {me.Money}", 34, MoneyGreen);
        if (gm.CanBuy(me) && !gm.BuyMenuOpen)
            Label(new Rect(0f, RefHeight - 60f, width, 30f), $"Press B to buy  ({gm.BuyTimeLeft:0}s)", 20, Gold, TextAnchor.MiddleCenter);
    }

    // ----------------------------------------------------------------- drawing helpers

    GUIStyle Style(int size, TextAnchor anchor, bool bold = true)
    {
        int key = size * 100 + (int)anchor * 2 + (bold ? 1 : 0);
        if (!styles.TryGetValue(key, out var style))
        {
            style = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                alignment = anchor,
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                wordWrap = false,
                clipping = TextClipping.Overflow,
            };
            style.normal.textColor = Color.white;
            styles[key] = style;
        }
        return style;
    }

    void Label(Rect rect, string text, int size, Color color, TextAnchor anchor = TextAnchor.MiddleLeft)
    {
        var style = Style(size, anchor);
        GUI.color = new Color(0f, 0f, 0f, 0.8f * color.a);
        GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, style);
        GUI.color = color;
        GUI.Label(rect, text, style);
        GUI.color = Color.white;
    }

    static void Fill(Rect rect, Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    bool Button(Rect rect, string text, bool selected = false, bool interactable = true, int size = 22)
    {
        bool hover = rect.Contains(Event.current.mousePosition);
        Color background = !interactable ? new Color(0.2f, 0.2f, 0.2f, 0.7f)
                         : selected ? new Color(0.85f, 0.55f, 0.15f, 0.95f)
                         : hover ? new Color(0.35f, 0.35f, 0.35f, 0.9f)
                         : new Color(0.18f, 0.18f, 0.18f, 0.9f);
        Fill(rect, background);
        Label(rect, text, size, interactable ? Color.white : new Color(1f, 1f, 1f, 0.4f), TextAnchor.MiddleCenter);
        return GUI.Button(rect, GUIContent.none, GUIStyle.none) && interactable;
    }

    static Texture2D BuildScopeMask(int size)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        float radius = size / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius)) / radius;
                pixels[y * size + x] = new Color32(0, 0, 0, (byte)(Mathf.Clamp01((d - 0.96f) / 0.02f) * 255f));
            }
        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }
}

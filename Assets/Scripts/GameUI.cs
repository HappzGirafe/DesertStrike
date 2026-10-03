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
        "E plant / defuse the bomb  ·  G drop the bomb  ·  B buy (in your spawn)  ·  Tab scores  ·  Esc pause";

    GameManager gm;
    Texture2D scopeMask;
    readonly Dictionary<int, GUIStyle> styles = new Dictionary<int, GUIStyle>();
    float width;
    float scale;

    // Inventory screen
    SkinPreview preview;
    bool inventoryOpen;
    WeaponData inventoryWeapon = WeaponData.Glock;
    WeaponSkin previewSkin;   // shown instead of the equipped skin (set by -ds-inventory for screenshots)

    // LAN screen
    bool lanOpen;
    string joinAddress = "";

    // Settings screen
    bool settingsOpen;

    const string NameKey = "DesertStrike.name";

    public static Color TeamColor(Team team) => team == Team.Terrorists ? TerroristColor : SwatColor;

    void Awake()
    {
        gm = GetComponent<GameManager>();
        useGUILayout = false;   // everything is placed by hand; skips IMGUI's layout pass every frame
        scopeMask = BuildScopeMask(512);
        preview = gameObject.AddComponent<SkinPreview>();
        gm.Net.PlayerName = PlayerPrefs.GetString(NameKey, Environment.UserName);
        lanOpen = Array.IndexOf(Environment.GetCommandLineArgs(), "-ds-find") >= 0;   // smoke test: search the network
        settingsOpen = Array.IndexOf(Environment.GetCommandLineArgs(), "-ds-settings") >= 0;   // smoke-test screenshots
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-ds-showfps") >= 0) GameSettings.ShowFpsThisRun();

        // -ds-inventory [weapon id] [skin id] opens the inventory at startup (used for smoke-test screenshots).
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-ds-inventory");
        if (index >= 0)
        {
            inventoryOpen = true;
            if (index + 1 < args.Length)
                inventoryWeapon = WeaponData.Find(args[index + 1]) ?? WeaponData.Glock;
            if (index + 2 < args.Length) previewSkin = WeaponSkins.For(inventoryWeapon).Find(s => s.Id == args[index + 2]);
        }
    }

    void OnGUI()
    {
        // IMGUI runs OnGUI once for every input event as well as for drawing, and a Mac sends many mouse-move
        // events per frame. The HUD has nothing to click, so during play only the drawing pass does any work.
        bool interactive = gm.State == MatchState.Menu || gm.State == MatchState.MatchOver || gm.BuyMenuOpen || gm.IsPaused;
        if (Event.current.type != EventType.Repaint && !interactive) return;
        if (Event.current.type == EventType.Repaint) RenderScaler.Present();
        if (!settingsOpen && VoiceChat.Instance != null && VoiceChat.Instance.Testing) VoiceChat.Instance.Testing = false;

        scale = Screen.height / RefHeight;
        width = Screen.width / scale;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

        if (gm.State == MatchState.Menu)
        {
            bool searching = lanOpen && !inventoryOpen && gm.Net.Role == NetSession.Mode.Off;
            if (searching) gm.Net.StartDiscovery();
            else gm.Net.StopDiscovery();

            if (inventoryOpen) DrawInventory();
            else if (gm.Net.IsHost) DrawHostLobby();
            else if (gm.Net.IsClient) DrawClientLobby();
            else if (lanOpen) DrawLanMenu();
            else if (settingsOpen) DrawSettings();
            else DrawMainMenu();
            DrawFps(20f, 20f);
            return;
        }

        DrawNameTags();
        DrawHud();
        DrawFps(20f, 268f);
        if (gm.BuyMenuOpen) DrawBuyMenu();
        if (gm.State == MatchState.MatchOver) DrawMatchOver();
        else if (Input.GetKey(KeyCode.Tab)) DrawScoreboard(170f);
        if (!gm.IsPaused) settingsOpen = false;
        else if (settingsOpen) DrawSettings();
        else DrawPauseMenu();
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

        y = TeamRow(x, y, allowSpectate: true);
        y = MatchSettingsRows(x, y);

        if (Button(new Rect(x + 40f, y, 280f, 66f), "START MATCH", true, true, 30)) gm.StartMatch();
        if (Button(new Rect(x + 330f, y, 130f, 66f), "LAN GAME", false, true, 20)) OpenLan();
        if (Button(new Rect(x + 470f, y, 130f, 66f), "INVENTORY", false, true, 20)) inventoryOpen = true;
        if (Button(new Rect(x + 610f, y, 130f, 66f), "SETTINGS", false, true, 20)) settingsOpen = true;
        y += 82f;
        if (!string.IsNullOrEmpty(gm.MenuNotice))
        {
            Label(new Rect(x, y - 6f, w, 26f), gm.MenuNotice, 18, Danger, TextAnchor.MiddleCenter);
            y += 22f;
        }
        Label(new Rect(x + 20f, y, w - 40f, 100f), ControlsHelp, 16, Dim, TextAnchor.UpperCenter);
    }

    float TeamRow(float x, float y, bool allowSpectate)
    {
        string[] options = allowSpectate ? new[] { "Terrorists", "SWAT", "Watch bots" } : new[] { "Terrorists", "SWAT" };
        int selected = (int)gm.Side;
        if (!allowSpectate && gm.Side == PlayerSide.Spectate) selected = -1;
        return OptionRow(x, y, "Your team", options, selected, i =>
        {
            gm.Side = (PlayerSide)i;
            if (gm.Side != PlayerSide.Spectate) gm.Net.PreferredTeam = gm.Side == PlayerSide.Terrorists ? Team.Terrorists : Team.Swat;
        });
    }

    float MatchSettingsRows(float x, float y)
    {
        y = OptionRow(x, y, "Players per team", new[] { "1v1", "2v2", "3v3", "4v4", "5v5" }, gm.TeamSize - 1, i => gm.TeamSize = i + 1);
        y = OptionRow(x, y, "Bot difficulty", new[] { "Easy", "Normal", "Hard" }, (int)gm.Difficulty, i => gm.Difficulty = (BotDifficulty)i);
        int[] rounds = { 3, 5, 8, 16 };
        y = OptionRow(x, y, "Rounds to win", new[] { "3", "5", "8", "16" }, Array.IndexOf(rounds, gm.RoundsToWin), i => gm.RoundsToWin = rounds[i]);
        return OptionRow(x, y, "Friendly fire (teammates can hurt each other)", new[] { "Off", "On" }, gm.FriendlyFire ? 1 : 0, i => gm.FriendlyFire = i == 1);
    }

    void OpenLan()
    {
        lanOpen = true;
        gm.MenuNotice = null;
        if (gm.Side == PlayerSide.Spectate) gm.Side = PlayerSide.Terrorists;
        gm.Net.PreferredTeam = gm.Side == PlayerSide.Swat ? Team.Swat : Team.Terrorists;
    }

    // ----------------------------------------------------------------- LAN game screens

    void DrawLanMenu()
    {
        Fill(new Rect(0f, 0f, width, RefHeight), new Color(0f, 0f, 0f, 0.35f));
        float w = 780f, x = (width - w) / 2f, y = 70f;
        Fill(new Rect(x, y, w, 940f), PanelColor);
        Label(new Rect(x, y + 20f, w, 60f), "LAN GAME", 50, Gold, TextAnchor.MiddleCenter);
        Label(new Rect(x, y + 78f, w, 28f), "Play together with PCs on the same WiFi or network", 20, Dim, TextAnchor.MiddleCenter);
        y += 125f;

        Label(new Rect(x + 40f, y, 200f, 40f), "Your name", 22, Color.white);
        string name = GUI.TextField(new Rect(x + 220f, y + 2f, w - 260f, 36f), gm.Net.PlayerName ?? "", 16, InputStyle());
        if (name != gm.Net.PlayerName)
        {
            gm.Net.PlayerName = name;
            PlayerPrefs.SetString(NameKey, name);
        }
        y += 60f;
        y = TeamRow(x, y, allowSpectate: false);

        if (Button(new Rect(x + 40f, y, w - 80f, 60f), "HOST A GAME", true, true, 26))
        {
            gm.MenuNotice = null;
            gm.Net.StartHost();
        }
        y += 80f;

        Label(new Rect(x + 40f, y, w - 80f, 32f), "Games on your network", 22, Color.white);
        y += 40f;
        var hosts = gm.Net.FoundHosts;
        if (hosts.Count == 0)
        {
            Label(new Rect(x + 40f, y, w - 80f, 30f), "Searching...  (start HOST A GAME on the other PC)", 18, Dim);
            y += 40f;
        }
        foreach (var host in hosts)
        {
            Fill(new Rect(x + 40f, y, w - 80f, 50f), new Color(1f, 1f, 1f, 0.06f));
            string state = host.InMatch ? "match running" : "in lobby";
            Label(new Rect(x + 55f, y, w - 300f, 50f), $"{host.Name}   ({host.Players} players, {state})   {host.Address}", 20, Color.white);
            if (Button(new Rect(x + w - 200f, y + 6f, 150f, 38f), "JOIN", false, true, 20)) gm.Net.Join(host.Address);
            y += 58f;
        }

        y += 10f;
        Label(new Rect(x + 40f, y, 220f, 40f), "Or join by IP", 22, Color.white);
        joinAddress = GUI.TextField(new Rect(x + 220f, y + 2f, w - 430f, 36f), joinAddress, 40, InputStyle());
        if (Button(new Rect(x + w - 200f, y, 160f, 40f), "JOIN", false, !string.IsNullOrWhiteSpace(joinAddress), 20)) gm.Net.Join(joinAddress);
        y += 56f;

        string notice = !string.IsNullOrEmpty(gm.Net.Status) ? gm.Net.Status : gm.MenuNotice;
        if (!string.IsNullOrEmpty(notice))
        {
            Label(new Rect(x + 40f, y, w - 80f, 30f), notice, 17, Danger);
            y += 34f;
        }
        Label(new Rect(x + 40f, y, w - 80f, 30f), "Voice chat: in the match, hold V to talk. Everyone in the game hears you.", 15, Dim);
        y += 30f;
        Label(new Rect(x + 40f, y, w - 80f, 60f),
              "Windows may ask whether Desert Strike may use the network: allow it (at least on private networks).",
              15, Dim);

        if (Button(new Rect(x + w / 2f - 150f, 70f + 940f - 80f, 300f, 56f), "BACK"))
        {
            lanOpen = false;
            gm.MenuNotice = null;
        }
    }

    void DrawHostLobby()
    {
        Fill(new Rect(0f, 0f, width, RefHeight), new Color(0f, 0f, 0f, 0.35f));
        float w = 780f, x = (width - w) / 2f, y = 70f;
        Fill(new Rect(x, y, w, 940f), PanelColor);
        Label(new Rect(x, y + 20f, w, 60f), "HOSTING A LAN GAME", 44, Gold, TextAnchor.MiddleCenter);
        Label(new Rect(x, y + 75f, w, 28f), $"Other PCs find this game automatically. Your IP: {string.Join(", ", NetSession.LocalAddresses())}",
              18, Dim, TextAnchor.MiddleCenter);
        y += 120f;

        Label(new Rect(x + 40f, y, w - 80f, 32f), "Players", 22, Color.white);
        y += 36f;
        if (gm.Side != PlayerSide.Spectate)
        {
            Label(new Rect(x + 60f, y, w - 120f, 30f), $"{gm.Net.PlayerName}  ({NetSession.TeamName(gm.Net.PreferredTeam)}, you)", 20, TeamColor(gm.Net.PreferredTeam));
            y += 32f;
        }
        foreach (var peer in gm.Net.Peers)
        {
            Label(new Rect(x + 60f, y, w - 120f, 30f), $"{peer.Name}  ({NetSession.TeamName(peer.Team)})", 20, TeamColor(peer.Team));
            y += 32f;
        }
        if (gm.Net.Peers.Count == 0)
        {
            Label(new Rect(x + 60f, y, w - 120f, 30f), "Waiting for someone to join...  (bots fill the empty places)", 18, Dim);
            y += 32f;
        }
        y += 14f;

        y = TeamRow(x, y, allowSpectate: true);
        y = MatchSettingsRows(x, y);
        if (Button(new Rect(x + 40f, y, w - 80f, 66f), "START MATCH", true, true, 30)) gm.StartMatch();
        y += 80f;
        if (Button(new Rect(x + w / 2f - 150f, y, 300f, 52f), "STOP HOSTING")) gm.Net.Stop();
    }

    void DrawClientLobby()
    {
        Fill(new Rect(0f, 0f, width, RefHeight), new Color(0f, 0f, 0f, 0.35f));
        float w = 780f, h = 640f, x = (width - w) / 2f, y = (RefHeight - h) / 2f;
        Fill(new Rect(x, y, w, h), PanelColor);
        var net = gm.Net;
        Label(new Rect(x, y + 20f, w, 60f), net.Connected ? $"JOINED {net.HostName.ToUpperInvariant()}" : "CONNECTING...", 40, Gold, TextAnchor.MiddleCenter);
        Label(new Rect(x, y + 75f, w, 28f), net.Connected ? net.LobbySettings : net.Status, 18, Dim, TextAnchor.MiddleCenter);
        y += 125f;
        foreach (var line in net.LobbyPlayers)
        {
            Label(new Rect(x + 60f, y, w - 120f, 30f), line, 20, Color.white);
            y += 32f;
        }
        y += 20f;
        if (net.Connected)
            Label(new Rect(x, y, w, 30f), "Waiting for the host to start the match...", 22, Color.white, TextAnchor.MiddleCenter);
        Label(new Rect(x, y + 40f, w, 26f), "Voice chat: in the match, hold V to talk.", 17, Dim, TextAnchor.MiddleCenter);
        if (Button(new Rect(x + w / 2f - 150f, (RefHeight + h) / 2f - 80f, 300f, 56f), "LEAVE")) net.Stop();
    }

    GUIStyle InputStyle()
    {
        var style = new GUIStyle(GUI.skin.textField) { fontSize = 22, alignment = TextAnchor.MiddleLeft };
        style.padding.left = 10;
        return style;
    }

    void DrawInventory()
    {
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
        {
            CloseInventory();
            return;
        }

        Fill(new Rect(0f, 0f, width, RefHeight), new Color(0f, 0f, 0f, 0.45f));
        float w = 1240f, h = 820f, x = (width - w) / 2f, y = (RefHeight - h) / 2f;
        Fill(new Rect(x, y, w, h), PanelColor);
        Label(new Rect(x, y + 15f, w, 60f), "INVENTORY", 48, Gold, TextAnchor.MiddleCenter);
        Label(new Rect(x, y + 68f, w, 30f), "Pick a skin for each weapon. Hover to preview, click to equip. Your choice is saved.", 20, Dim, TextAnchor.MiddleCenter);

        // Weapon list on the left.
        float top = y + 115f;
        var weapons = WeaponSkins.SkinnableWeapons;
        if (Array.IndexOf(weapons, inventoryWeapon) < 0 && weapons.Length > 0) inventoryWeapon = weapons[0];
        for (int i = 0; i < weapons.Length; i++)
        {
            if (Button(new Rect(x + 30f, top + i * 48f, 210f, 42f), weapons[i].Name, weapons[i] == inventoryWeapon, true, 20))
            {
                inventoryWeapon = weapons[i];
                previewSkin = null;
            }
        }
        if (weapons.Length == 0)
        {
            Label(new Rect(x, top, w, 40f), "No skins found in Assets/Resources/Skins.", 22, Danger, TextAnchor.MiddleCenter);
            if (Button(new Rect(x + w / 2f - 150f, y + h - 80f, 300f, 56f), "BACK")) CloseInventory();
            return;
        }

        var previewRect = new Rect(x + 260f, top, 576f, 360f);
        float listX = previewRect.xMax + 20f, listWidth = x + w - 30f - listX;
        var equipped = WeaponSkins.Equipped(inventoryWeapon);

        // Skin list on the right (hovering a skin previews it).
        WeaponSkin hovered = null;
        float rowY = top;
        foreach (var skin in WeaponSkins.For(inventoryWeapon))
        {
            var row = new Rect(listX, rowY, listWidth, 56f);
            if (row.Contains(Event.current.mousePosition)) hovered = skin;
            bool isEquipped = skin == equipped;
            if (Button(row, isEquipped ? $"{skin.Name}\nEQUIPPED" : skin.Name, isEquipped, true, 20))
            {
                WeaponSkins.Equip(inventoryWeapon, skin);
                previewSkin = null;
                SoundFX.Play(SoundFX.Buy, Vector3.zero, 0.6f, 1f, false);
            }
            DrawSwatch(new Rect(row.x + 10f, row.y + 15f, 26f, 26f), WeaponSkins.MaterialFor(skin, SkinPart.Main));
            DrawSwatch(new Rect(row.x + 40f, row.y + 15f, 26f, 26f), WeaponSkins.MaterialFor(skin, SkinPart.Grip));
            rowY += 64f;
        }

        var shown = previewSkin ?? hovered ?? equipped;
        preview.Show(inventoryWeapon, shown);
        GUI.DrawTexture(previewRect, preview.Texture);
        Label(new Rect(previewRect.x, previewRect.yMax + 10f, previewRect.width, 36f), $"{inventoryWeapon.Name}   |   {shown.Name}", 26, Color.white, TextAnchor.MiddleCenter);
        Label(new Rect(previewRect.x, previewRect.yMax + 50f, previewRect.width, 60f),
              "Add your own: make a folder in Assets/Resources/Skins/<weapon>/<skin name>/\nwith main.png, grip.png, detail.png (and model.fbx or skin.json if you like)",
              15, Dim, TextAnchor.UpperCenter);

        if (Button(new Rect(x + w / 2f - 150f, y + h - 80f, 300f, 56f), "BACK")) CloseInventory();
    }

    void CloseInventory()
    {
        inventoryOpen = false;
        previewSkin = null;
        preview.Hide();
    }

    /// <summary>Small square showing a skin part; an empty dark square means the part keeps its normal look.</summary>
    static void DrawSwatch(Rect rect, Material material)
    {
        Fill(new Rect(rect.x - 2f, rect.y - 2f, rect.width + 4f, rect.height + 4f), new Color(0f, 0f, 0f, 0.6f));
        if (material == null) Fill(rect, new Color(0.25f, 0.25f, 0.27f));
        else if (material.mainTexture != null)
        {
            GUI.color = material.color;
            GUI.DrawTexture(rect, material.mainTexture, ScaleMode.ScaleAndCrop);
            GUI.color = Color.white;
        }
        else Fill(rect, material.color);
    }

    float OptionRow(float x, float y, string title, string[] options, int selected, Action<int> onSelect, float rowWidth = 700f)
    {
        Label(new Rect(x + 40f, y, rowWidth, 34f), title, 22, Color.white);
        float buttonWidth = (rowWidth - (options.Length - 1) * 10f) / options.Length;
        for (int i = 0; i < options.Length; i++)
            if (Button(new Rect(x + 40f + i * (buttonWidth + 10f), y + 38f, buttonWidth, 48f), options[i], i == selected))
                onSelect(i);
        return y + 105f;
    }

    float SensitivitySlider(float x, float y, float w)
    {
        Label(new Rect(x, y, w, 34f), $"Mouse sensitivity   {PlayerController.MouseSensitivity:0.0}", 22, Color.white);
        float sensitivity = GUI.HorizontalSlider(new Rect(x, y + 42f, w, 24f), PlayerController.MouseSensitivity, 0.3f, 8f);
        if (!Mathf.Approximately(sensitivity, PlayerController.MouseSensitivity)) GameSettings.SetSensitivity(sensitivity);
        return y + 85f;
    }

    // ----------------------------------------------------------------- settings

    static readonly string[] QualityHelp =
    {
        "Low: no shadows, no lights, half-size textures. Coolest and quietest for laptops.",
        "Medium: simple shadows close by, no extra lights. Recommended for MacBook Air.",
        "High: soft shadows far away, gun-flash lights, smoothed edges.",
    };

    static readonly string[] VoiceModeHelp =
    {
        "You are heard only while you hold V.",
        "You are heard whenever you speak, no key needed (use headphones, or the others hear themselves).",
    };

    // Two columns: display and graphics on the left, voice and controls on the right.
    void DrawSettings()
    {
        Fill(new Rect(0f, 0f, width, RefHeight), new Color(0f, 0f, 0f, 0.45f));
        const float top = 90f, height = 820f;
        float w = Mathf.Min(1480f, width - 40f), x = (width - w) / 2f;
        float column = (w - 120f) / 2f, right = x + column + 40f;
        Fill(new Rect(x, top, w, height), PanelColor);
        Label(new Rect(x, top + 20f, w, 60f), "SETTINGS", 50, Gold, TextAnchor.MiddleCenter);
        Label(new Rect(x + 40f, top + 105f, column, 30f), "DISPLAY & GRAPHICS", 18, Gold);
        Label(new Rect(right + 40f, top + 105f, column, 30f), "VOICE & CONTROLS", 18, Gold);

        float y = top + 145f;
        y = OptionRow(x, y, "Display", new[] { "Fullscreen", "Window" }, GameSettings.Fullscreen ? 0 : 1,
                      i => GameSettings.SetFullscreen(i == 0), column);
        y = OptionRow(x, y, "Graphics", new[] { "Low", "Medium", "High" }, (int)GameSettings.Quality,
                      i => GameSettings.SetQuality((GraphicsQuality)i), column);
        WrapLabel(new Rect(x + 40f, y - 14f, column, 26f), QualityHelp[(int)GameSettings.Quality], 16, Dim);
        y += 24f;
        y = OptionRow(x, y, "Frame limit (lower = cooler and longer battery)", new[] { "30 FPS", "60 FPS", "120 FPS", "Unlimited" },
                      Array.IndexOf(GameSettings.FrameLimits, GameSettings.FrameLimit), i => GameSettings.SetFrameLimit(GameSettings.FrameLimits[i]), column);
        y = OptionRow(x, y, "3D resolution (lower = faster; menus stay sharp)", new[] { "100%", "75%", "50%" },
                      Array.IndexOf(GameSettings.RenderScales, GameSettings.RenderScale), i => GameSettings.SetRenderScale(GameSettings.RenderScales[i]), column);
        OptionRow(x, y, "Show FPS counter", new[] { "Off", "On" }, GameSettings.ShowFps ? 1 : 0, i => GameSettings.SetShowFps(i == 1), column);

        y = top + 145f;
        y = OptionRow(right, y, "Voice chat in LAN games", new[] { "Off", "On" }, GameSettings.VoiceChat ? 1 : 0,
                      i => GameSettings.SetVoiceChat(i == 1), column);
        y = OptionRow(right, y, "When the others hear you", new[] { "Hold V", "Open mic" }, (int)GameSettings.Voice,
                      i => GameSettings.SetVoiceMode((VoiceMode)i), column);
        WrapLabel(new Rect(right + 40f, y - 14f, column, 26f), VoiceModeHelp[(int)GameSettings.Voice], 16, Dim);
        y += 24f;
        y = MicrophoneTest(right, y, column);
        SensitivitySlider(right + 40f, y, column);

        if (Button(new Rect(x + w / 2f - 150f, top + height - 80f, 300f, 56f), "BACK")) settingsOpen = false;
    }

    // Shows whether the game hears the microphone, without a second computer: a level bar that moves when you
    // speak, the microphone's name, or what is wrong and how to fix it.
    float MicrophoneTest(float x, float y, float rowWidth)
    {
        var voice = VoiceChat.Instance;
        if (voice == null) return y;
        y -= 12f;
        if (Button(new Rect(x + 40f, y, 250f, 44f), voice.Testing ? "STOP TEST" : "TEST MICROPHONE", voice.Testing, true, 18))
            voice.Testing = !voice.Testing;
        LevelBar(new Rect(x + 310f, y + 15f, rowWidth - 270f, 14f), voice.Testing && voice.State == VoiceChat.MicState.On ? voice.Level : 0f);
        string status;
        bool problem = voice.Testing && voice.Problem != null;
        if (!voice.Testing) status = "Check that the game hears you: press the button and speak.";
        else if (problem) status = voice.Problem;
        else if (voice.State != VoiceChat.MicState.On) status = "Starting the microphone...";
        else status = voice.DeviceName + ": speak, the bar should move.";
        WrapLabel(new Rect(x + 40f, y + 48f, rowWidth, 44f), status, 15, problem ? Danger : Dim);
        return y + 104f;
    }

    // FPS, then how many milliseconds the CPU and the graphics chip (GPU) spend on a frame: when one of them is
    // close to 1000 / FPS, that one is what holds the frame rate back.
    void DrawFps(float x, float y)
    {
        if (!GameSettings.ShowFps) return;
        float fps = PerfStats.Fps;
        Fill(new Rect(x, y, 420f, 74f), PanelColor);
        Label(new Rect(x + 10f, y + 2f, 110f, 30f), $"{Mathf.RoundToInt(fps)} FPS", 22,
              fps >= (GameSettings.FrameLimit > 0 ? GameSettings.FrameLimit : 60) * 0.9f ? MoneyGreen : fps >= 30f ? Gold : Danger);
        string gpu = PerfStats.GpuMs > 0f ? $"{PerfStats.GpuMs:0.0} ms" : "n/a";
        Label(new Rect(x + 120f, y + 2f, 300f, 30f), $"CPU {PerfStats.CpuMs:0.0} ms    GPU {gpu}", 18, Color.white);
        Label(new Rect(x + 10f, y + 36f, 405f, 30f), PerfStats.Device, 15, Dim);
    }

    void DrawPauseMenu()
    {
        Fill(new Rect(0f, 0f, width, RefHeight), new Color(0f, 0f, 0f, 0.55f));
        float w = 440f, x = (width - w) / 2f, y = 260f;
        Fill(new Rect(x, y, w, 520f), PanelColor);
        bool online = gm.Net.Role != NetSession.Mode.Off;
        Label(new Rect(x, y + 15f, w, 60f), online ? "MENU" : "PAUSED", 44, Gold, TextAnchor.MiddleCenter);
        if (online) Label(new Rect(x, y + 62f, w, 24f), "The LAN game keeps running", 16, Dim, TextAnchor.MiddleCenter);
        y += 90f;
        if (Button(new Rect(x + 40f, y, w - 80f, 56f), "RESUME")) gm.SetPaused(false);
        y += 70f;
        if (gm.IsClient)
        {
            if (Button(new Rect(x + 40f, y, w - 80f, 56f), "LEAVE GAME")) gm.ReturnToMenu();
            y += 70f;
        }
        else
        {
            if (Button(new Rect(x + 40f, y, w - 80f, 56f), "RESTART MATCH")) gm.StartMatch();
            y += 70f;
        }
        if (Button(new Rect(x + 40f, y, w - 80f, 56f), online && !gm.IsClient ? "END LAN GAME" : "MAIN MENU")) gm.ReturnToMenu();
        y += 70f;
        if (Button(new Rect(x + 40f, y, w - 80f, 56f), "SETTINGS")) settingsOpen = true;
        y += 70f;
        if (Button(new Rect(x + 40f, y, w - 80f, 56f), "QUIT GAME")) gm.QuitGame();
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
        if (gm.IsClient)
        {
            Label(new Rect(0f, RefHeight - 190f, width, 40f), "Waiting for the host to play again...", 22, Dim, TextAnchor.MiddleCenter);
            if (Button(new Rect(center - 120f, RefHeight - 130f, 240f, 60f), "LEAVE GAME")) gm.ReturnToMenu();
            return;
        }
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
                    if (Button(new Rect(cx, cy, columnWidth, 80f), text, false, !owned && me.Money >= weapon.Price))
                        gm.BuyWeapon(weapon);
                    cy += 92f;
                }
            }
            else
            {
                bool hasVest = me.Armor >= 100;
                if (Button(new Rect(cx, cy, columnWidth, 80f), $"Kevlar Vest\n{(hasVest ? "OWNED" : "$" + WeaponData.KevlarPrice)}",
                           false, !hasVest && me.Money >= WeaponData.KevlarPrice))
                    gm.BuyArmor(false);
                cy += 92f;
                bool full = hasVest && me.Helmet;
                int helmetPrice = hasVest ? WeaponData.KevlarHelmetPrice - WeaponData.KevlarPrice : WeaponData.KevlarHelmetPrice;
                if (Button(new Rect(cx, cy, columnWidth, 80f), $"Kevlar + Helmet\n{(full ? "OWNED" : "$" + helmetPrice)}",
                           false, !full && me.Money >= helmetPrice))
                    gm.BuyArmor(true);
                cy += 92f;
                if (me.Team == Team.Swat
                    && Button(new Rect(cx, cy, columnWidth, 80f), $"Defuse Kit\n{(me.HasDefuseKit ? "OWNED" : "$" + WeaponData.DefuseKitPrice)}",
                              false, !me.HasDefuseKit && me.Money >= WeaponData.DefuseKitPrice))
                    gm.BuyDefuseKit();
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
        DrawVoice();

        var player = gm.Player;
        if (player != null && player.Self.IsAlive)
        {
            if (player.IsScoped) DrawScope();
            else DrawCrosshair(player);
            DrawHitMarker();
            DrawVitals(player.Self);
            DrawAmmo(player.Self);
            DrawMoney(player.Self);
            DrawBombHints(player.Self);
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
        {
            Label(new Rect(0f, 300f, width, 60f), $"ROUND {gm.Round}   -   GET READY  {Mathf.CeilToInt(gm.StateEndsAt - Time.time)}", 40, Color.white, TextAnchor.MiddleCenter);
            if (!string.IsNullOrEmpty(gm.PlayerAmmoResupply))
                Label(new Rect(0f, 360f, width, 36f), $"Free ammo:  {gm.PlayerAmmoResupply}", 24, Gold, TextAnchor.MiddleCenter);
        }
        if (gm.State == MatchState.RoundEnd && gm.Banner != null)
            Label(new Rect(0f, 280f, width, 80f), gm.Banner, 56, gm.LastWinner.HasValue ? TeamColor(gm.LastWinner.Value) : Color.white, TextAnchor.MiddleCenter);
        else if (gm.Message != null && Time.time < gm.MessageUntil)
            Label(new Rect(0f, 220f, width, 60f), gm.Message, 42, Danger, TextAnchor.MiddleCenter);
    }

    // Voice chat, under the radar. In a LAN match it always shows the microphone: "Hold V to talk", "You" with the
    // level while V is held, "Open mic" with the level, or what is wrong with the microphone. Then everyone who can
    // be heard right now. Outside LAN games it shows "You" only while V is held (to check the microphone).
    void DrawVoice()
    {
        var voice = VoiceChat.Instance;
        if (voice == null || !GameSettings.VoiceChat || (!voice.InMatch && !voice.Holding)) return;
        float y = GameSettings.ShowFps ? 352f : 272f;
        bool openMic = voice.InMatch && GameSettings.Voice == VoiceMode.OpenMic;
        bool micWanted = voice.Holding || openMic;
        if (micWanted && voice.Problem != null)
        {
            Fill(new Rect(20f, y, 600f, 62f), PanelColor);
            WrapLabel(new Rect(30f, y + 4f, 580f, 56f), voice.Problem, 16, Danger);
            y += 66f;
        }
        else if (micWanted && voice.State != VoiceChat.MicState.On)
        {
            VoiceRow(y, "Starting the microphone...", Dim);
            y += 34f;
        }
        else if (voice.Holding || voice.Talking)
        {
            VoiceRow(y, "You", MoneyGreen, voice.Level);
            if (!voice.InSession) Label(new Rect(272f, y, 420f, 30f), "Voice chat works in LAN games", 16, Dim);
            y += 34f;
        }
        else if (openMic)
        {
            VoiceRow(y, "Open mic", Dim, voice.Level);
            y += 34f;
        }
        else
        {
            VoiceRow(y, "Hold V to talk", Dim);
            y += 34f;
        }
        foreach (string name in voice.Speaking())
        {
            VoiceRow(y, name, SpeakerColor(name));
            y += 34f;
        }
    }

    void VoiceRow(float y, string name, Color color, float level = -1f)
    {
        Fill(new Rect(20f, y, 240f, 30f), PanelColor);
        Fill(new Rect(30f, y + 9f, 12f, 12f), color);   // a small "talking" light
        Label(new Rect(52f, y, 205f, 30f), name, 18, color);
        if (level >= 0f) LevelBar(new Rect(150f, y + 11f, 100f, 8f), level);
    }

    // How loud the microphone is (square root, so quiet speech still shows).
    void LevelBar(Rect rect, float level)
    {
        Fill(rect, new Color(0f, 0f, 0f, 0.5f));
        Fill(new Rect(rect.x, rect.y, rect.width * Mathf.Sqrt(Mathf.Clamp01(level)), rect.height), MoneyGreen);
    }

    Color SpeakerColor(string name)
    {
        foreach (var c in gm.Combatants)
            if (c.DisplayName == name) return TeamColor(c.Team);
        return Color.white;
    }

    void DrawBombHints(Combatant me)
    {
        var bomb = gm.Bomb;
        if (bomb.User == me && bomb.UseProgress > 0f)
        {
            bool planting = bomb.State == BombState.Carried;
            float w = 420f, x = (width - w) / 2f, y = RefHeight * 0.62f;
            Label(new Rect(0f, y - 38f, width, 32f), planting ? "PLANTING THE BOMB..." : "DEFUSING THE BOMB...", 24, Gold, TextAnchor.MiddleCenter);
            Fill(new Rect(x, y, w, 14f), new Color(0f, 0f, 0f, 0.6f));
            Fill(new Rect(x, y, w * Mathf.Clamp01(bomb.UseProgress), 14f), planting ? TerroristColor : SwatColor);
            return;
        }
        if (gm.State != MatchState.Live) return;

        string hint = null;
        Color color = Color.white;
        if (bomb.State == BombState.Carried && bomb.Carrier == me)
        {
            string site = gm.Map.SiteAt(me.transform.position);
            hint = site != null ? $"Hold E to plant the bomb on {site}" : "You have the bomb: plant it on site A or B   (G to drop it)";
            color = TerroristColor;
        }
        else if (bomb.State == BombState.Planted && me.Team == Team.Swat
                 && Vector3.Distance(me.transform.position, bomb.Position) < BombManager.DefuseRadius)
        {
            hint = me.HasDefuseKit ? "Hold E to defuse (5 s with your kit)" : "Hold E to defuse (10 s; a defuse kit makes it 5 s)";
            color = SwatColor;
        }
        if (hint != null) Label(new Rect(0f, RefHeight - 180f, width, 30f), hint, 20, color, TextAnchor.MiddleCenter);
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
        string clock = $"{(int)remaining / 60}:{(int)remaining % 60:00}";
        if (gm.Bomb.State == BombState.Planted && gm.State == MatchState.Live)
        {
            // The round clock stops once the bomb is planted; show the bomb's instead.
            float fuse = gm.Bomb.TimeLeft;
            clock = $"C4  {(int)fuse / 60}:{(int)fuse % 60:00}";
            timerColor = Time.time % 1f < 0.5f ? Danger : Color.white;
        }
        Label(new Rect(x, 12f, w, 48f), clock, 38, timerColor, TextAnchor.MiddleCenter);

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
        var bomb = gm.Bomb;
        bool terroristView = me == null || me.Team == Team.Terrorists;
        foreach (var c in gm.Combatants)
        {
            if (!c.IsAlive || (me != null && c.Team != me.Team)) continue;
            bool carrier = terroristView && bomb.State == BombState.Carried && bomb.Carrier == c;
            float size = c.IsPlayer || carrier ? 10f : 8f;
            Fill(RadarDot(area, c.transform.position, size), carrier ? Danger : c.IsPlayer ? Color.white : TeamColor(c.Team));
        }

        // The planted bomb is shown to everyone; a dropped one only to the Terrorists.
        bool showBomb = bomb.State == BombState.Planted || (bomb.State == BombState.Dropped && terroristView);
        if (showBomb && (bomb.State != BombState.Planted || Time.time % 0.6f < 0.4f))
        {
            Fill(RadarDot(area, bomb.Position, 12f), Color.black);
            Fill(RadarDot(area, bomb.Position, 9f), Danger);
        }
    }

    static Rect RadarDot(Rect area, Vector3 world, float size)
    {
        Vector2 p = MapBuilder.ToRadar(world);
        return new Rect(area.x + p.x * area.width - size / 2f, area.y + (1f - p.y) * area.height - size / 2f, size, size);
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
            // Viewport coordinates, because the camera may draw to a smaller texture than the screen (RenderScaler).
            Vector3 view = cam.WorldToViewportPoint(c.transform.position + Vector3.up * 2.1f);
            if (view.z < 0.5f || view.z > 70f) continue;
            var rect = new Rect(view.x * width - 100f, (1f - view.y) * RefHeight - 14f, 200f, 28f);
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

    void WrapLabel(Rect rect, string text, int size, Color color)
    {
        int key = 1000000 + size;
        if (!styles.TryGetValue(key, out var style))
        {
            style = new GUIStyle(Style(size, TextAnchor.UpperLeft)) { wordWrap = true, clipping = TextClipping.Clip };
            styles[key] = style;
        }
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

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Menus and HUD, drawn with IMGUI so the project needs no UI assets.</summary>
public partial class GameUI : MonoBehaviour
{
    const float BaseHeight = 1080f;   // everything is laid out for 1080p and scaled to the screen
    // The height the screen counts as: 1080, or less in a match on a phone with a bigger interface (Settings), so the
    // match's screen text grows and its corners stay in the corners.
    float RefHeight = BaseHeight;

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
        "1 / 2 / 3 weapons  ·  Q last weapon  ·  Ctrl crouch  ·  Shift walk (silent)  ·  V talk (LAN)\n" +
        "E plant / defuse, take a gun  ·  G drop the gun (knife in hand: the bomb)  ·  B buy (in your spawn)  ·  Tab scores  ·  Esc pause";

    const string TouchHelp =
        "Left thumb: move (a small push walks quietly)  ·  Right side: drag to look around\n" +
        "FIRE (drag it to aim while shooting)  ·  JUMP  ·  CROUCH on / off  ·  R reload  ·  GUN next weapon\n" +
        "SCOPE (AWP) or AUTO (TEC-DC9, M1911)  ·  PLANT / DEFUSE (hold)  ·  BUY in your spawn  ·  TAB scores  ·  II pause\n" +
        "Move or resize the buttons: Settings > TOUCH CONTROLS";

    GameManager gm;
    Texture2D scopeMask, svdScopeMask;
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

    // Settings screen, and its crosshair page
    bool settingsOpen;
    bool crosshairOpen;
    bool touchLayoutOpen;   // Settings > TOUCH CONTROLS: moving the phone's buttons
    bool languagesOpen;     // Settings > LANGUAGES
    TouchControls touch;

    // Friends screen
    bool friendsOpen;
    string friendName = "";
    string friendMessage;
    bool friendMessageBad;
    string removeConfirm;        // the friend whose REMOVE was clicked once (a second click removes)
    Vector2 friendsScroll;

    // Log-in / sign-up screen: asked at every start (tests skip it unless -ds-account-screen), and opened by
    // clicking something that needs an account.
    bool askAccount;
    bool accountOpen;
    bool accountSignUp;
    string accountName;
    string accountPassword = "", accountRepeat = "";
    string accountMessage;
    bool accountFocus = true;

    const string NameKey = "DesertStrike.name";

    public static Color TeamColor(Team team) => team == Team.Terrorists ? TerroristColor : SwatColor;

    void Awake()
    {
        gm = GetComponent<GameManager>();
        useGUILayout = false;   // everything is placed by hand; skips IMGUI's layout pass every frame
        scopeMask = BuildScopeMask(512);
        svdScopeMask = BuildScopeMask(512, 0.86f);
        preview = gameObject.AddComponent<SkinPreview>();
        gm.Net.PlayerName = PlayerPrefs.GetString(NameKey, Environment.UserName);
        Accounts.Changed += () => gm.Net.PlayerName = Accounts.LoggedIn ? Accounts.Current : PlayerPrefs.GetString(NameKey, Environment.UserName);
        var commandLine = Environment.GetCommandLineArgs();
        int accountArg = Array.IndexOf(commandLine, "-ds-account-screen");   // [signup]: screenshots of it
        askAccount = accountArg >= 0 || !Array.Exists(commandLine, a => a.StartsWith("-ds-"));
        accountSignUp = accountArg >= 0 && accountArg + 1 < commandLine.Length && commandLine[accountArg + 1] == "signup";
        lanOpen = Array.IndexOf(Environment.GetCommandLineArgs(), "-ds-find") >= 0;   // smoke test: search the network
        settingsOpen = Array.IndexOf(Environment.GetCommandLineArgs(), "-ds-settings") >= 0;   // smoke-test screenshots
        friendsOpen = Array.IndexOf(Environment.GetCommandLineArgs(), "-ds-friends") >= 0;
        crosshairOpen = Array.IndexOf(Environment.GetCommandLineArgs(), "-ds-crosshair") >= 0;   // screenshots
        if (crosshairOpen) settingsOpen = true;
        touchLayoutOpen = Array.IndexOf(Environment.GetCommandLineArgs(), "-ds-touch-layout") >= 0;   // screenshots
        languagesOpen = Array.IndexOf(Environment.GetCommandLineArgs(), "-ds-languages") >= 0;   // screenshots
        if (languagesOpen) settingsOpen = true;
        int demo = Array.IndexOf(Environment.GetCommandLineArgs(), "-ds-keyboard-demo");
        if (demo >= 0 && demo + 2 < Environment.GetCommandLineArgs().Length)
        {
            string[] demoArgs = Environment.GetCommandLineArgs();
            keyboardDemo.Enqueue("@account-name");
            foreach (char c in demoArgs[demo + 1])
            {
                if (char.IsUpper(c)) keyboardDemo.Enqueue("SHIFT");
                keyboardDemo.Enqueue(char.ToLowerInvariant(c).ToString());
            }
            keyboardDemo.Enqueue("DONE");
            foreach (char c in demoArgs[demo + 2]) keyboardDemo.Enqueue(c.ToString());
            keyboardDemo.Enqueue("DONE");
            keyboardDemoFrame = 60;
        }
        if (touchLayoutOpen) settingsOpen = true;
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-ds-crosshair-demo") >= 0) GameSettings.CrosshairDemoThisRun();
        touch = GetComponent<TouchControls>();
        gm.Friends.Noticed += text =>
        {
            friendMessage = text;
            friendMessageBad = false;
        };
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-ds-showfps") >= 0) GameSettings.ShowFpsThisRun();
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-ds-hidefps") >= 0) GameSettings.HideFpsThisRun();   // clean screenshots

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
        bool interactive = gm.State == MatchState.Menu || gm.State == MatchState.MatchOver || gm.BuyMenuOpen || gm.IsPaused
                           || gm.Tutorial?.Current == Tutorial.Step.Done;
        if (Event.current.type != EventType.Repaint && !interactive) return;
        if (Event.current.type == EventType.Repaint) RenderScaler.Present();
        if (gm.ShowingViews) return;
        if (!settingsOpen && VoiceChat.Instance != null && VoiceChat.Instance.Testing) VoiceChat.Instance.Testing = false;

        UseHeight(BaseHeight);

        if (gm.State == MatchState.Menu)
        {
            if (askAccount && !Online.Restoring)
            {
                askAccount = false;
                accountOpen = !Accounts.LoggedIn;   // -ds-login (or the saved online session) has logged in already
                accountOnline = Online.ApiUrl != null;
            }
            Online.WantRooms = Online.WantFriends = false;   // the screens drawn below ask for what they show
            OpenTestScreen();
            HandleGameKeyboard();
            bool searching = lanOpen && !inventoryOpen && !accountOpen && gm.Net.Role == NetSession.Mode.Off;
            if (searching) gm.Net.StartDiscovery();
            else gm.Net.StopDiscovery();

            bool updateQuestion = Online.NewVersion != null && updateStep < 2;
            if (updateQuestion) DrawUpdateQuestion();
            else if (accountOpen) DrawAccount();
            else if (onlineAccountOpen) DrawOnlineAccount();
            else if (adminOpen) DrawAdmin();
            else if (inventoryOpen) DrawInventory();
            else if (gm.Net.IsHost) DrawHostLobby();
            else if (Online.Room != null || (gm.Net.IsClient && gm.Net.OnlineClient)) DrawOnlineRoom();
            else if (gm.Net.IsClient) DrawClientLobby();
            else if (onlineOpen) DrawOnline();
            else if (lanOpen) DrawLanMenu();
            else if (friendsOpen && Accounts.IsOnline && !friendsLan) DrawOnlineFriends();
            else if (friendsOpen) DrawFriends();
            else if (settingsOpen && languagesOpen) DrawLanguages();
            else if (settingsOpen && crosshairOpen) DrawCrosshairSettings();
            else if (settingsOpen && touchLayoutOpen) DrawTouchLayout();
            else if (settingsOpen) DrawSettings();
            else DrawMainMenu();
            if (Online.AskLink && !accountOpen && !updateQuestion) DrawLinkQuestion();
            else if (tutorialOffer && !accountOpen && !updateQuestion) DrawTutorialOffer();
            DrawTokenToast();
            DrawGameKeyboard();
            DrawFps(20f, 20f);
            return;
        }

        if (GameInput.Touch) UseHeight(BaseHeight / GameSettings.HudScale);
        DrawNameTags();
        DrawHud();
        DrawFps(20f, 268f);
        UseHeight(BaseHeight);   // menus and the phone's buttons keep their own size
        if (gm.BuyMenuOpen) DrawBuyMenu();
        if (gm.State == MatchState.MatchOver && gm.Tutorial == null) DrawMatchOver();
        else if (GameInput.ScoresHeld) DrawScoreboard(170f);
        if (gm.Tutorial != null && !gm.IsPaused) DrawTutorial();
        DrawTokenToast();
        if (!gm.IsPaused && gm.State != MatchState.MatchOver && touch != null) touch.Draw();
        if (!gm.IsPaused) settingsOpen = crosshairOpen = touchLayoutOpen = languagesOpen = false;
        else if (settingsOpen && languagesOpen) DrawLanguages();
        else if (settingsOpen && crosshairOpen) DrawCrosshairSettings();
        else if (settingsOpen && touchLayoutOpen) DrawTouchLayout();
        else if (settingsOpen) DrawSettings();
        else DrawPauseMenu();
    }

    // ----------------------------------------------------------------- menus

    // Two columns: who plays on the left, where and how on the right; the buttons and controls below.
    void DrawMainMenu()
    {
        Fill(new Rect(0f, 0f, width, RefHeight), new Color(0f, 0f, 0f, 0.3f));
        const float top = 70f;
        float w = Mathf.Min(1480f, width - 40f), x = (width - w) / 2f;
        float column = (w - 120f) / 2f, right = x + column + 40f;
        Fill(new Rect(x, top, w, 720f), PanelColor);
        Label(new Rect(x, top + 20f, w, 80f), "LOW STRIKE", 66, Gold, TextAnchor.MiddleCenter);
        Label(new Rect(x, top + 95f, w, 30f), $"Terrorists vs SWAT on {gm.Map.Description}", 22, Dim, TextAnchor.MiddleCenter);
        DrawAccountBox(x + 24f, top + 22f);
        if (Accounts.IsOnline && Online.Tokens >= 0)   // between the title and EXIT
            Label(new Rect(x + w - 164f - 16f - 330f, top + 30f, 330f, 30f), $"Halloween Tokens: {Online.Tokens}", 20, Pumpkin, TextAnchor.MiddleRight);
        // iPhones close apps from the app switcher only (Apple's rules); everywhere else the game can quit itself.
        if (Application.platform != RuntimePlatform.IPhonePlayer && Button(new Rect(x + w - 164f, top + 22f, 140f, 46f), "EXIT", false, true, 20))
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
        if (Button(new Rect(bx, y, small, 66f), "LAN GAME", false, true, 21)) OpenLan();
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
        Label(new Rect(x + 20f, y, w - 40f, 100f), GameInput.Touch ? TouchHelp : ControlsHelp, 16, Dim, TextAnchor.UpperCenter);
    }

    // A newer Low Strike is on the website: asked at every start. YES, NOW opens the download page; LATER warns that
    // online play may not work, and only I AGREE closes it (NO goes back to the question).
    int updateStep;   // 0 the question, 1 the warning, 2 answered (this run)

    void DrawUpdateQuestion()
    {
        Fill(new Rect(0f, 0f, width, RefHeight), new Color(0f, 0f, 0f, 0.6f));
        float w = Mathf.Min(900f, width - 40f), h = 400f, x = (width - w) / 2f, y = (RefHeight - h) / 2f;
        Fill(new Rect(x, y, w, h), new Color(0.05f, 0.05f, 0.06f, 0.96f));
        float half = (w - 120f) / 2f, by = y + h - 110f;
        if (updateStep == 0)
        {
            Label(new Rect(x, y + 24f, w, 50f), "NEW UPDATE", 38, Gold, TextAnchor.MiddleCenter);
            Label(new Rect(x + 40f, y + 110f, w - 80f, 40f), $"Low Strike {Online.NewVersion}", 26, Color.white, TextAnchor.MiddleCenter);
            Label(new Rect(x + 40f, y + 160f, w - 80f, 60f), "A new update is out. Do you want to update now?", 24, Color.white, TextAnchor.MiddleCenter);
            if (Button(new Rect(x + 50f, by, half, 64f), "YES, NOW", true, true, 24))
            {
                Debug.Log("[DesertStrike] Update: opening " + Online.DownloadUrl);
                Application.OpenURL(Online.DownloadUrl);
                updateStep = 2;
            }
            if (Button(new Rect(x + 70f + half, by, half, 64f), "LATER", false, true, 24)) updateStep = 1;
        }
        else
        {
            Label(new Rect(x, y + 24f, w, 50f), "OUTDATED VERSION", 38, Danger, TextAnchor.MiddleCenter);
            Label(new Rect(x + 40f, y + 120f, w - 80f, 90f), "You will not be able to play online properly on an outdated version.", 24, Color.white,
                  TextAnchor.MiddleCenter);
            if (Button(new Rect(x + 50f, by, half, 64f), "I AGREE", false, true, 24))
            {
                Debug.Log("[DesertStrike] Update: later (the player agreed)");
                updateStep = 2;
            }
            if (Button(new Rect(x + 70f + half, by, half, 64f), "NO, I DON'T AGREE", true, true, 22)) updateStep = 0;
        }
    }

    /// <summary>"37 h", or "25 min" under an hour.</summary>
    static string PlayedText(int seconds) =>
        seconds >= 3600 ? $"{seconds / 3600} {Loc.T("h")}" : $"{seconds / 60} {Loc.T("min")}";

    void UseHeight(float height)
    {
        RefHeight = height;
        scale = Screen.height / RefHeight;
        width = Screen.width / scale;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
    }

    float TeamRow(float x, float y, bool allowSpectate, float rowWidth = 700f)
    {
        string[] options = allowSpectate ? new[] { "Terrorists", "SWAT", "Watch bots" } : new[] { "Terrorists", "SWAT" };
        int selected = (int)gm.Side;
        if (!allowSpectate && gm.Side == PlayerSide.Spectate) selected = -1;
        return OptionRow(x, y, "Your team", options, selected, i =>
        {
            gm.Side = (PlayerSide)i;
            if (gm.Side != PlayerSide.Spectate) gm.Net.PreferredTeam = gm.Side == PlayerSide.Terrorists ? Team.Terrorists : Team.Swat;
        }, rowWidth);
    }

    /// <summary>The map: choosing one loads it at once, so it shows behind the menu.</summary>
    float MapRow(float x, float y, float rowWidth)
    {
        var names = new string[MapCatalog.All.Length];
        int selected = -1;
        for (int i = 0; i < names.Length; i++)
        {
            names[i] = MapCatalog.All[i].Name;
            if (MapCatalog.All[i].Id == gm.Map.Id) selected = i;
        }
        return OptionRow(x, y, "Map", names, selected, i => gm.SelectMap(MapCatalog.All[i].Id), rowWidth,
                         i => MapCatalog.All[i].AccountOnly && !Accounts.LoggedIn);
    }

    float PlayersRow(float x, float y, float rowWidth) =>
        TeamCountsRow(x, y, rowWidth, "menu", gm.TerroristCount, gm.SwatCount, gm.SetTeamCounts);

    /// <summary>
    /// How many players each team has (people, and bots for the rest): a number for the Terrorists and one for SWAT,
    /// typed in or changed with - and +. The teams may differ, e.g. 2 against 5.
    /// </summary>
    float TeamCountsRow(float x, float y, float rowWidth, string id, int terrorists, int swat, Action<int, int> onChange)
    {
        Label(new Rect(x + 40f, y, rowWidth, 34f), "Players in each team", 22, Color.white);
        float half = (rowWidth - 20f) / 2f;
        int t = CountField(new Rect(x + 40f, y + 38f, half, 48f), id + "-t", "Terrorists", terrorists);
        int s = CountField(new Rect(x + 40f + half + 20f, y + 38f, half, 48f), id + "-swat", "SWAT", swat);
        if (t != terrorists || s != swat) onChange(t, s);
        return y + 105f;
    }

    // What is typed in each team's number box (it counts once it is a number).
    readonly Dictionary<string, string> countTexts = new Dictionary<string, string>();

    // "Terrorists  [-] [ 5 ] [+]": the number is typed (1 to GameManager.MaxPerTeam), or changed by one.
    int CountField(Rect rect, string id, string team, int value)
    {
        const float labelWidth = 112f, buttonWidth = 46f, gap = 6f;
        Label(new Rect(rect.x, rect.y, labelWidth - 4f, rect.height), team, 19, Dim, TextAnchor.MiddleLeft);
        var minus = new Rect(rect.x + labelWidth, rect.y, buttonWidth, rect.height);
        var field = new Rect(minus.xMax + gap, rect.y, rect.xMax - minus.xMax - 2f * gap - buttonWidth, rect.height);
        var plus = new Rect(field.xMax + gap, rect.y, buttonWidth, rect.height);

        bool editing = OnPhone ? keyboardField == id : GUI.GetNameOfFocusedControl() == id;
        countTexts.TryGetValue(id, out string before);
        string text = TextInput(field, id, editing && before != null ? before : value.ToString(), 2, numbers: true);
        text = new string(Array.FindAll(text.ToCharArray(), char.IsDigit));
        if (text != before && int.TryParse(text, out int typed) && typed >= 1) value = Mathf.Min(typed, GameManager.MaxPerTeam);
        countTexts[id] = text;

        int stepped = value;
        if (Button(minus, "-", false, value > 1)) stepped = value - 1;
        if (Button(plus, "+", false, value < GameManager.MaxPerTeam)) stepped = value + 1;
        if (stepped != value) countTexts[id] = stepped.ToString();
        return Mathf.Clamp(stepped, 1, GameManager.MaxPerTeam);
    }

    float DifficultyRow(float x, float y, float rowWidth) =>
        OptionRow(x, y, "Bot difficulty", new[] { "Easy", "Normal", "Hard", "Extreme" }, (int)gm.Difficulty, i => gm.Difficulty = (BotDifficulty)i, rowWidth,
                  i => (BotDifficulty)i == BotDifficulty.Extreme && !Accounts.LoggedIn);

    float RoundsRow(float x, float y, float rowWidth)
    {
        int[] rounds = { 3, 5, 8, 16 };
        return OptionRow(x, y, "Rounds to win", new[] { "3", "5", "8", "16" }, Array.IndexOf(rounds, gm.RoundsToWin), i => gm.RoundsToWin = rounds[i], rowWidth);
    }

    /// <summary>Friendly fire, and next to it whether a small match (1v1 to 3v3) closes one site's half of the map.</summary>
    float FriendlyFireAndSiteRow(float x, float y, float rowWidth)
    {
        float half = (rowWidth - 20f) / 2f;
        OptionRow(x, y, "Friendly fire", new[] { "Off", "On" }, gm.FriendlyFire ? 1 : 0, i => gm.FriendlyFire = i == 1, half);
        return ClosedSiteRow(x + half + 20f, y, half, gm.TeamSize, gm.CloseHalf ? gm.ClosedSite : null, gm.SetClosedSite);
    }

    /// <summary>
    /// "Site in play": A or B, and small matches (1v1, 2v2, 3v3) play on that site with the other site's half of the
    /// map shut; or the whole map. With bigger teams it shows dimmed (they always have the whole map).
    /// <paramref name="closedSite"/>: the shut site, or null for the whole map; <paramref name="onSelect"/> gets the same.
    /// </summary>
    float ClosedSiteRow(float x, float y, float rowWidth, int teamSize, string closedSite, Action<string> onSelect)
    {
        bool counts = teamSize <= 3;
        Label(new Rect(x + 40f, y, rowWidth, 34f), counts ? "Site in play" : "Site in play (1v1-3v3)", 22, counts ? Color.white : Dim);
        // The buttons, their share of the row and the site each one shuts (null: none).
        var choices = new[] { ("Site A", 0.27f, "B"), ("Site B", 0.27f, "A"), ("Whole map", 0.46f, (string)null) };
        float bx = x + 40f;
        for (int i = 0; i < choices.Length; i++)
        {
            var (text, share, shut) = choices[i];
            var rect = new Rect(bx, y + 38f, (rowWidth - 20f) * share, 48f);
            bx = rect.xMax + 10f;
            bool selected = closedSite == shut;
            if (Button(rect, text, selected && counts, true)) onSelect(shut);
            if (selected && !counts) Fill(new Rect(rect.x, rect.yMax - 4f, rect.width, 4f), Dim);
        }
        return y + 105f;
    }

    void OpenLan()
    {
        lanOpen = true;
        gm.MenuNotice = null;
        if (gm.Side == PlayerSide.Spectate) gm.Side = PlayerSide.Terrorists;
        gm.Net.PreferredTeam = gm.Side == PlayerSide.Swat ? Team.Swat : Team.Terrorists;
    }

    // ----------------------------------------------------------------- friends

    /// <summary>
    /// Friends (they need an account): add one by nickname, answer requests, and see who is online, which means
    /// the friend has the game open on the same WiFi or network. With the online status hidden (Settings) nobody
    /// sees you, and you see nobody's status either.
    /// </summary>
    void DrawFriends()
    {
        var friends = gm.Friends;
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
        {
            friendsOpen = false;
            return;
        }
        bool enter = Event.current.type == EventType.KeyDown && GUI.GetNameOfFocusedControl() == "friend-name" &&
                     (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
        if (enter) Event.current.Use();
        bool submit = enter || KeyboardDone("friend-name");

        Fill(new Rect(0f, 0f, width, RefHeight), new Color(0f, 0f, 0f, 0.35f));
        const float top = 70f, h = 940f;
        float w = 820f, x = (width - w) / 2f, y = top;
        Fill(new Rect(x, y, w, h), PanelColor);
        Label(new Rect(x, y + 20f, w, 60f), Accounts.IsOnline ? "LAN FRIENDS" : "FRIENDS", 50, Gold, TextAnchor.MiddleCenter);
        Label(new Rect(x, y + 78f, w, 28f), "Friends who play on the same WiFi or network show as online", 20, Dim, TextAnchor.MiddleCenter);
        y += 130f;
        float bx = x + 40f, bw = w - 80f;
        if (Accounts.IsOnline)
        {
            if (Button(new Rect(bx, y - 14f, bw, 40f), "BACK TO ONLINE FRIENDS", false, true, 18)) friendsLan = false;
            y += 40f;
        }
        else if (Online.State == OnlineClient.Service.Online || Online.ApiUrl != null)
        {
            Label(new Rect(bx, y - 16f, bw, 26f), "Friends over the internet: log in with an online account (main menu).", 16, Dim, TextAnchor.MiddleCenter);
            y += 18f;
        }

        if (!Accounts.LoggedIn)
        {
            WrapLabel(new Rect(bx, y, bw, 60f), "Friends need an account. Log in or sign up, then add your friends by their nickname.", 21, Color.white);
            y += 80f;
            if (Button(new Rect(bx, y, bw, 60f), "LOG IN / SIGN UP", true, true, 24)) OpenAccount("Log in or sign up to have friends");
        }
        else
        {
            bool hidden = Accounts.HideStatus;
            Label(new Rect(bx, y, bw, 30f), $"You: {Accounts.Current}", 22, Gold);
            Label(new Rect(bx, y, bw, 30f), hidden ? "Your online status: hidden" : "Your online status: visible to friends", 18,
                  hidden ? Dim : MoneyGreen, TextAnchor.MiddleRight);
            y += 48f;

            Label(new Rect(bx, y, bw, 30f), "Add a friend by nickname", 20, Color.white);
            y += 34f;
            friendName = TextInput(new Rect(bx, y, bw - 250f, 44f), "friend-name", friendName, 16);
            if (Button(new Rect(bx + bw - 236f, y, 236f, 44f), "SEND REQUEST", false, !string.IsNullOrWhiteSpace(friendName), 20) ||
                (submit && !string.IsNullOrWhiteSpace(friendName)))
            {
                string name = friendName.Trim();
                string problem = friends.SendRequest(name);
                friendMessageBad = problem != null;
                friendMessage = problem ?? (Accounts.IsFriend(Accounts.FriendIdNamed(name))
                    ? $"{name} is now your friend"
                    : $"Friend request sent to {name}. They get it while their game is open on this network.");
                if (problem == null) friendName = "";
            }
            y += 52f;
            if (!string.IsNullOrEmpty(friendMessage))
                WrapLabel(new Rect(bx, y, bw, 44f), friendMessage, 17, friendMessageBad ? Danger : Gold);
            y += 46f;

            var requests = friends.Incoming;
            if (requests.Count > 0)
            {
                Label(new Rect(bx, y, bw, 30f), $"Friend requests ({requests.Count})", 20, Gold);
                y += 36f;
                foreach (var request in requests.ToArray())   // answering changes the list
                {
                    Fill(new Rect(bx, y, bw, 50f), new Color(1f, 1f, 1f, 0.06f));
                    Label(new Rect(bx + 15f, y, bw - 330f, 50f), $"{request.Name} wants to be your friend", 20, Color.white);
                    if (Button(new Rect(bx + bw - 310f, y + 6f, 150f, 38f), "ACCEPT", true, true, 18)) friends.Accept(request);
                    if (Button(new Rect(bx + bw - 150f, y + 6f, 140f, 38f), "DECLINE", false, true, 18)) friends.Decline(request);
                    y += 58f;
                }
                y += 8f;
            }

            var list = Accounts.FriendList();
            var online = new HashSet<string>();
            if (!hidden)
                foreach (var (id, _) in list)
                    if (friends.IsOnline(id)) online.Add(id);
            // Online friends first, then by name.
            list.Sort((a, b) => online.Contains(a.Id) != online.Contains(b.Id)
                ? (online.Contains(a.Id) ? -1 : 1)
                : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            Label(new Rect(bx, y, bw, 30f), $"Your friends ({list.Count})", 20, Color.white);
            if (!hidden && list.Count > 0) Label(new Rect(bx, y, bw, 30f), $"{online.Count} online", 18, MoneyGreen, TextAnchor.MiddleRight);
            y += 38f;

            float listBottom = top + h - 160f;
            if (list.Count == 0)
                Label(new Rect(bx, y, bw, 30f), "No friends yet: send a request above.", 18, Dim);
            else
            {
                const float row = 54f;
                var view = new Rect(bx, y, bw, Mathf.Max(row, listBottom - y));
                var content = new Rect(0f, 0f, list.Count * row > view.height ? bw - 20f : bw, list.Count * row);
                friendsScroll = GUI.BeginScrollView(view, friendsScroll, content, false, false);
                for (int i = 0; i < list.Count; i++)
                {
                    var (id, name) = list[i];
                    float ry = i * row;
                    bool isOnline = online.Contains(id);
                    Fill(new Rect(0f, ry, content.width, row - 6f), new Color(1f, 1f, 1f, isOnline ? 0.1f : 0.05f));
                    Color dot = hidden ? new Color(1f, 1f, 1f, 0.2f) : isOnline ? MoneyGreen : new Color(0.5f, 0.5f, 0.5f);
                    Fill(new Rect(16f, ry + 17f, 14f, 14f), dot);
                    Label(new Rect(44f, ry, 320f, row - 6f), name, 21, isOnline || hidden ? Color.white : new Color(1f, 1f, 1f, 0.75f));
                    Label(new Rect(content.width - 350f, ry, 170f, row - 6f), hidden ? "Hidden" : isOnline ? "Online" : "Offline", 19,
                          isOnline ? MoneyGreen : Dim, TextAnchor.MiddleRight);
                    bool confirm = removeConfirm == id;
                    if (Button(new Rect(content.width - 160f, ry + 7f, 150f, 34f), confirm ? "REALLY?" : "REMOVE", confirm, true, 16))
                    {
                        if (confirm)
                        {
                            friends.Remove(id);
                            removeConfirm = null;
                        }
                        else removeConfirm = id;
                    }
                }
                GUI.EndScrollView();
            }
            WrapLabel(new Rect(bx, listBottom + 10f, bw, 50f), hidden
                ? "Your online status is hidden (Settings): nobody sees when you are online, and you can't see when your friends are."
                : "Your friends see you online while the game is open on the same network. To hide it: Settings > Online status.", 16, Dim);
        }

        if (Button(new Rect(x + w / 2f - 150f, top + h - 80f, 300f, 56f), "BACK"))
        {
            friendsOpen = false;
            removeConfirm = null;
        }
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
        if (Accounts.LoggedIn)
            Label(new Rect(x + 220f, y + 2f, w - 260f, 36f), $"{Accounts.Current}   (your account)", 22, Gold);
        else
        {
            string name = TextInput(new Rect(x + 220f, y + 2f, w - 260f, 36f), "lan-name", gm.Net.PlayerName, 16);
            if (name != gm.Net.PlayerName)
            {
                gm.Net.PlayerName = name;
                PlayerPrefs.SetString(NameKey, name);
            }
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
        joinAddress = TextInput(new Rect(x + 220f, y + 2f, w - 430f, 36f), "join-address", joinAddress, 40, false, true);
        if (Button(new Rect(x + w - 200f, y, 160f, 40f), "JOIN", false, !string.IsNullOrWhiteSpace(joinAddress), 20)) gm.Net.Join(joinAddress);
        y += 56f;

        string notice = !string.IsNullOrEmpty(gm.Net.Status) ? gm.Net.Status : gm.MenuNotice;
        if (!string.IsNullOrEmpty(notice))
        {
            Label(new Rect(x + 40f, y, w - 80f, 30f), notice, 17, Danger);
            y += 34f;
        }
        Label(new Rect(x + 40f, y, w - 80f, 30f), "Voice chat: hold V to talk, in the lobby and in the match. Everyone in the game hears you.", 15, Dim);
        y += 30f;
        Label(new Rect(x + 40f, y, w - 80f, 60f),
              "Windows may ask whether Low Strike may use the network: allow it (at least on private networks).",
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
        const float top = 70f;
        float w = Mathf.Min(1480f, width - 40f), x = (width - w) / 2f;
        float column = (w - 120f) / 2f, right = x + column + 40f;
        Fill(new Rect(x, top, w, 940f), PanelColor);
        Label(new Rect(x, top + 20f, w, 60f), "HOSTING A LAN GAME", 44, Gold, TextAnchor.MiddleCenter);
        Label(new Rect(x, top + 75f, w, 28f), $"Other PCs find this game automatically. Your IP: {string.Join(", ", NetSession.LocalAddresses())}",
              18, Dim, TextAnchor.MiddleCenter);

        DrawLobbyVoice(x + 40f, top + 106f, w - 80f);
        float y = top + 160f;
        Label(new Rect(x + 40f, y, column, 32f), "Players", 22, Color.white);
        y += 36f;
        if (gm.Side != PlayerSide.Spectate)
        {
            Label(new Rect(x + 60f, y, column - 20f, 30f), $"{gm.Net.PlayerName}  ({NetSession.TeamName(gm.Net.PreferredTeam)}, you)", 20, TeamColor(gm.Net.PreferredTeam));
            y += 32f;
        }
        foreach (var peer in gm.Net.Peers)
        {
            Label(new Rect(x + 60f, y, column - 20f, 30f), $"{peer.Name}  ({NetSession.TeamName(peer.Team)})", 20, TeamColor(peer.Team));
            y += 32f;
        }
        if (gm.Net.Peers.Count == 0)
        {
            Label(new Rect(x + 60f, y, column - 20f, 30f), "Waiting for someone to join...  (bots fill the empty places)", 18, Dim);
            y += 32f;
        }
        float leftEnd = TeamRow(x, y + 14f, allowSpectate: true, column);

        float rightEnd = MapRow(right, top + 160f, column);
        rightEnd = PlayersRow(right, rightEnd, column);
        rightEnd = DifficultyRow(right, rightEnd, column);
        rightEnd = RoundsRow(right, rightEnd, column);
        rightEnd = FriendlyFireAndSiteRow(right, rightEnd, column);

        y = Mathf.Max(leftEnd, rightEnd) + 6f;
        if (Button(new Rect(x + (w - 640f) / 2f, y, 640f, 66f), "START MATCH", true, true, 30)) gm.StartMatch();
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
        DrawLobbyVoice(x + 40f, y + 36f, w - 80f);
        if (Button(new Rect(x + w / 2f - 150f, (RefHeight + h) / 2f - 80f, 300f, 56f), "LEAVE")) net.Stop();
    }

    GUIStyle InputStyle()
    {
        var style = new GUIStyle(GUI.skin.textField) { fontSize = 22, alignment = TextAnchor.MiddleLeft };
        style.padding.left = 10;
        return style;
    }

    // Phones: the game's own keyboard. The phone's system keyboard (TouchScreenKeyboard) did not type into the
    // fields on every phone, so on a phone a field is a box that opens this keyboard at the bottom of the screen:
    // letters, digits, symbols, SHIFT, space, delete and DONE (DONE counts as Enter: log in, send the request).
    string keyboardField, keyboardDone, keyboardText = "";
    bool keyboardSecret, keyboardShift, keyboardSymbols;
    int keyboardMax, keyboardSeenFrame;
    const float KeyboardHeight = 430f;
    static readonly string[] LetterRows = { "1234567890", "qwertyuiop", "asdfghjkl", "zxcvbnm" };
    static readonly string[] SymbolRows = { "1234567890", "-_.@#$%&*+", "!?/()=:;,'", "~^[]{}<>|" };

    // -ds-keyboard-demo <nickname> <password>: types them on the game's keyboard, key by key, and logs in (tests).
    readonly Queue<string> keyboardDemo = new Queue<string>();
    int keyboardDemoFrame;

    static bool OnPhone => Application.isMobilePlatform || GameInput.Touch;

    static string FieldName(string id) => id switch
    {
        "account-name" => "Nickname",
        "account-password" => "Password",
        "account-repeat" => "Password again",
        "friend-name" => "Friend's nickname",
        "lan-name" => "Your name",
        "join-address" => "IP address",
        "online-code" => "Room code",
        "online-room-name" => "Room name",
        "online-search" => "Player's nickname",
        "pw-old" => "Current password",
        "pw-new" => "New password",
        "pw-repeat" => "New password again",
        "delete-password" => "Your password",
        "admin-nick" => "Player's nickname",
        "admin-amount" => "Amount",
        _ when id.EndsWith("-t") => "Terrorists",
        _ when id.EndsWith("-swat") => "SWAT",
        _ => "",
    };

    /// <summary>A text field (<paramref name="secret"/>: a password, shown as stars; <paramref name="numbers"/>:
    /// the keyboard starts on digits and symbols). On a computer IMGUI's field; on a phone a box that opens the
    /// game's keyboard when tapped.</summary>
    string TextInput(Rect rect, string id, string value, int maxLength, bool secret = false, bool numbers = false)
    {
        value ??= "";
        if (!OnPhone)
        {
            GUI.SetNextControlName(id);
            return secret ? GUI.PasswordField(rect, value, '*', maxLength, InputStyle()) : GUI.TextField(rect, value, maxLength, InputStyle());
        }

        bool editing = keyboardField == id;
        if (editing)
        {
            value = keyboardText;
            keyboardSeenFrame = Time.frameCount;
            Fill(new Rect(rect.x - 3f, rect.y - 3f, rect.width + 6f, rect.height + 6f), Gold);
        }
        string shown = secret ? new string('*', value.Length) : value;
        GUI.Label(rect, editing ? shown + "|" : shown, InputStyle());
        var e = Event.current;
        if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition) && !editing)
        {
            OpenKeyboard(id, value, maxLength, secret, numbers);
            e.Use();
        }
        return value;
    }

    void OpenKeyboard(string id, string value, int maxLength, bool secret, bool numbers = false)
    {
        keyboardField = id;
        keyboardText = value ?? "";
        keyboardMax = maxLength;
        keyboardSecret = secret;
        keyboardSymbols = numbers;
        keyboardShift = false;
        keyboardSeenFrame = Time.frameCount;
    }

    bool KeyboardOpen => keyboardField != null && Time.frameCount - keyboardSeenFrame <= 2;   // its field is still drawn

    // The keys and where they are (1080p units), for drawing and for taps.
    List<(Rect Rect, string Key)> KeyboardKeys()
    {
        var keys = new List<(Rect, string)>();
        const float keyHeight = 64f, gap = 8f;
        float rowsWidth = Mathf.Min(width - 40f, 1400f), left = (width - rowsWidth) / 2f;
        float top = RefHeight - KeyboardHeight + 62f;
        float keyWidth = (rowsWidth - 9f * gap) / 10f;   // ten keys fill a row
        var rows = keyboardSymbols ? SymbolRows : LetterRows;
        for (int r = 0; r < rows.Length; r++)
        {
            string row = rows[r];
            float rowWidth = row.Length * keyWidth + (row.Length - 1) * gap;
            float x = left + (rowsWidth - rowWidth) / 2f, y = top + r * (keyHeight + gap);
            for (int i = 0; i < row.Length; i++)
            {
                string key = row[i].ToString();
                if (!keyboardSymbols && keyboardShift) key = key.ToUpperInvariant();
                keys.Add((new Rect(x + i * (keyWidth + gap), y, keyWidth, keyHeight), key));
            }
        }
        // Bottom row, in tenths of the width: SHIFT 2, ?123 / ABC 2, space 2.5, DEL 1.5, DONE 2.
        float bottom = top + rows.Length * (keyHeight + gap), unit = (rowsWidth + gap) / 10f, bx = left;
        foreach (var (key, units) in new[] { ("SHIFT", 2f), (keyboardSymbols ? "ABC" : "?123", 2f), ("SPACE", 2.5f), ("DEL", 1.5f), ("DONE", 2f) })
        {
            keys.Add((new Rect(bx, bottom, unit * units - gap, keyHeight), key));
            bx += unit * units;
        }
        return keys;
    }

    // Taps on the keyboard, before anything else on the screen sees them (the keyboard is drawn last, on top).
    void HandleGameKeyboard()
    {
        if (!KeyboardOpen) return;
        var e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)   // Android's back button closes it
        {
            keyboardField = null;
            e.Use();
            return;
        }
        if (!new Rect(0f, RefHeight - KeyboardHeight, width, KeyboardHeight).Contains(e.mousePosition)) return;
        if (e.type == EventType.MouseDown)
        {
            foreach (var (rect, key) in KeyboardKeys())
                if (rect.Contains(e.mousePosition))
                {
                    PressKey(key);
                    break;
                }
            e.Use();
        }
        else if (e.type == EventType.MouseUp || e.type == EventType.MouseDrag) e.Use();
    }

    void PressKey(string key)
    {
        switch (key)
        {
            case "SHIFT": keyboardShift = !keyboardShift; break;
            case "?123": keyboardSymbols = true; break;
            case "ABC": keyboardSymbols = false; break;
            case "DEL": if (keyboardText.Length > 0) keyboardText = keyboardText.Substring(0, keyboardText.Length - 1); break;
            case "DONE":
                keyboardDone = keyboardField;
                keyboardField = null;
                break;
            case "SPACE": Type(" "); break;
            default:
                Type(keyboardShift && !keyboardSymbols ? key.ToUpperInvariant() : key);
                keyboardShift = false;   // SHIFT is for one letter, like on a phone
                break;
        }
    }

    void Type(string text)
    {
        if (keyboardText.Length < keyboardMax) keyboardText += text;
    }

    void DrawGameKeyboard()
    {
        RunKeyboardDemo();
        if (!KeyboardOpen) return;
        var area = new Rect(0f, RefHeight - KeyboardHeight, width, KeyboardHeight);
        Fill(area, new Color(0.07f, 0.07f, 0.08f, 0.97f));
        string shown = keyboardSecret ? new string('*', keyboardText.Length) : keyboardText;
        Label(new Rect(30f, area.y + 8f, width - 60f, 44f), $"{FieldName(keyboardField)}:   {shown}|", 26, Gold);
        foreach (var (rect, key) in KeyboardKeys())
        {
            bool special = key.Length > 1;
            bool lit = key == "DONE" || (key == "SHIFT" && keyboardShift);
            Fill(rect, lit ? new Color(0.85f, 0.55f, 0.15f, 0.95f) : special ? new Color(0.27f, 0.27f, 0.3f, 0.95f) : new Color(0.19f, 0.19f, 0.21f, 0.95f));
            Label(rect, key == "SPACE" ? "space" : key, special ? 20 : 28, Color.white, TextAnchor.MiddleCenter);
        }
    }

    // One key every quarter of a second, as a finger would.
    void RunKeyboardDemo()
    {
        if (keyboardDemo.Count == 0 || Event.current.type != EventType.Repaint || Time.frameCount < keyboardDemoFrame) return;
        keyboardDemoFrame = Time.frameCount + 15;
        string key = keyboardDemo.Dequeue();
        if (key.StartsWith("@")) OpenKeyboard(key.Substring(1), "", key == "@account-name" ? 16 : 64, key != "@account-name");
        else PressKey(key);
    }

    /// <summary>Whether the phone keyboard of that field was just closed with Done (once).</summary>
    bool KeyboardDone(string id)
    {
        if (keyboardDone != id) return false;
        keyboardDone = null;
        return true;
    }

    void DrawInventory()
    {
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
        {
            CloseInventory();
            return;
        }

        if (buying != null)
        {
            DrawBuySkin();
            return;
        }
        Fill(new Rect(0f, 0f, width, RefHeight), new Color(0f, 0f, 0f, 0.45f));
        float w = 1240f, h = 820f, x = (width - w) / 2f, y = (RefHeight - h) / 2f;
        Fill(new Rect(x, y, w, h), PanelColor);
        Label(new Rect(x, y + 15f, w, 60f), "INVENTORY", 48, Gold, TextAnchor.MiddleCenter);
        if (Accounts.IsOnline && Online.Tokens >= 0)
            Label(new Rect(x + w - 330f, y + 26f, 300f, 34f), $"Halloween Tokens: {Online.Tokens}", 22, Pumpkin, TextAnchor.MiddleRight);
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
            bool locked = !WeaponSkins.CanUse(skin);
            bool forTokens = locked && skin.TokenPrice > 0;   // sold for Halloween Tokens, not bought yet
            if (locked && Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition))
            {
                Event.current.Use();
                if (forTokens && Accounts.IsOnline)
                {
                    buying = skin;
                    buyProblem = null;
                }
                else if (forTokens) OpenAccount("Log in online to buy skins with Halloween Tokens", online: true);
                else OpenAccount($"Log in or sign up to use the {skin.Name} skin");
                return;
            }
            string text = isEquipped ? $"{skin.Name}\nEQUIPPED"
                        : forTokens ? $"{skin.Name}\n{Online.PriceOf(skin.WeaponId, skin.Id, skin.TokenPrice)} Halloween Tokens" : skin.Name;
            if (Button(row, text, isEquipped, !locked, 20) && WeaponSkins.Equip(inventoryWeapon, skin))
            {
                previewSkin = null;
                SoundFX.Play(SoundFX.Buy, Vector3.zero, 0.6f, 1f, false);
            }
            if (locked) DrawLock(row.xMax - 32f, row.y + 18f, forTokens ? Pumpkin : LockColor);
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

    /// <summary>A titled row of choices; `locked` marks ones that need an account (clicking one asks to log in).</summary>
    float OptionRow(float x, float y, string title, string[] options, int selected, Action<int> onSelect, float rowWidth = 700f,
                    Func<int, bool> locked = null)
    {
        Label(new Rect(x + 40f, y, rowWidth, 34f), title, 22, Color.white);
        float buttonWidth = (rowWidth - (options.Length - 1) * 10f) / options.Length;
        for (int i = 0; i < options.Length; i++)
        {
            var rect = new Rect(x + 40f + i * (buttonWidth + 10f), y + 38f, buttonWidth, 48f);
            bool isLocked = locked != null && locked(i);
            if (isLocked && Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                Event.current.Use();
                OpenAccount($"Log in or sign up to unlock {options[i]}");
            }
            if (Button(rect, options[i], i == selected, !isLocked)) onSelect(i);
            if (isLocked) DrawLock(rect.xMax - 26f, rect.y + 14f, LockColor);
        }
        return y + 105f;
    }

    // ----------------------------------------------------------------- account

    static readonly Color LockColor = new Color(1f, 0.82f, 0.4f, 0.95f);

    void OpenAccount(string message = null, bool? online = null)
    {
        if (inventoryOpen) CloseInventory();
        settingsOpen = false;
        accountOpen = true;
        accountMessage = message;
        accountPassword = accountRepeat = "";
        accountFocus = true;
        accountOnline = online ?? Online.ApiUrl != null;
    }

    /// <summary>Top left of the main menu: who is playing, and the way to log in or out.</summary>
    void DrawAccountBox(float x, float y)
    {
        if (Accounts.LoggedIn)
        {
            Label(new Rect(x, y, 420f, 28f), $"Logged in as {Accounts.Current}{(Accounts.IsOnline ? "  (online)" : "")}", 20, Gold);
            if (Button(new Rect(x, y + 32f, 140f, 38f), "LOG OUT", false, true, 18))
            {
                if (Accounts.IsOnline) Online.LogOut();
                else Accounts.LogOut();
            }
            if (Accounts.IsOnline && Button(new Rect(x + 150f, y + 32f, 140f, 38f), "ACCOUNT", false, true, 18))
            {
                onlineAccountOpen = true;
                accountPageMessage = null;
            }
            if (Accounts.IsOnline && Online.IsAdmin && Button(new Rect(x + 300f, y + 32f, 120f, 38f), "ADMIN", false, true, 18)) OpenAdmin();
        }
        else
        {
            Label(new Rect(x, y, 300f, 28f), "Playing as a guest", 20, Dim);
            if (Button(new Rect(x, y + 32f, 220f, 38f), "LOG IN / SIGN UP", false, true, 18)) OpenAccount();
        }
    }

    /// <summary>
    /// Log in or sign up (asked at every start): a nickname no other account on this computer has, and a
    /// password. With an account come the Halloween map, the RPG's Web skin and Extreme bots, and the skins
    /// equipped are kept with it; "Play as guest" goes on without one.
    /// </summary>
    void DrawAccount()
    {
        if (accountName == null) accountName = Accounts.LastName;
        bool enter = Event.current.type == EventType.KeyDown &&
                     (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
        if (enter) Event.current.Use();
        // On a phone, Done on the (last) password keyboard logs in or creates the account.
        bool submit = enter || KeyboardDone(accountSignUp ? "account-repeat" : "account-password");
        if (KeyboardDone("account-name")) OpenKeyboard("account-password", accountPassword, 64, true);
        if (accountSignUp && KeyboardDone("account-password")) OpenKeyboard("account-repeat", accountRepeat, 64, true);

        Fill(new Rect(0f, 0f, width, RefHeight), new Color(0f, 0f, 0f, 0.45f));
        float w = 760f, h = (accountSignUp ? 790f : 700f) + 100f, x = (width - w) / 2f, y = (RefHeight - h) / 2f;
        Fill(new Rect(x, y, w, h), PanelColor);
        Label(new Rect(x, y + 20f, w, 70f), "LOW STRIKE", 58, Gold, TextAnchor.MiddleCenter);
        Label(new Rect(x, y + 88f, w, 30f), "Log in or sign up", 22, Dim, TextAnchor.MiddleCenter);
        float fx = x + 60f, fw = w - 120f, cy = y + 140f;
        // Online (one account on every computer and phone) or only on this computer.
        if (Button(new Rect(fx, cy, fw / 2f - 5f, 44f), "ONLINE ACCOUNT", accountOnline, true, 19))
        {
            accountOnline = true;
            accountMessage = null;
        }
        if (Button(new Rect(fx + fw / 2f + 5f, cy, fw / 2f - 5f, 44f), "THIS COMPUTER ONLY", !accountOnline, true, 19))
        {
            accountOnline = false;
            accountMessage = null;
        }
        if (accountOnline) OnlineStatusLine(new Rect(x, cy + 50f, w, 28f));
        cy += 96f;
        if (Button(new Rect(fx, cy, fw / 2f - 5f, 52f), "LOG IN", !accountSignUp))
        {
            accountSignUp = false;
            accountMessage = null;
            accountFocus = true;
        }
        if (Button(new Rect(fx + fw / 2f + 5f, cy, fw / 2f - 5f, 52f), "SIGN UP", accountSignUp))
        {
            accountSignUp = true;
            accountMessage = null;
            accountFocus = true;
        }
        cy += 76f;

        Label(new Rect(fx, cy, fw, 30f), accountSignUp ? "Nickname (3 to 16 letters, digits, _ or -)" : "Nickname", 20, Color.white);
        accountName = TextInput(new Rect(fx, cy + 32f, fw, 42f), "account-name", accountName, 16);
        cy += 90f;
        int minimum = accountOnline ? 8 : Accounts.MinPasswordLength;
        Label(new Rect(fx, cy, fw, 30f), accountSignUp ? $"Password (at least {minimum} characters)" : "Password", 20, Color.white);
        accountPassword = TextInput(new Rect(fx, cy + 32f, fw, 42f), "account-password", accountPassword, 64, true);
        cy += 90f;
        if (accountSignUp)
        {
            Label(new Rect(fx, cy, fw, 30f), "Password again", 20, Color.white);
            accountRepeat = TextInput(new Rect(fx, cy + 32f, fw, 42f), "account-repeat", accountRepeat, 64, true);
            cy += 90f;
        }
        if (accountFocus && Event.current.type == EventType.Repaint)
        {
            GUI.FocusControl(string.IsNullOrEmpty(accountName) || accountSignUp ? "account-name" : "account-password");
            accountFocus = false;
        }

        if (!string.IsNullOrEmpty(accountMessage))
            Label(new Rect(x + 20f, cy, w - 40f, 30f), accountMessage, 19, Danger, TextAnchor.MiddleCenter);
        cy += 42f;
        bool waiting = accountOnline && Online.Busy;
        string action = waiting ? "CONNECTING..." : accountSignUp ? "CREATE ACCOUNT" : "LOG IN";
        if ((Button(new Rect(fx, cy, fw, 64f), action, true, !waiting, 28) || submit) && !waiting)
        {
            if (!accountOnline)
            {
                accountMessage = accountSignUp ? Accounts.SignUp(accountName, accountPassword, accountRepeat)
                                               : Accounts.LogIn(accountName, accountPassword);
                if (accountMessage == null)
                {
                    tutorialOffer = accountSignUp;   // a new account: offer the practice round
                    accountOpen = false;
                    accountPassword = accountRepeat = "";
                }
            }
            else if (accountSignUp && accountPassword != accountRepeat) accountMessage = "The two passwords are not the same";
            else if (Online.State == OnlineClient.Service.NotSetUp) accountMessage = "Online play is not open yet: use THIS COMPUTER ONLY";
            else
            {
                accountMessage = null;
                bool signingUp = accountSignUp;
                Online.LogIn(accountName, accountPassword, accountSignUp, problem =>
                {
                    accountMessage = problem;
                    if (problem != null) return;
                    tutorialOffer = signingUp;   // a new account: offer the practice round
                    accountOpen = false;
                    accountPassword = accountRepeat = "";
                });
            }
        }
        cy += 80f;
        if (Button(new Rect(fx, cy, fw, 50f), "PLAY AS GUEST", false, true, 22))
        {
            accountOpen = false;
            accountMessage = null;
            accountPassword = accountRepeat = "";
        }
        cy += 66f;
        Label(new Rect(x + 30f, cy, w - 60f, 80f), accountOnline
              ? "An online account works on every computer and phone: online rooms, friends over the internet,\nand your skins and settings kept on the server. Plus the Halloween map, the Web skin and Extreme bots."
              : "With an account: the Halloween map, the RPG's Web skin and Extreme bots,\nand the skins you equip are kept with your account on this computer.",
              17, Dim, TextAnchor.UpperCenter);
    }

    /// <summary>A small padlock: something that needs an account.</summary>
    static void DrawLock(float x, float y, Color color)
    {
        Fill(new Rect(x, y + 8f, 16f, 12f), color);
        Fill(new Rect(x + 2.5f, y, 2.5f, 9f), color);
        Fill(new Rect(x + 11f, y, 2.5f, 9f), color);
        Fill(new Rect(x + 2.5f, y, 11f, 2.5f), color);
    }

    float SensitivitySlider(float x, float y, float w)
    {
        Label(new Rect(x, y, w, 34f), $"{(GameInput.Touch ? "Touch" : "Mouse")} sensitivity   {PlayerController.MouseSensitivity:0.0}", 22, Color.white);
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
        if (!Application.isMobilePlatform && !GameInput.Touch)   // a phone is always full screen
            y = OptionRow(x, y, "Display", new[] { "Fullscreen", "Window" }, GameSettings.Fullscreen ? 0 : 1,
                          i => GameSettings.SetFullscreen(i == 0), column);
        else
            y = OptionRow(x, y, "Interface size in the match", new[] { "100%", "115%", "130%", "150%" },
                          Array.IndexOf(GameSettings.HudScales, GameSettings.HudScale), i => GameSettings.SetHudScale(GameSettings.HudScales[i]), column);
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
        y = OptionRow(right, y, "Voice chat (LAN and online games)", new[] { "Off", "On" }, GameSettings.VoiceChat ? 1 : 0,
                      i => GameSettings.SetVoiceChat(i == 1), column);
        y = OptionRow(right, y, "When the others hear you", new[] { "Hold V", "Open mic" }, (int)GameSettings.Voice,
                      i => GameSettings.SetVoiceMode((VoiceMode)i), column);
        WrapLabel(new Rect(right + 40f, y - 14f, column, 26f), VoiceModeHelp[(int)GameSettings.Voice], 16, Dim);
        y += 24f;
        y = MicrophoneTest(right, y, column);
        y = SensitivitySlider(right + 40f, y, column);
        y = OptionRow(right, y, "Online status (friends)", new[] { "Visible", "Hidden" }, Accounts.HideStatus ? 1 : 0,
                      i => Accounts.SetHideStatus(i == 1), column, i => !Accounts.LoggedIn);
        WrapLabel(new Rect(right + 40f, y - 14f, column, 44f),
                  !Accounts.LoggedIn ? "Friends need an account: log in to choose."
                  : Accounts.HideStatus ? "Hidden: nobody sees when you are online, and you can't see when your friends are online either."
                  : "Visible: your friends see when you are online.", 16, Dim);

        bool tutorial = gm.State == MatchState.Menu && gm.Net.Role == NetSession.Mode.Off;
        int count = (GameInput.Touch ? 4 : 3) + (tutorial ? 1 : 0);
        float bw = Mathf.Min(300f, (w - 80f - (count - 1) * 20f) / count), bx = x + (w - count * bw - (count - 1) * 20f) / 2f, by = top + height - 80f;
        if (tutorial)
        {
            if (Button(new Rect(bx, by, bw, 56f), "TUTORIAL"))
            {
                settingsOpen = false;
                gm.StartTutorial();
            }
            bx += bw + 20f;
        }
        if (Button(new Rect(bx, by, bw, 56f), "CROSSHAIR")) crosshairOpen = true;
        bx += bw + 20f;
        if (Button(new Rect(bx, by, bw, 56f), "LANGUAGES")) languagesOpen = true;
        bx += bw + 20f;
        if (GameInput.Touch)
        {
            if (Button(new Rect(bx, by, bw, 56f), "TOUCH CONTROLS")) touchLayoutOpen = true;
            bx += bw + 20f;
        }
        if (Button(new Rect(bx, by, bw, 56f), "BACK")) settingsOpen = false;
    }

    /// <summary>Settings > LANGUAGES: the website's six; a click switches at once (and is saved).</summary>
    void DrawLanguages()
    {
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
        {
            languagesOpen = false;
            Event.current.Use();
            return;
        }
        Fill(new Rect(0f, 0f, width, RefHeight), new Color(0f, 0f, 0f, 0.45f));
        float w = Mathf.Min(900f, width - 40f), h = 600f, x = (width - w) / 2f, y = (RefHeight - h) / 2f;
        Fill(new Rect(x, y, w, h), PanelColor);
        Label(new Rect(x, y + 20f, w, 60f), "LANGUAGES", 50, Gold, TextAnchor.MiddleCenter);
        Label(new Rect(x, y + 84f, w, 30f), "Choose the game's language", 20, Dim, TextAnchor.MiddleCenter);
        float bw = (w - 120f) / 2f;
        for (int i = 0; i < Loc.Languages.Length; i++)
        {
            var (code, name) = Loc.Languages[i];
            var rect = new Rect(x + 40f + (i % 2) * (bw + 40f), y + 136f + (i / 2) * 92f, bw, 72f);
            if (Button(rect, name, code == Loc.Language, true, 28)) Loc.Set(code);
        }
        if (Button(new Rect(x + w / 2f - 150f, y + h - 84f, 300f, 56f), "BACK")) languagesOpen = false;
    }

    // ----------------------------------------------------------------- moving the phone's buttons (Settings > TOUCH CONTROLS)

    /// <summary>Every on-screen button where it is: drag one to move it, tap one and change its size, RESET puts them
    /// all back. Saved on the device at once.</summary>
    void DrawTouchLayout()
    {
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
        {
            touchLayoutOpen = false;
            Event.current.Use();
            return;
        }
        Fill(new Rect(0f, 0f, width, RefHeight), new Color(0f, 0f, 0f, 0.35f));
        if (touch == null) return;
        touch.DrawEditor();   // first, so a touch on a button is the button's

        float w = 600f, x = (width - w) / 2f, y = 100f, h = touch.Selected != null ? 300f : 210f;
        Fill(new Rect(x, y, w, h), PanelColor);
        Label(new Rect(x, y + 12f, w, 44f), "TOUCH CONTROLS", 34, Gold, TextAnchor.MiddleCenter);
        Label(new Rect(x + 20f, y + 58f, w - 40f, 28f), "Drag a button to move it. Tap one to change its size.", 18, Dim, TextAnchor.MiddleCenter);
        float cy = y + 96f;
        if (touch.Selected is TouchButton selected)
        {
            float size = TouchControls.SizeOf(selected);
            Label(new Rect(x + 30f, cy, w - 60f, 30f), $"Size of {selected.ToString().ToUpperInvariant()}   {Mathf.RoundToInt(size * 100f)}%", 20, Color.white);
            float changed = GUI.HorizontalSlider(new Rect(x + 30f, cy + 38f, w - 60f, 30f), size, 0.6f, 1.6f);
            if (Mathf.Abs(changed - size) > 0.005f) touch.ResizeSelected(Mathf.Round(changed * 20f) / 20f);
            cy += 90f;
        }
        if (Button(new Rect(x + 30f, cy, w / 2f - 40f, 56f), "RESET")) TouchControls.ResetLayout();
        if (Button(new Rect(x + w / 2f + 10f, cy, w / 2f - 40f, 56f), "DONE", true)) touchLayoutOpen = false;
    }

    // ----------------------------------------------------------------- crosshair page (Settings > CROSSHAIR)

    /// <summary>The crosshair like Counter-Strike 2's: colour (or your own with R, G, B), length, thickness, gap,
    /// opacity, outline, centre dot, T shape, dynamic or static; with a live preview.</summary>
    void DrawCrosshairSettings()
    {
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
        {
            crosshairOpen = false;
            Event.current.Use();
            return;
        }
        Fill(new Rect(0f, 0f, width, RefHeight), new Color(0f, 0f, 0f, 0.45f));
        const float top = 90f, height = 860f;
        float w = Mathf.Min(1480f, width - 40f), x = (width - w) / 2f;
        float column = (w - 120f) / 2f, right = x + column + 40f;
        Fill(new Rect(x, top, w, height), PanelColor);
        Label(new Rect(x, top + 20f, w, 60f), "CROSSHAIR", 50, Gold, TextAnchor.MiddleCenter);

        float y = top + 110f;
        y = OptionRow(x, y, "Color", GameSettings.CrosshairColorNames, GameSettings.CrosshairColorIndex,
                      i => GameSettings.SetCrosshair(color: i), column);
        if (GameSettings.CrosshairColorIndex == GameSettings.CrosshairColorNames.Length - 1)
        {
            var c = GameSettings.CrosshairCustom;
            float r = SettingSlider(x + 40f, y - 10f, column, "Red", c.r * 255f, 0f, 255f, "0");
            float g = SettingSlider(x + 40f, y + 40f, column, "Green", c.g * 255f, 0f, 255f, "0");
            float b = SettingSlider(x + 40f, y + 90f, column, "Blue", c.b * 255f, 0f, 255f, "0");
            var color = new Color(Mathf.Round(r) / 255f, Mathf.Round(g) / 255f, Mathf.Round(b) / 255f);
            if (color != c) GameSettings.SetCrosshair(custom: color);
            y += 150f;
        }
        y += 10f;
        float length = SettingSlider(x + 40f, y, column, "Length", GameSettings.CrosshairLength, 0f, 30f, "0");
        float thickness = SettingSlider(x + 40f, y + 70f, column, "Thickness", GameSettings.CrosshairThickness, 1f, 8f, "0");
        float gap = SettingSlider(x + 40f, y + 140f, column, "Gap", GameSettings.CrosshairGap, -5f, 20f, "0");
        float alpha = SettingSlider(x + 40f, y + 210f, column, "Opacity", GameSettings.CrosshairAlpha * 100f, 20f, 100f, "0") / 100f;
        if (!Mathf.Approximately(Mathf.Round(length), GameSettings.CrosshairLength)) GameSettings.SetCrosshair(length: Mathf.Round(length));
        if (!Mathf.Approximately(Mathf.Round(thickness), GameSettings.CrosshairThickness)) GameSettings.SetCrosshair(thickness: Mathf.Round(thickness));
        if (!Mathf.Approximately(Mathf.Round(gap), GameSettings.CrosshairGap)) GameSettings.SetCrosshair(gap: Mathf.Round(gap));
        if (Mathf.Abs(alpha - GameSettings.CrosshairAlpha) > 0.004f) GameSettings.SetCrosshair(alpha: Mathf.Round(alpha * 100f) / 100f);

        // Preview on a desert-coloured patch; a dynamic crosshair breathes as if shooting.
        var preview = new Rect(right + 40f, top + 110f, column, 230f);
        Fill(preview, new Color(0.72f, 0.62f, 0.45f));
        Fill(new Rect(preview.x, preview.y, preview.width, preview.height * 0.45f), new Color(0.45f, 0.6f, 0.8f));
        float breathing = GameSettings.CrosshairDynamic ? (Mathf.Sin(Time.unscaledTime * 2.5f) * 0.5f + 0.5f) * 14f : 0f;
        // Twice the size it has in the game, so the details show.
        var saved = GUI.matrix;
        GUI.matrix = saved * Matrix4x4.TRS(preview.center, Quaternion.identity, Vector3.one)
                           * Matrix4x4.Scale(new Vector3(2f, 2f, 1f)) * Matrix4x4.TRS(-preview.center, Quaternion.identity, Vector3.one);
        DrawCrosshairShape(preview.center, GameSettings.CrosshairGap + breathing, false);
        GUI.matrix = saved;
        Label(new Rect(preview.x + 10f, preview.yMax - 30f, preview.width - 20f, 26f),
              GameSettings.CrosshairDynamic ? "Preview, 2x (opens up as if you were shooting)" : "Preview, 2x", 15, new Color(0f, 0f, 0f, 0.6f));

        y = preview.yMax + 20f;
        y = OptionRow(right, y, "Outline", new[] { "Off", "On" }, GameSettings.CrosshairOutline ? 1 : 0, i => GameSettings.SetCrosshair(outline: i == 1), column);
        y = OptionRow(right, y, "Center dot", new[] { "Off", "On" }, GameSettings.CrosshairDot ? 1 : 0, i => GameSettings.SetCrosshair(dot: i == 1), column);
        y = OptionRow(right, y, "Shape", new[] { "Cross", "T" }, GameSettings.CrosshairT ? 1 : 0, i => GameSettings.SetCrosshair(tShape: i == 1), column);
        OptionRow(right, y, "Style (dynamic opens up with the spread)", new[] { "Static", "Dynamic" }, GameSettings.CrosshairDynamic ? 1 : 0,
                  i => GameSettings.SetCrosshair(dynamic: i == 1), column);

        if (Button(new Rect(x + w / 2f - 470f, top + height - 80f, 300f, 56f), "RESET")) GameSettings.ResetCrosshair();
        if (Button(new Rect(x + w / 2f - 150f, top + height - 80f, 300f, 56f), "BACK")) crosshairOpen = false;
    }

    float SettingSlider(float x, float y, float w, string label, float value, float min, float max, string format)
    {
        Label(new Rect(x, y, w, 30f), $"{label}   {value.ToString(format)}", 20, Color.white);
        return GUI.HorizontalSlider(new Rect(x, y + 34f, w, 24f), value, min, max);
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
        DrawMuteList(x + w + 20f, y);
        Label(new Rect(x, y + 15f, w, 60f), online ? "MENU" : "PAUSED", 44, Gold, TextAnchor.MiddleCenter);
        if (online) Label(new Rect(x, y + 62f, w, 24f), gm.Net.OnlineClient ? "The online game keeps running" : "The LAN game keeps running", 16, Dim, TextAnchor.MiddleCenter);
        y += 90f;
        if (Button(new Rect(x + 40f, y, w - 80f, 56f), "RESUME")) gm.SetPaused(false);
        y += 70f;
        if (gm.IsClient)
        {
            if (Button(new Rect(x + 40f, y, w - 80f, 56f), gm.Net.OnlineClient ? "LEAVE ROOM" : "LEAVE GAME")) LeaveGame();
            y += 70f;
        }
        else if (gm.Tutorial == null)
        {
            if (Button(new Rect(x + 40f, y, w - 80f, 56f), "RESTART MATCH")) gm.StartMatch();
            y += 70f;
        }
        if (gm.Tutorial != null)
        {
            if (Button(new Rect(x + 40f, y, w - 80f, 56f), "SKIP TUTORIAL")) gm.EndTutorial();
        }
        else if (Button(new Rect(x + 40f, y, w - 80f, 56f), online && !gm.IsClient ? "END LAN GAME" : "MAIN MENU")) LeaveGame();
        y += 70f;
        if (Button(new Rect(x + 40f, y, w - 80f, 56f), "SETTINGS")) settingsOpen = true;
        y += 70f;
        // iPhones and iPads close apps from the app switcher, not from inside the app.
        if (Application.platform != RuntimePlatform.IPhonePlayer && Button(new Rect(x + 40f, y, w - 80f, 56f), "QUIT GAME")) gm.QuitGame();
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
            bool room = gm.Net.OnlineClient;
            Label(new Rect(0f, RefHeight - 190f, width, 40f), room ? "Back to the room in a few seconds..." : "Waiting for the host to play again...",
                  22, Dim, TextAnchor.MiddleCenter);
            if (Button(new Rect(center - 120f, RefHeight - 130f, 240f, 60f), room ? "LEAVE ROOM" : "LEAVE GAME")) LeaveGame();
            return;
        }
        if (Button(new Rect(center - 260f, RefHeight - 130f, 240f, 60f), "PLAY AGAIN")) gm.StartMatch();
        if (Button(new Rect(center + 20f, RefHeight - 130f, 240f, 60f), "MAIN MENU")) gm.ReturnToMenu();
    }

    void DrawBuyMenu()
    {
        var me = gm.Player.Self;
        bool canBuy = gm.CanBuy(me), canSell = gm.CanSell(me);
        float w = 940f, h = 690f, x = (width - w) / 2f, y = (RefHeight - h) / 2f;
        Fill(new Rect(x, y, w, h), new Color(0f, 0f, 0f, 0.85f));
        Label(new Rect(x + 30f, y + 15f, 400f, 50f), "BUY MENU", 36, Gold);
        Label(new Rect(x + w - 330f, y + 15f, 300f, 50f), $"$ {me.Money}", 36, MoneyGreen, TextAnchor.MiddleRight);
        string buyTime = canBuy ? $"{Mathf.Max(0f, gm.BuyTimeLeft):0}s of buy time left" : "Buy time is over";
        Label(new Rect(x + 30f, y + 62f, w - 60f, 28f), $"{buyTime}  ·  {Mathf.Max(0f, gm.SellTimeLeft):0}s to sell  ·  {(GameInput.Touch ? "CLOSE" : "B / Esc")} to close  ·  weapons carry over if you survive", 18, Dim);

        string[] titles = { "PISTOLS", "SMG / SHOTGUN", "RIFLES & HEAVY", "EQUIPMENT" };
        WeaponData[][] columns =
        {
            new[] { WeaponData.Glock, WeaponData.Usp, WeaponData.TecDc9, WeaponData.M1911, WeaponData.Deagle },
            new[] { WeaponData.Uzi, WeaponData.Mp5, WeaponData.Shotgun },
            new[] { WeaponData.Ak47, WeaponData.M4a1, WeaponData.Svd, WeaponData.Awp, WeaponData.Rpg },
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
                    if (Button(new Rect(cx, cy, columnWidth, 80f), text, false, canBuy && !owned && me.Money >= weapon.Price))
                        gm.BuyWeapon(weapon);
                    cy += 92f;
                }
            }
            else
            {
                bool hasVest = me.Armor >= 100;
                if (Button(new Rect(cx, cy, columnWidth, 80f), $"Kevlar Vest\n{(hasVest ? "OWNED" : "$" + WeaponData.KevlarPrice)}",
                           false, canBuy && !hasVest && me.Money >= WeaponData.KevlarPrice))
                    gm.BuyArmor(false);
                cy += 92f;
                bool full = hasVest && me.Helmet;
                int helmetPrice = hasVest ? WeaponData.KevlarHelmetPrice - WeaponData.KevlarPrice : WeaponData.KevlarHelmetPrice;
                if (Button(new Rect(cx, cy, columnWidth, 80f), $"Kevlar + Helmet\n{(full ? "OWNED" : "$" + helmetPrice)}",
                           false, canBuy && !full && me.Money >= helmetPrice))
                    gm.BuyArmor(true);
                cy += 92f;
                if (me.Team == Team.Swat
                    && Button(new Rect(cx, cy, columnWidth, 80f), $"Defuse Kit\n{(me.HasDefuseKit ? "OWNED" : "$" + WeaponData.DefuseKitPrice)}",
                              false, canBuy && !me.HasDefuseKit && me.Money >= WeaponData.DefuseKitPrice))
                    gm.BuyDefuseKit();
            }
        }

        // Selling your own guns back (never the knife).
        float sy = y + h - 130f;
        Fill(new Rect(x + 30f, sy - 12f, w - 60f, 1f), new Color(1f, 1f, 1f, 0.15f));
        Label(new Rect(x + 30f, sy, 400f, 30f), "SELL YOUR WEAPONS", 20, Dim);
        Label(new Rect(x + w - 530f, sy, 500f, 30f), "full price in the round you bought it, half after that", 16, Dim, TextAnchor.MiddleRight);
        float sellWidth = (w - 60f - 20f) / 2f;
        var slots = new[] { WeaponSlot.Primary, WeaponSlot.Secondary };
        for (int i = 0; i < slots.Length; i++)
        {
            var weapon = me.Get(slots[i]);
            string text = weapon != null ? $"Sell {weapon.Data.Name}   +${weapon.SellPrice}"
                        : slots[i] == WeaponSlot.Primary ? "No main weapon" : "No pistol";
            if (Button(new Rect(x + 30f + i * (sellWidth + 20f), sy + 38f, sellWidth, 64f), text, false, canSell && weapon != null))
                gm.SellWeapon(slots[i]);
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
                // People with an online account: how long they have played Low Strike.
                int played = c.IsHuman && Accounts.IsOnline ? Online.PlayedSeconds(c.IsPlayer ? Accounts.Current : c.DisplayName) : -1;
                if (played >= 0) Label(new Rect(x + 20f, y, 500f, rowHeight), PlayedText(played), 17, Dim, TextAnchor.MiddleRight);
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
            if (player.IsScoped) DrawScope(player.Self.Current?.Data == WeaponData.Svd);
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
            Label(new Rect(0f, RefHeight - 82f, width, 30f), GameInput.Touch ? "Tap < or > to switch player" : "Left / right click to switch player", 18, Dim, TextAnchor.MiddleCenter);
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
            if (!voice.InSession) Label(new Rect(272f, y, 420f, 30f), "Voice chat works in LAN and online games", 16, Dim);
            y += 34f;
        }
        else if (openMic)
        {
            VoiceRow(y, "Open mic", Dim, voice.Level);
            y += 34f;
        }
        else
        {
            VoiceRow(y, $"Hold {GameInput.TalkName} to talk", Dim);
            y += 34f;
        }
        foreach (string name in voice.Speaking())
        {
            VoiceRow(y, name, SpeakerColor(name));
            y += 34f;
        }
    }

    /// <summary>
    /// Voice chat in a lobby (an online room, or a LAN game before its match), as in the match: who is talking, and
    /// how to talk (hold V; on a phone, a button to hold). One line, centred in <paramref name="w"/>.
    /// </summary>
    void DrawLobbyVoice(float x, float y, float w)
    {
        var voice = VoiceChat.Instance;
        if (voice == null || !voice.InLobby) return;
        if (!GameSettings.VoiceChat)
        {
            Label(new Rect(x, y, w, 36f), "Voice chat is off (Settings)", 17, Dim, TextAnchor.MiddleCenter);
            return;
        }
        float left = x;
        if (GameInput.Touch)
        {
            var rect = new Rect(x, y, 260f, 46f);
            bool held = voice.Holding;
            Fill(rect, held ? new Color(0.85f, 0.55f, 0.15f, 0.95f) : new Color(0.18f, 0.18f, 0.18f, 0.9f));
            Label(rect, "HOLD TO TALK", 19, Color.white, TextAnchor.MiddleCenter);
            if (GUI.RepeatButton(rect, GUIContent.none, GUIStyle.none)) voice.LobbyButtonUntil = Time.unscaledTime + 0.2f;
            left += 276f;
        }
        var names = new List<string>();
        if (voice.Talking) names.Add(Loc.T("You"));
        names.AddRange(voice.Speaking());
        bool openMic = GameSettings.Voice == VoiceMode.OpenMic;
        string text;
        Color color = Dim;
        if ((voice.Holding || openMic) && voice.Problem != null)
        {
            text = voice.Problem;
            color = Danger;
        }
        else if ((voice.Holding || openMic) && voice.State != VoiceChat.MicState.On) text = "Starting the microphone...";
        else if (names.Count > 0)
        {
            text = Loc.T("Talking now: {0}").Replace("{0}", string.Join(", ", names));
            color = MoneyGreen;
        }
        else text = openMic ? "Open mic: the others hear you when you speak" : $"Hold {GameInput.TalkName} to talk to the room";
        float width = GameInput.Touch ? w - (left - x) : w;
        Label(new Rect(left, y, width, 46f), text, 18, color, GameInput.Touch ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter);
        if (voice.Talking && !GameInput.Touch) LevelBar(new Rect(x + w / 2f - 60f, y + 40f, 120f, 6f), voice.Level);
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
            string sites = gm.Map.ClosedSite == null ? "site A or B" : gm.Map.ClosedSite == "A" ? "site B (A is closed)" : "site A (B is closed)";
            hint = site != null ? $"Hold {GameInput.UseName} to plant the bomb on {site}" : $"You have the bomb: plant it on {sites}   (knife in hand + {GameInput.DropName}: drop it)";
            color = TerroristColor;
        }
        else if (bomb.State == BombState.Planted && me.Team == Team.Swat
                 && Vector3.Distance(me.transform.position, bomb.Position) < BombManager.DefuseRadius)
        {
            hint = me.HasDefuseKit ? $"Hold {GameInput.UseName} to defuse (5 s with your kit)" : $"Hold {GameInput.UseName} to defuse (10 s; a defuse kit makes it 5 s)";
            color = SwatColor;
        }
        if (hint == null && gm.Drops.Nearest(me, WeaponDrops.SwapRadius) is WeaponDrops.Dropped gun && me.Get(gun.Data.Slot) != null)
            hint = $"{(GameInput.Touch ? "Tap" : "Press")} {GameInput.UseName} to take the {gun.Data.Name}";
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
        Label(new Rect(x, 54f, w, 26f), gm.Warmup ? "WARMUP" : $"Round {gm.Round}  ·  first to {gm.RoundsToWin}", 16,
              gm.Warmup ? Gold : Dim, TextAnchor.MiddleCenter);
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

    Rect RadarDot(Rect area, Vector3 world, float size)
    {
        Vector2 p = gm.Map.ToRadar(world);
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
        if (player.Self.Current.Data.ZoomFov > 0f) return;   // sniper rifles (AWP, SVD): no crosshair, only the scope
        var center = new Vector2(width / 2f, RefHeight / 2f);
        if (player.Self.Current.Data.IsMelee)
        {
            DrawCrosshairShape(center, 0f, true);
            return;
        }
        // Dynamic: the gap matches the real spread cone at a 75 degree field of view (it closes up when the
        // first shot is dead on). Static: always the set gap.
        float spread = GameSettings.CrosshairDynamic
            ? Mathf.Tan(player.CurrentSpread * Mathf.Deg2Rad) * RefHeight / (2f * Mathf.Tan(37.5f * Mathf.Deg2Rad))
            : 0f;
        DrawCrosshairShape(center, GameSettings.CrosshairGap + spread, false);
    }

    /// <summary>The crosshair from the settings: four lines (three for the T shape) <paramref name="gap"/> away
    /// from the centre, an optional dot and a one-pixel black outline; just the dot for the knife.</summary>
    static void DrawCrosshairShape(Vector2 center, float gap, bool dotOnly)
    {
        var color = GameSettings.CrosshairColor;
        float length = GameSettings.CrosshairLength, thick = GameSettings.CrosshairThickness;
        var rects = new List<Rect>();
        if (!dotOnly && length > 0f)
        {
            rects.Add(new Rect(center.x - gap - length, center.y - thick / 2f, length, thick));
            rects.Add(new Rect(center.x + gap, center.y - thick / 2f, length, thick));
            if (!GameSettings.CrosshairT) rects.Add(new Rect(center.x - thick / 2f, center.y - gap - length, thick, length));
            rects.Add(new Rect(center.x - thick / 2f, center.y + gap, thick, length));
        }
        if (dotOnly || GameSettings.CrosshairDot)
        {
            float dot = Mathf.Max(thick, dotOnly ? 4f : 2f);
            rects.Add(new Rect(center.x - dot / 2f, center.y - dot / 2f, dot, dot));
        }
        if (GameSettings.CrosshairOutline)
            foreach (var r in rects)
                Fill(new Rect(r.x - 1f, r.y - 1f, r.width + 2f, r.height + 2f), new Color(0f, 0f, 0f, color.a));
        foreach (var r in rects) Fill(r, color);
    }

    void DrawScope(bool svd)
    {
        float side = RefHeight, x = (width - side) / 2f;
        GUI.DrawTexture(new Rect(x, 0f, side, side), svd ? svdScopeMask : scopeMask);
        Fill(new Rect(0f, 0f, x + 1f, RefHeight), Color.black);
        Fill(new Rect(x + side - 1f, 0f, x + 1f, RefHeight), Color.black);
        if (!svd)
        {
            Fill(new Rect(x, RefHeight / 2f - 0.75f, side, 1.5f), Color.black);
            Fill(new Rect(width / 2f - 0.75f, 0f, 1.5f, RefHeight), Color.black);
            return;
        }
        // The SVD's: a thick black ring and thin lines from it towards the middle, with a small gap at the centre.
        float cx = width / 2f, cy = RefHeight / 2f, gap = side * 0.022f, reach = side * 0.46f;
        Fill(new Rect(cx - reach, cy - 0.75f, reach - gap, 1.5f), Color.black);
        Fill(new Rect(cx + gap, cy - 0.75f, reach - gap, 1.5f), Color.black);
        Fill(new Rect(cx - 0.75f, cy - reach, 1.5f, reach - gap), Color.black);
        Fill(new Rect(cx - 0.75f, cy + gap, 1.5f, reach - gap), Color.black);
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
            Label(new Rect(0f, RefHeight - 60f, width, 30f), $"{(GameInput.Touch ? "Tap" : "Press")} {GameInput.BuyName} to buy  ({gm.BuyTimeLeft:0}s)", 20, Gold, TextAnchor.MiddleCenter);
        else if (gm.CanSell(me) && !gm.BuyMenuOpen && (me.Primary != null || me.Secondary != null))
            Label(new Rect(0f, RefHeight - 60f, width, 30f), $"{(GameInput.Touch ? "Tap" : "Press")} {GameInput.BuyName} to sell  ({gm.SellTimeLeft:0}s)", 20, Gold, TextAnchor.MiddleCenter);
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
        text = Loc.T(text);
        if (!Loc.English) size = FitSize(text, size, rect.width);
        var style = Style(size, anchor);
        GUI.color = new Color(0f, 0f, 0f, 0.8f * color.a);
        GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, style);
        GUI.color = color;
        GUI.Label(rect, text, style);
        GUI.color = Color.white;
    }

    // A translation can be longer than the English the layout was made for: its font shrinks (to 70% at most) to fit.
    readonly Dictionary<string, int> fitted = new Dictionary<string, int>();

    int FitSize(string text, int size, float width)
    {
        if (string.IsNullOrEmpty(text) || width <= 0f) return size;
        string key = size + "|" + (int)width + "|" + text;
        if (fitted.TryGetValue(key, out int fit)) return fit;
        fit = size;
        int smallest = Mathf.Max(10, Mathf.RoundToInt(size * 0.7f));
        var content = new GUIContent(text);
        while (fit > smallest && Style(fit, TextAnchor.MiddleLeft).CalcSize(content).x > width) fit--;
        if (fitted.Count > 3000) fitted.Clear();
        fitted[key] = fit;
        return fit;
    }

    void WrapLabel(Rect rect, string text, int size, Color color)
    {
        text = Loc.T(text);
        if (!Loc.English) (size, rect.height) = FitWrapped(text, size, rect);
        var style = WrapStyle(size);
        GUI.color = color;
        GUI.Label(rect, text, style);
        GUI.color = Color.white;
    }

    GUIStyle WrapStyle(int size)
    {
        int key = 1000000 + size;
        if (!styles.TryGetValue(key, out var style))
        {
            style = new GUIStyle(Style(size, TextAnchor.UpperLeft)) { wordWrap = true, clipping = TextClipping.Clip };
            styles[key] = style;
        }
        return style;
    }

    // The same for wrapped text: smaller until its lines fit the box (to 70% at most). A box made for one line of
    // English may take two lines of the translation, a little taller than the box.
    readonly Dictionary<string, (int, float)> fittedWrap = new Dictionary<string, (int, float)>();

    (int, float) FitWrapped(string text, int size, Rect rect)
    {
        if (string.IsNullOrEmpty(text) || rect.width <= 0f || rect.height <= 0f) return (size, rect.height);
        string key = size + "|" + (int)rect.width + "|" + (int)rect.height + "|" + text;
        if (fittedWrap.TryGetValue(key, out var fit)) return fit;
        int smallest = Mathf.Max(10, Mathf.RoundToInt(size * 0.7f)), font = size;
        var content = new GUIContent(text);
        float room = Mathf.Max(rect.height, smallest * 2.6f);
        while (font > smallest && WrapStyle(font).CalcHeight(content, rect.width) > room) font--;
        fit = (font, Mathf.Max(rect.height, Mathf.Min(room, WrapStyle(font).CalcHeight(content, rect.width))));
        if (fittedWrap.Count > 3000) fittedWrap.Clear();
        fittedWrap[key] = fit;
        return fit;
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

    /// <param name="ring">Where a thick black ring starts inside the lens (1: no ring, just the edge).</param>
    static Texture2D BuildScopeMask(int size, float ring = 1f)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        float radius = size / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius)) / radius;
                float edge = Mathf.Min(ring, 1f) * 0.96f;
                pixels[y * size + x] = new Color32(0, 0, 0, (byte)(Mathf.Clamp01((d - edge) / 0.02f) * 255f));
            }
        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }
}

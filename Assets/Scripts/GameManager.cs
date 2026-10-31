using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using Random = UnityEngine.Random;

public enum MatchState { Menu, Freeze, Live, RoundEnd, MatchOver }

public enum PlayerSide { Terrorists, Swat, Spectate }

public class KillFeedEntry
{
    public string Killer, Victim, Weapon;
    public Team KillerTeam, VictimTeam;
    public bool Headshot;
    public bool Noscope;
    public float Time;
}

/// <summary>
/// Runs the match: builds the map, spawns both teams, and handles rounds, the bomb, money, the kill feed
/// and the spectator camera. Put it on an empty GameObject in a scene and press Play.
///
/// In a LAN game the host's GameManager runs everything as usual (other players are RemotePlayerControllers);
/// a client's GameManager runs nothing and only mirrors what the host sends (see NetSession).
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public const float FreezeTime = 4f;
    public const float BuyTime = 25f;       // counted from the start of the freeze
    public const float SellTime = 30f;      // selling your own guns back, counted the same way
    public const float RoundTime = 115f;
    public const float RoundEndDelay = 5f;
    public const int StartMoney = 800;
    public const int MaxMoney = 16000;
    public const float WarmupTime = 30f;
    public const int WarmupMoney = 16000;

    /// <summary>
    /// The warm-up before every match: 30 s with no bomb, $16000 to buy anything (in your spawn), and back to life a
    /// moment after dying. Then everything is taken away: the match starts with a pistol and $800.
    /// </summary>
    public bool Warmup { get; private set; }
    bool noWarmup;   // -ds-no-warmup: straight to the first round (tests)
    bool dropTest;   // -ds-drop-test: bots drop their main guns every round (tests)
    int droppedInRound = -1;
    readonly Dictionary<Combatant, float> respawnAt = new Dictionary<Combatant, float>();
    public const int WinReward = 3250;
    public const int LossReward = 1400;
    public const int LossBonusStep = 500;
    public const int PlantedLossBonus = 800;   // Terrorists who planted but still lost

    static readonly string[] TerroristNames =
        { "Viper", "Jackal", "Cobra", "Scorpion", "Raven", "Wolf", "Dagger", "Ghost", "Mamba", "Hyena", "Fang", "Shadow" };
    static readonly string[] SwatNames =
        { "Hawk", "Falcon", "Ranger", "Bishop", "Echo", "Titan", "Sentinel", "Patriot", "Eagle", "Knight", "Shield", "Arrow" };

    /// <summary>True for a bot's name (no account may take one).</summary>
    public static bool IsBotName(string name) =>
        Array.Exists(TerroristNames, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) ||
        Array.Exists(SwatNames, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

    // Match settings, chosen in the main menu (in a LAN game the host's count).
    public PlayerSide Side = PlayerSide.Terrorists;
    /// <summary>Players in each team (people, and bots for the rest), typed in the menu: the teams may differ.</summary>
    public int TerroristCount = 5, SwatCount = 5;
    public const int MaxPerTeam = 10;
    /// <summary>The bigger team's size.</summary>
    public int TeamSize => Mathf.Max(TerroristCount, SwatCount);
    public int PlayersOn(Team team) => team == Team.Terrorists ? TerroristCount : SwatCount;
    public BotDifficulty Difficulty = BotDifficulty.Normal;
    public int RoundsToWin = 8;
    public bool FriendlyFire;
    /// <summary>The bombsite shut when no team has more than 3 players ("A" or "B"), unless <see cref="CloseHalf"/> is
    /// off (the menu's "Whole map"); both are saved.</summary>
    public string ClosedSite = "B";
    public bool CloseHalf = true;
    public string ActiveClosedSite => Tutorial == null && CloseHalf && TeamSize <= 3 && MapCatalog.Find(MapId)?.OneSite != true ? ClosedSite : null;
    const string ClosedSiteKey = "DesertStrike.closedSite", CloseHalfKey = "DesertStrike.closeHalf";
    const string TerroristCountKey = "DesertStrike.terrorists", SwatCountKey = "DesertStrike.swat";

    /// <summary>How many players each team has (1 to <see cref="MaxPerTeam"/>); saved.</summary>
    public void SetTeamCounts(int terrorists, int swat)
    {
        TerroristCount = Mathf.Clamp(terrorists, 1, MaxPerTeam);
        SwatCount = Mathf.Clamp(swat, 1, MaxPerTeam);
        PlayerPrefs.SetInt(TerroristCountKey, TerroristCount);
        PlayerPrefs.SetInt(SwatCountKey, SwatCount);
    }
    /// <summary>The map chosen in the menu (saved); in a LAN game the client plays the host's map instead.</summary>
    public string MapId { get; private set; } = MapCatalog.DefaultId;

    public MatchState State { get; private set; } = MatchState.Menu;
    public GameMap Map { get; private set; }
    public NetSession Net { get; private set; }
    public Friends Friends { get; private set; }
    public BombManager Bomb { get; private set; }
    public WeaponDrops Drops { get; private set; }
    public Camera MainCamera { get; private set; }
    public PlayerController Player { get; private set; }
    public Combatant Spectated { get; private set; }
    public readonly List<Combatant> Combatants = new List<Combatant>();
    public readonly List<KillFeedEntry> KillFeed = new List<KillFeedEntry>();

    public int Round { get; private set; }
    public int TerroristWins { get; private set; }
    public int SwatWins { get; private set; }
    public string Banner { get; private set; }
    public Team? LastWinner { get; private set; }
    public float StateEndsAt { get; private set; }
    public bool IsPaused { get; private set; }
    /// <summary>The console is open (` in a match): typing there, not playing (see Cheats).</summary>
    public bool ConsoleOpen { get; set; }
    /// <summary>"Cheats for everyone": an admin let every player in this match use the console's cheats (Cheats). Kept
    /// where the match runs and sent to every game with the match; off again at every new match.</summary>
    public bool CheatsForEveryone { get; private set; }

    /// <summary>Where the match runs: an admin turns cheats for everyone on or off (this game's own player, or a guest
    /// the host vouched for: RemotePlayerController). Every player sees who did it.</summary>
    public void SetCheatsForEveryone(bool on, string by)
    {
        if (Net.IsClient || CheatsForEveryone == on) return;
        CheatsForEveryone = on;
        Debug.Log($"[Cheats] Cheats for everyone {(on ? "on" : "off")} ({by})");
        ShowMessage(on ? $"{by} turned on cheats for everyone: ` or CONSOLE, then /cheats on" : $"{by} turned off cheats for everyone", 6f);
        if (on) return;
        // Off: the cheats of everyone who is not an admin stop at once (their games switch them off too).
        foreach (var c in Combatants)
        {
            var remote = c.GetComponent<RemotePlayerController>();
            bool admin = remote != null ? remote.Peer != null && remote.Peer.IsAdmin : c.IsPlayer && Online != null && Online.IsAdmin;
            if (!admin) c.CheatFlags = 0;
        }
    }
    public bool BuyMenuOpen { get; private set; }
    public float HitMarkerTime { get; private set; } = -1f;
    public bool HitMarkerHeadshot { get; private set; }
    public bool HitMarkerKill { get; private set; }
    public float DamageFlashTime { get; private set; } = -1f;
    public string PlayerAmmoResupply { get; private set; }   // what the free round-start ammo gave the player
    public string Message { get; private set; }              // short center-screen news, e.g. the bomb was planted
    public float MessageUntil { get; private set; }
    public string MenuNotice { get; set; }                   // shown in the menu, e.g. why a LAN game ended

    /// <summary>This PC is a network client in a running match: the host decides everything.</summary>
    public bool IsClient => clientMatch && Net.IsClient;
    /// <summary>-ds-server: an online game host with no player of its own (see <see cref="OnlineHost"/>).</summary>
    public bool ServerMode { get; private set; }
    public OnlineClient Online { get; private set; }
    public bool SpectatingNow => State != MatchState.Menu && (Player == null || !Player.Self.IsAlive);
    public float BuyTimeLeft => Tutorial != null ? 99f : Warmup ? StateEndsAt - Time.time : BuyTime - (Time.time - roundStartedAt);
    public float SellTimeLeft => Warmup ? StateEndsAt - Time.time : SellTime - (Time.time - roundStartedAt);
    /// <summary>When this round went live (its buy time began <see cref="FreezeTime"/> before).</summary>
    public float RoundLiveAt => roundStartedAt + FreezeTime;

    readonly List<BotController> bots = new List<BotController>();
    float roundStartedAt;
    int terroristLossStreak, swatLossStreak;
    int nextNetId = 1;
    bool clientMatch;

    // Command-line automation (used for smoke tests): -ds-autostart, -ds-side, -ds-money, -ds-quit-after,
    // -ds-capture, -ds-timescale, -ds-host, -ds-join <ip>, -ds-find, -ds-name <name>, -ds-client-fire,
    // -ds-perf (logs FPS and drawn bodies), -ds-nocull, -ds-quality <low|medium|high>, -ds-fps <limit>, -ds-scale <0.25-1>,
    // -ds-map <id>
    bool autoStart, autoHost, autoFind, clientFireTest, perfLog;
    string autoJoin;
    WeaponSlot? holdSlot;   // -ds-hold <primary|secondary|knife>: the player keeps this weapon out (screenshots)
    WeaponData giveWeapon;  // -ds-give <weapon>: the player gets this weapon every round
    bool fireTest;          // -ds-fire-test: the player fires straight ahead every 3 s, with a screenshot just after
    string radarFile;       // -ds-radar-png <file>: saves the map's radar picture (checking the map's orientation)
    string navMeshFile;     // -ds-navmesh-obj <file>: saves where bots can walk, as an .obj (finding cut-off places)
    string exportClosed;    // -ds-export-closed <folder>: writes the map and its closing walls for Blender, and quits (see MapExport)
    bool startTutorial;     // -ds-tutorial
    bool cheatsTest;        // -ds-cheats-test (logged in as an admin): every cheat on, flies up firing, logs what each did
    bool pauseMenuTest;     // -ds-pause-menu: opens the pause menu once the match is on (screenshots of it)
    bool cheatsShopTested;
    float cheatsEveryoneFor = -1f, cheatsEveryoneAt = -1f;   // -ds-cheats-everyone [seconds]: an admin host turns it on (and off)
    float cheatsTestAt = -1f;
    bool tutorialTest;      // -ds-tutorial-test: plays the tutorial through with pretend controls (GameInput.Fake), logging each step
    Tutorial.Step testStep = (Tutorial.Step)(-1);
    float testStepAt;
    string hideParts;       // -ds-hide <text>: map parts whose names contain it are not drawn (finding what costs time)
    bool poseGallery;       // -ds-pose-gallery: figures in every pose in a row above the map, seen from the side
    int sellTestStep = -1;  // -ds-sell-test: in round 1 the player buys, sells and opens the shop by itself (logged)
    List<(Vector3 Eye, Vector3 Target)> views;   // -ds-view "x,y,z,tx,ty,tz;...": the menu camera shows these spots
    int viewShot = -1;                           // (2 s each, without the menu), with a screenshot of each in -ds-capture
    bool keepRunning;       // -ds-background: tests keep running when their window is not in front
    bool teleportTest;      // -ds-teleport-test: a client jumps 8 m ahead every 3 s, like a cheat (the host must refuse it)
    float nextTeleport;
    int startMoney = StartMoney;
    float timeScale = 1f;
    float quitAt = -1f;
    string captureDirectory;
    float captureEvery = 8f;   // -ds-capture-every <seconds>
    float nextCaptureAt, nextTestShot;
    int captureIndex;
    float perfSince = -1f;
    string profileFile;   // -ds-profile <file>: records 600 frames of a live round with Unity's profiler (development builds)
    int profileFramesLeft = 600;
    int perfFrames, perfDrawn, perfInView;

    void Awake()
    {
        Instance = this;
        GameSettings.Load();
        SoundFX.Init();
        CreateSun();
        MainCamera = CreateCamera();
        // The saved map, unless it needs an account: nobody is logged in yet (OnAccountChanged loads it then).
        var saved = MapCatalog.Find(PlayerPrefs.GetString(MapKey, MapCatalog.DefaultId));
        MapId = saved != null && (!saved.AccountOnly || Accounts.LoggedIn) ? saved.Id : MapCatalog.DefaultId;
        GameMap.KeepMeshesApart = Array.IndexOf(Environment.GetCommandLineArgs(), "-ds-export-closed") >= 0;
        LoadMap(MapId);
        Accounts.Changed += OnAccountChanged;
        gameObject.AddComponent<VisibilityCuller>();
        gameObject.AddComponent<RenderScaler>();
        gameObject.AddComponent<PerfStats>();
        Net = gameObject.AddComponent<NetSession>();
        Friends = gameObject.AddComponent<Friends>();
        gameObject.AddComponent<TouchControls>();
        gameObject.AddComponent<VoiceChat>();
        Bomb = gameObject.AddComponent<BombManager>();
        Drops = gameObject.AddComponent<WeaponDrops>();
        gameObject.AddComponent<BotKnowledge>();
        gameObject.AddComponent<PlayerTactics>();
        string savedSite = PlayerPrefs.GetString(ClosedSiteKey, "B");
        ClosedSite = savedSite == "A" ? "A" : "B";
        CloseHalf = PlayerPrefs.GetInt(CloseHalfKey, 1) == 1;
        TerroristCount = Mathf.Clamp(PlayerPrefs.GetInt(TerroristCountKey, 5), 1, MaxPerTeam);
        SwatCount = Mathf.Clamp(PlayerPrefs.GetInt(SwatCountKey, 5), 1, MaxPerTeam);
        ReadCommandLine();
        if (siteCheck && views != null) CloseSite(ClosedSite);   // -ds-closed-site with -ds-view: pictures of the wall
        if (exportClosed != null)
        {
            MapExport.ClosedHalves(Map, exportClosed);
            Application.Quit();
        }
        if (ServerMode) gameObject.AddComponent<OnlineHost>();
        else
        {
            Online = gameObject.AddComponent<OnlineClient>();
            if (GetComponent<GameUI>() == null) gameObject.AddComponent<GameUI>();
        }
    }

    void Start()
    {
        if (autoHost) Net.StartHost();
        if (autoJoin != null) Net.Join(autoJoin);
        if (autoStart && !autoHost && autoJoin == null) StartMatch();
        if (startTutorial) StartTutorial();
    }

    // ----------------------------------------------------------------- match flow

    public void StartMatch()
    {
        if (Net.IsClient) return;
        ClearMatch();
        CheatsForEveryone = false;   // a new match starts without (an admin turns it on again)
        CloseSite(ActiveClosedSite);
        if (siteCheck && Map.ClosedSite != null) Invoke(nameof(LogSiteCheck), 1.5f);
        Round = 0;
        TerroristWins = SwatWins = 0;
        terroristLossStreak = swatLossStreak = 0;
        int terroristHumans = 0, swatHumans = 0;

        if (Side != PlayerSide.Spectate)
        {
            var team = Side == PlayerSide.Terrorists ? Team.Terrorists : Team.Swat;
            SpawnPlayer(team, Net.IsHost ? Net.PlayerName : "You");
            if (team == Team.Terrorists) terroristHumans++; else swatHumans++;
        }
        foreach (var peer in Net.Peers)
        {
            if (peer.Spectator) continue;   // watching (online rooms)
            AddRemotePlayer(peer, midRound: false);
            if (peer.Team == Team.Terrorists) terroristHumans++; else swatHumans++;
        }

        // Bots fill the rest of each team.
        var terroristNames = new Queue<string>(Shuffled(TerroristNames));
        var swatNames = new Queue<string>(Shuffled(SwatNames));
        for (int i = terroristHumans; i < TerroristCount; i++) SpawnBot(Team.Terrorists, BotName(terroristNames, TerroristNames, i));
        for (int i = swatHumans; i < SwatCount; i++) SpawnBot(Team.Swat, BotName(swatNames, SwatNames, i));

        foreach (var c in Combatants) c.Money = startMoney;
        Debug.Log($"[DesertStrike] Match started on {Map.Name}: {Side}, {TerroristCount}v{SwatCount}, {Difficulty}, first to {RoundsToWin}, friendly fire {(FriendlyFire ? "on" : "off")}, {Net.Peers.Count} remote players");
        matchStartedAt = Time.time;
        if (noWarmup || Tutorial != null) StartRound();
        else StartWarmup();
    }

    // The next unused name; a big team that runs out of names numbers them ("Viper 2").
    static string BotName(Queue<string> names, string[] all, int index) =>
        names.Count > 0 ? names.Dequeue() : $"{all[index % all.Length]} {index / all.Length + 1}";

    void StartWarmup()
    {
        Warmup = true;
        Round = 0;
        State = MatchState.Live;
        roundStartedAt = Time.time;
        StateEndsAt = Time.time + WarmupTime;
        Banner = null;
        BuyMenuOpen = false;
        Spectated = null;
        Effects.ClearDecals();
        Drops.Clear();
        Bomb.Clear();   // no bomb in the warm-up
        respawnAt.Clear();
        foreach (var c in Combatants) RespawnForWarmup(c);
        PlayerTactics.Instance.AssignStyles(bots);
        var terroristBots = bots.FindAll(b => b.Self.Team == Team.Terrorists);
        var swatBots = Shuffled(bots.FindAll(b => b.Self.Team == Team.Swat));
        BotController.PlanRound(terroristBots, swatBots, Map, Time.time);
        ShowMessage("WARMUP: 30 SECONDS", 4f);
        SoundFX.Play(SoundFX.RoundStart, Vector3.zero, 0.4f, 1f, false);
        Debug.Log("[DesertStrike] Warm-up started");
    }

    // Warm-up: back at the team's spawn, with $16000 and a pistol (bots buy at once).
    void RespawnForWarmup(Combatant c)
    {
        c.ResetForRound(false);
        c.Money = WarmupMoney;
        bool terrorist = c.Team == Team.Terrorists;
        var spawns = terrorist ? Map.TerroristSpawns : Map.SwatSpawns;
        c.SpawnCount++;
        c.GetComponent<ICombatantController>().Respawn(spawns[Random.Range(0, spawns.Count)], terrorist ? Map.TerroristSpawnYaw : Map.SwatSpawnYaw);
        var bot = c.GetComponent<BotController>();
        if (bot != null) bot.BuyGear();
        if (c.IsPlayer) Spectated = null;
    }

    // The warm-up is over: everything is taken away and the first round starts (pistol, $800, a clean score).
    void EndWarmup()
    {
        Warmup = false;
        respawnAt.Clear();
        KillFeed.Clear();
        foreach (var c in Combatants)
        {
            c.Kills = c.Deaths = 0;
            c.Money = startMoney;
        }
        Round = 0;
        Debug.Log("[DesertStrike] Warm-up over");
        StartRound();
    }

    public void ReturnToMenu()
    {
        if (Tutorial != null)
        {
            Tutorial = null;
            (Side, TerroristCount, SwatCount, Difficulty, RoundsToWin, FriendlyFire) = beforeTutorial;
        }
        ClearMatch();
        CloseSite(null);
        Net.Stop();
        clientMatch = false;
        State = MatchState.Menu;
        LoadMap(MapId);   // after a LAN game on the host's map, back to this player's own choice
    }

    /// <summary>Online game host: the match is over (or everyone left); back to the room's lobby. The players stay
    /// connected; those whose connection dropped are let go.</summary>
    public void BackToLobby()
    {
        ClearMatch();
        CloseSite(null);
        foreach (var peer in Net.Peers) peer.Player = null;
        Net.ForgetAwayPlayers();
        State = MatchState.Menu;
        Round = 0;
        TerroristWins = SwatWins = 0;
        Debug.Log("[DesertStrike] Back to the lobby");
    }

    /// <summary>Online player: the game server went back to the room's lobby after the match; still connected.</summary>
    public void EndClientMatch()
    {
        ClearMatch();
        clientMatch = false;
        State = MatchState.Menu;
        Debug.Log("[Net] The match is over; back in the room");
    }

    /// <summary>Online game host: the room's settings, from the online API (in the lobby only).</summary>
    public void ApplyRoomSettings(string mapId, int terrorists, int swat, BotDifficulty difficulty, int roundsToWin, bool friendlyFire, string closedSite)
    {
        if (closedSite == "A" || closedSite == "B")
        {
            ClosedSite = closedSite;
            CloseHalf = true;
        }
        else if (closedSite == "OFF") CloseHalf = false;   // the whole map
        if (State != MatchState.Menu) return;
        if (MapCatalog.Find(mapId) != null && mapId != MapId)
        {
            MapId = mapId;
            LoadMap(mapId);
        }
        TerroristCount = Mathf.Clamp(terrorists, 1, MaxPerTeam);
        SwatCount = Mathf.Clamp(swat, 1, MaxPerTeam);
        Difficulty = difficulty;
        RoundsToWin = Mathf.Clamp(roundsToWin, 1, 16);
        FriendlyFire = friendlyFire;
    }

    // ----------------------------------------------------------------- Tokens and the tutorial

    float matchStartedAt;

    /// <summary>
    /// The match is over: an online account is paid in Tokens for it (1 a kill, 5 for the win; the API
    /// caps what a player's own games report at a few a day). An online room's game host reports its matches itself,
    /// and the tutorial pays nothing.
    /// </summary>
    void OnMatchOver()
    {
        if (Tutorial != null || Net.OnlineClient || Net.OnlineHost || Player == null || Online == null || !Online.LoggedIn) return;
        var me = Player.Self;
        Team winner = TerroristWins >= RoundsToWin ? Team.Terrorists : Team.Swat;
        // The bots' difficulty multiplies the Tokens; a LAN guest does not know the host's, so it counts as Normal.
        Online.ReportMatch(me.Kills, me.Team == winner, Round, Time.time - matchStartedAt, Net.IsClient ? "Normal" : Difficulty.ToString());
    }

    /// <summary>The tutorial running now (a practice round), or null.</summary>
    public Tutorial Tutorial { get; private set; }

    (PlayerSide side, int terrorists, int swat, BotDifficulty difficulty, int rounds, bool friendlyFire) beforeTutorial;

    /// <summary>The practice round for new accounts (see <see cref="Tutorial"/>); the menu's settings come back after it.</summary>
    public void StartTutorial()
    {
        if (Net.Role != NetSession.Mode.Off || State != MatchState.Menu) return;
        beforeTutorial = (Side, TerroristCount, SwatCount, Difficulty, RoundsToWin, FriendlyFire);
        Side = PlayerSide.Terrorists;
        TerroristCount = SwatCount = 1;
        Difficulty = BotDifficulty.Easy;
        RoundsToWin = 1;
        FriendlyFire = false;
        Tutorial = new Tutorial();
        LoadMap(Tutorial.MapId);
        StartMatch();
        foreach (var c in Combatants) c.Money = WarmupMoney;   // enough for anything in the shop
        Debug.Log("[Tutorial] Started");
    }

    /// <summary>Ends the tutorial (finished or skipped): back to the menu and its settings.</summary>
    public void EndTutorial()
    {
        if (Tutorial == null) return;
        Debug.Log(Tutorial.Current == Tutorial.Step.Done ? "[Tutorial] Finished" : $"[Tutorial] Skipped at {Tutorial.Current}");
        SetPaused(false);
        ReturnToMenu();
    }

    // ----------------------------------------------------------------- maps

    const string MapKey = "DesertStrike.map";

    /// <summary>The map chosen in the menu: loads it at once (it shows behind the menu) and remembers it.</summary>
    public void SelectMap(string id)
    {
        var entry = MapCatalog.Find(id);
        if (entry == null || Net.IsClient || (entry.AccountOnly && !Accounts.LoggedIn)) return;
        MapId = id;
        PlayerPrefs.SetString(MapKey, id);
        LoadMap(id);
    }

    /// <summary>
    /// After logging in: back to the saved map if it needs an account (Halloween). After logging out: off such a map,
    /// and Extreme bots become Hard.
    /// </summary>
    void OnAccountChanged()
    {
        if (State != MatchState.Menu || Net.IsClient) return;
        var saved = MapCatalog.Find(PlayerPrefs.GetString(MapKey, MapCatalog.DefaultId));
        if (Accounts.LoggedIn)
        {
            if (saved != null && saved.AccountOnly && Map.Id != saved.Id)
            {
                MapId = saved.Id;
                LoadMap(MapId);
            }
            return;
        }
        if (MapCatalog.Find(Map.Id)?.AccountOnly == true)
        {
            MapId = MapCatalog.DefaultId;
            LoadMap(MapId);
        }
        if (Difficulty == BotDifficulty.Extreme) Difficulty = BotDifficulty.Hard;
    }

    /// <summary>A LAN client plays on the host's map (from the lobby, or when the match starts).</summary>
    public void UseHostMap(string id)
    {
        if (MapCatalog.Find(id) != null) LoadMap(id);
    }

    /// <summary>
    /// Shuts a bombsite's half of the map for a small match (null: both sites open again). A map with its own version
    /// for that (made in Blender) is loaded in its place; on any other map the game walls the half off itself.
    /// </summary>
    void CloseSite(string site)
    {
        LoadMap(Map.Id, MapCatalog.HasClosedVersion(Map.Id, site) ? site : null);
        Map.SetClosedSite(site);
    }

    /// <summary>Replaces the current map (its objects, NavMesh, radar and lighting) with another one, or with its own
    /// version for small matches that has site <paramref name="closed"/>'s half shut.</summary>
    public void LoadMap(string id, string closed = null)
    {
        if (Map != null && Map.Id == id && Map.ClosedVersion == closed) return;
        float started = Time.realtimeSinceStartup;
        if (Map != null)
        {
            Bomb?.Clear();
            Drops?.Clear();
            Effects.ClearDecals();
            // Hidden at once, so the new map's spawn checks and radar picture do not see it before it is gone.
            if (Map.Root != null) Map.Root.gameObject.SetActive(false);
            Map.Unload();
        }
        var map = MapCatalog.Create(id, closed);
        ApplyLighting(map.Lighting);
        map.Build(transform);
        Map = map;
        Debug.Log($"[DesertStrike] Map {map.Name}{(map.ClosedVersion != null ? $" (its version with {map.ClosedVersion} closed)" : "")} " +
                  $"loaded in {(Time.realtimeSinceStartup - started) * 1000f:0} ms");
    }

    void ApplyLighting(MapLighting lighting)
    {
        var sun = RenderSettings.sun;
        sun.transform.rotation = Quaternion.Euler(lighting.SunAngles);
        sun.color = lighting.Sun;
        sun.intensity = lighting.SunIntensity;
        sun.shadowStrength = lighting.ShadowStrength;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = lighting.Sky;
        RenderSettings.ambientEquatorColor = lighting.Equator;
        RenderSettings.ambientGroundColor = lighting.Ground;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = lighting.Fog;
        RenderSettings.fogStartDistance = lighting.FogStart;
        RenderSettings.fogEndDistance = lighting.FogEnd;
        // Every map has its own sky; where the graphics chip cannot draw it, the fog colour (the sky's at the horizon).
        RenderSettings.skybox = MapSky.For(lighting);
        MainCamera.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
        MainCamera.backgroundColor = lighting.Fog;
        if (MainCamera.TryGetComponent(out PostEffects post)) post.SetLook(lighting);
    }

    /// <summary>A LAN game ended (the host quit, or the connection was lost).</summary>
    public void OnNetworkGameEnded(string reason)
    {
        Debug.Log("[Net] " + reason);
        ReturnToMenu();
        MenuNotice = reason;
    }

    public void QuitGame()
    {
        Net.Stop();
        // Leaving a LAN game lets the game pause when its window is not in front; it must still finish quitting.
        Application.runInBackground = true;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    /// <summary>The pause menu. In a LAN game the match keeps running behind it.</summary>
    public void SetPaused(bool paused)
    {
        IsPaused = paused;
        bool freezeTime = paused && Net.Role == NetSession.Mode.Off;
        Time.timeScale = freezeTime ? 0f : timeScale;
        AudioListener.pause = freezeTime;
    }

    public string MatchDescription() =>
        $"{Map.Name}  ·  {TerroristCount}v{SwatCount}  ·  {Difficulty} bots  ·  first to {RoundsToWin}  ·  friendly fire {(FriendlyFire ? "on" : "off")}"
        + (ActiveClosedSite != null ? $"  ·  site {ActiveClosedSite} closed" : "");

    /// <summary>Which bombsite small matches shut ("A" or "B"), or none (null or "OFF": they use the whole map); saved.</summary>
    public void SetClosedSite(string site)
    {
        CloseHalf = site == "A" || site == "B";
        if (CloseHalf)
        {
            ClosedSite = site;
            PlayerPrefs.SetString(ClosedSiteKey, site);
        }
        PlayerPrefs.SetInt(CloseHalfKey, CloseHalf ? 1 : 0);
    }

    /// <summary>The setting as the online API keeps it: "A", "B", or "OFF" for the whole map.</summary>
    public string ClosedSiteSetting => CloseHalf ? ClosedSite : "OFF";

    bool siteCheck;   // -ds-closed-site: log whether the walled-off map still plays (tests)
    void LogSiteCheck() => Debug.Log("[Map] Closed site check: " + Map.CheckClosedSite());

    void StartRound()
    {
        Round++;
        State = MatchState.Freeze;
        roundStartedAt = Time.time;
        StateEndsAt = Time.time + FreezeTime;
        Banner = null;
        Message = null;
        BuyMenuOpen = false;
        Spectated = null;
        Effects.ClearDecals();
        Drops.Clear();

        var terroristSpawns = Shuffled(Map.TerroristSpawns);
        var swatSpawns = Shuffled(Map.SwatSpawns);
        int ti = 0, si = 0;
        PlayerAmmoResupply = null;
        foreach (var c in Combatants)
        {
            bool keepGear = Round > 1 && c.IsAlive;
            c.ResetForRound(keepGear);
            if (Round > 1)
            {
                string added = c.AddRoundAmmo();
                if (c.IsPlayer && added.Length > 0)
                {
                    PlayerAmmoResupply = added;
                    Debug.Log($"[DesertStrike] Ammo resupply for {c.DisplayName}: {added}");
                }
            }
            bool terrorist = c.Team == Team.Terrorists;
            Vector3 position = terrorist ? terroristSpawns[ti++ % terroristSpawns.Count] : swatSpawns[si++ % swatSpawns.Count];
            c.SpawnCount++;
            c.GetComponent<ICombatantController>().Respawn(position, terrorist ? Map.TerroristSpawnYaw : Map.SwatSpawnYaw);
        }

        PlayerTactics.Instance.AssignStyles(bots);
        foreach (var bot in bots) bot.BuyGear();
        var terroristBots = bots.FindAll(b => b.Self.Team == Team.Terrorists);
        var swatBots = Shuffled(bots.FindAll(b => b.Self.Team == Team.Swat));
        BotController.PlanRound(terroristBots, swatBots, Map, Time.time + FreezeTime);
        Bomb.ResetForRound();

        SoundFX.Play(SoundFX.RoundStart, Vector3.zero, 0.4f, 1f, false);
        Debug.Log($"[DesertStrike] Round {Round} started (T {TerroristWins} - {SwatWins} SWAT), bomb carried by {(Bomb.Carrier != null ? Bomb.Carrier.DisplayName : "nobody")}");
    }

    void EndRound(Team winner, string reason)
    {
        if (Warmup || (State != MatchState.Live && State != MatchState.Freeze)) return;
        State = MatchState.RoundEnd;
        StateEndsAt = Time.time + RoundEndDelay;
        LastWinner = winner;
        Banner = reason;
        BuyMenuOpen = false;

        if (winner == Team.Terrorists)
        {
            TerroristWins++;
            terroristLossStreak = 0;
            swatLossStreak = Mathf.Min(swatLossStreak + 1, 4);
        }
        else
        {
            SwatWins++;
            swatLossStreak = 0;
            terroristLossStreak = Mathf.Min(terroristLossStreak + 1, 4);
        }

        foreach (var c in Combatants)
        {
            int streak = c.Team == Team.Terrorists ? terroristLossStreak : swatLossStreak;
            int reward = c.Team == winner ? WinReward : LossReward + LossBonusStep * (streak - 1);
            if (c.Team == Team.Terrorists && winner != Team.Terrorists && Bomb.WasPlanted) reward += PlantedLossBonus;
            AddMoney(c, reward);
        }
        Debug.Log($"[DesertStrike] Round {Round}: {reason} (T {TerroristWins} - {SwatWins} SWAT)");
    }

    void Update()
    {
        Tutorial?.Update(this);
        if (State != MatchState.Menu)
        {
            Cheats.Update(this);
            if (!IsClient) Cheats.ApplyOnHost(this);
        }
        else ConsoleOpen = false;
        HandleInput();
        if (!IsClient) RunRound();
        UpdateCursor();
        RunAutomation();
        if (perfLog) LogPerformance();
        if (profileFile != null) RecordProfile();
    }

    void RunRound()
    {
        switch (State)
        {
            case MatchState.Freeze:
                if (Time.time >= StateEndsAt)
                {
                    State = MatchState.Live;
                    StateEndsAt = Time.time + RoundTime;
                }
                break;
            case MatchState.Live when Warmup:
                if (Time.time >= StateEndsAt)
                {
                    EndWarmup();
                    break;
                }
                // Back to life a moment after dying (a player who joined meanwhile too).
                foreach (var c in Combatants)
                {
                    if (c.IsAlive)
                    {
                        respawnAt.Remove(c);
                        continue;
                    }
                    if (!respawnAt.TryGetValue(c, out float at)) respawnAt[c] = Time.time + 1f;
                    else if (Time.time >= at)
                    {
                        respawnAt.Remove(c);
                        RespawnForWarmup(c);
                    }
                }
                break;
            case MatchState.Live when Tutorial != null:
                StateEndsAt = Mathf.Max(StateEndsAt, Time.time + 60f);   // the tutorial has no hurry
                break;
            case MatchState.Live:
                // Once the bomb is planted the round clock stops; the bomb decides.
                if (Time.time >= StateEndsAt && Bomb.State != BombState.Planted) EndRound(Team.Swat, "TIME IS UP - SWAT WIN");
                break;
            case MatchState.RoundEnd:
                if (Time.time >= StateEndsAt)
                {
                    if (TerroristWins >= RoundsToWin || SwatWins >= RoundsToWin)
                    {
                        State = MatchState.MatchOver;
                        Debug.Log($"[DesertStrike] Match over: T {TerroristWins} - {SwatWins} SWAT");
                        OnMatchOver();
                    }
                    else StartRound();
                }
                break;
        }
        KillFeed.RemoveAll(k => Time.time - k.Time > 6f);
    }

    void LateUpdate()
    {
        if (State == MatchState.Menu) OrbitMenuCamera();
        else if (SpectatingNow) FollowSpectated();
    }

    void HandleInput()
    {
        if (State == MatchState.Menu || State == MatchState.MatchOver) return;
        if (GameInput.ConsoleDown && !IsPaused)
        {
            ConsoleOpen = !ConsoleOpen;
            if (ConsoleOpen) BuyMenuOpen = false;
        }
        if (ConsoleOpen && GameInput.PauseDown)
        {
            ConsoleOpen = false;   // Esc closes the console, it does not pause
            return;
        }
        if (GameInput.PauseDown)
        {
            if (BuyMenuOpen) BuyMenuOpen = false;
            else SetPaused(!IsPaused);
        }
        if (IsPaused) return;

        if (Player != null && GameInput.BuyDown)
            BuyMenuOpen = !BuyMenuOpen && CanShop(Player.Self);
        if (BuyMenuOpen && !CanShop(Player.Self)) BuyMenuOpen = false;

        if (SpectatingNow)
        {
            if (GameInput.SpectateNextDown) Spectated = NextSpectateTarget(Spectated, 1);
            if (GameInput.SpectatePreviousDown) Spectated = NextSpectateTarget(Spectated, -1);
        }
    }

    void ShowMessage(string text, float seconds)
    {
        Message = text;
        MessageUntil = Time.time + seconds;
    }

    // ----------------------------------------------------------------- buying and selling (only in your spawn, early in the round)

    public bool InBuyZone(Combatant c) =>
        (c.Team == Team.Terrorists ? Map.TerroristBuyZone : Map.SwatBuyZone).Contains(c.transform.position + Vector3.up * 0.5f);

    public bool CanBuy(Combatant c) =>
        c != null && c.IsAlive && (ShopsAnywhere(c) || (InBuyZone(c) || Tutorial != null)   // the tutorial's shop step comes after walking away
        && (State == MatchState.Freeze || (State == MatchState.Live && BuyTimeLeft > 0f)));

    public bool CanSell(Combatant c) =>
        c != null && c.IsAlive && (ShopsAnywhere(c) || InBuyZone(c)
        && (State == MatchState.Freeze || (State == MatchState.Live && SellTimeLeft > 0f)));

    /// <summary>Shop anywhere (Cheats): buying and selling all round long, anywhere on the map.</summary>
    public bool ShopsAnywhere(Combatant c) =>
        Cheats.Has(c, Cheats.ShopFlag) && (State == MatchState.Freeze || State == MatchState.Live);

    /// <summary>The shop (B) is open while you can buy or sell.</summary>
    public bool CanShop(Combatant c) => CanBuy(c) || CanSell(c);

    public bool TryBuyWeapon(Combatant c, WeaponData weapon) => weapon != null && CanBuy(c) && c.TryBuy(weapon);

    public bool TrySellWeapon(Combatant c, WeaponSlot slot) => CanSell(c) && c.TrySell(slot);

    public bool TryBuyArmor(Combatant c, bool helmet) => CanBuy(c) && c.TryBuyArmor(helmet);

    public bool TryBuyDefuseKit(Combatant c) => CanBuy(c) && c.TryBuyDefuseKit();

    /// <summary>The buy menu: buys for the player here (asking the host in a LAN game).</summary>
    public void BuyWeapon(WeaponData weapon)
    {
        if (IsClient) Net.SendAction(NetAction.BuyWeapon, WeaponData.IndexOf(weapon));
        else if (Player == null || !TryBuyWeapon(Player.Self, weapon)) return;
        SoundFX.Play(SoundFX.Buy, Vector3.zero, 0.6f, 1f, false);
    }

    /// <summary>The buy menu: sells the player's gun in that slot (asking the host in a LAN game).</summary>
    public void SellWeapon(WeaponSlot slot)
    {
        if (IsClient) Net.SendAction(NetAction.SellWeapon, (int)slot);
        else if (Player == null || !TrySellWeapon(Player.Self, slot)) return;
        SoundFX.Play(SoundFX.Buy, Vector3.zero, 0.6f, 0.8f, false);
    }

    public void BuyArmor(bool helmet)
    {
        if (IsClient) Net.SendAction(NetAction.BuyArmor, helmet ? 1 : 0);
        else if (Player == null || !TryBuyArmor(Player.Self, helmet)) return;
        SoundFX.Play(SoundFX.Buy, Vector3.zero, 0.6f, 1f, false);
    }

    public void BuyDefuseKit()
    {
        if (IsClient) Net.SendAction(NetAction.BuyDefuseKit, 0);
        else if (Player == null || !TryBuyDefuseKit(Player.Self)) return;
        SoundFX.Play(SoundFX.Buy, Vector3.zero, 0.6f, 1f, false);
    }

    // ----------------------------------------------------------------- called by combatants and the bomb

    public int AliveCount(Team team)
    {
        int count = 0;
        foreach (var c in Combatants)
            if (c.Team == team && c.IsAlive) count++;
        return count;
    }

    public void OnKilled(Combatant victim, Combatant killer, WeaponData weapon, bool headshot)
    {
        KillFeed.Add(new KillFeedEntry
        {
            Killer = killer != null ? killer.DisplayName : "",
            Victim = victim.DisplayName,
            Weapon = weapon.Name,
            KillerTeam = killer != null ? killer.Team : victim.Team,
            VictimTeam = victim.Team,
            Headshot = headshot,
            // People drop out of the scope only after the shot resolves, so this is the state it was fired in.
            Noscope = weapon.ZoomFov > 0f && killer != null && killer.IsHuman && !killer.Scoped,
            Time = Time.time,
        });
        if (KillFeed.Count > 6) KillFeed.RemoveAt(0);

        BotKnowledge.Instance.OnKill(victim, killer);
        PlayerTactics.Instance.OnKill(victim, killer, headshot);
        Drops.OnDeath(victim);
        if (killer != null && killer != victim && killer.Team != victim.Team)
        {
            killer.Kills++;
            AddMoney(killer, weapon.KillReward);
        }
        if (victim.IsPlayer) Spectated = killer != null && killer.IsAlive ? killer : null;
        Debug.Log($"[DesertStrike] {(killer != null ? killer.DisplayName : "?")} killed {victim.DisplayName} with {weapon.Name}{(headshot ? " (headshot)" : "")}");
        CheckElimination();
    }

    void CheckElimination()
    {
        if (Warmup || (State != MatchState.Live && State != MatchState.Freeze)) return;
        if (AliveCount(Team.Swat) == 0) EndRound(Team.Terrorists, "TERRORISTS WIN");
        // With the bomb planted, SWAT still have to defuse it even after killing every Terrorist.
        else if (AliveCount(Team.Terrorists) == 0 && Bomb.State != BombState.Planted) EndRound(Team.Swat, "SWAT WIN");
    }

    public void OnBombPlanted(Combatant planter, string site)
    {
        ShowMessage($"BOMB HAS BEEN PLANTED ON {site}", 3f);
        PlayerTactics.Instance.OnBombPlanted(planter, site);
        ReportNoise(Bomb.Position, planter.Team, 200f);
        Debug.Log($"[DesertStrike] {planter.DisplayName} planted the bomb on {site}");
    }

    public void OnBombDefused(Combatant defuser)
    {
        Debug.Log($"[DesertStrike] {defuser.DisplayName} defused the bomb");
        EndRound(Team.Swat, "BOMB DEFUSED - SWAT WIN");
    }

    public void OnBombExploded()
    {
        Debug.Log("[DesertStrike] The bomb exploded");
        EndRound(Team.Terrorists, "TARGET BOMBED - TERRORISTS WIN");
    }

    /// <summary>Gunshots and running footsteps alert enemy bots within <paramref name="radius"/>.</summary>
    public void ReportNoise(Vector3 position, Team source, float radius)
    {
        float radiusSqr = radius * radius;
        foreach (var bot in bots)
            if (bot.Self.Team != source && (bot.transform.position - position).sqrMagnitude < radiusSqr)
                bot.HearNoise(position);
    }

    /// <summary>A shot or rocket hit someone: show the hit marker to whoever fired it.</summary>
    public void ReportHit(Combatant shooter, bool headshot, bool kill)
    {
        if (shooter == null) return;
        if (!IsClient) PlayerTactics.Instance.OnHit(shooter);
        if (shooter.IsPlayer) ShowHitMarker(headshot, kill);
        else if (shooter.IsHuman) Net.RecordHitMarker(shooter, headshot, kill);
    }

    public void ShowHitMarker(bool headshot, bool kill)
    {
        HitMarkerTime = Time.time;
        HitMarkerHeadshot = headshot;
        HitMarkerKill = kill;
        SoundFX.Play(headshot ? SoundFX.Headshot : SoundFX.HitMarker, Vector3.zero, 0.5f, 1f, false);
    }

    public Combatant FindByNetId(int netId)
    {
        if (netId == 0) return null;
        foreach (var c in Combatants)
            if (c.NetId == netId) return c;
        return null;
    }

    static void AddMoney(Combatant c, int amount) => c.Money = Mathf.Min(MaxMoney, c.Money + amount);

    // ----------------------------------------------------------------- spawning

    void SpawnPlayer(Team team, string playerName, bool mirror = false, int netId = 0)
    {
        var go = new GameObject("Player") { layer = Ballistics.CharacterLayer };
        go.transform.position = (team == Team.Terrorists ? Map.TerroristSpawns : Map.SwatSpawns)[0];
        go.AddComponent<CharacterController>();
        var combatant = go.AddComponent<Combatant>();
        combatant.DisplayName = playerName;
        combatant.Team = team;
        combatant.IsPlayer = true;
        combatant.IsHuman = true;
        combatant.IsMirror = mirror;
        combatant.NetId = netId != 0 ? netId : nextNetId++;
        combatant.SkinChoices = WeaponSkins.EquippedChoices();
        combatant.Damaged += (attacker, amount) =>
        {
            DamageFlashTime = Time.time;
            SoundFX.Play(SoundFX.Hurt, Vector3.zero, 0.6f, 1f, false);
        };
        Player = go.AddComponent<PlayerController>();
        Player.Setup(MainCamera);
        Combatants.Add(combatant);
    }

    void SpawnBot(Team team, string botName)
    {
        var go = new GameObject("Bot " + botName) { layer = Ballistics.CharacterLayer };
        go.transform.position = (team == Team.Terrorists ? Map.TerroristSpawns : Map.SwatSpawns)[0];
        var combatant = go.AddComponent<Combatant>();
        combatant.DisplayName = botName;
        combatant.Team = team;
        combatant.NetId = nextNetId++;
        var bot = go.AddComponent<BotController>();
        bot.Setup(Difficulty);
        Combatants.Add(combatant);
        bots.Add(bot);
    }

    /// <summary>Host: give a player on another PC a combatant. Mid-round they wait for the next round.</summary>
    public void AddRemotePlayer(NetSession.Peer peer, bool midRound)
    {
        var go = new GameObject("Remote " + peer.Name) { layer = Ballistics.CharacterLayer };
        go.transform.position = (peer.Team == Team.Terrorists ? Map.TerroristSpawns : Map.SwatSpawns)[0];
        var combatant = go.AddComponent<Combatant>();
        combatant.DisplayName = peer.Name;
        combatant.Team = peer.Team;
        combatant.IsHuman = true;
        combatant.NetId = nextNetId++;
        combatant.SkinChoices = peer.SkinChoices;
        combatant.Money = startMoney;
        var remote = go.AddComponent<RemotePlayerController>();
        remote.Setup(peer);
        peer.Player = remote;
        Combatants.Add(combatant);
        if (midRound)
        {
            combatant.ResetForRound(false);
            remote.SitOutRound();
        }
    }

    /// <summary>Host: a player on another PC left. During a match a bot plays on in their place (<see cref="StandIn"/>).</summary>
    public void RemoveRemotePlayer(NetSession.Peer peer)
    {
        if (peer.Player == null) return;
        if (StandIn(peer)) return;
        var combatant = peer.Player.Self;
        Bomb.ReleaseFrom(combatant);
        Combatants.Remove(combatant);
        Destroy(combatant.gameObject);
        peer.Player = null;
        CheckElimination();
    }

    bool MatchRunning => (State == MatchState.Freeze || State == MatchState.Live || State == MatchState.RoundEnd) && Tutorial == null;

    /// <summary>
    /// Host: a person left the match (or lost the connection): a bot takes over their player, with everything it
    /// has (health, guns, money, the bomb, their kills), and plays on the way they play (<see cref="PlayerTactics"/>)
    /// until they come back (<see cref="HandBack"/>) or the match ends.
    /// </summary>
    public bool StandIn(NetSession.Peer peer)
    {
        var remote = peer.Player;
        if (remote == null || !MatchRunning) return false;
        var c = remote.Self;
        peer.Player = null;
        DestroyImmediate(remote);
        var obstacle = c.GetComponent<UnityEngine.AI.NavMeshObstacle>();
        if (obstacle != null) DestroyImmediate(obstacle);
        c.IsHuman = false;
        c.StandInFor = peer.Name;
        c.CheatFlags = 0;
        c.Scoped = false;
        c.DisplayName = "BOT " + peer.Name;
        var bot = c.gameObject.AddComponent<BotController>();
        bot.Setup(Difficulty == BotDifficulty.Easy ? BotDifficulty.Normal : Difficulty);
        bot.StandInAccount = peer.AccountId;
        bot.StandInKills = c.Kills;
        bot.StyleChosen = true;
        bot.UseStyle(PlayerTactics.Instance.StyleOf(peer));
        bots.Add(bot);
        bot.TakeOver();
        if (State == MatchState.Freeze && BuyTimeLeft > 0f && InBuyZone(c)) bot.BuyGear();
        if (State != MatchState.RoundEnd) bot.PlanTakeover(Map, RoundLiveAt);
        ShowMessage($"{peer.Name} LEFT: A BOT PLAYS FOR THEM", 3f);
        Debug.Log($"[Net] A bot plays for {peer.Name} ({(bot.Style != null ? "their way" : "no style of theirs yet")})");
        return true;
    }

    /// <summary>
    /// Host: a person came back to the match (online: the same account; LAN: the same name) while a bot plays for
    /// them: their player is theirs again, where it is now. False when no bot plays for them.
    /// </summary>
    public bool HandBack(NetSession.Peer peer)
    {
        if (!MatchRunning || peer.Spectator || peer.Player != null) return false;
        var bot = bots.Find(b => b.Self.StandInFor != null &&
                                 (peer.AccountId != null ? b.StandInAccount == peer.AccountId : b.StandInAccount == null && b.Self.StandInFor == peer.Name));
        if (bot == null) return false;
        var c = bot.Self;
        bots.Remove(bot);
        bot.Release();
        DestroyImmediate(bot);
        c.IsHuman = true;
        c.StandInFor = null;
        c.DisplayName = peer.Name;
        c.SkinChoices = peer.SkinChoices ?? c.SkinChoices;
        peer.Team = c.Team;   // they play on where their player is
        var remote = c.gameObject.AddComponent<RemotePlayerController>();
        remote.Setup(peer);
        remote.TakeBack();
        peer.Player = remote;
        c.SpawnCount++;   // their game takes the place the host has for them (as after a respawn)
        ShowMessage($"{peer.Name} IS BACK", 3f);
        Debug.Log($"[Net] {peer.Name} plays again (the bot that played for them steps aside)");
        return true;
    }

    /// <summary>Host: the kills a person made before a bot took over for them (null: no bot plays for them).</summary>
    public int? StandInKills(NetSession.Peer peer)
    {
        var bot = bots.Find(b => b.Self.StandInFor != null && peer.AccountId != null && b.StandInAccount == peer.AccountId);
        return bot != null ? bot.StandInKills : (int?)null;
    }

    void ClearMatch()
    {
        MainCamera.transform.SetParent(null, true);
        foreach (var c in Combatants)
            if (c != null) Destroy(c.gameObject);
        Combatants.Clear();
        bots.Clear();
        KillFeed.Clear();
        Player = null;
        Spectated = null;
        BuyMenuOpen = false;
        Message = null;
        Bomb.Clear();
        Drops.Clear();
        Effects.ClearDecals();
        SetPaused(false);
    }

    // ----------------------------------------------------------------- network client: mirror the host

    public void BeginClientMatch()
    {
        ClearMatch();
        clientMatch = true;
        matchStartedAt = Time.time;
        MenuNotice = null;
        Debug.Log("[Net] The host started the match");
    }

    public void ApplyNetMatch(NetMatchState match, string resupply)
    {
        if (match.Round != Round) Debug.Log($"[Net] Round {match.Round} (T {match.TerroristWins} - {match.SwatWins} SWAT)");
        if (match.State != State && match.State == MatchState.Freeze) SoundFX.Play(SoundFX.RoundStart, Vector3.zero, 0.4f, 1f, false);
        bool over = match.State == MatchState.MatchOver && State != MatchState.MatchOver;
        State = match.State;
        Round = match.Round;
        TerroristWins = match.TerroristWins;
        SwatWins = match.SwatWins;
        RoundsToWin = match.RoundsToWin;
        StateEndsAt = Time.time + match.Remaining;
        roundStartedAt = Time.time - (BuyTime - match.BuyTimeLeft);
        Banner = string.IsNullOrEmpty(match.Banner) ? null : match.Banner;
        LastWinner = match.LastWinner;
        FriendlyFire = match.FriendlyFire;
        CloseSite(match.ClosedSite);
        Warmup = match.Warmup;
        CheatsForEveryone = match.CheatsForEveryone;
        Message = string.IsNullOrEmpty(match.Message) ? null : match.Message;
        MessageUntil = Time.time + match.MessageLeft;
        PlayerAmmoResupply = string.IsNullOrEmpty(resupply) ? null : resupply;
        if (State == MatchState.MatchOver || State == MatchState.RoundEnd) BuyMenuOpen = false;
        if (over) OnMatchOver();
    }

    public void ApplyNetCombatant(NetCombatantState s, int myId)
    {
        var c = FindByNetId(s.NetId);
        bool me = s.NetId == myId;
        bool created = c == null;
        if (created)
        {
            if (me)
            {
                SpawnPlayer(s.Team, s.Name, mirror: true, netId: s.NetId);
                c = Player.Self;
                Debug.Log($"[Net] Playing as {s.Name} ({NetSession.TeamName(s.Team)})");
            }
            else c = SpawnPuppet(s);
            c.Health = s.Health;
        }

        c.DisplayName = s.Name;
        c.Armor = s.Armor;
        c.Helmet = s.Helmet;
        c.HasDefuseKit = s.HasKit;
        c.Money = s.Money;
        c.Kills = s.Kills;
        c.Deaths = s.Deaths;
        if (!me)
        {
            c.Height = s.Height;
            c.Scoped = s.Scoped;
        }
        c.ApplyNetLoadout(s.Primary, s.Secondary, s.Slot, s.Mag, s.Reserve, s.Reload, s.AutoMode, s.PrimarySell, s.SecondarySell,
                          s.PrimarySkin, s.SecondarySkin, s.KnifeSkin);
        if (!created) c.ApplyNetHealth(s.Health);

        if (me)
        {
            // We move ourselves; the host only places us when it respawns us.
            if (created || s.SpawnCount != (c.SpawnCount & 0xFF))
            {
                c.SpawnCount = s.SpawnCount;
                if (c.IsAlive) Player.Respawn(s.Position, s.Yaw);
                else Player.ForceDead();
            }
        }
        else c.GetComponent<PuppetController>().ApplyState(s.Position, s.Yaw, s.SpawnCount);
    }

    Combatant SpawnPuppet(NetCombatantState s)
    {
        var go = new GameObject("Net " + s.Name) { layer = Ballistics.CharacterLayer };
        go.transform.SetPositionAndRotation(s.Position, Quaternion.Euler(0f, s.Yaw, 0f));
        var combatant = go.AddComponent<Combatant>();
        combatant.DisplayName = s.Name;
        combatant.Team = s.Team;
        combatant.IsHuman = s.IsHuman;
        combatant.IsMirror = true;
        combatant.NetId = s.NetId;
        combatant.Health = s.Health;
        go.AddComponent<PuppetController>().Setup();
        Combatants.Add(combatant);
        return combatant;
    }

    public void RemoveNetCombatantsExcept(HashSet<int> keep)
    {
        for (int i = Combatants.Count - 1; i >= 0; i--)
        {
            var c = Combatants[i];
            if (keep.Contains(c.NetId)) continue;
            if (Player != null && c == Player.Self)
            {
                MainCamera.transform.SetParent(null, true);
                Player = null;
            }
            if (Spectated == c) Spectated = null;
            Combatants.RemoveAt(i);
            Destroy(c.gameObject);
        }
    }

    public void ApplyNetKillFeed(List<KillFeedEntry> feed)
    {
        KillFeed.Clear();
        KillFeed.AddRange(feed);
    }

    // ----------------------------------------------------------------- camera

    Camera CreateCamera()
    {
        var cam = Camera.main;
        if (cam == null)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            cam = go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();
        }
        cam.nearClipPlane = 0.03f;
        cam.farClipPlane = 300f;   // the fog is solid from 260 m
        // No HDR: the picture stays 8 bits a colour (half the memory and bandwidth of a float one), and on Low, where
        // PostEffects is off, the camera draws straight to the screen instead of to an extra buffer that is copied over.
        cam.allowHDR = false;
        cam.fieldOfView = 75f;
        // Bloom, colour grading, vignette and FXAA on Medium and High (off on Low and on servers without graphics).
        if (!cam.TryGetComponent(out PostEffects _)) cam.gameObject.AddComponent<PostEffects>();
        return cam;   // sky, background colour and the post effects' look: set with each map's lighting
    }

    void OrbitMenuCamera()
    {
        var t = MainCamera.transform;
        t.SetParent(null, true);
        MainCamera.fieldOfView = 60f;
        if (views != null && views.Count > 0)
        {
            // -ds-view: each spot for 2 s, from 3 s after the start (the map has loaded by then).
            var (eye, target) = views[Mathf.Clamp((int)((Time.realtimeSinceStartup - 3f) / 2f), 0, views.Count - 1)];
            t.position = eye;
            t.LookAt(target);
            return;
        }
        float angle = Time.unscaledTime * 0.05f;
        t.position = new Vector3(Mathf.Cos(angle) * 75f, 55f, Mathf.Sin(angle) * 75f);
        t.LookAt(Vector3.zero);
    }

    /// <summary>True while -ds-view shows its spots: the menu is hidden for the screenshots.</summary>
    public bool ShowingViews => views != null && State == MatchState.Menu;

    void FollowSpectated()
    {
        if (Spectated == null || !Spectated.IsAlive) Spectated = NextSpectateTarget(Spectated, 1);
        if (Spectated == null) return;

        Vector3 pivot = Spectated.transform.position + Vector3.up * 1.7f;
        Vector3 offset = -Spectated.transform.forward * 3.2f + Vector3.up * 0.9f;
        float distance = offset.magnitude;
        if (Physics.SphereCast(pivot, 0.25f, offset.normalized, out var hit, distance, Ballistics.EnvironmentMask, QueryTriggerInteraction.Ignore))
            distance = hit.distance;

        var t = MainCamera.transform;
        float blend = 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime);
        t.position = Vector3.Lerp(t.position, pivot + offset.normalized * distance, blend);
        Vector3 look = pivot + Spectated.transform.forward * 4f - t.position;
        if (look.sqrMagnitude > 0.01f) t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(look), blend);
        MainCamera.fieldOfView = 75f;
    }

    Combatant NextSpectateTarget(Combatant current, int step)
    {
        var candidates = new List<Combatant>();
        Team? team = Player != null ? Player.Self.Team : (Team?)null;
        foreach (var c in Combatants)
            if (c.IsAlive && !c.IsPlayer && (team == null || c.Team == team)) candidates.Add(c);
        if (candidates.Count == 0)
            foreach (var c in Combatants)
                if (c.IsAlive && !c.IsPlayer) candidates.Add(c);
        if (candidates.Count == 0) return null;

        int index = candidates.IndexOf(current);
        if (index < 0) return candidates[0];
        return candidates[((index + step) % candidates.Count + candidates.Count) % candidates.Count];
    }

    void UpdateCursor()
    {
        bool free = State == MatchState.Menu || State == MatchState.MatchOver || IsPaused || BuyMenuOpen || ConsoleOpen || TouchControls.Active
                    || Tutorial?.Current == Tutorial.Step.Done;
        Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = free;
    }

    // ----------------------------------------------------------------- setup helpers

    /// <summary>The scene's sun (each map sets its colour, strength, direction and how dark its shadows are).</summary>
    static void CreateSun()
    {
        Light sun = null;
        foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (light.type == LightType.Directional) { sun = light; break; }
        if (sun == null)
        {
            sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
        }
        // Soft wherever the graphics quality has shadows at all (GameSettings).
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.7f;
        RenderSettings.sun = sun;
    }

    static List<T> Shuffled<T>(IEnumerable<T> items)
    {
        var list = new List<T>(items);
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }

    void ReadCommandLine()
    {
        var args = Environment.GetCommandLineArgs();
        WeaponData equipWeapon = null;
        string equipSkin = null;
        for (int i = 0; i < args.Length; i++)
        {
            bool hasValue = i + 1 < args.Length;
            switch (args[i])
            {
                case "-ds-autostart":
                    autoStart = true;
                    break;
                case "-ds-server":
                    // -ds-server [-ds-port <udp port>] [-ds-room-id <slot id>] [-ds-control <internal API url>]: an
                    // online game host, started by online-server/ (see OnlineHost).
                    ServerMode = true;
                    Side = PlayerSide.Spectate;
                    break;
                case "-ds-port" when hasValue:
                    OnlineHost.Port = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "-ds-room-id" when hasValue:
                    OnlineHost.SlotId = args[++i];
                    break;
                case "-ds-control" when hasValue:
                    OnlineHost.ControlUrl = args[++i].TrimEnd('/');
                    break;
                case "-ds-side" when hasValue:
                    if (Enum.TryParse(args[++i], true, out PlayerSide side)) Side = side;
                    break;
                case "-ds-timescale" when hasValue:
                    timeScale = float.Parse(args[++i], CultureInfo.InvariantCulture);
                    Time.timeScale = timeScale;
                    break;
                case "-ds-money" when hasValue:
                    startMoney = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "-ds-quit-after" when hasValue:
                    quitAt = float.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "-ds-capture-every" when hasValue:
                    captureEvery = float.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "-ds-capture" when hasValue:
                    captureDirectory = args[++i];
                    Directory.CreateDirectory(captureDirectory);
                    nextCaptureAt = 5f;
                    break;
                case "-ds-host":
                    autoHost = true;
                    break;
                case "-ds-join" when hasValue:
                    autoJoin = args[++i];
                    break;
                case "-ds-name" when hasValue:
                    Net.PlayerName = args[++i];
                    break;
                case "-ds-friendly-fire":
                    FriendlyFire = true;
                    break;
                case "-ds-client-fire":
                    clientFireTest = true;
                    break;
                case "-ds-find":
                    autoFind = true;
                    break;
                case "-ds-hold" when hasValue:
                    if (Enum.TryParse(args[++i], true, out WeaponSlot slot)) holdSlot = slot;
                    break;
                case "-ds-equip" when i + 2 < args.Length:
                    // -ds-equip <weapon> <skin>: uses that skin for this run only (not saved)
                    equipWeapon = WeaponData.Find(args[++i]);
                    equipSkin = args[++i];
                    if (equipWeapon != null) WeaponSkins.EquipThisRun(equipWeapon, equipSkin);
                    break;
                case "-ds-forget-skin" when i + 2 < args.Length:
                    // -ds-forget-skin <weapon> <skin>: this run acts as if the game did not have that skin, so it has
                    // to come from another player's game (tests skin sharing)
                    if (WeaponData.Find(args[++i]) is WeaponData forgetWeapon) WeaponSkins.Forget(forgetWeapon, args[++i]);
                    else i++;
                    break;
                case "-ds-export-closed" when hasValue:
                    exportClosed = args[++i];
                    break;
                case "-ds-tutorial":
                    // -ds-tutorial: the practice round at once (tests)
                    startTutorial = true;
                    break;
                case "-ds-cheats-test":
                    cheatsTest = true;
                    break;
                case "-ds-pause-menu":
                    pauseMenuTest = true;
                    break;
                case "-ds-cheats-everyone":
                    cheatsEveryoneFor = hasValue && float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float keep) ? keep : 0f;
                    if (hasValue && cheatsEveryoneFor > 0f) i++;
                    break;
                case "-ds-tutorial-test":
                    startTutorial = tutorialTest = true;
                    break;
                case "-ds-skin-roundtrip" when i + 2 < args.Length:
                    // -ds-skin-roundtrip <weapon> <skin>: forgets the skin and reads its pack back, as if it had come
                    // from another player's game (tests SkinPack; use -ds-equip to see it)
                    if (WeaponData.Find(args[++i]) is WeaponData packWeapon)
                    {
                        string packSkin = args[++i];
                        byte[] pack = SkinPack.Local(packWeapon, packSkin);
                        WeaponSkins.Forget(packWeapon, packSkin);
                        bool readBack = pack != null && SkinPack.Load(pack, packWeapon.Id, packSkin);
                        Debug.Log($"[Skins] Round trip of {packWeapon.Name}/{packSkin} ({pack?.Length / 1024} KB): {(readBack ? "read back" : "FAILED")}");
                    }
                    else i++;
                    break;
                case "-ds-rotate" when i + 3 < args.Length:
                    // -ds-rotate <x> <y> <z>: turns the skin chosen with -ds-equip in the hand, for this run (trying angles)
                    var turn = new Vector3(float.Parse(args[++i], CultureInfo.InvariantCulture), float.Parse(args[++i], CultureInfo.InvariantCulture),
                                           float.Parse(args[++i], CultureInfo.InvariantCulture));
                    if (equipWeapon != null && WeaponSkins.Find(equipWeapon, equipSkin) is WeaponSkin turned) turned.Rotation = turn;
                    break;
                case "-ds-login" when i + 2 < args.Length:
                    // -ds-login <nickname> <password>: logs in at the start (signing up first when there is no such
                    // account); use it with -ds-accounts-file <file> so tests keep their accounts apart.
                    string login = args[++i], password = args[++i];
                    if (Accounts.LogIn(login, password) != null) Accounts.SignUp(login, password, password);
                    break;
                case "-ds-difficulty" when hasValue:
                    if (Enum.TryParse(args[++i], true, out BotDifficulty difficulty)) Difficulty = difficulty;
                    break;
                case "-ds-no-warmup":
                    noWarmup = true;
                    break;
                case "-ds-drop-test":
                    dropTest = true;
                    break;
                case "-ds-rounds" when hasValue:
                    // -ds-rounds <1-16>: rounds to win (tests)
                    RoundsToWin = Mathf.Clamp(int.Parse(args[++i], CultureInfo.InvariantCulture), 1, 16);
                    break;
                case "-ds-players" when hasValue:
                    // -ds-players <n> or <terrorists>v<swat> ("2v4"): players per team, this run (tests)
                    string[] counts = args[++i].ToLowerInvariant().Split('v');
                    TerroristCount = Mathf.Clamp(int.Parse(counts[0], CultureInfo.InvariantCulture), 1, MaxPerTeam);
                    SwatCount = counts.Length > 1 ? Mathf.Clamp(int.Parse(counts[1], CultureInfo.InvariantCulture), 1, MaxPerTeam) : TerroristCount;
                    break;
                case "-ds-closed-site" when hasValue:
                    // -ds-closed-site <a|b|off>: the site small matches wall off, or the whole map, this run (tests)
                    string closeArg = args[++i].ToUpperInvariant();
                    CloseHalf = closeArg != "OFF";
                    if (CloseHalf) ClosedSite = closeArg == "A" ? "A" : "B";
                    siteCheck = true;
                    break;
                case "-ds-attack" when hasValue:
                    // -ds-attack <a|b>: the Terrorist bots always go for that bombsite (tests)
                    BotController.ForcedSiteA = args[++i].ToLowerInvariant() == "a";
                    break;
                case "-ds-background":
                    keepRunning = true;
                    break;
                case "-ds-teleport-test":
                    teleportTest = true;
                    break;
                case "-ds-perf":
                    perfLog = true;
                    break;
                case "-ds-give" when hasValue:
                    giveWeapon = WeaponData.Find(args[++i]);
                    break;
                case "-ds-fire-test":
                    fireTest = true;
                    break;
                case "-ds-pose-gallery":
                    poseGallery = true;
                    break;
                case "-ds-sell-test":
                    sellTestStep = 0;
                    break;
                case "-ds-hide" when hasValue:
                    hideParts = args[++i];
                    break;
                case "-ds-navmesh-obj" when hasValue:
                    navMeshFile = args[++i];
                    break;
                case "-ds-radar-png" when hasValue:
                    radarFile = args[++i];
                    break;
                case "-ds-view" when hasValue:
                    views = new List<(Vector3, Vector3)>();
                    foreach (string spot in args[++i].Split(';'))
                    {
                        var n = Array.ConvertAll(spot.Split(','), s => float.Parse(s, CultureInfo.InvariantCulture));
                        if (n.Length == 6) views.Add((new Vector3(n[0], n[1], n[2]), new Vector3(n[3], n[4], n[5])));
                    }
                    break;
                case "-ds-map" when hasValue:
                    // -ds-map <dune|village|halloween|industrial>: plays that map this run (not saved)
                    string mapId = args[++i];
                    if (MapCatalog.Find(mapId) != null)
                    {
                        MapId = mapId;
                        LoadMap(mapId);
                    }
                    break;
                case "-ds-profile" when hasValue:
                    profileFile = args[++i];
                    break;
                case "-ds-nocull":
                    VisibilityCuller.Disabled = true;
                    break;
                case "-ds-quality" when hasValue:
                    if (Enum.TryParse(args[++i], true, out GraphicsQuality quality)) GameSettings.Override(quality, GameSettings.FrameLimit);
                    break;
                case "-ds-scale" when hasValue:
                    GameSettings.RenderScaleThisRun(float.Parse(args[++i], CultureInfo.InvariantCulture));
                    break;
                case "-ds-fps" when hasValue:
                    GameSettings.Override(GameSettings.Quality, int.Parse(args[++i], CultureInfo.InvariantCulture));
                    break;
            }
        }
        if (Side != PlayerSide.Spectate) Net.PreferredTeam = Side == PlayerSide.Terrorists ? Team.Terrorists : Team.Swat;
    }

    void RecordProfile()
    {
        if (!UnityEngine.Profiling.Profiler.enabled)
        {
            if (State != MatchState.Live || Time.time - roundStartedAt < 8f) return;
            UnityEngine.Profiling.Profiler.logFile = profileFile;
            UnityEngine.Profiling.Profiler.enableBinaryLog = true;
            UnityEngine.Profiling.Profiler.enabled = true;
            Debug.Log("[perf] profiling started: " + profileFile);
            return;
        }
        if (--profileFramesLeft > 0) return;
        UnityEngine.Profiling.Profiler.enabled = false;
        UnityEngine.Profiling.Profiler.logFile = "";
        Debug.Log("[perf] profiling finished");
        profileFile = null;
    }

    // Every 5 seconds: frames per second, and how many of the bodies inside the view were actually drawn.
    void LogPerformance()
    {
        float now = Time.realtimeSinceStartup;
        if (perfSince < 0f) perfSince = now;
        perfFrames++;
        perfInView += VisibilityCuller.InView;
        perfDrawn += VisibilityCuller.Drawn;
        if (now - perfSince < 5f) return;
        Debug.Log($"[perf] {State} {GameSettings.Quality} limit {GameSettings.FrameLimit}: {perfFrames / (now - perfSince):0.0} fps, " +
                  $"bodies in view {perfInView / (float)perfFrames:0.0}, drawn {perfDrawn / (float)perfFrames:0.0}, " +
                  $"CPU {PerfStats.CpuMs:0.00} ms, GPU {PerfStats.GpuMs:0.00} ms, batches {PerfStats.Batches}, setpass {PerfStats.SetPassCalls}, " +
                  $"tris {PerfStats.Triangles}, {PerfStats.Device}" +
                  (VisibilityCuller.Disabled ? " (culling off)" : ""));
        perfSince = now;
        perfFrames = perfInView = perfDrawn = 0;
    }

    /// <summary>A row of figures in the sky over the map, each in one pose, facing along +x so the camera (set as
    /// the -ds-view spot) sees them from the side: checking the animations.</summary>
    void ShowPoseGallery()
    {
        var poses = new (string Pose, float Time)[]
        {
            ("stand", 0f), ("walk", Mathf.PI / 2f), ("walk", Mathf.PI * 1.5f), ("crouch", 0f), ("crouch walk", Mathf.PI / 2f),
            ("jump", 0f), ("dying", 0.18f), ("dying", 0.42f), ("dying", 0.55f), ("dead", 0f),
        };
        Vector3 start = new Vector3(-10f, 40f, 0f);
        for (int i = 0; i < poses.Length; i++)
        {
            var go = new GameObject("Pose " + poses[i].Pose) { layer = Ballistics.CharacterLayer };
            go.transform.SetPositionAndRotation(start + Vector3.right * (i * 2.5f), Quaternion.Euler(0f, 90f, 0f));
            var figure = go.AddComponent<Combatant>();
            figure.Team = i % 2 == 0 ? Team.Terrorists : Team.Swat;
            figure.DisplayName = poses[i].Pose;
            var body = go.AddComponent<CharacterBody>();
            body.Setup();
            figure.Money = 10000;
            figure.TryBuy(figure.Team == Team.Terrorists ? WeaponData.Ak47 : WeaponData.M4a1);
            body.ShowPose(poses[i].Pose, poses[i].Time);
        }
        Vector3 middle = start + Vector3.right * ((poses.Length - 1) * 2.5f / 2f) + Vector3.up * 0.9f;
        views = new List<(Vector3, Vector3)> { (middle + new Vector3(0f, 0.6f, -15f), middle) };
    }

    /// <summary>-ds-cheats-test: the console's "/cheats on" and every switch, then 2 s flying up while firing (aimbot,
    /// infinite ammo) until two kills or 40 s, with screenshots (-ds-capture); on the way three guns from the mouse wheel
    /// (all weapons), a dive through the floor and back and a flight out of the spawn (no clip), and a buy there after the
    /// buy time (shop anywhere). Logs height, ammo, money, weapons and kills, then "/cheats off".</summary>
    void RunCheatsTest()
    {
        if (State != MatchState.Live || Warmup || !Player.Self.IsAlive) return;   // after warmup (online rooms have one)
        if (cheatsTestAt < 0f && !Cheats.Allowed(this)) return;                   // a player who waits for cheats for everyone
        var me = Player.Self;
        var fake = GameInput.Fake ??= new GameInput.Pretend();
        fake.Move = fake.Look = Vector2.zero;
        fake.Jump = fake.Fire = fake.Crouch = false;
        if (cheatsTestAt < 0f)
        {
            cheatsTestAt = Time.time;
            ConsoleOpen = true;
            Debug.Log($"[CheatsTest] /cheats on: {Cheats.Command(this, "/cheats on")}");
            Cheats.Aimbot = Cheats.Fly = Cheats.InfiniteAmmo = Cheats.WallHack = Cheats.InfiniteMoney = Cheats.InfiniteHealth = true;
            Cheats.ShopAnywhere = Cheats.AllWeapons = true;
            Debug.Log($"[CheatsTest] start: height {Player.transform.position.y:0.0} m, ${me.Money}, {me.Current?.Data.Name} {me.Current?.Mag}/{me.Current?.Reserve}");
            if (captureDirectory != null) nextCaptureAt = Time.realtimeSinceStartup + 1f;
            return;
        }
        float t = Time.time - cheatsTestAt;
        if (t > 2f) ConsoleOpen = false;
        if (t > 2f && t < 2.5f) fake.Jump = true;   // fly up a little
        if (t > 3.5f && t - Time.deltaTime <= 3.5f)
            Debug.Log($"[CheatsTest] flying: height {Player.transform.position.y:0.0} m, {me.Current?.Data.Name} {me.Current?.Mag}/{me.Current?.Reserve}");
        if (t > 3f && t < 40f) fake.Fire = true;   // the aimbot aims at whoever comes into sight
        if (Passed(t, 4.6f) || Passed(t, 5.2f) || Passed(t, 5.8f))
        {
            Cheats.TakeWeapon(this, Cheats.NextWeapon(me.Current.Data, 1));
            Debug.Log($"[CheatsTest] all weapons: next is {Cheats.NextWeapon(me.Current.Data, 1).Name}; in hand {me.Current.Data.Name}");
        }
        if (Passed(t, 6.4f))
        {
            Cheats.NoClip = true;
            Debug.Log($"[CheatsTest] no clip: diving from {Player.transform.position.y:0.0} m, in hand {me.Current.Data.Name}");
        }
        if (t > 6.4f && t < 8f) fake.Crouch = true;   // down through the floor
        if (Passed(t, 8f)) Debug.Log($"[CheatsTest] no clip: under the floor at {Player.transform.position.y:0.0} m");
        if (t > 8f && t < 9.8f) fake.Jump = true;     // and back up
        if (Passed(t, 9.8f)) Debug.Log($"[CheatsTest] no clip: back at {Player.transform.position.y:0.0} m");
        if (t > 9.8f && t < 13f) fake.Move = new Vector2(0f, 1f);   // out of the spawn, through whatever is in the way
        if (Passed(t, 13f))
        {
            Cheats.NoClip = false;
            Debug.Log($"[CheatsTest] no clip off at {Player.transform.position}");
        }
        if (!cheatsShopTested && t > 13f && BuyTimeLeft < 0f)
        {
            cheatsShopTested = true;
            bool canBuy = CanBuy(me), inZone = InBuyZone(me);
            BuyWeapon(WeaponData.Awp);
            Debug.Log($"[CheatsTest] shop anywhere: can buy {canBuy} (in the buy zone {inZone}, buy time left {BuyTimeLeft:0} s), bought the AWP: {me.Primary?.Data.Name}");
        }
        if (captureDirectory != null && Mathf.Abs(t - 3.5f) < Time.deltaTime) nextCaptureAt = Time.realtimeSinceStartup;
        if ((t > 40f || me.Kills >= 2 && cheatsShopTested) && Cheats.On)
        {
            Debug.Log($"[CheatsTest] after: height {Player.transform.position.y:0.0} m, ${me.Money}, {me.Current?.Data.Name} {me.Current?.Mag}/{me.Current?.Reserve}, kills {me.Kills}, health {me.Health}, deaths {me.Deaths}");
            Debug.Log($"[CheatsTest] /cheats off: {Cheats.Command(this, "/cheats off")}; on now: {Cheats.On}");
            GameInput.Fake = null;
            cheatsTest = false;
        }
    }

    static bool Passed(float t, float at) => t >= at && t - Time.deltaTime < at;

    /// <summary>-ds-tutorial-test: does each step of the tutorial with pretend controls, the way a player would (only the
    /// walk to the bombsite is a jump there), with a screenshot of each step (-ds-capture), and presses FINISH at the end.</summary>
    void RunTutorialTest()
    {
        var fake = GameInput.Fake ??= new GameInput.Pretend();
        var step = Tutorial.Current;
        if (step != testStep)
        {
            Debug.Log($"[TutorialTest] {Time.time:0.0}s now: {step} ({State})");
            testStep = step;
            testStepAt = Time.time;
            if (captureDirectory != null) nextCaptureAt = Time.realtimeSinceStartup + 0.4f;
        }
        fake.Move = fake.Look = Vector2.zero;
        fake.Jump = fake.Crouch = fake.Fire = fake.Use = fake.Buy = false;
        if (State != MatchState.Live && step != Tutorial.Step.Done) return;
        float since = Time.time - testStepAt;
        if (since < 1f) return;   // a moment to read it
        var me = Player.Self;
        switch (step)
        {
            case Tutorial.Step.Walk: fake.Move = new Vector2(0.3f, 1f); break;
            case Tutorial.Step.Look: fake.Look = new Vector2(2f, 0f); break;
            case Tutorial.Step.Jump: fake.Jump = Time.frameCount % 30 == 0; break;
            case Tutorial.Step.Crouch: fake.Crouch = true; break;
            case Tutorial.Step.Buy:
                if (!BuyMenuOpen) fake.Buy = Time.frameCount % 30 == 0;   // B
                else if (since > 2f) BuyWeapon(WeaponData.Ak47);          // the shop's own button
                break;
            case Tutorial.Step.Shoot:
                if (BuyMenuOpen) { fake.Buy = Time.frameCount % 30 == 0; break; }
                Combatant target = null;
                foreach (var c in Combatants) if (c.Team != me.Team) target = c;
                if (target == null) break;
                var eye = Camera.main.transform;
                Vector3 to = target.transform.position + Vector3.up * 1.2f - eye.position;
                float yawOff = Mathf.DeltaAngle(eye.eulerAngles.y, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
                float pitchOff = Mathf.DeltaAngle(eye.eulerAngles.x, -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg);
                fake.Look = new Vector2(yawOff, -pitchOff) * 0.5f;   // degrees (pretend looking is not scaled)
                fake.Fire = Mathf.Abs(yawOff) < 1.5f && Mathf.Abs(pitchOff) < 1.5f && Time.frameCount % 8 < 4;
                break;
            case Tutorial.Step.Plant:
                if (Map.SiteAt(Player.transform.position) == null)
                {
                    Player.Respawn(Map.SitePoint(true), Player.transform.eulerAngles.y);
                    Debug.Log($"[TutorialTest] Went to site {Map.SiteAt(Player.transform.position)}, carrying the bomb: {Bomb.Carrier == me}");
                }
                fake.Use = true;   // E held
                break;
            case Tutorial.Step.Done:
                if (since > 3f)
                {
                    tutorialTest = false;
                    GameInput.Fake = null;
                    EndTutorial();   // FINISH
                    Debug.Log($"[TutorialTest] Finished: {State}, map {MapId}, side {Side}, {TerroristCount}v{SwatCount}, {Difficulty}, rounds {RoundsToWin}");
                }
                break;
        }
    }

    /// <summary>-ds-sell-test (with -ds-money 5000): buy an AK-47 and sell it back (full price), then after buy time
    /// sell the pistol and check that selling stops after <see cref="SellTime"/>. Goes through the shop's own calls, so
    /// on a LAN client it asks the host; every step is logged, with screenshots of the shop.</summary>
    void RunSellTest()
    {
        var me = Player.Self;
        float t = Time.time - roundStartedAt;
        string Gear() => $"${me.Money}, main {me.Primary?.Data.Name ?? "none"} (sells {me.Primary?.SellPrice}), pistol {me.Secondary?.Data.Name ?? "none"} (sells {me.Secondary?.SellPrice}), holding {me.Current?.Data.Name}";
        void Step(float after, Action action)
        {
            if (t <= after) return;
            action();
            sellTestStep++;
        }
        switch (sellTestStep)
        {
            case 0: Step(1f, () => { Debug.Log($"[SellTest] {t:0.0}s start: {Gear()}"); BuyWeapon(WeaponData.Ak47); BuyMenuOpen = true; nextCaptureAt = Time.realtimeSinceStartup + 1f; }); break;
            case 1: Step(2.5f, () => { Debug.Log($"[SellTest] {t:0.0}s bought: {Gear()}"); SellWeapon(WeaponSlot.Primary); SellWeapon(WeaponSlot.Knife); }); break;
            case 2: Step(4f, () => Debug.Log($"[SellTest] {t:0.0}s sold main (and tried the knife): {Gear()}, knife {me.Knife != null}")); break;
            case 3: Step(27f, () => { BuyMenuOpen = CanShop(me); Debug.Log($"[SellTest] {t:0.0}s can buy {CanBuy(me)}, can sell {CanSell(me)}, shop open {BuyMenuOpen}"); BuyWeapon(WeaponData.Mp5); nextCaptureAt = Time.realtimeSinceStartup + 0.3f; }); break;
            case 4: Step(28.2f, () => { Debug.Log($"[SellTest] {t:0.0}s tried to buy an MP5 after buy time: {Gear()}"); SellWeapon(WeaponSlot.Secondary); }); break;
            case 5: Step(29.5f, () => Debug.Log($"[SellTest] {t:0.0}s sold pistol: {Gear()}")); break;
            case 6: Step(31f, () => { Debug.Log($"[SellTest] {t:0.0}s after sell time: can sell {CanSell(me)}, shop open {BuyMenuOpen}"); sellTestStep = -2; }); break;
        }
    }

    void RunAutomation()
    {
        float now = Time.realtimeSinceStartup;
        if (keepRunning || ServerMode) Application.runInBackground = true;   // leaving a LAN game would switch it off again
        if (holdSlot.HasValue && Player != null && Player.Self.IsAlive && Player.Self.Current != null
            && Player.Self.Current.Data.Slot != holdSlot.Value)
            Player.Self.Equip(holdSlot.Value);
        // Hosting test: start the match once a client has joined.
        if (autoHost && autoStart && State == MatchState.Menu && Net.Peers.Count > 0) StartMatch();
        // -ds-drop-test: when a round goes live, every bot drops its main gun (they should go and take one again).
        if (dropTest && State == MatchState.Live && !Warmup && Round != droppedInRound)
        {
            droppedInRound = Round;
            int dropped = 0;
            foreach (var bot in bots)
                if (Drops.Drop(bot.Self, WeaponSlot.Primary)) dropped++;
            Debug.Log($"[DropTest] round {Round}: {dropped} bots dropped their main gun");
        }
        // Discovery test: join the first game found on the network.
        if (autoFind && Net.Role == NetSession.Mode.Off && Net.FoundHosts.Count > 0)
        {
            autoFind = false;
            Net.Join(Net.FoundHosts[0].Address);
        }
        if (poseGallery && Map != null)
        {
            poseGallery = false;
            ShowPoseGallery();
        }
        if (hideParts != null && Map?.Root != null)
        {
            int hidden = 0;
            foreach (var part in Map.Root.GetComponentsInChildren<Renderer>())
                if (part.name.Contains(hideParts))
                {
                    part.enabled = false;
                    hidden++;
                }
            Debug.Log($"[DesertStrike] -ds-hide {hideParts}: {hidden} map parts hidden");
            hideParts = null;
        }
        if (navMeshFile != null)
        {
            var mesh = UnityEngine.AI.NavMesh.CalculateTriangulation();
            var obj = new System.Text.StringBuilder();
            foreach (var v in mesh.vertices) obj.AppendLine(FormattableString.Invariant($"v {v.x:0.###} {v.y:0.###} {v.z:0.###}"));
            for (int t = 0; t + 2 < mesh.indices.Length; t += 3)
                obj.AppendLine($"f {mesh.indices[t] + 1} {mesh.indices[t + 1] + 1} {mesh.indices[t + 2] + 1}");
            File.WriteAllText(navMeshFile, obj.ToString());
            navMeshFile = null;
        }
        if (radarFile != null && Map.Radar != null)
        {
            File.WriteAllBytes(radarFile, Map.Radar.EncodeToPNG());
            radarFile = null;
        }
        if (giveWeapon != null && Player != null && Player.Self.IsAlive && Player.Self.Get(giveWeapon.Slot)?.Data != giveWeapon)
        {
            Player.Self.Money += giveWeapon.Price;
            Player.Self.TryBuy(giveWeapon);
            Player.Self.Equip(giveWeapon.Slot, instant: true);
        }
        if (sellTestStep >= 0 && Player != null && Round == 1) RunSellTest();
        if (tutorialTest && Tutorial != null && Player != null) RunTutorialTest();
        if (cheatsTest && Player != null) RunCheatsTest();
        if (pauseMenuTest && State == MatchState.Live && !Warmup && Time.time - roundStartedAt > 3f)
        {
            pauseMenuTest = false;
            SetPaused(true);
        }
        if (cheatsEveryoneFor >= 0f && State == MatchState.Live && !Warmup && Cheats.IsAdmin(this))
        {
            if (cheatsEveryoneAt < 0f)
            {
                cheatsEveryoneAt = Time.time;
                Debug.Log("[CheatsTest] " + Cheats.SetForEveryone(this, true));
            }
            else if (cheatsEveryoneFor > 0f && Time.time - cheatsEveryoneAt > cheatsEveryoneFor)
            {
                Debug.Log("[CheatsTest] " + Cheats.SetForEveryone(this, false));
                cheatsEveryoneFor = -1f;
            }
        }
        if (teleportTest && IsClient && Player != null && Player.Self.IsAlive && State == MatchState.Live && now >= nextTeleport)
        {
            nextTeleport = now + 3f;
            Vector3 from = Player.transform.position;
            Player.Respawn(from + Player.transform.forward * 8f, Player.transform.eulerAngles.y);
            Debug.Log($"[Test] Jumped 8 m from {from} (a cheat the host should refuse)");
        }
        // Client test: shoot straight ahead now and then, to check that shots reach the host.
        bool firing = (clientFireTest && IsClient) || fireTest;
        if (firing && Player != null && Player.Self.IsAlive && State == MatchState.Live && now >= nextTestShot)
        {
            nextTestShot = now + (fireTest ? 3f : 1f);
            Player.Self.TryFire(Player.transform.forward, 0f);
            if (fireTest && captureDirectory != null) nextCaptureAt = now + 0.1f;
        }
        if (views != null && captureDirectory != null)
        {
            // One screenshot per -ds-view spot, 1.5 s after the camera got there, instead of one every 8 s.
            nextCaptureAt = float.MaxValue;
            int shot = (int)((now - 4.5f) / 2f);
            if (now >= 4.5f && shot > viewShot && shot < views.Count)
            {
                viewShot = shot;
                ScreenCapture.CaptureScreenshot(Path.Combine(captureDirectory, $"view_{shot:00}.png"));
            }
        }
        if (captureDirectory != null && now >= nextCaptureAt)
        {
            nextCaptureAt = now + captureEvery;
            ScreenCapture.CaptureScreenshot(Path.Combine(captureDirectory, $"shot_{captureIndex++:00}.png"));
        }
        if (quitAt > 0f && now >= quitAt)
        {
            quitAt = -1f;
            Debug.Log($"[DesertStrike] Auto-quit after {now:0}s: round {Round}, T {TerroristWins} - {SwatWins} SWAT");
            QuitGame();
        }
    }
}

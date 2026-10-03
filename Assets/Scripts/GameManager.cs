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
    public const float RoundTime = 115f;
    public const float RoundEndDelay = 5f;
    public const int StartMoney = 800;
    public const int MaxMoney = 16000;
    public const int WinReward = 3250;
    public const int LossReward = 1400;
    public const int LossBonusStep = 500;
    public const int PlantedLossBonus = 800;   // Terrorists who planted but still lost

    static readonly string[] TerroristNames = { "Viper", "Jackal", "Cobra", "Scorpion", "Raven", "Wolf", "Dagger", "Ghost" };
    static readonly string[] SwatNames = { "Hawk", "Falcon", "Ranger", "Bishop", "Echo", "Titan", "Sentinel", "Patriot" };

    // Match settings, chosen in the main menu (in a LAN game the host's count).
    public PlayerSide Side = PlayerSide.Terrorists;
    public int TeamSize = 5;
    public BotDifficulty Difficulty = BotDifficulty.Normal;
    public int RoundsToWin = 8;
    public bool FriendlyFire;

    public MatchState State { get; private set; } = MatchState.Menu;
    public MapBuilder Map { get; private set; }
    public NetSession Net { get; private set; }
    public BombManager Bomb { get; private set; }
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
    public bool SpectatingNow => State != MatchState.Menu && (Player == null || !Player.Self.IsAlive);
    public float BuyTimeLeft => BuyTime - (Time.time - roundStartedAt);

    readonly List<BotController> bots = new List<BotController>();
    float roundStartedAt;
    int terroristLossStreak, swatLossStreak;
    int nextNetId = 1;
    bool clientMatch;

    // Command-line automation (used for smoke tests): -ds-autostart, -ds-side, -ds-money, -ds-quit-after,
    // -ds-capture, -ds-timescale, -ds-host, -ds-join <ip>, -ds-find, -ds-name <name>, -ds-client-fire,
    // -ds-perf (logs FPS and drawn bodies), -ds-nocull, -ds-quality <low|medium|high>, -ds-fps <limit>, -ds-scale <0.25-1>
    bool autoStart, autoHost, autoFind, clientFireTest, perfLog;
    string autoJoin;
    WeaponSlot? holdSlot;   // -ds-hold <primary|secondary|knife>: the player keeps this weapon out (screenshots)
    int startMoney = StartMoney;
    float timeScale = 1f;
    float quitAt = -1f;
    string captureDirectory;
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
        SetupLighting();
        Map = new MapBuilder();
        Map.Build(transform);
        MainCamera = CreateCamera();
        gameObject.AddComponent<VisibilityCuller>();
        gameObject.AddComponent<RenderScaler>();
        gameObject.AddComponent<PerfStats>();
        Net = gameObject.AddComponent<NetSession>();
        Bomb = gameObject.AddComponent<BombManager>();
        if (GetComponent<GameUI>() == null) gameObject.AddComponent<GameUI>();
        ReadCommandLine();
    }

    void Start()
    {
        if (autoHost) Net.StartHost();
        if (autoJoin != null) Net.Join(autoJoin);
        if (autoStart && !autoHost && autoJoin == null) StartMatch();
    }

    // ----------------------------------------------------------------- match flow

    public void StartMatch()
    {
        if (Net.IsClient) return;
        ClearMatch();
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
            AddRemotePlayer(peer, midRound: false);
            if (peer.Team == Team.Terrorists) terroristHumans++; else swatHumans++;
        }

        // Bots fill the rest of each team.
        var terroristNames = new Queue<string>(Shuffled(TerroristNames));
        var swatNames = new Queue<string>(Shuffled(SwatNames));
        for (int i = terroristHumans; i < TeamSize; i++) SpawnBot(Team.Terrorists, terroristNames.Dequeue());
        for (int i = swatHumans; i < TeamSize; i++) SpawnBot(Team.Swat, swatNames.Dequeue());

        foreach (var c in Combatants) c.Money = startMoney;
        Debug.Log($"[DesertStrike] Match started: {Side}, {TeamSize}v{TeamSize}, {Difficulty}, first to {RoundsToWin}, friendly fire {(FriendlyFire ? "on" : "off")}, {Net.Peers.Count} remote players");
        StartRound();
    }

    public void ReturnToMenu()
    {
        ClearMatch();
        Net.Stop();
        clientMatch = false;
        State = MatchState.Menu;
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
        $"{TeamSize}v{TeamSize}  ·  {Difficulty} bots  ·  first to {RoundsToWin}  ·  friendly fire {(FriendlyFire ? "on" : "off")}";

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
            c.GetComponent<ICombatantController>().Respawn(position, terrorist ? 0f : 180f);
        }

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
        if (State != MatchState.Live && State != MatchState.Freeze) return;
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
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (BuyMenuOpen) BuyMenuOpen = false;
            else SetPaused(!IsPaused);
        }
        if (IsPaused) return;

        if (Player != null && Input.GetKeyDown(KeyCode.B))
            BuyMenuOpen = !BuyMenuOpen && CanBuy(Player.Self);
        if (BuyMenuOpen && !CanBuy(Player.Self)) BuyMenuOpen = false;

        if (SpectatingNow)
        {
            if (Input.GetMouseButtonDown(0)) Spectated = NextSpectateTarget(Spectated, 1);
            if (Input.GetMouseButtonDown(1)) Spectated = NextSpectateTarget(Spectated, -1);
        }
    }

    void ShowMessage(string text, float seconds)
    {
        Message = text;
        MessageUntil = Time.time + seconds;
    }

    // ----------------------------------------------------------------- buying (only in your spawn, during buy time)

    public bool InBuyZone(Combatant c) =>
        (c.Team == Team.Terrorists ? Map.TerroristBuyZone : Map.SwatBuyZone).Contains(c.transform.position + Vector3.up * 0.5f);

    public bool CanBuy(Combatant c) =>
        c != null && c.IsAlive && InBuyZone(c)
        && (State == MatchState.Freeze || (State == MatchState.Live && BuyTimeLeft > 0f));

    public bool TryBuyWeapon(Combatant c, WeaponData weapon) => weapon != null && CanBuy(c) && c.TryBuy(weapon);

    public bool TryBuyArmor(Combatant c, bool helmet) => CanBuy(c) && c.TryBuyArmor(helmet);

    public bool TryBuyDefuseKit(Combatant c) => CanBuy(c) && c.TryBuyDefuseKit();

    /// <summary>The buy menu: buys for the player here (asking the host in a LAN game).</summary>
    public void BuyWeapon(WeaponData weapon)
    {
        if (IsClient) Net.SendAction(NetAction.BuyWeapon, WeaponData.IndexOf(weapon));
        else if (Player == null || !TryBuyWeapon(Player.Self, weapon)) return;
        SoundFX.Play(SoundFX.Buy, Vector3.zero, 0.6f, 1f, false);
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
        if (State != MatchState.Live && State != MatchState.Freeze) return;
        if (AliveCount(Team.Swat) == 0) EndRound(Team.Terrorists, "TERRORISTS WIN");
        // With the bomb planted, SWAT still have to defuse it even after killing every Terrorist.
        else if (AliveCount(Team.Terrorists) == 0 && Bomb.State != BombState.Planted) EndRound(Team.Swat, "SWAT WIN");
    }

    public void OnBombPlanted(Combatant planter, string site)
    {
        ShowMessage($"BOMB HAS BEEN PLANTED ON {site}", 3f);
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

    /// <summary>Host: a player on another PC left.</summary>
    public void RemoveRemotePlayer(NetSession.Peer peer)
    {
        if (peer.Player == null) return;
        var combatant = peer.Player.Self;
        Bomb.ReleaseFrom(combatant);
        Combatants.Remove(combatant);
        Destroy(combatant.gameObject);
        peer.Player = null;
        CheckElimination();
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
        Effects.ClearDecals();
        SetPaused(false);
    }

    // ----------------------------------------------------------------- network client: mirror the host

    public void BeginClientMatch()
    {
        ClearMatch();
        clientMatch = true;
        MenuNotice = null;
        Debug.Log("[Net] The host started the match");
    }

    public void ApplyNetMatch(NetMatchState match, string resupply)
    {
        if (match.Round != Round) Debug.Log($"[Net] Round {match.Round} (T {match.TerroristWins} - {match.SwatWins} SWAT)");
        if (match.State != State && match.State == MatchState.Freeze) SoundFX.Play(SoundFX.RoundStart, Vector3.zero, 0.4f, 1f, false);
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
        Message = string.IsNullOrEmpty(match.Message) ? null : match.Message;
        MessageUntil = Time.time + match.MessageLeft;
        PlayerAmmoResupply = string.IsNullOrEmpty(resupply) ? null : resupply;
        if (State == MatchState.MatchOver || State == MatchState.RoundEnd) BuyMenuOpen = false;
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
            var weapon = s.Slot == WeaponSlot.Primary ? s.Primary : s.Slot == WeaponSlot.Secondary ? s.Secondary : WeaponData.Knife;
            if (weapon != null && !string.IsNullOrEmpty(s.Skin))
            {
                if (c.SkinChoices == null) c.SkinChoices = new Dictionary<string, string>();
                c.SkinChoices[weapon.Id] = s.Skin;
            }
        }
        c.ApplyNetLoadout(s.Primary, s.Secondary, s.Slot, s.Mag, s.Reserve, s.Reload, s.AutoMode);
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
        combatant.SkinSeed = s.SkinSeed;
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
        // No HDR: the camera then draws straight to the screen instead of to an extra buffer that is copied over.
        cam.allowHDR = false;
        cam.fieldOfView = 75f;
        cam.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.62f, 0.75f, 0.9f);
        return cam;
    }

    void OrbitMenuCamera()
    {
        float angle = Time.unscaledTime * 0.05f;
        var t = MainCamera.transform;
        t.SetParent(null, true);
        t.position = new Vector3(Mathf.Cos(angle) * 75f, 55f, Mathf.Sin(angle) * 75f);
        t.LookAt(Vector3.zero);
        MainCamera.fieldOfView = 60f;
    }

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
        bool free = State == MatchState.Menu || State == MatchState.MatchOver || IsPaused || BuyMenuOpen;
        Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = free;
    }

    // ----------------------------------------------------------------- setup helpers

    static void SetupLighting()
    {
        Light sun = null;
        foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (light.type == LightType.Directional) { sun = light; break; }
        if (sun == null)
        {
            sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
        }
        sun.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
        sun.color = new Color(1f, 0.93f, 0.8f);
        sun.intensity = 0.95f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.7f;
        RenderSettings.sun = sun;

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.46f, 0.5f, 0.58f);
        RenderSettings.ambientEquatorColor = new Color(0.44f, 0.4f, 0.34f);
        RenderSettings.ambientGroundColor = new Color(0.26f, 0.22f, 0.18f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.84f, 0.77f, 0.64f);
        RenderSettings.fogStartDistance = 60f;
        RenderSettings.fogEndDistance = 260f;
        // Shadow distance and quality are set by GameSettings.
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
                case "-ds-rotate" when i + 3 < args.Length:
                    // -ds-rotate <x> <y> <z>: turns the skin chosen with -ds-equip in the hand, for this run (trying angles)
                    var turn = new Vector3(float.Parse(args[++i], CultureInfo.InvariantCulture), float.Parse(args[++i], CultureInfo.InvariantCulture),
                                           float.Parse(args[++i], CultureInfo.InvariantCulture));
                    if (equipWeapon != null && WeaponSkins.Find(equipWeapon, equipSkin) is WeaponSkin turned) turned.Rotation = turn;
                    break;
                case "-ds-perf":
                    perfLog = true;
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

    void RunAutomation()
    {
        float now = Time.realtimeSinceStartup;
        if (holdSlot.HasValue && Player != null && Player.Self.IsAlive && Player.Self.Current != null
            && Player.Self.Current.Data.Slot != holdSlot.Value)
            Player.Self.Equip(holdSlot.Value);
        // Hosting test: start the match once a client has joined.
        if (autoHost && autoStart && State == MatchState.Menu && Net.Peers.Count > 0) StartMatch();
        // Discovery test: join the first game found on the network.
        if (autoFind && Net.Role == NetSession.Mode.Off && Net.FoundHosts.Count > 0)
        {
            autoFind = false;
            Net.Join(Net.FoundHosts[0].Address);
        }
        // Client test: shoot straight ahead now and then, to check that shots reach the host.
        if (clientFireTest && IsClient && Player != null && Player.Self.IsAlive && State == MatchState.Live && now >= nextTestShot)
        {
            nextTestShot = now + 1f;
            Player.Self.TryFire(Player.transform.forward, 0f);
        }
        if (captureDirectory != null && now >= nextCaptureAt)
        {
            nextCaptureAt = now + 8f;
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

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
/// Runs the match: builds the map, spawns both teams, and handles rounds, money, the kill feed
/// and the spectator camera. Put it on an empty GameObject in a scene and press Play.
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

    static readonly string[] TerroristNames = { "Viper", "Jackal", "Cobra", "Scorpion", "Raven", "Wolf", "Dagger", "Ghost" };
    static readonly string[] SwatNames = { "Hawk", "Falcon", "Ranger", "Bishop", "Echo", "Titan", "Sentinel", "Patriot" };

    // Match settings, chosen in the main menu.
    public PlayerSide Side = PlayerSide.Terrorists;
    public int TeamSize = 5;
    public BotDifficulty Difficulty = BotDifficulty.Normal;
    public int RoundsToWin = 8;

    public MatchState State { get; private set; } = MatchState.Menu;
    public MapBuilder Map { get; private set; }
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

    public bool SpectatingNow => State != MatchState.Menu && (Player == null || !Player.Self.IsAlive);
    public float BuyTimeLeft => BuyTime - (Time.time - roundStartedAt);

    readonly List<BotController> bots = new List<BotController>();
    float roundStartedAt;
    int terroristLossStreak, swatLossStreak;

    // Command-line automation (used for smoke tests): -ds-autostart, -ds-side, -ds-money, -ds-quit-after, -ds-capture
    bool autoStart;
    int startMoney = StartMoney;
    float timeScale = 1f;
    float quitAt = -1f;
    string captureDirectory;
    float nextCaptureAt;
    int captureIndex;

    void Awake()
    {
        Instance = this;
        SoundFX.Init();
        SetupLighting();
        Map = new MapBuilder();
        Map.Build(transform);
        MainCamera = CreateCamera();
        if (GetComponent<GameUI>() == null) gameObject.AddComponent<GameUI>();
        ReadCommandLine();
    }

    void Start()
    {
        if (autoStart) StartMatch();
    }

    // ----------------------------------------------------------------- match flow

    public void StartMatch()
    {
        ClearMatch();
        Round = 0;
        TerroristWins = SwatWins = 0;
        terroristLossStreak = swatLossStreak = 0;

        if (Side != PlayerSide.Spectate)
            SpawnPlayer(Side == PlayerSide.Terrorists ? Team.Terrorists : Team.Swat);

        var terroristNames = new Queue<string>(Shuffled(TerroristNames));
        var swatNames = new Queue<string>(Shuffled(SwatNames));
        int terroristBots = TeamSize - (Side == PlayerSide.Terrorists ? 1 : 0);
        int swatBots = TeamSize - (Side == PlayerSide.Swat ? 1 : 0);
        for (int i = 0; i < terroristBots; i++) SpawnBot(Team.Terrorists, terroristNames.Dequeue());
        for (int i = 0; i < swatBots; i++) SpawnBot(Team.Swat, swatNames.Dequeue());

        foreach (var c in Combatants) c.Money = startMoney;
        Debug.Log($"[DesertStrike] Match started: {Side}, {TeamSize}v{TeamSize}, {Difficulty}, first to {RoundsToWin}");
        StartRound();
    }

    public void ReturnToMenu()
    {
        ClearMatch();
        State = MatchState.Menu;
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public void SetPaused(bool paused)
    {
        IsPaused = paused;
        Time.timeScale = paused ? 0f : timeScale;
        AudioListener.pause = paused;
    }

    void StartRound()
    {
        Round++;
        State = MatchState.Freeze;
        roundStartedAt = Time.time;
        StateEndsAt = Time.time + FreezeTime;
        Banner = null;
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
            float yaw = terrorist ? 0f : 180f;
            if (c.IsPlayer) Player.Respawn(position, yaw);
            else c.GetComponent<BotController>().Respawn(position, Quaternion.Euler(0f, yaw, 0f));
        }

        foreach (var bot in bots) bot.BuyGear();
        var terroristBots = bots.FindAll(b => b.Self.Team == Team.Terrorists);
        var swatBots = Shuffled(bots.FindAll(b => b.Self.Team == Team.Swat));
        BotController.PlanRound(terroristBots, swatBots, Map, Time.time + FreezeTime);

        SoundFX.Play(SoundFX.RoundStart, Vector3.zero, 0.4f, 1f, false);
        Debug.Log($"[DesertStrike] Round {Round} started (T {TerroristWins} - {SwatWins} SWAT)");
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
            AddMoney(c, c.Team == winner ? WinReward : LossReward + LossBonusStep * (streak - 1));
        }
        Debug.Log($"[DesertStrike] Round {Round}: {reason} (T {TerroristWins} - {SwatWins} SWAT)");
    }

    void Update()
    {
        HandleInput();
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
                if (Time.time >= StateEndsAt) EndRound(Team.Swat, "TIME IS UP - SWAT WIN");
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
        UpdateCursor();
        RunAutomation();
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

    // ----------------------------------------------------------------- called by combatants

    public bool CanBuy(Combatant c) =>
        c != null && c.IsAlive && (State == MatchState.Freeze || (State == MatchState.Live && BuyTimeLeft > 0f));

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
            // The player drops out of the scope only after the shot resolves, so this is the state it was fired in.
            Noscope = weapon.ZoomFov > 0f && killer != null && killer.IsPlayer && !Player.IsScoped,
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

        if (AliveCount(Team.Terrorists) == 0) EndRound(Team.Swat, "SWAT WIN");
        else if (AliveCount(Team.Swat) == 0) EndRound(Team.Terrorists, "TERRORISTS WIN");
    }

    /// <summary>Gunshots and running footsteps alert enemy bots within <paramref name="radius"/>.</summary>
    public void ReportNoise(Vector3 position, Team source, float radius)
    {
        float radiusSqr = radius * radius;
        foreach (var bot in bots)
            if (bot.Self.Team != source && (bot.transform.position - position).sqrMagnitude < radiusSqr)
                bot.HearNoise(position);
    }

    public void ShowHitMarker(bool headshot, bool kill)
    {
        HitMarkerTime = Time.time;
        HitMarkerHeadshot = headshot;
        HitMarkerKill = kill;
        SoundFX.Play(headshot ? SoundFX.Headshot : SoundFX.HitMarker, Vector3.zero, 0.5f, 1f, false);
    }

    static void AddMoney(Combatant c, int amount) => c.Money = Mathf.Min(MaxMoney, c.Money + amount);

    // ----------------------------------------------------------------- spawning

    void SpawnPlayer(Team team)
    {
        var go = new GameObject("Player") { layer = Ballistics.CharacterLayer };
        go.transform.position = (team == Team.Terrorists ? Map.TerroristSpawns : Map.SwatSpawns)[0];
        go.AddComponent<CharacterController>();
        var combatant = go.AddComponent<Combatant>();
        combatant.DisplayName = "You";
        combatant.Team = team;
        combatant.IsPlayer = true;
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
        var bot = go.AddComponent<BotController>();
        bot.Setup(Difficulty);
        Combatants.Add(combatant);
        bots.Add(bot);
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
        Effects.ClearDecals();
        SetPaused(false);
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
        cam.farClipPlane = 500f;
        cam.fieldOfView = 75f;
        cam.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.62f, 0.75f, 0.9f);
        return cam;
    }

    void OrbitMenuCamera()
    {
        float angle = Time.unscaledTime * 0.05f;
        var t = MainCamera.transform;
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
        QualitySettings.shadowDistance = 120f;
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
            }
        }
    }

    void RunAutomation()
    {
        float now = Time.realtimeSinceStartup;
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

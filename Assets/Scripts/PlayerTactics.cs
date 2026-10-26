using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Random = UnityEngine.Random;

/// <summary>One round of a real player: the way they went, the site they went for, how they held at the end.</summary>
[Serializable]
public class TacticPath
{
    public string site = "";                          // Terrorists: the site they went for; SWAT: the one they held ("": none)
    public List<float> points = new List<float>();    // x, y, z, and the seconds after the round went live they left the place
    public float lookX, lookZ;                        // where they looked at the end (flat, length 1)
    public float hold;                                // seconds they stayed at the end
    public bool crouch;                               // crouched there

    public int Count => points.Count / 4;
    public Vector3 Point(int i) => new Vector3(points[i * 4], points[i * 4 + 1], points[i * 4 + 2]);
    public float Time(int i) => points[i * 4 + 3];
}

/// <summary>How one player plays one side of one map.</summary>
[Serializable]
public class TeamTactics
{
    public int rounds;                                // rounds counted
    public int siteA, siteB;                          // Terrorists: rounds they went for each site
    public List<TacticPath> paths = new List<TacticPath>();   // the latest rounds first
    public List<int> buys = new List<int>();          // pairs: weapon index (WeaponData.All), rounds they started with it
    public int armor, helmet, kit;                    // rounds they started with each
    public List<float> kills = new List<float>();     // x, y, z, dx, dz: where they got kills from (the latest first)
    public int killCount, headshots;

    public void Fix()
    {
        if (paths == null) paths = new List<TacticPath>();
        if (buys == null) buys = new List<int>();
        if (kills == null) kills = new List<float>();
        paths.RemoveAll(p => p == null || p.points == null || p.points.Count < 8 || p.points.Count % 4 != 0);
        if (buys.Count % 2 != 0) buys.Clear();
        if (kills.Count % 5 != 0) kills.Clear();
    }
}

/// <summary>A player's way of playing a map (both sides): what the bots copy.</summary>
[Serializable]
public class TacticStyle
{
    public int v = 1;
    public TeamTactics t = new TeamTactics(), swat = new TeamTactics();

    public TeamTactics Of(Team team) => team == Team.Terrorists ? t : swat;
    public bool Usable(Team team) => Of(team).paths.Count > 0;

    public TacticStyle Fixed()
    {
        if (t == null) t = new TeamTactics();
        if (swat == null) swat = new TeamTactics();
        t.Fix();
        swat.Fix();
        return this;
    }
}

/// <summary>
/// The bots copy real players. Every round a game runs (an online room's game host, a LAN host, single player) it
/// follows each person from the moment the round goes live until their first fight: the way they walk (a place
/// every 5 m, and when they left it, so waiting counts too), the site they go for, where they end up holding and how
/// (looking where, crouched or not), what they start the round with, and later where they get their kills from.
/// That is each player's style on that map, the latest 8 rounds of each side.
///
/// Bots take a style each (online rooms: other players' from the online server; elsewhere the ones saved on this
/// computer and some from the online server) and play its rounds again: the same ways, at the same pace, to the same
/// sites, buying the same guns (BotController). When someone leaves a match, a bot takes over their player and
/// plays on with their own style (GameManager.StandIn) until they come back.
///
/// Online the styles are kept by the online API per account (online-server/tactics.py); LAN and single player keep
/// them on this computer (Tactics/&lt;map&gt;/&lt;name&gt;.json), by name.
/// </summary>
public class PlayerTactics : MonoBehaviour
{
    public const int MaxPaths = 8, MaxPoints = 32, MaxKills = 16;
    const float Sample = 0.5f, Step = 5f, MaxRecord = 80f;

    public static PlayerTactics Instance { get; private set; }

    /// <summary>Bots copy styles in online rooms and from Normal up (Easy bots stay simple).</summary>
    public bool Active => gm != null && gm.Tutorial == null && (gm.ServerMode || gm.Difficulty >= BotDifficulty.Normal);

    class Profile
    {
        public string Id;      // online: the account; otherwise the player's name
        public string Name;
        public TacticStyle Style = new TacticStyle();
        public bool Changed;
    }

    class Recording
    {
        public Combatant Who;
        public Profile Profile;
        public Team Team;
        public TacticPath Path = new TacticPath();
        public float Arrived;   // when they got to the latest place
        public int Health;
        public string Site;
        public bool Contact;    // shot or was shot: the plan is over, the fight begins
    }

    [Serializable] class Entry { public string user_id; public TacticStyle data; }

    [Serializable]
    class Answer
    {
        public string map;
        public List<Entry> players = new List<Entry>();
        public List<TacticStyle> others = new List<TacticStyle>();
    }

    [Serializable]
    class Upload
    {
        public string map;
        public List<Entry> players = new List<Entry>();
    }

    GameManager gm;
    string mapId;
    // Online game host: only the styles the API sent (or that it has none of yet); elsewhere every saved one.
    readonly Dictionary<string, Profile> profiles = new Dictionary<string, Profile>();
    readonly List<TacticStyle> others = new List<TacticStyle>();   // other players' styles (no names), for the bots
    readonly List<Recording> recordings = new List<Recording>();
    readonly HashSet<string> asking = new HashSet<string>();
    MatchState lastState;
    float nextSample, nextAsk;
    bool learnFromBots;   // -ds-learn-from-bots: bots count as people (tests the whole way round without players)

    void Awake()
    {
        Instance = this;
        gm = GetComponent<GameManager>();
        learnFromBots = Array.IndexOf(Environment.GetCommandLineArgs(), "-ds-learn-from-bots") >= 0;
    }

    void Update()
    {
        if (gm.Map != null && gm.Map.Id != mapId) UseMap(gm.Map.Id);
        if (gm.State != lastState)
        {
            bool host = !gm.Net.IsClient && gm.Tutorial == null;
            if (lastState == MatchState.Freeze && gm.State == MatchState.Live && !gm.Warmup && host) StartRecording();
            if (gm.State != MatchState.Live) FinishAll();
            if (gm.State == MatchState.MatchOver || gm.State == MatchState.Menu) Save();
            lastState = gm.State;
        }
        if (gm.ServerMode && Time.unscaledTime >= nextAsk) AskForPlayers();
        if (recordings.Count == 0 || Time.time < nextSample) return;
        nextSample = Time.time + Sample;
        float t = Time.time - gm.RoundLiveAt;
        for (int i = recordings.Count - 1; i >= 0; i--)
        {
            var r = recordings[i];
            if (r.Who != null && r.Who.Health < r.Health) r.Contact = true;
            if (r.Who == null || (!r.Who.IsHuman && !learnFromBots) || !r.Who.IsAlive || r.Contact || t > MaxRecord || r.Path.Count >= MaxPoints)
            {
                Finish(r);
                recordings.RemoveAt(i);
                continue;
            }
            Place(r, t);
        }
    }

    // ----------------------------------------------------------------- watching people

    void StartRecording()
    {
        recordings.Clear();
        foreach (var c in gm.Combatants)
        {
            if ((!c.IsHuman && !learnFromBots) || !c.IsAlive) continue;
            var profile = ProfileOf(c);
            if (profile == null) continue;
            var team = profile.Style.Of(c.Team);
            team.rounds++;
            var bought = c.Primary != null ? c.Primary.Data
                       : c.Secondary != null && c.Secondary.Data != WeaponData.Glock && c.Secondary.Data != WeaponData.Usp ? c.Secondary.Data : null;
            if (bought != null) AddBuy(team, WeaponData.IndexOf(bought));
            if (c.Armor > 0) team.armor++;
            if (c.Helmet) team.helmet++;
            if (c.HasDefuseKit) team.kit++;
            profile.Changed = true;
            var r = new Recording { Who = c, Profile = profile, Team = c.Team, Health = c.Health };
            Place(r, 0f);
            recordings.Add(r);
        }
    }

    static void AddBuy(TeamTactics team, int weapon)
    {
        for (int i = 0; i + 1 < team.buys.Count; i += 2)
            if (team.buys[i] == weapon)
            {
                team.buys[i + 1]++;
                return;
            }
        team.buys.Add(weapon);
        team.buys.Add(1);
    }

    // A new place every Step metres; standing near the last one only moves the time they left it.
    void Place(Recording r, float t)
    {
        Vector3 p = r.Who.transform.position;
        var points = r.Path.points;
        int n = r.Path.Count;
        if (n > 0 && (p - r.Path.Point(n - 1)).sqrMagnitude < Step * Step)
        {
            points[n * 4 - 1] = t;
            return;
        }
        points.Add(p.x);
        points.Add(p.y);
        points.Add(p.z);
        points.Add(t);
        r.Arrived = t;
        if (r.Site == null && r.Team == Team.Terrorists) r.Site = gm.Map.SiteAt(p);
    }

    void Finish(Recording r)
    {
        var path = r.Path;
        int n = path.Count;
        if (n < 2 || Flat(path.Point(0), path.Point(n - 1)) < 6f) return;   // stayed in spawn: nothing to copy
        Vector3 look = r.Who != null ? r.Who.transform.forward : path.Point(n - 1) - path.Point(n - 2);
        look.y = 0f;
        look = look.sqrMagnitude > 0.01f ? look.normalized : Vector3.forward;
        path.lookX = Round(look.x);
        path.lookZ = Round(look.z);
        path.hold = Round(Mathf.Clamp(path.Time(n - 1) - r.Arrived, 0f, 60f));
        path.crouch = r.Who != null && r.Who.Height < Combatant.StandHeight - 0.2f;
        path.site = r.Team == Team.Terrorists ? r.Site ?? Heading(path) : gm.Map.SiteAt(path.Point(n - 1)) ?? "";
        for (int i = 0; i < path.points.Count; i++) path.points[i] = Round(path.points[i]);
        var team = r.Profile.Style.Of(r.Team);
        team.paths.Insert(0, path);
        if (team.paths.Count > MaxPaths) team.paths.RemoveRange(MaxPaths, team.paths.Count - MaxPaths);
        if (path.site == "A" && r.Team == Team.Terrorists) team.siteA++;
        if (path.site == "B" && r.Team == Team.Terrorists) team.siteB++;
        r.Profile.Changed = true;
        Debug.Log($"[Tactics] {r.Profile.Name} ({NetSession.TeamName(r.Team)}): a way of {n} places" +
                  (path.site.Length > 0 ? $" to {path.site}" : "") + $", holding {path.hold:0} s");
    }

    // A Terrorist's way that ended before a site (the fight came first): the open site it clearly went towards, or "".
    string Heading(TacticPath path)
    {
        Vector3 start = path.Point(0), end = path.Point(path.Count - 1);
        float GainOn(bool siteA) => Flat(start, gm.Map.SitePoint(siteA)) - Flat(end, gm.Map.SitePoint(siteA));
        float a = gm.Map.ClosedSite == "A" ? float.MinValue : GainOn(true);
        float b = gm.Map.ClosedSite == "B" ? float.MinValue : GainOn(false);
        if (a > 10f && a > b + 8f) return "A";
        if (b > 10f && b > a + 8f) return "B";
        return "";
    }

    void FinishAll()
    {
        foreach (var r in recordings) Finish(r);
        recordings.Clear();
    }

    /// <summary>GameManager: a shot hit someone. The shooter's plan is over (they are fighting).</summary>
    public void OnHit(Combatant shooter)
    {
        foreach (var r in recordings)
            if (r.Who == shooter) r.Contact = true;
    }

    /// <summary>GameManager: the bomb went down. Everyone's plan is over; the planter went for that site.</summary>
    public void OnBombPlanted(Combatant planter, string site)
    {
        foreach (var r in recordings)
            if (r.Who == planter) r.Site = site;
        FinishAll();
    }

    /// <summary>GameManager: someone was killed (on the machine that runs the match).</summary>
    public void OnKill(Combatant victim, Combatant killer, bool headshot)
    {
        if (gm.Net.IsClient || gm.Tutorial != null || killer == null || (!killer.IsHuman && !learnFromBots) || killer.Team == victim.Team) return;
        var profile = ProfileOf(killer);
        if (profile == null) return;
        Vector3 shot = victim.transform.position - killer.transform.position;
        shot.y = 0f;
        if (shot.sqrMagnitude < 0.01f) return;
        shot.Normalize();
        var team = profile.Style.Of(killer.Team);
        Vector3 p = killer.transform.position;
        team.kills.InsertRange(0, new[] { Round(p.x), Round(p.y), Round(p.z), Round(shot.x), Round(shot.z) });
        if (team.kills.Count > MaxKills * 5) team.kills.RemoveRange(MaxKills * 5, team.kills.Count - MaxKills * 5);
        team.killCount++;
        if (headshot) team.headshots++;
        profile.Changed = true;
    }

    static float Round(float v) => Mathf.Round(v * 100f) / 100f;

    static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

    // Who a person is: online, their account (the API keeps their style); otherwise their name (kept on this computer).
    Profile ProfileOf(Combatant c)
    {
        string id, name;
        if (c.IsPlayer)
        {
            if (gm.ServerMode) return null;
            name = Accounts.LoggedIn ? Accounts.Current : gm.Net.PlayerName;
            id = name;
        }
        else
        {
            var remote = c.GetComponent<RemotePlayerController>();
            if (remote != null)
            {
                name = remote.Peer.Name;
                id = gm.ServerMode ? remote.Peer.AccountId : name;
            }
            else if (learnFromBots && !gm.ServerMode) id = name = c.DisplayName;
            else return null;
        }
        if (string.IsNullOrEmpty(id)) return null;
        if (profiles.TryGetValue(id, out var profile)) return profile;
        if (gm.ServerMode) return null;   // not sent by the API yet: nothing is kept (it could write over theirs)
        profile = new Profile { Id = id, Name = name };
        profiles[id] = profile;
        return profile;
    }

    // ----------------------------------------------------------------- for the bots

    /// <summary>The style of a person in this game (with what they did this match): for the bot that plays for them.</summary>
    public TacticStyle StyleOf(NetSession.Peer peer)
    {
        string id = gm.ServerMode ? peer.AccountId : peer.Name;
        return id != null && profiles.TryGetValue(id, out var profile) ? profile.Style : null;
    }

    /// <summary>
    /// Gives the bots of a match a real player's style each (different ones while there are enough), once. Online
    /// rooms copy other players than those in the room; elsewhere also the people who played on this computer.
    /// </summary>
    public void AssignStyles(List<BotController> bots)
    {
        if (!Active) return;
        var pool = new List<TacticStyle>(others);
        if (!gm.ServerMode)
            foreach (var p in profiles.Values) pool.Add(p.Style);
        if (pool.Count == 0) return;   // nothing yet (the download may still come): next round
        var used = new HashSet<TacticStyle>();
        foreach (var bot in bots)
            if (bot.Style != null) used.Add(bot.Style);
        int given = 0;
        foreach (var bot in bots)
        {
            if (bot.StyleChosen) continue;
            bot.StyleChosen = true;
            if (Random.value < 0.15f) continue;   // a few keep the map's own plans
            var usable = pool.FindAll(s => s.Usable(bot.Self.Team) && !used.Contains(s));
            if (usable.Count == 0) usable = pool.FindAll(s => s.Usable(bot.Self.Team));
            if (usable.Count == 0) continue;
            var style = usable[Random.Range(0, usable.Count)];
            used.Add(style);
            bot.UseStyle(style);
            given++;
        }
        if (given > 0) Debug.Log($"[Tactics] {given} bot(s) play like real players ({pool.Count} styles on {mapId})");
    }

    // ----------------------------------------------------------------- keeping the styles

    string Folder(string id) => Path.Combine(Application.persistentDataPath, "Tactics", id);

    static string FileName(string name)
    {
        var sb = new StringBuilder();
        foreach (char ch in name) sb.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_');
        return sb.ToString() + ".json";
    }

    void UseMap(string id)
    {
        Save();
        FinishAll();
        mapId = id;
        profiles.Clear();
        others.Clear();
        asking.Clear();
        if (!gm.ServerMode)
        {
            try
            {
                string folder = Folder(id);
                if (Directory.Exists(folder))
                    foreach (string file in Directory.GetFiles(folder, "*.json"))
                    {
                        var style = Read(File.ReadAllText(file));
                        string name = Path.GetFileNameWithoutExtension(file);
                        if (style != null) profiles[name] = new Profile { Id = name, Name = name, Style = style };
                    }
                string online = Path.Combine(Application.persistentDataPath, "Tactics", id + ".online.json");
                if (File.Exists(online)) TakeOthers(File.ReadAllText(online));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Tactics] Could not read the saved styles: " + e.Message);
            }
        }
        StartCoroutine(DownloadOthers(id));
    }

    static TacticStyle Read(string json)
    {
        try
        {
            var style = JsonUtility.FromJson<TacticStyle>(json);
            return style?.Fixed();
        }
        catch (Exception)
        {
            return null;
        }
    }

    bool TakeOthers(string json)
    {
        Answer answer;
        try
        {
            answer = JsonUtility.FromJson<Answer>(json);
        }
        catch (Exception)
        {
            return false;
        }
        if (answer == null || answer.map != mapId || answer.others == null) return false;
        others.Clear();
        foreach (var style in answer.others)
            if (style != null) others.Add(style.Fixed());
        return true;
    }

    // Other players' styles for the bots: from the online API (the game hosts by the internal address).
    IEnumerator DownloadOthers(string id)
    {
        string url;
        if (gm.ServerMode) url = $"{OnlineHost.ControlUrl}/internal/tactics/{id}";
        else
        {
            float until = Time.realtimeSinceStartup + 30f;
            while ((gm.Online == null || gm.Online.ApiUrl == null) && Time.realtimeSinceStartup < until) yield return new WaitForSecondsRealtime(1f);
            if (gm.Online == null || gm.Online.ApiUrl == null) yield break;
            url = $"{gm.Online.ApiUrl}/tactics/{id}";
        }
        using (var request = UnityWebRequest.Get(url))
        {
            if (gm.ServerMode) request.SetRequestHeader("X-Internal-Key", OnlineHost.InternalKey ?? "");
            request.timeout = 10;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success || id != mapId) yield break;
            string text = request.downloadHandler.text;
            if (!TakeOthers(text)) yield break;
            Debug.Log($"[Tactics] {others.Count} player style(s) on {id} from the online server");
            if (gm.ServerMode) yield break;
            try
            {
                Directory.CreateDirectory(Path.Combine(Application.persistentDataPath, "Tactics"));
                File.WriteAllText(Path.Combine(Application.persistentDataPath, "Tactics", id + ".online.json"), text);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Tactics] Could not save the styles: " + e.Message);
            }
        }
    }

    // Online game host: the styles of the room's players come from the API (each account's, on this map).
    void AskForPlayers()
    {
        nextAsk = Time.unscaledTime + 2f;
        if (mapId == null) return;
        var wanted = new List<string>();
        foreach (var peer in gm.Net.Peers)
            if (peer.AccountId != null && !profiles.ContainsKey(peer.AccountId) && !asking.Contains(peer.AccountId)) wanted.Add(peer.AccountId);
        if (wanted.Count > 0) StartCoroutine(Ask(mapId, wanted));
    }

    IEnumerator Ask(string id, List<string> accounts)
    {
        foreach (var a in accounts) asking.Add(a);
        string url = $"{OnlineHost.ControlUrl}/internal/tactics/{id}?users={UnityWebRequest.EscapeURL(string.Join(",", accounts))}";
        using (var request = UnityWebRequest.Get(url))
        {
            request.SetRequestHeader("X-Internal-Key", OnlineHost.InternalKey ?? "");
            request.timeout = 10;
            yield return request.SendWebRequest();
            foreach (var a in accounts) asking.Remove(a);
            if (id != mapId) yield break;
            Answer answer = null;
            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    answer = JsonUtility.FromJson<Answer>(request.downloadHandler.text);
                }
                catch (Exception)
                {
                }
            }
            if (answer == null || answer.map != id)
            {
                nextAsk = Time.unscaledTime + 15f;   // the API is not answering: later
                yield break;
            }
            foreach (string account in accounts)
            {
                var entry = answer.players?.Find(e => e != null && e.user_id == account);
                var peer = gm.Net.Peers.Find(p => p.AccountId == account);
                profiles[account] = new Profile
                {
                    Id = account, Name = peer != null ? peer.Name : account,
                    Style = entry?.data != null ? entry.data.Fixed() : new TacticStyle(),
                };
            }
        }
    }

    void Save()
    {
        var changed = new List<Profile>();
        foreach (var p in profiles.Values)
            if (p.Changed) changed.Add(p);
        if (gm.ServerMode)
        {
            // Those who left the room: asked for again if they come back (they may have played elsewhere meanwhile).
            foreach (var p in new List<Profile>(profiles.Values))
                if (!gm.Net.Peers.Exists(peer => peer.AccountId == p.Id)) profiles.Remove(p.Id);
        }
        if (changed.Count == 0 || mapId == null) return;
        foreach (var p in changed) p.Changed = false;
        if (gm.ServerMode)
        {
            var upload = new Upload { map = mapId };
            foreach (var p in changed) upload.players.Add(new Entry { user_id = p.Id, data = p.Style });
            StartCoroutine(SendToControl(JsonUtility.ToJson(upload), changed.Count));
            return;
        }
        try
        {
            string folder = Folder(mapId);
            Directory.CreateDirectory(folder);
            foreach (var p in changed) File.WriteAllText(Path.Combine(folder, FileName(p.Id)), JsonUtility.ToJson(p.Style));
            Debug.Log($"[Tactics] Saved the styles of {changed.Count} player(s) on {mapId}");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Tactics] Could not save the styles: " + e.Message);
        }
    }

    // Quitting in the middle of a match: what this computer saw is kept all the same.
    void OnApplicationQuit()
    {
        if (gm.ServerMode) return;
        FinishAll();
        Save();
    }

    IEnumerator SendToControl(string json, int count)
    {
        using (var request = new UnityWebRequest($"{OnlineHost.ControlUrl}/internal/tactics", "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("X-Internal-Key", OnlineHost.InternalKey ?? "");
            request.timeout = 10;
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success) Debug.Log($"[Tactics] Sent the styles of {count} player(s) to the online API");
            else Debug.LogWarning("[Tactics] Could not send the styles: " + request.error);
        }
    }
}

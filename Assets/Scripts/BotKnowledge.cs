using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Networking;
using Random = UnityEngine.Random;

/// <summary>
/// The bots learn from real players. Every match a game runs (an online room's game host, a LAN host, single player)
/// counts its people, per team, in 3 m cells of the map: where they stand, and where they get kills from (and which
/// way they shot). After the match the counts go to the online API (online-server/bot_knowledge.py), one database for
/// all games; every game downloads it for its map (and keeps a copy for when it is offline). Learned this match counts
/// at once too.
///
/// Bots use it in online rooms (with friends or anyone) and on Hard and Extreme: they walk and hold with their aim on
/// the spots where enemy players get their kills from, react faster to an enemy who shows up where they were already
/// aiming, and take the positions players of their own team like to hold.
/// </summary>
public class BotKnowledge : MonoBehaviour
{
    public const float Cell = 3f, Layer = 2.5f;

    public struct Spot
    {
        public Vector3 Position;    // the cell's middle, near its floor
        public float Weight;        // 0..1: the map's strongest cell of its kind (and team) is 1
        public Vector3 Direction;   // kill spots: the way people shot from here (flat, length 1)
    }

    [Serializable]
    class Answer
    {
        public string map;
        public int matches;
        public List<float> stand = new List<float>();
        public List<float> kill = new List<float>();
    }

    [Serializable]
    class Upload
    {
        public string map, mode;
        public List<float> cells = new List<float>();
    }

    public static BotKnowledge Instance { get; private set; }

    /// <summary>The bots use what people do: online rooms, Hard and Extreme.</summary>
    public bool Active => gm != null && (gm.ServerMode || gm.Difficulty >= BotDifficulty.Hard);
    /// <summary>Matches the downloaded knowledge comes from (0: nothing learned yet on this map).</summary>
    public int Matches { get; private set; }

    GameManager gm;
    string mapId;
    // [team]: where people of that team got kills from, and where they stood (downloaded); and this run's own kills
    readonly List<Spot>[] kills = { new List<Spot>(), new List<Spot>() };
    readonly List<Spot>[] stands = { new List<Spot>(), new List<Spot>() };
    readonly List<Spot>[] freshKills = { new List<Spot>(), new List<Spot>() };
    Coroutine downloading;
    // This match's counts, sent after it: (team, kind, cell x, y, z) -> (weight, dx, dz)
    readonly Dictionary<(int, int, int, int, int), Vector3> counts = new Dictionary<(int, int, int, int, int), Vector3>();
    int countedKills;
    int preAims, favoured;   // this round: how often the bots used what they learned (logged at the round's end)
    float nextSample;
    MatchState lastState;
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
            // A match over, or left: what people did goes to the online server.
            if (gm.State == MatchState.MatchOver || gm.State == MatchState.Menu) Send();
            if (gm.State == MatchState.RoundEnd && preAims + favoured > 0)
                Debug.Log($"[Bots] This round the bots aimed at {preAims} spots players shoot from and took {favoured} spots players hold");
            if (gm.State == MatchState.RoundEnd) preAims = favoured = 0;
            lastState = gm.State;
        }
        if (gm.State != MatchState.Live || gm.Net.IsClient || Time.time < nextSample) return;
        nextSample = Time.time + 2f;
        foreach (var c in gm.Combatants)
            if (c.IsAlive && (c.IsHuman || learnFromBots)) Count(c.Team, 0, c.transform.position, Vector3.zero);
    }

    /// <summary>GameManager: someone was killed (on the machine that runs the match).</summary>
    public void OnKill(Combatant victim, Combatant killer)
    {
        if (gm.Net.IsClient || killer == null || killer == victim || killer.Team == victim.Team) return;
        if (!killer.IsHuman && !learnFromBots) return;
        Vector3 shot = victim.transform.position - killer.transform.position;
        shot.y = 0f;
        if (shot.sqrMagnitude < 0.01f) return;
        shot.Normalize();
        Count(killer.Team, 1, killer.transform.position, shot);
        countedKills++;
        // Learned at once: the bots of this match know the spot from now on.
        freshKills[TeamIndex(killer.Team)].Add(new Spot { Position = CellMiddle(CellOf(killer.transform.position)), Weight = 0.6f, Direction = shot });
    }

    // ----------------------------------------------------------------- for the bots

    /// <summary>
    /// Where a bot should have its aim: the strongest spot enemy players get their kills from that it can see from
    /// <paramref name="eye"/>, 5 to 45 m away, within <paramref name="maxAngle"/> degrees of <paramref name="facing"/>.
    /// </summary>
    public Vector3? PreAim(Team botTeam, Vector3 eye, Vector3 facing, float maxAngle)
    {
        int enemy = TeamIndex(botTeam == Team.Terrorists ? Team.Swat : Team.Terrorists);
        if (kills[enemy].Count + freshKills[enemy].Count == 0) return null;
        facing.y = 0f;
        if (facing.sqrMagnitude < 0.01f) return null;
        // The best few by weight and distance, then the first of them in sight (sight lines cost the most).
        var best = new List<(float score, Vector3 point)>();
        foreach (var spot in Both(kills[enemy], freshKills[enemy]))
        {
            Vector3 point = spot.Position + Vector3.up * 1.3f;
            Vector3 to = point - eye;
            float distance = new Vector2(to.x, to.z).magnitude;
            if (distance < 5f || distance > 45f || Mathf.Abs(to.y) > 12f) continue;
            if (Vector3.Angle(facing, new Vector3(to.x, 0f, to.z)) > maxAngle) continue;
            float score = spot.Weight * (1f - distance / 60f);
            if (best.Count < 4) best.Add((score, point));
            else
            {
                int worst = 0;
                for (int i = 1; i < best.Count; i++)
                    if (best[i].score < best[worst].score) worst = i;
                if (score > best[worst].score) best[worst] = (score, point);
            }
        }
        best.Sort((a, b) => b.score.CompareTo(a.score));
        foreach (var (_, point) in best)
            if (Ballistics.HasLineOfSight(eye, point))
            {
                preAims++;
                return point;
            }
        return null;
    }

    /// <summary>A spot players of <paramref name="team"/> like to hold (where they get kills from), picked at random
    /// by weight among the strongest, on the navmesh and reachable from <paramref name="from"/>.</summary>
    public bool FavouredSpot(Team team, Vector3 from, out Spot found)
    {
        bool ok = PickReachable(new List<Spot>(Both(kills[TeamIndex(team)], freshKills[TeamIndex(team)])), from, out found);
        if (ok) favoured++;
        return ok;
    }

    /// <summary>A place where players of the other team often are: worth checking when hunting.</summary>
    public bool EnemyHaunt(Team team, Vector3 from, out Spot found) =>
        PickReachable(new List<Spot>(stands[TeamIndex(team == Team.Terrorists ? Team.Swat : Team.Terrorists)]), from, out found);

    static IEnumerable<Spot> Both(List<Spot> a, List<Spot> b)
    {
        foreach (var s in a) yield return s;
        foreach (var s in b) yield return s;
    }

    static bool PickReachable(List<Spot> strongest, Vector3 from, out Spot found)
    {
        found = default;
        if (strongest.Count == 0) return false;
        strongest.Sort((a, b) => b.Weight.CompareTo(a.Weight));
        if (strongest.Count > 12) strongest.RemoveRange(12, strongest.Count - 12);
        strongest.RemoveAll(s => s.Weight < 0.15f);
        for (int tries = 0; tries < 3 && strongest.Count > 0; tries++)
        {
            float total = 0f;
            foreach (var s in strongest) total += s.Weight;
            float pick = Random.value * total;
            int index = 0;
            while (index < strongest.Count - 1 && (pick -= strongest[index].Weight) > 0f) index++;
            var spot = strongest[index];
            strongest.RemoveAt(index);
            if (!NavMesh.SamplePosition(spot.Position, out var hit, 2.5f, NavMesh.AllAreas)) continue;
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(from, hit.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
            spot.Position = hit.position;
            found = spot;
            return true;
        }
        return false;
    }

    // ----------------------------------------------------------------- counting and sending

    static int TeamIndex(Team team) => team == Team.Terrorists ? 0 : 1;

    static (int, int, int) CellOf(Vector3 p) =>
        (Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt((p.y + 0.5f) / Layer), Mathf.FloorToInt(p.z / Cell));

    static Vector3 CellMiddle((int x, int y, int z) cell) => new Vector3((cell.x + 0.5f) * Cell, cell.y * Layer, (cell.z + 0.5f) * Cell);

    void Count(Team team, int kind, Vector3 position, Vector3 shot)
    {
        var (x, y, z) = CellOf(position);
        var key = (TeamIndex(team), kind, x, y, z);
        counts.TryGetValue(key, out var sum);
        float cap = kind == 1 ? 30f : 150f;   // what one match may add to a cell (the API checks the same)
        if (sum.x >= cap) return;
        counts[key] = new Vector3(sum.x + 1f, sum.y + shot.x, sum.z + shot.z);
    }

    void Send()
    {
        if (counts.Count == 0) return;
        int samples = 0;
        foreach (var c in counts.Values) samples += Mathf.RoundToInt(c.x);
        bool worth = countedKills > 0 || samples >= 15;
        var upload = new Upload { map = mapId, mode = gm.ServerMode ? "online" : gm.Net.IsHost ? "lan" : "solo" };
        // The strongest 300 cells (the API's limit for one match).
        var cells = new List<KeyValuePair<(int, int, int, int, int), Vector3>>(counts);
        cells.Sort((a, b) => (b.Key.Item2 * 1000f + b.Value.x).CompareTo(a.Key.Item2 * 1000f + a.Value.x));
        for (int i = 0; i < cells.Count && i < 300; i++)
        {
            var (team, kind, x, y, z) = cells[i].Key;
            var v = cells[i].Value;
            upload.cells.AddRange(new float[] { team, kind, x, y, z, v.x, v.y, v.z });
        }
        counts.Clear();
        countedKills = 0;
        if (!worth) return;
        string json = JsonUtility.ToJson(upload);
        if (gm.ServerMode)
        {
            StartCoroutine(SendToControl(json));
            return;
        }
        if (gm.Online != null && gm.Online.LoggedIn) gm.Online.SendKnowledge(json);
        else Debug.Log("[Bots] What people did this match stays on this computer (log in online to share it with every game)");
    }

    IEnumerator SendToControl(string json)
    {
        using (var request = new UnityWebRequest($"{OnlineHost.ControlUrl}/internal/bot-knowledge", "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("X-Internal-Key", OnlineHost.InternalKey ?? "");
            request.timeout = 10;
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success) Debug.Log("[Bots] Sent what people did this match to the online API");
            else Debug.LogWarning("[Bots] Could not send what people did: " + request.error);
        }
    }

    // ----------------------------------------------------------------- the map's knowledge

    string CachePath(string id) => Path.Combine(Application.persistentDataPath, "BotKnowledge", id + ".json");

    void UseMap(string id)
    {
        Send();   // anything counted on the map before
        mapId = id;
        Matches = 0;
        foreach (var list in kills) list.Clear();
        foreach (var list in stands) list.Clear();
        foreach (var list in freshKills) list.Clear();
        try
        {
            string path = CachePath(id);
            if (File.Exists(path)) Apply(File.ReadAllText(path), fromCache: true);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Bots] Could not read the saved knowledge: " + e.Message);
        }
        if (downloading != null) StopCoroutine(downloading);
        downloading = StartCoroutine(Download(id));
    }

    IEnumerator Download(string id)
    {
        string url;
        string key = null;
        if (gm.ServerMode)
        {
            url = $"{OnlineHost.ControlUrl}/internal/bot-knowledge/{id}";
            key = OnlineHost.InternalKey;
        }
        else
        {
            // The API's address comes from the website first (OnlineClient); give it a little time.
            float until = Time.realtimeSinceStartup + 30f;
            while ((gm.Online == null || gm.Online.ApiUrl == null) && Time.realtimeSinceStartup < until) yield return new WaitForSecondsRealtime(1f);
            if (gm.Online == null || gm.Online.ApiUrl == null) yield break;
            url = $"{gm.Online.ApiUrl}/bot-knowledge/{id}";
        }
        using (var request = UnityWebRequest.Get(url))
        {
            if (key != null) request.SetRequestHeader("X-Internal-Key", key);
            request.timeout = 10;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success || id != mapId) yield break;
            string text = request.downloadHandler.text;
            if (!Apply(text, fromCache: false)) yield break;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CachePath(id)));
                File.WriteAllText(CachePath(id), text);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Bots] Could not save the knowledge: " + e.Message);
            }
        }
    }

    bool Apply(string json, bool fromCache)
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
        if (answer == null || answer.map != mapId) return false;
        for (int team = 0; team < 2; team++)
        {
            kills[team].Clear();
            stands[team].Clear();
        }
        for (int i = 0; i + 6 < answer.kill.Count; i += 7)
        {
            var k = answer.kill;
            int team = Mathf.Clamp((int)k[i], 0, 1);
            kills[team].Add(new Spot
            {
                Position = CellMiddle(((int)k[i + 1], (int)k[i + 2], (int)k[i + 3])), Weight = Mathf.Clamp01(k[i + 4]),
                Direction = new Vector3(k[i + 5], 0f, k[i + 6]).normalized,
            });
        }
        for (int i = 0; i + 4 < answer.stand.Count; i += 5)
        {
            var s = answer.stand;
            int team = Mathf.Clamp((int)s[i], 0, 1);
            stands[team].Add(new Spot { Position = CellMiddle(((int)s[i + 1], (int)s[i + 2], (int)s[i + 3])), Weight = Mathf.Clamp01(s[i + 4]) });
        }
        Matches = answer.matches;
        Debug.Log($"[Bots] Knowledge of {mapId}{(fromCache ? " (saved)" : "")}: {answer.matches} matches, " +
                  $"{kills[0].Count + kills[1].Count} kill spots, {stands[0].Count + stands[1].Count} places people stand");
        return true;
    }
}

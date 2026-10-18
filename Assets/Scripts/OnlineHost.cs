using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// -ds-server: this copy of the game is an online game host with no player of its own, started by online-server/
/// on the owner's PC (normally with -batchmode -nographics, so without a window). It runs one room's match at a
/// time: every second it reports to the online API's internal address on this PC (-ds-control, localhost only)
/// which room it has, its players and the match state, and gets back the room's settings, who may be in it and
/// whether the owner pressed START. Players reach it through a playit.gg UDP tunnel to its port (-ds-port) and
/// bring a ticket from the API (<see cref="JoinTicket"/>), checked with GAME_JOIN_SECRET, which the API passes in
/// the environment (LOWSTRIKE_GAME_JOIN_SECRET). After a match it goes back to the room's lobby; when the room
/// closes it waits for the next one.
/// </summary>
public class OnlineHost : MonoBehaviour
{
    public static int Port = NetSession.GamePort;
    public static string SlotId = "room-1";
    public static string ControlUrl = "http://127.0.0.1:8001";
    /// <summary>The key for the internal API (BotKnowledge sends with it too).</summary>
    public static string InternalKey { get; private set; }

    const float MatchOverPause = 12f;   // the result stays on the players' screens this long
    const float EmptyMatchEnd = 30f;    // a match nobody is connected to ends after this long

    [Serializable] class HostPlayer { public string user_id; public string team; public bool connected; }

    [Serializable]
    class Report
    {
        public string room_id = "", state = "lobby", version = "", map = "";
        public int protocol, match_number, terrorist_wins, swat_wins;
        public List<HostPlayer> players = new List<HostPlayer>();
    }

    [Serializable] class Member { public string user_id, nick, team; }

    [Serializable]
    class Room
    {
        public string id, name, map, difficulty, owner_id, closed_site;
        public int team_size, rounds;
        public bool friendly_fire, start;
        public List<Member> members = new List<Member>();
    }

    [Serializable] class Answer { public bool has_room; public Room room; }

    GameManager gm;
    string key;
    bool asking;
    float nextReport, overSince = -1f, emptySince = -1f;
    int matchNumber;
    string lastProblem;

    void Start()
    {
        gm = GetComponent<GameManager>();
        string secret = Environment.GetEnvironmentVariable("LOWSTRIKE_GAME_JOIN_SECRET");
        if (string.IsNullOrEmpty(secret) || secret.Length < 32)
        {
            Debug.LogError("[Server] LOWSTRIKE_GAME_JOIN_SECRET is not set: start game hosts with online-server (it sets it), " +
                           "or set it to GAME_JOIN_SECRET from online-server/.env");
            gm.QuitGame();
            return;
        }
        key = JoinTicket.InternalKey(secret);
        InternalKey = key;
        Application.targetFrameRate = 60;   // without a screen nothing else holds it back
        QualitySettings.vSyncCount = 0;
        AudioListener.volume = 0f;
        gm.Side = PlayerSide.Spectate;
        gm.Net.PlayerName = "Low Strike";
        if (!gm.Net.StartHost(Port, online: true, secret: secret))
        {
            Debug.LogError($"[Server] {gm.Net.Status} (UDP port {Port})");
            gm.QuitGame();
            return;
        }
        gm.Net.HostName = "Online room";
        Debug.Log($"[Server] Game host {SlotId} on UDP port {Port}, reporting to {ControlUrl} (Low Strike {Application.version}, protocol {NetSession.Protocol})");
    }

    void Update()
    {
        if (gm == null || !gm.Net.IsHost) return;
        ManageMatch();
        if (!asking && Time.unscaledTime >= nextReport)
        {
            nextReport = Time.unscaledTime + 1f;
            StartCoroutine(ReportState());
        }
    }

    // After the result has been shown, or when nobody is left in the match: back to the lobby for the next one.
    void ManageMatch()
    {
        float now = Time.unscaledTime;
        overSince = gm.State == MatchState.MatchOver ? (overSince < 0f ? now : overSince) : -1f;
        bool empty = gm.State != MatchState.Menu && !gm.Net.Peers.Exists(p => !p.Away);
        emptySince = empty ? (emptySince < 0f ? now : emptySince) : -1f;
        if ((overSince >= 0f && now - overSince > MatchOverPause) || (emptySince >= 0f && now - emptySince > EmptyMatchEnd))
        {
            Debug.Log(overSince >= 0f ? "[Server] Match over: back to the lobby" : "[Server] Nobody is connected: the match ends");
            overSince = emptySince = -1f;
            gm.BackToLobby();
        }
    }

    IEnumerator ReportState()
    {
        asking = true;
        var net = gm.Net;
        var report = new Report
        {
            room_id = net.RoomId ?? "",
            state = gm.State == MatchState.Menu ? "lobby" : gm.State == MatchState.MatchOver ? "over" : "playing",
            protocol = NetSession.Protocol,
            version = Application.version,
            match_number = matchNumber,
            terrorist_wins = gm.TerroristWins,
            swat_wins = gm.SwatWins,
            map = gm.Map != null ? gm.Map.Id : "",
        };
        foreach (var peer in net.Peers)
            if (peer.AccountId != null)
                report.players.Add(new HostPlayer
                {
                    user_id = peer.AccountId,
                    team = peer.Spectator ? "spectator" : peer.Team == Team.Swat ? "swat" : "t",
                    connected = !peer.Away,
                });

        using (var request = new UnityWebRequest($"{ControlUrl}/internal/slots/{SlotId}/heartbeat", "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(report)));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("X-Internal-Key", key);
            request.timeout = 5;
            yield return request.SendWebRequest();
            asking = false;
            if (request.result != UnityWebRequest.Result.Success)
            {
                // The match goes on; the reports start again when the API is back.
                string problem = request.responseCode == 403 ? "the API refused our key (is GAME_JOIN_SECRET the same?)"
                               : request.responseCode == 404 ? $"the API has no slot {SlotId} in ROOM_SLOTS_JSON"
                               : request.error;
                if (problem != lastProblem) Debug.LogWarning("[Server] The online API does not answer: " + problem);
                lastProblem = problem;
                yield break;
            }
            if (lastProblem != null) Debug.Log("[Server] The online API answers again");
            lastProblem = null;
            Answer answer = null;
            try
            {
                answer = JsonUtility.FromJson<Answer>(request.downloadHandler.text);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Server] Could not read the API's answer: " + e.Message);
            }
            if (answer != null) Apply(answer);
        }
    }

    void Apply(Answer answer)
    {
        var net = gm.Net;
        var room = answer.has_room ? answer.room : null;
        if (room == null || string.IsNullOrEmpty(room.id))
        {
            if (net.RoomId != null) CloseRoom("The room was closed.");
            return;
        }
        if (net.RoomId != room.id)
        {
            if (net.RoomId != null) CloseRoom("The room was closed.");
            net.RoomId = room.id;
            matchNumber = 0;
            Debug.Log($"[Server] Now hosting room '{room.name}'");
        }
        net.HostName = room.name;
        net.AllowedAccounts.Clear();
        foreach (var member in room.members) net.AllowedAccounts.Add(member.user_id);
        // Players who left the room (in the menu, or their game closed) leave the game as well.
        foreach (var peer in net.Peers.ToArray())
            if (peer.AccountId != null && !net.AllowedAccounts.Contains(peer.AccountId)) net.Kick(peer, "You are no longer in this room.");

        if (gm.State != MatchState.Menu) return;
        Enum.TryParse(room.difficulty, out BotDifficulty difficulty);
        gm.ApplyRoomSettings(room.map, room.team_size, difficulty, room.rounds, room.friendly_fire, room.closed_site);
        if (room.start && net.Peers.Exists(p => !p.Away))
        {
            matchNumber++;
            Debug.Log($"[Server] The owner started match {matchNumber} in '{room.name}': {gm.MatchDescription()}");
            gm.StartMatch();
        }
    }

    void CloseRoom(string reason)
    {
        var net = gm.Net;
        Debug.Log($"[Server] Room closed; {net.Peers.Count} player(s) sent away");
        foreach (var peer in net.Peers.ToArray()) net.Kick(peer, reason);
        if (gm.State != MatchState.Menu) gm.BackToLobby();
        net.RoomId = null;
        net.AllowedAccounts.Clear();
        net.HostName = "Online room";
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// What the online API sends and takes (field names as in its JSON).
[Serializable] public class SkinPair { public string weapon, skin; }

[Serializable]
public class OnlineProfile
{
    public List<SkinPair> skins = new List<SkinPair>();
    public bool has_settings;
    public int crosshair_color, voice_mode;
    public float crosshair_r, crosshair_g, crosshair_b, crosshair_length, crosshair_thickness, crosshair_gap, crosshair_alpha;
    public bool crosshair_outline, crosshair_dot, crosshair_t, crosshair_dynamic, voice_chat;
}

[Serializable]
public class OnlineUser
{
    public string id, nick, created_at;
    public bool hide_status, linked_local, admin;
    public OnlineProfile profile = new OnlineProfile();
}

[Serializable] public class FriendInfo { public string id, nick, status; public int played; }
[Serializable] public class PlayedInfo { public string nick; public int played; }
[Serializable] public class RequestInfo { public string id, user_id, nick; }
[Serializable] public class MemberInfo { public string user_id, nick, team; public bool connected, owner; }

[Serializable]
public class RoomInfo
{
    public string id, name, map, status, owner, owner_id, difficulty, code, closed_site;
    public int team_size, terrorists, swat, max_players, players, spectators, rounds;
    public bool @private, friendly_fire, is_member, is_owner, server_online;

    /// <summary>Players in each team (an older API only has team_size, the same for both).</summary>
    public int Terrorists => terrorists > 0 ? terrorists : team_size;
    public int Swat => swat > 0 ? swat : team_size;
    public List<MemberInfo> members = new List<MemberInfo>();
}

[Serializable] public class InviteInfo { public string id, room_id, room_name, map, from, status; }
[Serializable] public class PlayerInfo { public string id, nick; public bool friend, request_sent, request_received; }
[Serializable] public class ShopItem { public string weapon, skin; public int price; }

/// <summary>The admin panel's view of a player's account (online-server/admin.py).</summary>
[Serializable]
public class AdminPlayer
{
    public string nick, created_at;
    public int tokens, today, day_limit, played;
    public List<string> owned = new List<string>();
    public List<ShopItem> shop = new List<ShopItem>();
}

[Serializable] public class AdminLogEntry { public double at; public string admin, target, action; }
[Serializable] public class HealthInfo { public string status, game_version; public int protocol, servers, rooms_free, players_online; }

[Serializable]
public class RoomSettingsBody
{
    public string name = "", map = "dune", difficulty = "Normal", team = "t", closed_site = "B";
    public int team_size = 5, terrorists = 5, swat = 5, rounds = 8;   // team_size: the bigger team (older APIs read only that)
    public bool friendly_fire, @private;

    public void SetTeams(int t, int s)
    {
        terrorists = t;
        swat = s;
        team_size = Math.Max(t, s);
    }
}

/// <summary>
/// The online part for players: the online account, friends over the internet, rooms, and joining a room's game
/// host. Talks to the online API (online-server/) over HTTPS and simply asks again every few seconds; nothing stays
/// open. While the API cannot be reached, everything offline (single player, LAN, local accounts) works as before.
///
/// Where the API is: -ds-api &lt;url&gt; (tests), otherwise the "api" in the website's online.json, so the server can
/// move without a new game; the last one found is remembered for when the website cannot be reached. Logged in, only
/// the session token is kept (PlayerPrefs), never the password. The account's skins and its crosshair and voice
/// settings are saved on the server whenever they change, and come back on any computer it logs in on.
/// </summary>
public class OnlineClient : MonoBehaviour
{
    public enum Service { Checking, NotSetUp, Online, Unavailable }

    public const string ConfigUrl = "https://happzgirafe.github.io/DesertStrike/online.json";
    const string ApiKey = "DesertStrike.online.api", TokenKey = "DesertStrike.online.token";

    [Serializable] class Config { public string api, latest_version, download; }
    [Serializable] class Credentials { public string nick, password; }
    [Serializable] class AuthAnswer { public string token; public OnlineUser user; }
    [Serializable] class Error { public string detail; }
    [Serializable] class PlayedAnswer { public List<PlayedInfo> players = new List<PlayedInfo>(); }
    [Serializable] class PresenceAnswer { public List<FriendInfo> friends = new List<FriendInfo>(); public bool hidden; public int requests, invites; public string room_id; }
    [Serializable] class FriendsAnswer { public List<FriendInfo> friends = new List<FriendInfo>(); public List<RequestInfo> incoming = new List<RequestInfo>(), outgoing = new List<RequestInfo>(); }
    [Serializable] class RoomsAnswer { public List<RoomInfo> rooms = new List<RoomInfo>(); public int servers, rooms_free; }
    [Serializable] class JoinAnswer { public RoomInfo room; public string udp_host, join_ticket; public int udp_port, protocol; }
    [Serializable] class InvitesAnswer { public List<InviteInfo> invites = new List<InviteInfo>(); }
    [Serializable] class PlayersAnswer { public List<PlayerInfo> players = new List<PlayerInfo>(); }
    [Serializable] class JoinBody { public string code = "", team = "t"; }
    [Serializable] class FriendBody { public string nick = "", user_id = ""; }
    [Serializable] class InviteBody { public string user_id; }
    [Serializable] class PasswordBody { public string password; }
    [Serializable] class PasswordChange { public string old_password, new_password; }
    [Serializable] class ProfileBody { public OnlineProfile profile; }
    [Serializable] class LinkBody { public bool linked_local = true; public OnlineProfile profile; }
    [Serializable] class HideBody { public bool hide_status; }
    [Serializable] class TokensAnswer { public int tokens, today, day_limit, earned; public List<string> owned = new List<string>(); public List<ShopItem> shop = new List<ShopItem>(); }
    [Serializable] class MatchBody { public int kills, rounds; public bool won; public float seconds; }
    [Serializable] class BuyBody { public string weapon, skin; }
    [Serializable] class AdminTokensBody { public string nick; public int amount; }
    [Serializable] class AdminSkinBody { public string nick, weapon, skin; public bool owned; }
    [Serializable] class AdminLogAnswer { public List<AdminLogEntry> entries = new List<AdminLogEntry>(); }

    public Service State { get; private set; } = Service.Checking;
    public string ApiUrl { get; private set; }
    public OnlineUser User { get; private set; }
    public bool LoggedIn => User != null;
    /// <summary>Logging back in with the saved session at the start (the log-in screen waits for it).</summary>
    public bool Restoring { get; private set; }
    public HealthInfo Health { get; private set; }
    public List<FriendInfo> Friends { get; private set; } = new List<FriendInfo>();
    public List<RequestInfo> Incoming { get; private set; } = new List<RequestInfo>();
    public List<RequestInfo> Outgoing { get; private set; } = new List<RequestInfo>();
    public List<InviteInfo> Invites { get; private set; } = new List<InviteInfo>();
    public List<RoomInfo> Rooms { get; private set; } = new List<RoomInfo>();
    public int ServersOnline { get; private set; }
    public int RoomsFree { get; private set; }
    public int RequestCount { get; private set; }
    public int InviteCount { get; private set; }
    public bool RoomsLoaded { get; private set; }
    /// <summary>The room this player is in, or null.</summary>
    public RoomInfo Room { get; private set; }
    /// <summary>A request the player made is under way (buttons wait).</summary>
    public bool Busy => busy > 0;
    /// <summary>Offer, once per account, to bring this computer's skins and settings to it.</summary>
    public bool AskLink { get; private set; }
    public string LinkSource { get; private set; }   // the local account whose skins would come along (null: the guest's)
    public string Problem { get; private set; }       // why the online server cannot be used right now
    /// <summary>The online screens set these while they are shown, so the lists they show are kept fresh.</summary>
    public bool WantRooms, WantFriends;

    GameManager gm;
    string token = "";
    int busy, failures, rejoins;
    float nextHealth, nextPresence, nextRooms, nextRoom, nextFriends, nextSync, nextRejoin;
    readonly HashSet<string> asking = new HashSet<string>();
    string syncedProfile;
    bool leaving;
    bool testApi;   // -ds-api: a test run, whose login is not saved (it would replace the player's own)

    // ----------------------------------------------------------------- Halloween Tokens

    /// <summary>The online account's Halloween Tokens (-1 until known; see online-server/tokens.py).</summary>
    public int Tokens { get; private set; } = -1;
    /// <summary>Tokens earned today from the player's own matches, and the most a day gives.</summary>
    public int TokensToday { get; private set; }
    public int TokenDayLimit { get; private set; } = 40;
    /// <summary>What the last match paid, and when (shown for a while after it).</summary>
    public int LastEarned { get; private set; }
    public float LastEarnedAt { get; private set; } = -100f;
    readonly HashSet<string> ownedSkins = new HashSet<string>();
    readonly Dictionary<string, int> skinPrices = new Dictionary<string, int>();
    int tokensBeforeRoomMatch = -1;
    float roomMatchTokensAt = -1f;
    bool wasInRoomMatch;

    /// <summary>Whether the account has bought this skin with Halloween Tokens.</summary>
    public bool Owns(string weapon, string skin) => ownedSkins.Contains(weapon + "/" + skin);

    /// <summary>A skin's price in Halloween Tokens (the server's, else <paramref name="fallback"/>, skin.json's).</summary>
    public int PriceOf(string weapon, string skin, int fallback) => skinPrices.TryGetValue(weapon + "/" + skin, out int price) ? price : fallback;

    void UseTokens(string text)
    {
        var answer = JsonUtility.FromJson<TokensAnswer>(text);
        Tokens = answer.tokens;
        TokensToday = answer.today;
        TokenDayLimit = answer.day_limit;
        ownedSkins.Clear();
        foreach (var owned in answer.owned) ownedSkins.Add(owned);
        skinPrices.Clear();
        foreach (var item in answer.shop) skinPrices[item.weapon + "/" + item.skin] = item.price;
    }

    void Earned(int amount)
    {
        if (amount <= 0) return;
        LastEarned = amount;
        LastEarnedAt = Time.unscaledTime;
        Debug.Log($"[Online] +{amount} Halloween Tokens (now {Tokens})");
    }

    public void RefreshTokens()
    {
        if (LoggedIn) Ask("tokens", "GET", "/tokens", null, UseTokens);
    }

    /// <summary>A match this game ran (against bots, or a LAN game) is over: the API pays for it (capped a day).</summary>
    public void ReportMatch(int kills, bool won, int rounds, float seconds)
    {
        if (!LoggedIn) return;
        Debug.Log($"[Online] Match over ({kills} kills, {(won ? "won" : "lost")}): asking for Halloween Tokens");
        Ask(null, "POST", "/tokens/match", new MatchBody { kills = kills, won = won, rounds = rounds, seconds = seconds }, text =>
        {
            UseTokens(text);
            Earned(JsonUtility.FromJson<TokensAnswer>(text).earned);
        });
    }

    /// <summary>Buys a skin with Halloween Tokens; <paramref name="done"/> gets what went wrong, or null.</summary>
    public void BuySkin(string weapon, string skin, Action<string> done)
    {
        busy++;
        Ask(null, "POST", "/tokens/buy", new BuyBody { weapon = weapon, skin = skin }, text =>
        {
            busy--;
            UseTokens(text);
            done?.Invoke(null);
        }, (code, message) =>
        {
            busy--;
            done?.Invoke(message);
        });
    }

    // An online room's match is paid by the API when its game host reports the end: a few seconds after the match,
    // the tokens are asked for again and the difference shown.
    void WatchRoomMatchTokens(float now)
    {
        bool inMatch = Room != null && gm.Net.OnlineClient && gm.State != MatchState.Menu;
        if (inMatch && !wasInRoomMatch) tokensBeforeRoomMatch = Tokens;
        if (gm.State == MatchState.MatchOver && wasInRoomMatch && roomMatchTokensAt < 0f) roomMatchTokensAt = now + 4f;
        wasInRoomMatch = inMatch && gm.State != MatchState.MatchOver;
        if (roomMatchTokensAt < 0f || now < roomMatchTokensAt) return;
        roomMatchTokensAt = -1f;
        int before = tokensBeforeRoomMatch;
        Ask("tokens", "GET", "/tokens", null, text =>
        {
            UseTokens(text);
            if (before >= 0) Earned(Tokens - before);
        });
    }

    // ----------------------------------------------------------------- the admin panel (online-server/admin.py)

    /// <summary>The server lists this account as an admin (it checks every admin request itself).</summary>
    public bool IsAdmin => LoggedIn && User.admin;
    /// <summary>What admins did lately, newest first.</summary>
    public List<AdminLogEntry> AdminLog { get; private set; } = new List<AdminLogEntry>();

    /// <summary>A player's account; <paramref name="done"/> gets it, or null and what went wrong.</summary>
    public void AdminFind(string nick, Action<AdminPlayer, string> done) =>
        AdminAsk("GET", "/admin/player?nick=" + UnityWebRequest.EscapeURL(nick.Trim()), null, done);

    /// <summary>Gives a player Halloween Tokens (a negative amount takes them).</summary>
    public void AdminTokens(string nick, int amount, Action<AdminPlayer, string> done) =>
        AdminAsk("POST", "/admin/tokens", new AdminTokensBody { nick = nick, amount = amount }, done);

    /// <summary>Gives a player a skin of the shop, or takes it back.</summary>
    public void AdminSkin(string nick, string weapon, string skin, bool owned, Action<AdminPlayer, string> done) =>
        AdminAsk("POST", "/admin/skins", new AdminSkinBody { nick = nick, weapon = weapon, skin = skin, owned = owned }, done);

    public void RefreshAdminLog()
    {
        if (IsAdmin) Ask("admin-log", "GET", "/admin/log", null, text => AdminLog = JsonUtility.FromJson<AdminLogAnswer>(text).entries);
    }

    void AdminAsk(string method, string path, object body, Action<AdminPlayer, string> done)
    {
        busy++;
        Ask(null, method, path, body, text =>
        {
            busy--;
            var player = JsonUtility.FromJson<AdminPlayer>(text);
            if (method == "POST")
            {
                Debug.Log($"[Admin] {player.nick} now has {player.tokens} Halloween Tokens and {string.Join(", ", player.owned)}");
                RefreshAdminLog();
                if (User != null && string.Equals(player.nick, User.nick, StringComparison.OrdinalIgnoreCase)) RefreshTokens();   // their own
            }
            done?.Invoke(player, null);
        }, (code, message) =>
        {
            busy--;
            done?.Invoke(null, message);
        });
    }

    /// <summary>A newer Low Strike than this one on the website (its online.json says which), or null.</summary>
    public string NewVersion { get; private set; }
    /// <summary>Where to get it: the website's download section.</summary>
    public string DownloadUrl { get; private set; } = "https://lowstrike.lokvetia.com/#download";

    // -ds-online <nickname> <password> <create|join> [t|swat|spectator] and -ds-online-start <players>: tests.
    string testNick, testPassword, testAction, testTeam = "t";
    string testScreen;   // -ds-online-screen <name> (GameUI opens it); "link" keeps the link question up for a screenshot
    int testStartAt;
    bool testLoggedIn, testJoined, testStarted;
    float nextTestStep;

    void Start()
    {
        gm = GetComponent<GameManager>();
        WeaponSkins.Bought = skin => LoggedIn && Owns(skin.WeaponId, skin.Id);
        var args = Environment.GetCommandLineArgs();
        testApi = Array.IndexOf(args, "-ds-api") >= 0;
        token = testApi ? "" : PlayerPrefs.GetString(TokenKey, "");
        Restoring = token.Length > 0;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-ds-api" && i + 1 < args.Length) UseApi(args[i + 1], remember: false);
            if (args[i] == "-ds-online" && i + 3 < args.Length)
            {
                testNick = args[i + 1];
                testPassword = args[i + 2];
                testAction = args[i + 3];
                if (i + 4 < args.Length && !args[i + 4].StartsWith("-")) testTeam = args[i + 4];
            }
            if (args[i] == "-ds-online-start" && i + 1 < args.Length) int.TryParse(args[i + 1], out testStartAt);
            if (args[i] == "-ds-online-screen" && i + 1 < args.Length) testScreen = args[i + 1];
            // -ds-fake-latest <version>: as if the website had that version (tests the update question)
            if (args[i] == "-ds-fake-latest" && i + 1 < args.Length) NoteLatest(args[i + 1], null);
        }
        if (ApiUrl == null)
        {
            string remembered = PlayerPrefs.GetString(ApiKey, "");
            if (remembered.Length > 0) UseApi(remembered, remember: false);
            StartCoroutine(LoadConfig());
        }
    }

    // ----------------------------------------------------------------- finding the API

    IEnumerator LoadConfig()
    {
        using (var request = UnityWebRequest.Get(ConfigUrl))
        {
            request.timeout = 10;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success && request.responseCode != 404)
            {
                if (ApiUrl == null)
                {
                    State = Service.Unavailable;
                    Problem = "Can't reach the website to find the online server. Check your internet connection.";
                    Restoring = false;
                }
                yield break;
            }
            Config config = null;
            try
            {
                config = JsonUtility.FromJson<Config>(request.downloadHandler.text);
            }
            catch (Exception)
            {
            }
            if (config != null && NewVersion == null) NoteLatest(config.latest_version, config.download);
            if (config == null || string.IsNullOrEmpty(config.api))
            {
                // The owner has not set up the online server (yet), or switched it off.
                PlayerPrefs.DeleteKey(ApiKey);
                ApiUrl = null;
                State = Service.NotSetUp;
                Problem = "Online play is not open yet. Single player and LAN games work.";
                Restoring = false;
                yield break;
            }
            UseApi(config.api, remember: true);
        }
    }

    void NoteLatest(string latest, string download)
    {
        if (!string.IsNullOrEmpty(download) && download.StartsWith("https://")) DownloadUrl = download;
        if (!IsNewer(latest, Application.version)) return;
        NewVersion = latest.Trim();
        Debug.Log($"[Online] Low Strike {NewVersion} is out (this is {Application.version})");
    }

    /// <summary>"1.20" is newer than "1.19" (numbers between the dots, not text).</summary>
    public static bool IsNewer(string version, string than)
    {
        if (string.IsNullOrEmpty(version)) return false;
        int[] Parts(string v) => Array.ConvertAll((v ?? "").Trim().Split('.'), p => int.TryParse(p, out int n) ? n : 0);
        int[] a = Parts(version), b = Parts(than);
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            int x = i < a.Length ? a[i] : 0, y = i < b.Length ? b[i] : 0;
            if (x != y) return x > y;
        }
        return false;
    }

    void UseApi(string url, bool remember)
    {
        url = (url ?? "").Trim().TrimEnd('/');
        // HTTPS only (plain HTTP just on this computer, for tests).
        if (!url.StartsWith("https://") && !url.StartsWith("http://127.0.0.1") && !url.StartsWith("http://localhost"))
        {
            Debug.LogWarning("[Online] Not an https address, not used: " + url);
            return;
        }
        if (remember) PlayerPrefs.SetString(ApiKey, url);
        if (url == ApiUrl) return;
        ApiUrl = url;
        nextHealth = 0f;
        Debug.Log("[Online] API: " + url);
        if (token.Length > 0) RestoreLogin();
    }

    // ----------------------------------------------------------------- keeping up to date

    void Update()
    {
        if (ApiUrl == null) return;
        float now = Time.unscaledTime;
        if (now >= nextHealth && (!LoggedIn || State != Service.Online))
        {
            nextHealth = now + (State == Service.Online ? 15f : RetryDelay());
            Ask("health", "GET", "/health", null, text =>
            {
                Health = JsonUtility.FromJson<HealthInfo>(text);
                if (token.Length > 0 && !LoggedIn && !Restoring) RestoreLogin();
            });
        }
        RunTest(now);
        if (!LoggedIn) return;

        if (now >= nextPresence)
        {
            nextPresence = now + (State == Service.Online ? 4f : RetryDelay());
            // In a match (playing, not watching), the time counts as played for the account.
            bool playing = gm.State != MatchState.Menu && gm.State != MatchState.MatchOver && gm.Side != PlayerSide.Spectate;
            Ask("presence", "GET", playing ? "/presence?playing=1" : "/presence", null, text =>
            {
                var answer = JsonUtility.FromJson<PresenceAnswer>(text);
                Friends = answer.friends;
                RequestCount = answer.requests;
                InviteCount = answer.invites;
                if (InviteCount > 0 && Invites.Count != InviteCount) nextFriends = 0f;
            });
        }
        AskPlayed(now);
        WatchRoomMatchTokens(now);
        if ((WantRooms || testAction == "join") && Room == null && now >= nextRooms)
        {
            nextRooms = now + 4f;
            Ask("rooms", "GET", "/rooms", null, text =>
            {
                var answer = JsonUtility.FromJson<RoomsAnswer>(text);
                Rooms = answer.rooms;
                ServersOnline = answer.servers;
                RoomsFree = answer.rooms_free;
                RoomsLoaded = true;
            });
        }
        if (Room != null && now >= nextRoom)
        {
            nextRoom = now + 3f;
            string id = Room.id;
            Ask("room", "GET", "/rooms/" + id, null, text =>
            {
                var room = JsonUtility.FromJson<RoomInfo>(text);
                if (Room == null || Room.id != id) return;
                if (!room.is_member) RoomGone("You are no longer in this room.");
                else Room = room;
            }, (code, message) =>
            {
                if (code == 404 && Room != null && Room.id == id) RoomGone("The room was closed.");
            });
        }
        if ((WantFriends || InviteCount != Invites.Count) && now >= nextFriends)
        {
            nextFriends = now + 4f;
            Ask("friends", "GET", "/friends", null, text =>
            {
                var answer = JsonUtility.FromJson<FriendsAnswer>(text);
                Friends = answer.friends;
                Incoming = answer.incoming;
                Outgoing = answer.outgoing;
                RequestCount = Incoming.Count;
            });
            Ask("invites", "GET", "/invites", null, text =>
            {
                Invites = JsonUtility.FromJson<InvitesAnswer>(text).invites;
                InviteCount = Invites.Count;
            });
        }
        if (now >= nextSync)
        {
            nextSync = now + 2f;
            SyncProfile();
        }
        WatchConnection(now);
    }

    float RetryDelay() => Mathf.Min(30f, 3f * Mathf.Pow(2f, Mathf.Min(failures, 4)));

    // In a room but not on its game server: after a dropped connection, join again by itself (three tries); the game
    // server keeps the player's place for a minute.
    void WatchConnection(float now)
    {
        if (Room == null || leaving) return;
        var net = gm.Net;
        if (net.Role != NetSession.Mode.Off)
        {
            if (net.Connected) rejoins = 0;
            return;
        }
        if (net.LastEndWasDrop && rejoins < 3 && now >= nextRejoin && !asking.Contains("join"))
        {
            rejoins++;
            nextRejoin = now + 5f;
            Debug.Log($"[Online] Joining the game server again (try {rejoins} of 3)");
            Rejoin(null);
        }
    }

    void RoomGone(string why)
    {
        Debug.Log("[Online] " + why);
        Room = null;
        if (gm.Net.OnlineClient) gm.ReturnToMenu();
        gm.MenuNotice = why;
    }

    // ----------------------------------------------------------------- account

    void RestoreLogin()
    {
        Restoring = true;
        Ask("me", "GET", "/me", null, text =>
        {
            Restoring = false;
            BeginLogin(JsonUtility.FromJson<OnlineUser>(text), null);
        }, (code, message) => Restoring = false);
    }

    /// <summary>Logs in, or signs up (then logs in). <paramref name="done"/> gets what went wrong, or null.</summary>
    public void LogIn(string nick, string password, bool signUp, Action<string> done)
    {
        string before = Accounts.LoggedIn && !Accounts.IsOnline ? Accounts.Current : null;
        busy++;
        Ask(null, "POST", signUp ? "/auth/register" : "/auth/login", new Credentials { nick = (nick ?? "").Trim(), password = password }, text =>
        {
            busy--;
            var answer = JsonUtility.FromJson<AuthAnswer>(text);
            token = answer.token;
            if (!testApi)
            {
                PlayerPrefs.SetString(TokenKey, token);
                PlayerPrefs.Save();
            }
            BeginLogin(answer.user, before);
            done?.Invoke(null);
        }, (code, message) =>
        {
            busy--;
            done?.Invoke(message);
        }, withToken: false);
    }

    void BeginLogin(OnlineUser user, string localBefore)
    {
        User = user;
        var skins = new List<(string, string)>();
        foreach (var pair in user.profile.skins) skins.Add((pair.weapon, pair.skin));
        Accounts.BeginOnline(user.id, user.nick, skins, user.hide_status);
        if (user.profile.has_settings) ApplySettings(user.profile);
        AskLink = !user.linked_local;
        LinkSource = localBefore ?? Accounts.LastLocalName;
        syncedProfile = JsonUtility.ToJson(CurrentProfile());
        nextPresence = nextRooms = nextFriends = 0f;
        RefreshTokens();
        Debug.Log($"[Online] Logged in as {user.nick}");
    }

    /// <summary>Yes: the skins of <see cref="LinkSource"/> (or the guest's) and this computer's crosshair and voice
    /// settings go to the online account. Either way the question is not asked again for it.</summary>
    public void AnswerLink(bool bring)
    {
        if (!LoggedIn) return;
        AskLink = false;
        if (bring) Accounts.ReplaceSkins(Accounts.LocalSkins(LinkSource));
        var body = new LinkBody { profile = bring ? CurrentProfile() : User.profile };
        Ask(null, "PATCH", "/me", body, text =>
        {
            User = JsonUtility.FromJson<OnlineUser>(text);
            syncedProfile = JsonUtility.ToJson(CurrentProfile());
        });
    }

    public void LogOut()
    {
        if (token.Length > 0) Ask(null, "POST", "/auth/logout", null, null);   // ends the session on the server too
        if (Room != null) LeaveRoom();
        Forget(null);
    }

    void Forget(string why)
    {
        token = "";
        if (!testApi)
        {
            PlayerPrefs.DeleteKey(TokenKey);
            PlayerPrefs.Save();
        }
        bool wasIn = User != null;
        User = null;
        Room = null;
        Friends.Clear();
        Incoming.Clear();
        Outgoing.Clear();
        Invites.Clear();
        Rooms.Clear();
        RequestCount = InviteCount = 0;
        Tokens = -1;
        ownedSkins.Clear();
        AskLink = false;
        if (Accounts.IsOnline) Accounts.LogOut();
        if (wasIn) Debug.Log("[Online] Logged out" + (why != null ? ": " + why : ""));
        if (why != null) gm.MenuNotice = why;
    }

    public void ChangePassword(string oldPassword, string newPassword, Action<string> done)
    {
        busy++;
        Ask(null, "POST", "/me/password", new PasswordChange { old_password = oldPassword, new_password = newPassword },
            text => { busy--; done(null); }, (code, message) => { busy--; done(message); });
    }

    /// <summary>Deletes the online account and everything the server keeps about it (after the password).</summary>
    public void DeleteAccount(string password, Action<string> done)
    {
        busy++;
        Ask(null, "DELETE", "/me", new PasswordBody { password = password }, text =>
        {
            busy--;
            string id = User?.id;
            Room = null;
            if (gm.Net.OnlineClient) gm.ReturnToMenu();
            Forget(null);
            Accounts.RemoveOnlineCopy(id);
            done(null);
        }, (code, message) =>
        {
            busy--;
            done(message);
        });
    }

    // The skins and the crosshair and voice settings, as the server keeps them.
    static OnlineProfile CurrentProfile()
    {
        var profile = new OnlineProfile
        {
            has_settings = true,
            crosshair_color = GameSettings.CrosshairColorIndex,
            crosshair_r = GameSettings.CrosshairCustom.r,
            crosshair_g = GameSettings.CrosshairCustom.g,
            crosshair_b = GameSettings.CrosshairCustom.b,
            crosshair_length = GameSettings.CrosshairLength,
            crosshair_thickness = GameSettings.CrosshairThickness,
            crosshair_gap = GameSettings.CrosshairGap,
            crosshair_alpha = GameSettings.CrosshairAlpha,
            crosshair_outline = GameSettings.CrosshairOutline,
            crosshair_dot = GameSettings.CrosshairDot,
            crosshair_t = GameSettings.CrosshairT,
            crosshair_dynamic = GameSettings.CrosshairDynamic,
            voice_mode = (int)GameSettings.Voice,
            voice_chat = GameSettings.VoiceChat,
        };
        foreach (var (weapon, skin) in Accounts.Skins()) profile.skins.Add(new SkinPair { weapon = weapon, skin = skin });
        return profile;
    }

    static void ApplySettings(OnlineProfile p)
    {
        GameSettings.SetCrosshair(p.crosshair_color, new Color(p.crosshair_r, p.crosshair_g, p.crosshair_b), p.crosshair_length,
                                  p.crosshair_thickness, p.crosshair_gap, p.crosshair_alpha, p.crosshair_outline, p.crosshair_dot,
                                  p.crosshair_t, p.crosshair_dynamic);
        GameSettings.SetVoiceMode((VoiceMode)Mathf.Clamp(p.voice_mode, 0, 1));
        GameSettings.SetVoiceChat(p.voice_chat);
    }

    // Saves changed skins, settings and the online-status choice on the server.
    void SyncProfile()
    {
        if (!LoggedIn || AskLink || !Accounts.IsOnline) return;
        var profile = CurrentProfile();
        string json = JsonUtility.ToJson(profile);
        if (json != syncedProfile && !asking.Contains("profile"))
            Ask("profile", "PATCH", "/me", new ProfileBody { profile = profile }, text => syncedProfile = json);
        if (Accounts.HideStatus != User.hide_status && !asking.Contains("hide"))
        {
            bool hide = Accounts.HideStatus;
            Ask("hide", "PATCH", "/me", new HideBody { hide_status = hide }, text => User.hide_status = hide);
        }
    }

    // ----------------------------------------------------------------- friends

    public void Search(string text, Action<List<PlayerInfo>> done) =>
        Ask("search", "GET", "/players?search=" + UnityWebRequest.EscapeURL(text ?? ""), null,
            answer => done(JsonUtility.FromJson<PlayersAnswer>(answer).players), (code, message) => done(null));

    public void SendFriendRequest(string nick, string userId, Action<string> done)
    {
        busy++;
        Ask(null, "POST", "/friends/requests", new FriendBody { nick = nick ?? "", user_id = userId ?? "" }, text =>
        {
            busy--;
            nextFriends = nextPresence = 0f;
            done(null);
        }, (code, message) =>
        {
            busy--;
            done(message);
        });
    }

    public void AnswerFriendRequest(RequestInfo request, bool accept)
    {
        Incoming.Remove(request);
        Outgoing.Remove(request);
        Ask(null, "POST", $"/friends/requests/{request.id}/{(accept ? "accept" : "decline")}", null, text => nextFriends = nextPresence = 0f);
    }

    public void RemoveFriend(string id)
    {
        Friends.RemoveAll(f => f.id == id);
        Ask(null, "DELETE", "/friends/" + id, null, text => nextFriends = 0f);
    }

    // ----------------------------------------------------------------- rooms

    public void CreateRoom(RoomSettingsBody settings, Action<string> done)
    {
        busy++;
        Ask("join", "POST", "/rooms", settings, text => { busy--; JoinWith(text); done?.Invoke(null); },
            (code, message) => { busy--; done?.Invoke(message); });
    }

    public void JoinRoom(string roomId, string team, Action<string> done)
    {
        busy++;
        Ask("join", "POST", $"/rooms/{roomId}/join", new JoinBody { team = team ?? "t" }, text => { busy--; JoinWith(text); done?.Invoke(null); },
            (code, message) => { busy--; done?.Invoke(message); });
    }

    public void JoinByCode(string code, string team, Action<string> done)
    {
        code = (code ?? "").Trim().ToUpperInvariant();
        if (code.Length == 0)
        {
            done?.Invoke("Type the room's code.");
            return;
        }
        busy++;
        Ask("join", "POST", $"/rooms/code/{UnityWebRequest.EscapeURL(code)}/join", new JoinBody { team = team ?? "t" },
            text => { busy--; JoinWith(text); done?.Invoke(null); }, (code2, message) => { busy--; done?.Invoke(message); });
    }

    /// <summary>The first open public room with a free place, or a message when there is none.</summary>
    public void QuickJoin(string team, Action<string> done)
    {
        var room = Rooms.Find(r => !r.@private && r.server_online && r.status == "waiting" && r.players < r.max_players)
                   ?? Rooms.Find(r => !r.@private && r.server_online && r.players < r.max_players);
        if (room == null)
        {
            done?.Invoke(RoomsFree > 0 ? "No open room has a free place: create one!" : "All rooms are busy. Try again in a while.");
            return;
        }
        JoinRoom(room.id, team, done);
    }

    /// <summary>Joins the room's game server again (a new ticket), after the connection was lost or it sent us away.</summary>
    public void Rejoin(Action<string> done)
    {
        if (Room == null) return;
        string team = gm.Net.PreferSpectator ? "spectator" : gm.Net.PreferredTeam == Team.Swat ? "swat" : "t";
        JoinRoom(Room.id, team, message =>
        {
            if (message != null) gm.MenuNotice = message;
            done?.Invoke(message);
        });
    }

    void JoinWith(string text)
    {
        var info = JsonUtility.FromJson<JoinAnswer>(text);
        Room = info.room;
        leaving = false;
        var me = Room.members.Find(m => m.user_id == User.id);
        string team = me != null ? me.team : "t";
        var net = gm.Net;
        net.PreferSpectator = team == "spectator";
        net.PreferredTeam = team == "swat" ? Team.Swat : Team.Terrorists;
        net.PlayerName = User.nick;
        gm.MenuNotice = null;
        nextRoom = Time.unscaledTime + 3f;
        Debug.Log($"[Online] In room '{Room.name}' ({(Room.is_owner ? "owner" : "member")}, {team}); joining its game server");
        if (gm.State != MatchState.Menu && !gm.IsClient) gm.ReturnToMenu();
        net.Join(info.udp_host, info.udp_port, info.join_ticket);
    }

    /// <summary>Terrorists, SWAT or watching, in the room's lobby (the game server takes it if that team has room).</summary>
    public void ChooseTeam(string team)
    {
        gm.Net.PreferSpectator = team == "spectator";
        if (team != "spectator") gm.Net.PreferredTeam = team == "swat" ? Team.Swat : Team.Terrorists;
    }

    public void LeaveRoom()
    {
        if (Room == null) return;
        leaving = true;
        Debug.Log($"[Online] Left room '{Room.name}'");
        Ask(null, "POST", $"/rooms/{Room.id}/leave", null, text => nextRooms = 0f);
        Room = null;
        if (gm.Net.Role != NetSession.Mode.Off || gm.State != MatchState.Menu) gm.ReturnToMenu();
        nextRooms = 0f;
    }

    public void StartRoomMatch(Action<string> done)
    {
        if (Room == null) return;
        busy++;
        Ask(null, "POST", $"/rooms/{Room.id}/start", null, text => { busy--; done?.Invoke(null); }, (code, message) => { busy--; done?.Invoke(message); });
    }

    /// <summary>Owner: changes the room's settings (between matches).</summary>
    public void ChangeRoom(RoomSettingsBody settings, Action<string> done)
    {
        if (Room == null) return;
        string id = Room.id;
        Ask(null, "PATCH", "/rooms/" + id, settings, text =>
        {
            if (Room != null && Room.id == id) Room = JsonUtility.FromJson<RoomInfo>(text);
            done?.Invoke(null);
        }, (code, message) => done?.Invoke(message));
    }

    public RoomSettingsBody SettingsOf(RoomInfo room) => new RoomSettingsBody
    {
        name = room.name, map = room.map, difficulty = room.difficulty, team_size = room.team_size, terrorists = room.Terrorists,
        swat = room.Swat, rounds = room.rounds,
        friendly_fire = room.friendly_fire, @private = room.@private, closed_site = string.IsNullOrEmpty(room.closed_site) ? "B" : room.closed_site,
    };

    public void InviteFriend(string userId, Action<string> done)
    {
        if (Room == null) return;
        Ask(null, "POST", $"/rooms/{Room.id}/invite", new InviteBody { user_id = userId }, text => done?.Invoke(null), (code, message) => done?.Invoke(message));
    }

    public void AnswerInvite(InviteInfo invite, bool accept, Action<string> done)
    {
        Invites.Remove(invite);
        InviteCount = Invites.Count;
        if (!accept)
        {
            Ask(null, "POST", $"/invites/{invite.id}/decline", null, null);
            return;
        }
        busy++;
        Ask("join", "POST", $"/invites/{invite.id}/accept", new JoinBody(), text => { busy--; JoinWith(text); done?.Invoke(null); },
            (code, message) => { busy--; done?.Invoke(message); });
    }

    // How long players have played (seconds), by nickname: for the names in a match (GameUI asks for them).
    readonly Dictionary<string, int> played = new Dictionary<string, int>();
    readonly HashSet<string> playedWanted = new HashSet<string>();
    float nextPlayed;

    /// <summary>Seconds played by an online account with this nickname, or -1 when not known (yet).</summary>
    public int PlayedSeconds(string nick)
    {
        if (string.IsNullOrEmpty(nick)) return -1;
        playedWanted.Add(nick);
        return played.TryGetValue(nick, out int seconds) ? seconds : -1;
    }

    void AskPlayed(float now)
    {
        if (playedWanted.Count == 0 || now < nextPlayed) return;
        nextPlayed = now + 30f;
        var nicks = new List<string>(playedWanted);
        playedWanted.Clear();
        if (nicks.Count > 20) nicks.RemoveRange(20, nicks.Count - 20);
        Ask("played", "GET", "/players/played?nicks=" + UnityWebRequest.EscapeURL(string.Join(",", nicks)), null, text =>
        {
            foreach (var p in JsonUtility.FromJson<PlayedAnswer>(text).players) played[p.nick] = p.played;
        });
    }

    /// <summary>What people did in a match this game ran, for every game's bots (BotKnowledge). Needs the login.</summary>
    public void SendKnowledge(string json)
    {
        if (ApiUrl == null || !LoggedIn || !asking.Add("knowledge")) return;
        StartCoroutine(Send("knowledge", "POST", "/bot-knowledge", json, token,
                            _ => Debug.Log("[Bots] Sent what people did this match to the online server"),
                            (code, message) => Debug.LogWarning($"[Bots] Could not send what people did: {message}")));
    }

    // ----------------------------------------------------------------- talking to the API

    // One request. A <paramref name="key"/> keeps the same kind from running twice at once. On failure the message is
    // the API's own ("All rooms are busy...") or says the server cannot be reached.
    void Ask(string key, string method, string path, object body, Action<string> ok, Action<long, string> fail = null, bool withToken = true)
    {
        if (ApiUrl == null)
        {
            fail?.Invoke(0, Problem ?? "Online play is not set up yet.");
            return;
        }
        if (key != null && !asking.Add(key)) return;
        StartCoroutine(Send(key, method, path, body != null ? JsonUtility.ToJson(body) : null, withToken ? token : null, ok, fail));
    }

    IEnumerator Send(string key, string method, string path, string json, string bearer, Action<string> ok, Action<long, string> fail)
    {
        using (var request = new UnityWebRequest(ApiUrl + path, method))
        {
            if (json != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                request.SetRequestHeader("Content-Type", "application/json");
            }
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("X-Game-Version", Application.version);
            if (!string.IsNullOrEmpty(bearer)) request.SetRequestHeader("Authorization", "Bearer " + bearer);
            request.timeout = 12;
            yield return request.SendWebRequest();
            if (key != null) asking.Remove(key);

            long code = request.responseCode;
            string text = request.downloadHandler != null ? request.downloadHandler.text : "";
            if (request.result == UnityWebRequest.Result.Success)
            {
                Reachable();
                try
                {
                    ok?.Invoke(text);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Online] Could not read the answer to {path}: {e.Message}");
                }
                yield break;
            }

            string detail = null;
            if (text.StartsWith("{"))
            {
                try
                {
                    detail = JsonUtility.FromJson<Error>(text)?.detail;
                }
                catch (Exception)
                {
                }
            }
            if (detail == null)
            {
                // No answer from the API itself: the internet is down here, or the server PC (or its tunnel) is off.
                failures++;
                if (State != Service.Unavailable) Debug.Log($"[Online] The online server is not available ({request.error})");
                State = Service.Unavailable;
                Problem = code == 0 ? "Can't reach the online server. Check your internet connection."
                                    : "The online server is not available right now. Try again later.";
                fail?.Invoke(code, Problem);
                yield break;
            }
            Reachable();
            if (code == 401 && !string.IsNullOrEmpty(bearer) && bearer == token)
            {
                Forget(detail);
                Restoring = false;
            }
            fail?.Invoke(code, detail);
        }
    }

    void Reachable()
    {
        failures = 0;
        if (State != Service.Online) Debug.Log("[Online] Online server available");
        State = Service.Online;
        Problem = null;
    }

    void OnApplicationQuit()
    {
        // Out of the room at once (else the server notices within a minute).
        if (Room != null && token.Length > 0)
        {
            var request = new UnityWebRequest($"{ApiUrl}/rooms/{Room.id}/leave", "POST");
            request.SetRequestHeader("Authorization", "Bearer " + token);
            request.timeout = 2;
            request.SendWebRequest();
        }
    }

    // ----------------------------------------------------------------- -ds-online (tests)

    // Logs in (signing up the first time), then creates a room or joins the first one; with -ds-online-start <n>
    // the owner starts the match once n players are on the game server. Every step is logged.
    void RunTest(float now)
    {
        if (testNick == null || State != Service.Online || now < nextTestStep || Busy) return;
        nextTestStep = now + 1f;
        if (!testLoggedIn)
        {
            if (LoggedIn)
            {
                testLoggedIn = true;
                return;
            }
            testLoggedIn = true;
            LogIn(testNick, testPassword, false, problem =>
            {
                if (problem == null) return;
                Debug.Log($"[Online] Test: log in failed ({problem}); signing up");
                LogIn(testNick, testPassword, true, again =>
                {
                    if (again != null) Debug.LogWarning("[Online] Test: sign up failed: " + again);
                });
            });
            return;
        }
        if (!LoggedIn) return;
        if (AskLink && testScreen != "link") AnswerLink(false);
        if (!testJoined)
        {
            if (testAction == "create")
            {
                testJoined = true;
                CreateRoom(new RoomSettingsBody
                {
                    name = testNick + " room", map = gm.Map.Id, team_size = gm.TeamSize, terrorists = gm.TerroristCount, swat = gm.SwatCount,
                    difficulty = gm.Difficulty.ToString(), rounds = gm.RoundsToWin, friendly_fire = gm.FriendlyFire, team = testTeam,
                    closed_site = gm.ClosedSiteSetting,
                }, problem => Debug.Log(problem == null ? "[Online] Test: room created" : "[Online] Test: could not create a room: " + problem));
            }
            else if (testAction == "join" && Rooms.Count > 0)
            {
                testJoined = true;
                JoinRoom(Rooms[0].id, testTeam, problem => Debug.Log(problem == null ? $"[Online] Test: joined '{Rooms[0].name}'" : "[Online] Test: could not join: " + problem));
            }
            else if (testAction.StartsWith("code:"))
            {
                testJoined = true;
                JoinByCode(testAction.Substring(5), testTeam, problem => Debug.Log(problem == null ? "[Online] Test: joined by code" : "[Online] Test: could not join: " + problem));
            }
            return;
        }
        if (testStartAt > 0 && !testStarted && Room != null && Room.is_owner && Room.status == "waiting"
            && Room.members.FindAll(m => m.connected).Count >= testStartAt)
        {
            testStarted = true;
            Debug.Log($"[Online] Test: {testStartAt} players on the game server; starting the match");
            StartRoomMatch(problem => { if (problem != null) Debug.LogWarning("[Online] Test: could not start: " + problem); });
        }
    }
}

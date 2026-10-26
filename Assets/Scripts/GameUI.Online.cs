using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The online screens (see <see cref="OnlineClient"/>): PLAY ONLINE (the rooms, quick join, join with a code,
/// create a room), the room (players and teams, settings, inviting friends, START MATCH, leaving), the online
/// friends, the online account (password, deleting it) and the one-time question about bringing this computer's
/// skins and settings along.
/// </summary>
public partial class GameUI
{
    bool onlineOpen;              // PLAY ONLINE
    bool onlineCreating;          // its CREATE ROOM page
    bool onlineAccountOpen;       // ACCOUNT (online): email, password, deleting the account
    bool friendsLan;              // the friends screen shows the LAN friends instead of the online ones
    bool accountOnline;           // the log-in screen logs in online (not only on this computer)
    int onlineTeam;               // 0 Terrorists, 1 SWAT, 2 watching: for joining and creating rooms
    bool onlinePrivate;
    bool roomEditing;             // the room's owner is changing its settings
    string onlineRoomName = "", onlineCode = "", onlineSearch = "", onlineMessage;
    bool onlineMessageBad;
    List<PlayerInfo> searchResults;
    string removeOnline;          // the online friend whose REMOVE was clicked once
    string oldPassword = "", newPassword = "", newRepeat = "", deletePassword = "", accountPageMessage;
    bool accountPageBad, deleteConfirm;
    Vector2 roomsScroll, onlineFriendsScroll, inviteScroll;

    OnlineClient Online => gm.Online;
    const string BrowserNoLan = "LAN games need the downloaded game: a browser cannot reach other computers on the WiFi.";
    string testScreen;   // -ds-online-screen <login|forgot|rooms|create|friends|account|admin|...>: opens it once logged in (screenshots)

    void OpenTestScreen()
    {
        if (testScreen == null)
        {
            int index = Array.IndexOf(Environment.GetCommandLineArgs(), "-ds-online-screen");
            if (index < 0 || index + 1 >= Environment.GetCommandLineArgs().Length) return;
            testScreen = Environment.GetCommandLineArgs()[index + 1];
        }
        if (testScreen == "" || testScreen == "link") return;
        if (testScreen == "login") OpenAccount(null, online: true);
        else if (testScreen == "forgot")
        {
            OpenAccount(null, online: true);
            OpenForgotPassword(Accounts.LastName);
        }
        else if (testScreen == "tutorial") tutorialOffer = true;
        else if (!Online.LoggedIn) return;
        else if (testScreen == "rooms" || testScreen == "create")
        {
            OpenOnline();
            onlineCreating = testScreen == "create";
        }
        else if (testScreen == "friends") friendsOpen = true;
        else if (testScreen == "account") onlineAccountOpen = true;
        else if (testScreen == "admin" || testScreen == "admin-give" || testScreen == "admin-code")
        {
            // "admin-give": also gives the admin 50 tokens and the Halloween AK-47, the way its buttons do;
            // "admin-code": makes a recovery code for the admin's own account, as RECOVERY CODE does.
            if (!Online.IsAdmin) return;
            OpenAdmin();
            if (testScreen == "admin-give")
            {
                AdminChangeTokens(Online.User.nick, 50);
                AdminChangeSkin(Online.User.nick, new ShopItem { weapon = "ak47", skin = "Halloween" }, true);
            }
            if (testScreen == "admin-code")
            {
                string nick = Online.User.nick;
                Online.AdminRecoveryCode(nick, (code, hours, problem) =>
                    AdminSay(problem ?? $"Recovery code for {nick}: {code}  (once, {hours} hours). They press FORGOT PASSWORD? > I HAVE A CODE.",
                             problem != null));
            }
        }
        else if (testScreen == "inventory" || testScreen == "buy")
        {
            // The AK-47 page of the inventory; "buy": asking to buy its Halloween skin (once the tokens are known).
            if (Online.Tokens < 0) return;
            inventoryOpen = true;
            inventoryWeapon = WeaponData.Find("ak47");
            if (testScreen == "buy") buying = WeaponSkins.Find(inventoryWeapon, "Halloween");
        }
        testScreen = "";
    }
    static readonly Color InMatchColor = new Color(1f, 0.78f, 0.35f);
    static readonly Color OfflineColor = new Color(0.5f, 0.5f, 0.5f);

    string OnlineTeamKey => onlineTeam == 1 ? "swat" : onlineTeam == 2 ? "spectator" : "t";

    void OpenOnline()
    {
        onlineOpen = true;
        onlineCreating = false;
        onlineMessage = null;
        gm.MenuNotice = null;
        onlineTeam = gm.Side == PlayerSide.Swat ? 1 : 0;
        if (Online.LoggedIn && onlineRoomName.Length == 0) onlineRoomName = $"{Online.User.nick}'s room";
    }

    void ShowOnlineResult(string problem, string success = null)
    {
        onlineMessage = problem ?? success;
        onlineMessageBad = problem != null;
    }

    // "Online · 12 players online" / "Connecting..." / "Server unavailable", at the top of the online screens.
    void OnlineStatusLine(Rect rect)
    {
        var online = Online;
        string text;
        Color color;
        switch (online.State)
        {
            case OnlineClient.Service.Online:
                var health = online.Health;
                text = health != null ? $"Online  ·  {health.players_online} player{(health.players_online == 1 ? "" : "s")} online" : "Online";
                if (online.LoggedIn) text += $"  ·  you: {online.User.nick}";
                color = MoneyGreen;
                break;
            case OnlineClient.Service.Checking:
                text = "Connecting...";
                color = Dim;
                break;
            case OnlineClient.Service.NotSetUp:
                text = online.Problem;
                color = Dim;
                break;
            default:
                text = "Server unavailable  ·  " + online.Problem;
                color = Danger;
                break;
        }
        Label(rect, text, 19, color, TextAnchor.MiddleCenter);
    }

    // ----------------------------------------------------------------- PLAY ONLINE

    void DrawOnline()
    {
        var online = Online;
        online.WantRooms = true;
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
        {
            if (onlineCreating) onlineCreating = false;
            else onlineOpen = false;
            return;
        }
        Fill(new Rect(0f, 0f, width, RefHeight), new Color(0f, 0f, 0f, 0.35f));
        const float top = 50f, h = 980f;
        float w = Mathf.Min(1480f, width - 40f), x = (width - w) / 2f;
        Fill(new Rect(x, top, w, h), PanelColor);
        Label(new Rect(x, top + 18f, w, 60f), onlineCreating ? "CREATE A ROOM" : "PLAY ONLINE", 50, Gold, TextAnchor.MiddleCenter);
        OnlineStatusLine(new Rect(x, top + 78f, w, 28f));
        float y = top + 130f, bottom = top + h - 80f;

        if (!online.LoggedIn)
        {
            float bx = x + (w - 760f) / 2f;
            if (online.State == OnlineClient.Service.NotSetUp)
                WrapLabel(new Rect(bx, y + 20f, 760f, 80f), "Online play is not open yet. Single player and LAN games work as always.", 22, Color.white);
            else
            {
                WrapLabel(new Rect(bx, y + 20f, 760f, 110f),
                          "Play with people anywhere: log in with an online account (or make one). It works on every computer and " +
                          "phone, keeps your friends and your skins, and needs no IP addresses, VPN or router settings.", 21, Color.white);
                if (online.Restoring) Label(new Rect(bx, y + 150f, 760f, 40f), "Logging you in...", 22, Dim, TextAnchor.MiddleCenter);
                else if (Button(new Rect(bx, y + 150f, 760f, 64f), "LOG IN / SIGN UP ONLINE", true, true, 26)) OpenAccount(null, online: true);
            }
            if (Button(new Rect(x + w / 2f - 150f, bottom, 300f, 56f), "BACK")) onlineOpen = false;
            return;
        }

        if (onlineCreating)
        {
            DrawCreateRoom(x, y, w, bottom);
            return;
        }

        // Left: the rooms. Right: quick join, a code, creating a room.
        float left = x + 40f, listWidth = w * 0.6f, right = left + listWidth + 40f, rightWidth = x + w - 40f - right;
        Label(new Rect(left, y, listWidth, 32f), $"Rooms ({online.Rooms.Count})", 24, Color.white);
        if (online.RoomsLoaded)
            Label(new Rect(left, y, listWidth, 32f), online.ServersOnline == 0 ? "The game servers are offline"
                  : $"{online.RoomsFree} of {online.ServersOnline} free for a new room", 17, online.ServersOnline == 0 ? Danger : Dim, TextAnchor.MiddleRight);
        y += 42f;
        float listBottom = bottom - 20f;
        if (online.Rooms.Count == 0)
        {
            Label(new Rect(left, y, listWidth, 36f), !online.RoomsLoaded ? "Loading..." : "No rooms right now: create one!", 20, Dim);
        }
        else
        {
            const float row = 74f;
            var view = new Rect(left, y, listWidth, listBottom - y);
            var content = new Rect(0f, 0f, online.Rooms.Count * row > view.height ? listWidth - 20f : listWidth, online.Rooms.Count * row);
            roomsScroll = GUI.BeginScrollView(view, roomsScroll, content, false, false);
            for (int i = 0; i < online.Rooms.Count; i++)
            {
                var room = online.Rooms[i];
                float ry = i * row, rw = content.width;
                bool full = room.players >= room.max_players && room.spectators >= 4;
                Fill(new Rect(0f, ry, rw, row - 8f), new Color(1f, 1f, 1f, 0.07f));
                Label(new Rect(16f, ry + 4f, rw - 220f, 32f), room.name, 22, Color.white);
                string state = !room.server_online ? "server offline" : room.status == "playing" ? "Playing" : "Waiting";
                Label(new Rect(16f, ry + 34f, rw - 220f, 28f),
                      $"{MapName(room.map)}  ·  {room.players}/{room.max_players} players{(room.spectators > 0 ? $" + {room.spectators} watching" : "")}  ·  " +
                      $"{state}  ·  {room.difficulty} bots  ·  host {room.owner}", 16, room.status == "playing" ? InMatchColor : Dim);
                if (room.@private) DrawLock(rw - 210f, ry + 24f, LockColor);
                if (Button(new Rect(rw - 170f, ry + 13f, 150f, 44f), full ? "FULL" : "JOIN", false, !full && room.server_online && !online.Busy, 20))
                    online.JoinRoom(room.id, OnlineTeamKey, problem => ShowOnlineResult(problem));
            }
            GUI.EndScrollView();
        }

        float ry2 = top + 130f;
        ry2 = OptionRow(right - 40f, ry2, "Your team", new[] { "Terrorists", "SWAT", "Watch" }, onlineTeam, i => onlineTeam = i, rightWidth);
        if (Button(new Rect(right, ry2, rightWidth, 64f), "QUICK JOIN", true, !online.Busy && online.Rooms.Count > 0, 26))
            online.QuickJoin(OnlineTeamKey, problem => ShowOnlineResult(problem));
        ry2 += 84f;

        bool enter = Event.current.type == EventType.KeyDown && GUI.GetNameOfFocusedControl() == "online-code" &&
                     (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
        if (enter) Event.current.Use();
        Label(new Rect(right, ry2, rightWidth, 30f), "Join a private room with its code", 20, Color.white);
        ry2 += 36f;
        onlineCode = TextInput(new Rect(right, ry2, rightWidth - 170f, 46f), "online-code", onlineCode, 8).ToUpperInvariant();
        if ((Button(new Rect(right + rightWidth - 156f, ry2, 156f, 46f), "JOIN", false, onlineCode.Trim().Length > 0 && !online.Busy, 20) || enter
             || KeyboardDone("online-code")) && onlineCode.Trim().Length > 0)
            online.JoinByCode(onlineCode, OnlineTeamKey, problem => ShowOnlineResult(problem));
        ry2 += 70f;

        bool canCreate = !online.RoomsLoaded || online.RoomsFree > 0;
        if (Button(new Rect(right, ry2, rightWidth, 64f), canCreate ? "CREATE ROOM" : "ALL ROOMS ARE BUSY", false, canCreate && !online.Busy, 24))
        {
            onlineCreating = true;
            onlineMessage = null;
            if (onlineRoomName.Length == 0) onlineRoomName = $"{online.User.nick}'s room";
        }
        ry2 += 84f;
        if (online.Busy) Label(new Rect(right, ry2, rightWidth, 30f), "Please wait...", 18, Dim);
        else if (!string.IsNullOrEmpty(onlineMessage ?? gm.MenuNotice))
            WrapLabel(new Rect(right, ry2, rightWidth, 80f), onlineMessage ?? gm.MenuNotice, 18, onlineMessageBad || onlineMessage == null ? Danger : Gold);
        ry2 += 90f;
        WrapLabel(new Rect(right, ry2, rightWidth, 150f),
                  "Rooms run on the Low Strike server, up to 5v5; bots fill the free places. The room's owner starts the match. " +
                  $"Voice chat: hold {GameInput.TalkName}, in the room and in the match.", 16, Dim);

        if (Button(new Rect(x + w / 2f - 150f, bottom, 300f, 56f), "BACK")) onlineOpen = false;
    }

    static string MapName(string id) => MapCatalog.Find(id)?.Name ?? id;

    void DrawCreateRoom(float x, float y, float w, float bottom)
    {
        var online = Online;
        float column = (w - 120f) / 2f, right = x + column + 40f;
        Label(new Rect(x + 40f, y, column, 34f), "Room name", 22, Color.white);
        onlineRoomName = TextInput(new Rect(x + 40f, y + 38f, column, 46f), "online-room-name", onlineRoomName, 24);
        float leftEnd = PlayersRow(x, y + 105f, column);
        leftEnd = DifficultyRow(x, leftEnd, column);
        leftEnd = OptionRow(x, leftEnd, "Who can join", new[] { "Everyone", "With the code" }, onlinePrivate ? 1 : 0, i => onlinePrivate = i == 1, column);
        float rightEnd = MapRow(right, y, column);
        rightEnd = RoundsRow(right, rightEnd, column);
        rightEnd = FriendlyFireAndSiteRow(right, rightEnd, column);
        rightEnd = OptionRow(right, rightEnd, "Your team", new[] { "Terrorists", "SWAT", "Watch" }, onlineTeam, i => onlineTeam = i, column);
        float by = Mathf.Max(leftEnd, rightEnd) + 4f;

        if (!string.IsNullOrEmpty(onlineMessage))
            Label(new Rect(x, by, w, 30f), onlineMessage, 19, onlineMessageBad ? Danger : Gold, TextAnchor.MiddleCenter);
        by += 40f;
        bool nameOk = onlineRoomName.Trim().Length >= 3;
        if (Button(new Rect(x + (w - 640f) / 2f, by, 640f, 66f), online.Busy ? "CREATING..." : "CREATE ROOM", true, nameOk && !online.Busy, 30))
        {
            online.CreateRoom(new RoomSettingsBody
            {
                name = onlineRoomName.Trim(), map = gm.Map.Id, team_size = gm.TeamSize, terrorists = gm.TerroristCount, swat = gm.SwatCount,
                difficulty = gm.Difficulty.ToString(),
                rounds = gm.RoundsToWin, friendly_fire = gm.FriendlyFire, @private = onlinePrivate, team = OnlineTeamKey,
                closed_site = gm.ClosedSiteSetting,
            }, problem =>
            {
                ShowOnlineResult(problem);
                if (problem == null) onlineCreating = false;
            });
        }
        if (!nameOk) Label(new Rect(x, by + 70f, w, 26f), "The room's name needs 3 to 24 characters.", 16, Dim, TextAnchor.MiddleCenter);
        if (Button(new Rect(x + w / 2f - 150f, bottom, 300f, 56f), "BACK")) onlineCreating = false;
    }

    // ----------------------------------------------------------------- the room

    void DrawOnlineRoom()
    {
        var online = Online;
        var room = online.Room;
        var net = gm.Net;
        online.WantFriends = true;
        Fill(new Rect(0f, 0f, width, RefHeight), new Color(0f, 0f, 0f, 0.35f));
        const float top = 50f, h = 980f;
        float w = Mathf.Min(1480f, width - 40f), x = (width - w) / 2f, bottom = top + h - 80f;
        Fill(new Rect(x, top, w, h), PanelColor);
        if (room == null)
        {
            Label(new Rect(x, top + 20f, w, 60f), "ONLINE ROOM", 44, Gold, TextAnchor.MiddleCenter);
            Label(new Rect(x, top + 120f, w, 30f), net.Connected ? "Connected" : "Connecting to the game server...", 22, Dim, TextAnchor.MiddleCenter);
            if (Button(new Rect(x + w / 2f - 150f, bottom, 300f, 56f), "LEAVE")) gm.ReturnToMenu();
            return;
        }

        bool playing = room.status == "playing";
        Label(new Rect(x, top + 16f, w, 60f), room.name.ToUpperInvariant(), 44, Gold, TextAnchor.MiddleCenter);
        Label(new Rect(x, top + 72f, w, 28f),
              $"Code {room.code}  ·  {(room.@private ? "private: join with the code or an invitation" : "public")}  ·  " +
              (playing ? "match running" : "waiting for the owner to start"), 19, Dim, TextAnchor.MiddleCenter);
        // The connection to the room's game server.
        string link;
        Color linkColor;
        if (net.IsClient && net.Connected)
        {
            link = net.PingMs >= 0f ? $"Connected to the game server  ·  ping {net.PingMs:0} ms" : "Connected to the game server";
            linkColor = MoneyGreen;
        }
        else if (net.IsClient)
        {
            link = string.IsNullOrEmpty(net.Status) ? "Connecting to the game server..." : net.Status;
            linkColor = Dim;
        }
        else
        {
            link = string.IsNullOrEmpty(gm.MenuNotice) ? (string.IsNullOrEmpty(net.Status) ? "Not connected to the game server" : net.Status) : gm.MenuNotice;
            linkColor = Danger;
        }
        Label(new Rect(x, top + 102f, w, 28f), link, 18, linkColor, TextAnchor.MiddleCenter);
        if (net.IsClient && net.Connected) DrawLobbyVoice(x + 160f, top + 130f, w - 320f);
        if (!net.IsClient && Button(new Rect(x + w / 2f - 110f, top + 134f, 220f, 40f), "REJOIN", true, !online.Busy, 18))
            online.Rejoin(problem => ShowOnlineResult(problem));

        float column = (w - 120f) / 2f, left = x + 40f, right = x + column + 80f, y = top + 185f;

        // Left: the players, and this player's team.
        int players = room.members.FindAll(m => m.team != "spectator").Count;
        Label(new Rect(left, y, column, 32f), $"Players {players}/{room.max_players}  (bots fill the free places)", 22, Color.white);
        y += 40f;
        var voice = VoiceChat.Instance;
        foreach (var m in room.members)
        {
            bool me = m.user_id == online.User.id;
            Color color = m.team == "spectator" ? Color.white : TeamColor(m.team == "swat" ? Team.Swat : Team.Terrorists);
            Fill(new Rect(left, y, column, 42f), new Color(1f, 1f, 1f, me ? 0.1f : 0.05f));
            Fill(new Rect(left + 12f, y + 15f, 12f, 12f), m.connected ? MoneyGreen : OfflineColor);
            string side = m.team == "spectator" ? "watching" : m.team == "swat" ? "SWAT" : "Terrorists";
            Label(new Rect(left + 34f, y, column - (me || voice == null ? 200f : 350f), 42f),
                  $"{m.nick}{(m.owner ? "  (owner)" : "")}{(me ? "  (you)" : "")}   ·  {side}{(m.connected ? "" : "  ·  not connected")}", 19, color);
            if (!me && voice != null)
            {
                bool muted = voice.IsMuted(m.nick);
                if (Button(new Rect(left + column - 130f, y + 5f, 120f, 32f), muted ? "UNMUTE" : "MUTE", muted, true, 15)) voice.SetMuted(m.nick, !muted);
                VolumeControl(new Rect(left + column - 280f, y + 5f, 140f, 32f), voice, m.nick, muted);
            }
            y += 48f;
        }
        y += 10f;
        int team = net.PreferSpectator ? 2 : net.PreferredTeam == Team.Swat ? 1 : 0;
        if (!playing)
            y = OptionRow(left - 40f, y, "Your team", new[] { "Terrorists", "SWAT", "Watch" }, team,
                          i => online.ChooseTeam(i == 1 ? "swat" : i == 2 ? "spectator" : "t"), column);

        // Right: the settings (the owner can change them between matches), and inviting friends.
        float ry = top + 185f;
        if (room.is_owner && !playing && roomEditing)
        {
            ry = DrawRoomSettingsEditor(right, ry, column, room);
        }
        else
        {
            Label(new Rect(right, ry, column, 32f), "Settings", 22, Color.white);
            if (room.is_owner && !playing && Button(new Rect(right + column - 160f, ry - 2f, 160f, 36f), "CHANGE", false, true, 17)) roomEditing = true;
            ry += 40f;
            WrapLabel(new Rect(right, ry, column, 90f),
                      $"{MapName(room.map)}  ·  {room.Terrorists}v{room.Swat}  ·  {room.difficulty} bots  ·  first to {room.rounds}  ·  " +
                      $"friendly fire {(room.friendly_fire ? "on" : "off")}" +
                      (Math.Max(room.Terrorists, room.Swat) <= 3 && (room.closed_site == "A" || room.closed_site == "B") ? $"  ·  site {room.closed_site} closed" : ""), 19, Dim);
            ry += 70f;
            ry = DrawInviteList(right, ry, column, bottom - 120f);
        }

        // Bottom: start (owner) and leave.
        float by = bottom - 90f;
        if (!string.IsNullOrEmpty(onlineMessage))
            Label(new Rect(x, by - 34f, w, 30f), onlineMessage, 18, onlineMessageBad ? Danger : Gold, TextAnchor.MiddleCenter);
        if (room.is_owner)
        {
            bool anyone = room.members.Exists(m => m.connected);
            if (Button(new Rect(x + (w - 640f) / 2f, by, 640f, 66f), playing ? "MATCH RUNNING" : anyone ? "START MATCH" : "WAITING FOR THE GAME SERVER...",
                       true, !playing && anyone && !online.Busy, 30))
                online.StartRoomMatch(problem => ShowOnlineResult(problem, "Starting..."));
        }
        else Label(new Rect(x, by, w, 66f), playing ? "The match is running: you join at the next round." : "The owner starts the match.", 22, Dim, TextAnchor.MiddleCenter);
        if (Button(new Rect(x + w / 2f - 150f, bottom, 300f, 56f), "LEAVE ROOM"))
        {
            online.LeaveRoom();
            roomEditing = false;
            onlineMessage = null;
        }
    }

    float DrawRoomSettingsEditor(float x, float y, float column, RoomInfo room)
    {
        var online = Online;
        void Change(Action<RoomSettingsBody> edit)
        {
            var settings = online.SettingsOf(room);
            edit(settings);
            online.ChangeRoom(settings, problem => ShowOnlineResult(problem));
        }
        var maps = MapCatalog.All;
        var names = Array.ConvertAll(maps, m => m.Name);
        y = OptionRow(x - 40f, y, "Map", names, Array.FindIndex(maps, m => m.Id == room.map), i => Change(s => s.map = maps[i].Id), column);
        y = TeamCountsRow(x - 40f, y, column, "room", room.Terrorists, room.Swat, (t, s) => Change(body => body.SetTeams(t, s)));
        string[] difficulties = { "Easy", "Normal", "Hard", "Extreme" };
        y = OptionRow(x - 40f, y, "Bot difficulty", difficulties, Array.IndexOf(difficulties, room.difficulty), i => Change(s => s.difficulty = difficulties[i]), column);
        int[] rounds = { 3, 5, 8, 16 };
        y = OptionRow(x - 40f, y, "Rounds to win", new[] { "3", "5", "8", "16" }, Array.IndexOf(rounds, room.rounds), i => Change(s => s.rounds = rounds[i]), column);
        float half = (column - 20f) / 2f;
        OptionRow(x - 40f, y, "Friendly fire", new[] { "Off", "On" }, room.friendly_fire ? 1 : 0, i => Change(s => s.friendly_fire = i == 1), half);
        y = MapCatalog.Find(room.map)?.OneSite == true ? OneSiteRow(x - 20f + half, y, half)
            : ClosedSiteRow(x - 20f + half, y, half, Math.Max(room.Terrorists, room.Swat), room.closed_site == "OFF" ? null : room.closed_site,
                            site => Change(s => s.closed_site = site ?? "OFF"));
        y = OptionRow(x - 40f, y, "Who can join", new[] { "Everyone", "With the code" }, room.@private ? 1 : 0, i => Change(s => s.@private = i == 1), column);
        if (Button(new Rect(x, y, 200f, 42f), "DONE", true, true, 18)) roomEditing = false;
        return y + 56f;
    }

    // Friends to invite into the room (online ones first); an invitation waits on the server for a friend who is offline.
    float DrawInviteList(float x, float y, float column, float listBottom)
    {
        var online = Online;
        Label(new Rect(x, y, column, 32f), "Invite friends", 22, Color.white);
        y += 40f;
        var friends = online.Friends;
        if (friends.Count == 0)
        {
            WrapLabel(new Rect(x, y, column, 60f), "Add friends in FRIENDS (main menu), or give them the room's code.", 17, Dim);
            return y + 60f;
        }
        const float row = 48f;
        var view = new Rect(x, y, column, Mathf.Max(row, listBottom - y));
        var content = new Rect(0f, 0f, friends.Count * row > view.height ? column - 20f : column, friends.Count * row);
        inviteScroll = GUI.BeginScrollView(view, inviteScroll, content, false, false);
        for (int i = 0; i < friends.Count; i++)
        {
            var f = friends[i];
            float ry = i * row;
            bool inRoom = online.Room.members.Exists(m => m.user_id == f.id);
            Fill(new Rect(0f, ry, content.width, row - 6f), new Color(1f, 1f, 1f, 0.05f));
            Fill(new Rect(12f, ry + 15f, 12f, 12f), StatusColor(f.status));
            Label(new Rect(34f, ry, content.width - 200f, row - 6f), $"{f.nick}   ·  {StatusText(f.status)}  ·  {PlayedText(f.played)}", 19, Color.white);
            if (Button(new Rect(content.width - 150f, ry + 4f, 140f, 34f), inRoom ? "IN ROOM" : "INVITE", false, !inRoom, 16))
            {
                string nick = f.nick;
                online.InviteFriend(f.id, problem => ShowOnlineResult(problem, $"Invitation sent to {nick}"));
            }
        }
        GUI.EndScrollView();
        return listBottom;
    }

    static Color StatusColor(string status) =>
        status == "in_match" ? InMatchColor : status == "online" ? MoneyGreen : status == "hidden" ? new Color(1f, 1f, 1f, 0.2f) : OfflineColor;

    static string StatusText(string status) =>
        status == "in_match" ? "In match" : status == "online" ? "Online" : status == "hidden" ? "Hidden" : "Offline";

    // ----------------------------------------------------------------- online friends

    /// <summary>Friends over the internet: find players by nickname, requests, invitations to rooms, and who is online
    /// or in a match. The LAN friends (same WiFi) are one click away.</summary>
    void DrawOnlineFriends()
    {
        var online = Online;
        online.WantFriends = true;
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
        {
            friendsOpen = false;
            return;
        }
        bool enter = Event.current.type == EventType.KeyDown && GUI.GetNameOfFocusedControl() == "online-search" &&
                     (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
        if (enter) Event.current.Use();

        Fill(new Rect(0f, 0f, width, RefHeight), new Color(0f, 0f, 0f, 0.35f));
        const float top = 50f, h = 980f;
        float w = 900f, x = (width - w) / 2f, y = top;
        Fill(new Rect(x, y, w, h), PanelColor);
        Label(new Rect(x, y + 16f, w, 60f), "FRIENDS", 50, Gold, TextAnchor.MiddleCenter);
        OnlineStatusLine(new Rect(x, y + 74f, w, 28f));
        y += 112f;
        float bx = x + 40f, bw = w - 80f;
        if (Button(new Rect(bx, y, bw / 2f - 5f, 44f), "ONLINE", true, true, 20)) friendsLan = false;
        if (Button(new Rect(bx + bw / 2f + 5f, y, bw / 2f - 5f, 44f), "LAN (same WiFi)", false, true, 20))
        {
            if (!WebLink.Available) friendsLan = true;
            else
            {
                onlineMessage = BrowserNoLan;   // the browser version
                onlineMessageBad = true;
            }
        }
        y += 60f;

        bool hidden = Accounts.HideStatus;
        Label(new Rect(bx, y, bw, 30f), hidden ? "Your online status: hidden (Settings)" : "Your online status: visible to friends", 18,
              hidden ? Dim : MoneyGreen);
        y += 38f;

        // Find a player.
        onlineSearch = TextInput(new Rect(bx, y, bw - 200f, 44f), "online-search", onlineSearch, 16);
        if ((Button(new Rect(bx + bw - 186f, y, 186f, 44f), "FIND", false, onlineSearch.Trim().Length >= 2, 20) || enter || KeyboardDone("online-search"))
            && onlineSearch.Trim().Length >= 2)
        {
            string text = onlineSearch.Trim();
            online.Search(text, list =>
            {
                searchResults = list ?? new List<PlayerInfo>();
                ShowOnlineResult(list == null ? "Could not search right now." : list.Count == 0 ? $"No player is called {text}." : null);
            });
        }
        y += 54f;
        if (searchResults != null)
        {
            foreach (var player in searchResults.GetRange(0, Mathf.Min(4, searchResults.Count)))
            {
                Fill(new Rect(bx, y, bw, 44f), new Color(1f, 1f, 1f, 0.06f));
                Label(new Rect(bx + 14f, y, bw - 240f, 44f), player.nick, 20, Color.white);
                string state = player.friend ? "FRIEND" : player.request_sent ? "ASKED" : player.request_received ? "ACCEPT" : "ADD FRIEND";
                if (Button(new Rect(bx + bw - 200f, y + 5f, 190f, 34f), state, player.request_received, !player.friend && !player.request_sent && !online.Busy, 16))
                {
                    var target = player;
                    online.SendFriendRequest(null, target.id, problem =>
                    {
                        ShowOnlineResult(problem, target.request_received ? $"{target.nick} is now your friend" : $"Friend request sent to {target.nick}");
                        if (problem == null) target.request_sent = !target.request_received;
                        if (problem == null && target.request_received) target.friend = true;
                    });
                }
                y += 50f;
            }
            if (searchResults.Count == 0) y += 4f;
        }
        if (!string.IsNullOrEmpty(onlineMessage))
        {
            Label(new Rect(bx, y, bw, 30f), onlineMessage, 17, onlineMessageBad ? Danger : Gold);
            y += 34f;
        }

        // Invitations to rooms, and friend requests.
        foreach (var invite in online.Invites.ToArray())
        {
            Fill(new Rect(bx, y, bw, 48f), new Color(1f, 0.78f, 0.35f, 0.12f));
            Label(new Rect(bx + 14f, y, bw - 330f, 48f), $"{invite.from} invites you to '{invite.room_name}' ({MapName(invite.map)})", 18, Color.white);
            if (Button(new Rect(bx + bw - 310f, y + 6f, 150f, 36f), "JOIN", true, !online.Busy && online.Room == null, 17))
                online.AnswerInvite(invite, true, problem =>
                {
                    ShowOnlineResult(problem);
                    if (problem == null) friendsOpen = false;
                });
            if (Button(new Rect(bx + bw - 150f, y + 6f, 140f, 36f), "DECLINE", false, true, 17)) online.AnswerInvite(invite, false, null);
            y += 54f;
        }
        foreach (var request in online.Incoming.ToArray())
        {
            Fill(new Rect(bx, y, bw, 48f), new Color(1f, 1f, 1f, 0.08f));
            Label(new Rect(bx + 14f, y, bw - 330f, 48f), $"{request.nick} wants to be your friend", 18, Color.white);
            if (Button(new Rect(bx + bw - 310f, y + 6f, 150f, 36f), "ACCEPT", true, true, 17)) online.AnswerFriendRequest(request, true);
            if (Button(new Rect(bx + bw - 150f, y + 6f, 140f, 36f), "DECLINE", false, true, 17)) online.AnswerFriendRequest(request, false);
            y += 54f;
        }
        foreach (var request in online.Outgoing.ToArray())
        {
            Label(new Rect(bx + 14f, y, bw - 200f, 36f), $"Asked {request.nick} (waiting for an answer)", 17, Dim);
            if (Button(new Rect(bx + bw - 150f, y + 2f, 140f, 32f), "CANCEL", false, true, 15)) online.AnswerFriendRequest(request, false);
            y += 40f;
        }

        // The friends: in a match, online, offline.
        var friends = online.Friends;
        int on = friends.FindAll(f => f.status == "online" || f.status == "in_match").Count;
        Label(new Rect(bx, y, bw, 32f), $"Your friends ({friends.Count})", 21, Color.white);
        if (!hidden && friends.Count > 0) Label(new Rect(bx, y, bw, 32f), $"{on} online", 18, MoneyGreen, TextAnchor.MiddleRight);
        y += 38f;
        float listBottom = top + h - 100f;
        if (friends.Count == 0)
            Label(new Rect(bx, y, bw, 30f), "No friends yet: find players by their nickname above.", 18, Dim);
        else
        {
            const float row = 52f;
            var view = new Rect(bx, y, bw, Mathf.Max(row, listBottom - y));
            var content = new Rect(0f, 0f, friends.Count * row > view.height ? bw - 20f : bw, friends.Count * row);
            onlineFriendsScroll = GUI.BeginScrollView(view, onlineFriendsScroll, content, false, false);
            for (int i = 0; i < friends.Count; i++)
            {
                var f = friends[i];
                float ry = i * row;
                Fill(new Rect(0f, ry, content.width, row - 6f), new Color(1f, 1f, 1f, f.status == "offline" ? 0.04f : 0.09f));
                Fill(new Rect(14f, ry + 16f, 14f, 14f), StatusColor(f.status));
                Label(new Rect(42f, ry, 300f, row - 6f), f.nick, 21, Color.white);
                Label(new Rect(content.width - 480f, ry, 150f, row - 6f), StatusText(f.status), 18, StatusColor(f.status), TextAnchor.MiddleRight);
                if (online.Room != null && Button(new Rect(content.width - 316f, ry + 6f, 150f, 34f), "INVITE", false, true, 16))
                {
                    string nick = f.nick;
                    online.InviteFriend(f.id, problem => ShowOnlineResult(problem, $"Invitation sent to {nick}"));
                }
                bool confirm = removeOnline == f.id;
                if (Button(new Rect(content.width - 156f, ry + 6f, 146f, 34f), confirm ? "REALLY?" : "REMOVE", confirm, true, 16))
                {
                    if (confirm)
                    {
                        online.RemoveFriend(f.id);
                        removeOnline = null;
                    }
                    else removeOnline = f.id;
                }
            }
            GUI.EndScrollView();
        }

        if (Button(new Rect(x + w / 2f - 150f, top + h - 76f, 300f, 56f), "BACK"))
        {
            friendsOpen = false;
            removeOnline = null;
            searchResults = null;
            onlineMessage = null;
        }
    }

    // ----------------------------------------------------------------- the online account

    void DrawOnlineAccount()
    {
        var online = Online;
        if (!online.LoggedIn || (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape))
        {
            onlineAccountOpen = false;
            return;
        }
        Fill(new Rect(0f, 0f, width, RefHeight), new Color(0f, 0f, 0f, 0.45f));
        float w = 760f, h = 930f, x = (width - w) / 2f, y = (RefHeight - h) / 2f;
        Fill(new Rect(x, y, w, h), PanelColor);
        Label(new Rect(x, y + 18f, w, 60f), "ONLINE ACCOUNT", 46, Gold, TextAnchor.MiddleCenter);
        Label(new Rect(x, y + 76f, w, 30f), $"{online.User.nick}  ·  since {online.User.created_at?.Substring(0, Mathf.Min(10, online.User.created_at.Length))}"
              + (online.User.played >= 0 ? $"  ·  Time in the game: {PlayedText(online.User.played)}" : ""), 20, Dim, TextAnchor.MiddleCenter);
        float fx = x + 60f, fw = w - 120f, cy = y + 126f;
        cy += DrawEmailSection(fx, fw, cy) + 10f;   // GameUI.Recovery

        Label(new Rect(fx, cy, fw, 30f), "Change the password", 22, Color.white);
        cy += 38f;
        oldPassword = TextInput(new Rect(fx, cy, fw, 42f), "pw-old", oldPassword, 128, true);
        Label(new Rect(fx + 8f, cy, fw, 42f), oldPassword.Length == 0 && !KeyboardOpenFor("pw-old") ? "current password" : "", 16, new Color(1f, 1f, 1f, 0.3f));
        cy += 50f;
        newPassword = TextInput(new Rect(fx, cy, fw, 42f), "pw-new", newPassword, 128, true);
        Label(new Rect(fx + 8f, cy, fw, 42f), newPassword.Length == 0 && !KeyboardOpenFor("pw-new") ? "new password (at least 8 characters)" : "", 16, new Color(1f, 1f, 1f, 0.3f));
        cy += 50f;
        newRepeat = TextInput(new Rect(fx, cy, fw, 42f), "pw-repeat", newRepeat, 128, true);
        Label(new Rect(fx + 8f, cy, fw, 42f), newRepeat.Length == 0 && !KeyboardOpenFor("pw-repeat") ? "new password again" : "", 16, new Color(1f, 1f, 1f, 0.3f));
        cy += 54f;
        if (Button(new Rect(fx, cy, fw, 52f), "CHANGE PASSWORD", false, oldPassword.Length > 0 && newPassword.Length > 0 && !online.Busy, 22))
        {
            if (newPassword != newRepeat) SetAccountPageMessage("The two new passwords are not the same.", true);
            else if (newPassword.Length < 8) SetAccountPageMessage("The new password needs at least 8 characters.", true);
            else
                online.ChangePassword(oldPassword, newPassword, problem =>
                {
                    SetAccountPageMessage(problem ?? "Password changed. Other computers and phones were logged out.", problem != null);
                    if (problem == null) oldPassword = newPassword = newRepeat = "";
                });
        }
        cy += 76f;

        Label(new Rect(fx, cy, fw, 30f), "Delete the account", 22, Danger);
        cy += 34f;
        WrapLabel(new Rect(fx, cy, fw, 50f), "Removes it from the server for good: nickname, password, friends, skins and settings.", 16, Dim);
        cy += 48f;
        deletePassword = TextInput(new Rect(fx, cy, fw, 42f), "delete-password", deletePassword, 128, true);
        Label(new Rect(fx + 8f, cy, fw, 42f), deletePassword.Length == 0 && !KeyboardOpenFor("delete-password") ? "your password" : "", 16, new Color(1f, 1f, 1f, 0.3f));
        cy += 52f;
        if (Button(new Rect(fx, cy, fw, 52f), deleteConfirm ? "REALLY DELETE IT? CLICK AGAIN" : "DELETE ACCOUNT", deleteConfirm, deletePassword.Length > 0 && !online.Busy, 20))
        {
            if (!deleteConfirm) deleteConfirm = true;
            else
            {
                deleteConfirm = false;
                online.DeleteAccount(deletePassword, problem =>
                {
                    deletePassword = "";
                    SetAccountPageMessage(problem ?? "Your online account was deleted.", problem != null);
                    if (problem == null)
                    {
                        onlineAccountOpen = false;
                        gm.MenuNotice = "Your online account was deleted.";
                    }
                });
            }
        }
        cy += 70f;
        if (!string.IsNullOrEmpty(accountPageMessage))
            WrapLabel(new Rect(fx, cy, fw, 50f), accountPageMessage, 18, accountPageBad ? Danger : Gold);
        cy += 60f;
        if (Button(new Rect(fx, cy, fw / 2f - 5f, 56f), "LOG OUT"))
        {
            online.LogOut();
            onlineAccountOpen = false;
        }
        if (Button(new Rect(fx + fw / 2f + 5f, cy, fw / 2f - 5f, 56f), "BACK"))
        {
            onlineAccountOpen = false;
            accountPageMessage = null;
            deleteConfirm = false;
            oldPassword = newPassword = newRepeat = deletePassword = "";
            emailInput = emailPassword = emailCode = "";
        }
    }

    bool KeyboardOpenFor(string id) => KeyboardOpen && keyboardField == id;

    void SetAccountPageMessage(string text, bool bad)
    {
        accountPageMessage = text;
        accountPageBad = bad;
    }

    // Once per online account: bring this computer's skins and settings along?
    void DrawLinkQuestion()
    {
        var online = Online;
        Fill(new Rect(0f, 0f, width, RefHeight), new Color(0f, 0f, 0f, 0.6f));
        float w = 820f, h = 400f, x = (width - w) / 2f, y = (RefHeight - h) / 2f;
        Fill(new Rect(x, y, w, h), new Color(0.05f, 0.05f, 0.06f, 0.96f));
        Label(new Rect(x, y + 24f, w, 50f), "WELCOME, " + online.User.nick.ToUpperInvariant(), 36, Gold, TextAnchor.MiddleCenter);
        string source = online.LinkSource != null ? $"the skins of your account {online.LinkSource} on this computer" : "the skins you chose here";
        WrapLabel(new Rect(x + 50f, y + 100f, w - 100f, 130f),
                  $"Use {source}, and this computer's crosshair and voice settings, for your online account? They are then saved " +
                  "with it and come along on every computer and phone you log in on. (Asked only once.)", 21, Color.white);
        if (Button(new Rect(x + 50f, y + h - 110f, (w - 120f) / 2f, 64f), "YES, BRING THEM", true, true, 22)) online.AnswerLink(true);
        if (Button(new Rect(x + 70f + (w - 120f) / 2f, y + h - 110f, (w - 120f) / 2f, 64f), "NO, START FRESH", false, true, 22)) online.AnswerLink(false);
    }

    /// <summary>Leaves the game: an online room as well (the menus' LEAVE GAME and MAIN MENU).</summary>
    void LeaveGame()
    {
        if (Online != null && Online.Room != null) Online.LeaveRoom();
        else gm.ReturnToMenu();
    }

    // A speaker's voice turned down or up again for this player only: - 70% + (in steps of 10%, at most 100%).
    void VolumeControl(Rect r, VoiceChat voice, string name, bool muted)
    {
        float volume = voice.VolumeOf(name), button = r.height + 4f;
        if (Button(new Rect(r.x, r.y, button, r.height), "-", false, !muted && volume > 0f, 18)) voice.SetVolume(name, volume - 0.1f);
        Label(new Rect(r.x + button, r.y, r.width - 2f * button, r.height), $"{Mathf.RoundToInt(volume * 100f)}%", 16,
              muted || volume <= 0f ? Dim : Color.white, TextAnchor.MiddleCenter);
        if (Button(new Rect(r.xMax - button, r.y, button, r.height), "+", false, !muted && volume < 1f, 18)) voice.SetVolume(name, volume + 0.1f);
    }

    // The pause menu's list of the other people in the game, each with their voice's volume and MUTE (voice chat, for
    // this player only).
    void DrawMuteList(float x, float y)
    {
        var voice = VoiceChat.Instance;
        if (voice == null || WebLink.Available) return;   // no voice chat in the browser version
        var names = new List<string>();
        foreach (var c in gm.Combatants)
            if (c.IsHuman && !c.IsPlayer && !names.Contains(c.DisplayName)) names.Add(c.DisplayName);
        if (Online != null && Online.Room != null && Online.LoggedIn)
            foreach (var m in Online.Room.members)
                if (m.user_id != Online.User.id && !names.Contains(m.nick)) names.Add(m.nick);
        if (names.Count == 0) return;
        float w = Mathf.Clamp(width - x - 20f, 440f, 520f);   // narrower beside the pause menu on a 4:3 screen
        Fill(new Rect(x, y, w, 70f + names.Count * 46f), PanelColor);
        Label(new Rect(x, y + 12f, w, 40f), "VOICE", 26, Gold, TextAnchor.MiddleCenter);
        y += 62f;
        foreach (string name in names)
        {
            bool muted = voice.IsMuted(name);
            Label(new Rect(x + 20f, y, w - 330f, 40f), name, 19, muted ? Dim : Color.white);
            VolumeControl(new Rect(x + w - 300f, y + 3f, 150f, 34f), voice, name, muted);
            if (Button(new Rect(x + w - 140f, y + 3f, 120f, 34f), muted ? "UNMUTE" : "MUTE", muted, true, 15)) voice.SetMuted(name, !muted);
            y += 46f;
        }
    }
}

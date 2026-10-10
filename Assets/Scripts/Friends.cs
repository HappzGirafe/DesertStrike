using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Friends and whether they are online. The friends list is kept with the account (<see cref="Accounts"/>); there
/// is no server, so "online" means the same thing as for LAN games: the friend has the game open on the same WiFi
/// or network. Every 2 s a running game tells the network that its player is online and names that player's friends;
/// a friend shows as online when their game said so in the last few seconds and counts this player as a friend too
/// (so removing someone also stops them seeing you). Friend requests go out the same way, by nickname, and are
/// answered with the accepting account's id. Hiding your online status (Settings) stops telling the network, and
/// then you cannot see your friends' status either.
///
/// The network part runs on its own thread, so it keeps going while the game waits in the background (the game
/// itself pauses there); requests and answers are handled on the main thread.
/// </summary>
public class Friends : MonoBehaviour
{
    public const int Port = 27017;
    const string Tag = "LSFRIENDS";
    const int Version = 1;
    const double TellEvery = 2.0;      // seconds between "I am online" messages
    const double OnlineFor = 7.0;      // a friend counts as online this long after their last message
    const float RequestSendsFor = 30f; // a request goes out again every 2 s for this long

    public class Request
    {
        public string Id, Name;
    }

    /// <summary>Friend requests waiting for an answer (this session only).</summary>
    public readonly List<Request> Incoming = new List<Request>();

    /// <summary>Something happened that the player should know ("Bob accepted your friend request").</summary>
    public event Action<string> Noticed;

    // Who said they are online, by account id (written by the network thread).
    class Seen
    {
        public double At;
        public string Name;
        public string[] FriendIds;
    }

    class Outgoing
    {
        public string Name;
        public float SentAt, NextSend;
    }

    class Repeat
    {
        public string Message;
        public int Left;
        public float NextAt;
    }

    readonly object gate = new object();
    readonly Dictionary<string, Seen> seen = new Dictionary<string, Seen>();
    readonly Queue<string[]> inbox = new Queue<string[]>();
    readonly Dictionary<string, Outgoing> outgoing = new Dictionary<string, Outgoing>();   // by lower-case nickname
    readonly List<Repeat> repeats = new List<Repeat>();
    readonly Dictionary<string, bool> wasOnline = new Dictionary<string, bool>();
    static readonly Stopwatch Clock = Stopwatch.StartNew();
    static double Now => Clock.Elapsed.TotalSeconds;

    UdpClient socket;
    Thread thread;
    volatile bool running;
    string presence;           // what the thread tells the network every 2 s (null: nothing)
    float nextPresenceUpdate, nextStatusCheck;

    // Tests: -ds-friend-request <nickname>, -ds-accept-friends, -ds-hide-status <on|off>.
    string testRequest;
    bool testAccept;
    string testHide;

    void Awake()
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-ds-friend-request");
        if (i >= 0 && i + 1 < args.Length) testRequest = args[i + 1];
        testAccept = Array.IndexOf(args, "-ds-accept-friends") >= 0;
        i = Array.IndexOf(args, "-ds-hide-status");
        if (i >= 0 && i + 1 < args.Length) testHide = args[i + 1];

        Accounts.Changed += () =>
        {
            Incoming.Clear();
            outgoing.Clear();
            wasOnline.Clear();
        };
        Open();
    }

    void Open()
    {
        try
        {
            socket = new UdpClient { EnableBroadcast = true };
            // Several games on one PC (and the LAN search) can listen on the same port.
            socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
            try { socket.Client.IOControl(-1744830452, new byte[] { 0 }, null); } catch (Exception) { }   // no "connection reset"
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Friends] Could not open the network (friends will show as offline): " + e.Message);
            socket?.Close();
            socket = null;
            return;
        }
        running = true;
        thread = new Thread(Run) { IsBackground = true, Name = "Friends" };
        thread.Start();
    }

    void OnApplicationQuit() => Close();

    void OnDestroy() => Close();

    void Close()
    {
        running = false;
        socket?.Close();
        socket = null;
    }

    // ----------------------------------------------------------------- the network thread

    void Run()
    {
        double nextTell = 0.0, nextTargets = 0.0;
        var targets = new List<IPEndPoint>();
        while (running)
        {
            var client = socket;
            if (client == null) break;
            try
            {
                while (client.Available > 0)
                {
                    IPEndPoint from = null;
                    Receive(Encoding.UTF8.GetString(client.Receive(ref from)));
                }
            }
            catch (SocketException) { }
            catch (ObjectDisposedException) { break; }

            double now = Now;
            if (now >= nextTargets)
            {
                targets = BroadcastTargets();   // again now and then: the WiFi may change
                nextTargets = now + 30.0;
            }
            if (now >= nextTell)
            {
                nextTell = now + TellEvery;
                string message;
                lock (gate) message = presence;
                if (message != null) Send(client, message, targets);
            }
            Thread.Sleep(50);
        }
    }

    void Receive(string text)
    {
        string[] parts = text.Split('|');
        if (parts.Length < 6 || parts[0] != Tag || parts[1] != Version.ToString()) return;
        lock (gate)
        {
            if (parts[2] == "P")
                seen[parts[3]] = new Seen
                {
                    At = Now,
                    Name = parts[4],
                    FriendIds = parts[5].Length > 0 ? parts[5].Split(',') : new string[0],
                };
            else if ((parts[2] == "R" || parts[2] == "A") && inbox.Count < 200)
                inbox.Enqueue(parts);
        }
    }

    static List<IPEndPoint> BroadcastTargets()
    {
        var targets = new List<IPEndPoint> { new IPEndPoint(IPAddress.Broadcast, Port) };
        foreach (var (address, mask) in NetSession.LocalIPv4())
            if (mask != null) targets.Add(new IPEndPoint(NetSession.BroadcastAddress(address, mask), Port));
        return targets;
    }

    static void Send(UdpClient client, string message, List<IPEndPoint> targets)
    {
        byte[] data = Encoding.UTF8.GetBytes(message);
        foreach (var target in targets)
        {
            try { client.Send(data, data.Length, target); }
            catch (Exception) { }   // no network right now: try again next time
        }
    }

    void Broadcast(string message)
    {
        var client = socket;
        if (client != null) Send(client, message, BroadcastTargets());
    }

    // ----------------------------------------------------------------- the game's side

    /// <summary>Whether a friend has the game open on this network (always false while your status is hidden).</summary>
    public bool IsOnline(string friendId)
    {
        string me = Accounts.CurrentId;
        if (me == null || Accounts.HideStatus) return false;
        lock (gate)
            return seen.TryGetValue(friendId, out var s) && Now - s.At < OnlineFor && Array.IndexOf(s.FriendIds, me) >= 0;
    }

    public int OnlineCount
    {
        get
        {
            int count = 0;
            foreach (var (id, _) in Accounts.FriendList())
                if (IsOnline(id)) count++;
            return count;
        }
    }

    /// <summary>Sends a friend request to whoever plays with that nickname on this network; returns what is
    /// wrong, or null when it went out.</summary>
    public string SendRequest(string name)
    {
        name = name?.Trim() ?? "";
        if (!Accounts.LoggedIn) return "Log in to add friends";
        if (!Accounts.ValidName(name)) return "Nickname: 3 to 16 letters, digits, _ or -";
        if (string.Equals(name, Accounts.Current, StringComparison.OrdinalIgnoreCase)) return "That is your own nickname";
        if (Accounts.FriendIdNamed(name) != null) return $"{name} is already your friend";
        if (Accounts.FriendList().Count >= Accounts.MaxFriends) return $"Your friends list is full ({Accounts.MaxFriends})";
        var incoming = Incoming.Find(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        if (incoming != null)
        {
            Accept(incoming);   // they asked first: that is a yes
            return null;
        }
        outgoing[name.ToLowerInvariant()] = new Outgoing { Name = name, SentAt = Time.unscaledTime, NextSend = 0f };
        Debug.Log($"[Friends] Friend request to {name}");
        return null;
    }

    public void Accept(Request request)
    {
        Incoming.Remove(request);
        if (!Accounts.AddFriend(request.Id, request.Name))
        {
            Noticed?.Invoke($"Your friends list is full ({Accounts.MaxFriends})");
            return;
        }
        Answer(request.Id);
        Debug.Log($"[Friends] {request.Name} is now a friend (accepted)");
        Noticed?.Invoke($"{request.Name} is now your friend");
    }

    public void Decline(Request request) => Incoming.Remove(request);

    public void Remove(string friendId)
    {
        Accounts.RemoveFriend(friendId);
        wasOnline.Remove(friendId);
    }

    // "Yes" to a request, a few times over (the network may drop one).
    void Answer(string toId) =>
        repeats.Add(new Repeat { Message = $"{Tag}|{Version}|A|{Accounts.CurrentId}|{Accounts.Current}|{toId}", Left = 3 });

    void Update()
    {
        float now = Time.unscaledTime;
        if (Accounts.LoggedIn) RunTests();

        // What the network thread tells everyone: nothing for guests and hidden players.
        if (now >= nextPresenceUpdate)
        {
            nextPresenceUpdate = now + 1f;
            string message = null;
            if (Accounts.LoggedIn && !Accounts.HideStatus)
            {
                var ids = new List<string>();
                foreach (var (id, _) in Accounts.FriendList()) ids.Add(id);
                message = $"{Tag}|{Version}|P|{Accounts.CurrentId}|{Accounts.Current}|{string.Join(",", ids)}";
            }
            lock (gate) presence = message;
        }

        // Requests go out again every 2 s for a while; answers three times, a second apart.
        if (Accounts.LoggedIn)
            foreach (var request in outgoing.Values)
                if (now - request.SentAt < RequestSendsFor && now >= request.NextSend)
                {
                    request.NextSend = now + 2f;
                    Broadcast($"{Tag}|{Version}|R|{Accounts.CurrentId}|{Accounts.Current}|{request.Name}");
                }
        for (int i = repeats.Count - 1; i >= 0; i--)
        {
            var repeat = repeats[i];
            if (now < repeat.NextAt) continue;
            Broadcast(repeat.Message);
            repeat.NextAt = now + 1f;
            if (--repeat.Left <= 0) repeats.RemoveAt(i);
        }

        while (true)
        {
            string[] parts;
            lock (gate)
            {
                if (inbox.Count == 0) break;
                parts = inbox.Dequeue();
            }
            Handle(parts);
        }

        if (now >= nextStatusCheck)
        {
            nextStatusCheck = now + 1f;
            LogStatusChanges();
        }
    }

    void Handle(string[] parts)
    {
        string me = Accounts.CurrentId;
        if (me == null) return;
        string fromId = parts[3], fromName = parts[4];
        if (fromId == me || !Accounts.ValidName(fromName)) return;

        if (parts[2] == "R")
        {
            if (!string.Equals(parts[5], Accounts.Current, StringComparison.OrdinalIgnoreCase)) return;
            if (Accounts.IsFriend(fromId))
            {
                Answer(fromId);   // already friends (they may have removed and asked again): say yes again
                return;
            }
            if (Incoming.Exists(r => r.Id == fromId)) return;
            Incoming.Add(new Request { Id = fromId, Name = fromName });
            Debug.Log($"[Friends] Friend request from {fromName}");
            Noticed?.Invoke($"{fromName} wants to be your friend");
        }
        else if (parts[2] == "A")
        {
            // Only an answer to a request this player sent counts.
            if (parts[5] != me || !outgoing.Remove(fromName.ToLowerInvariant())) return;
            if (!Accounts.AddFriend(fromId, fromName))
            {
                Noticed?.Invoke($"Your friends list is full ({Accounts.MaxFriends})");
                return;
            }
            Debug.Log($"[Friends] {fromName} is now a friend (they accepted)");
            Noticed?.Invoke($"{fromName} accepted your friend request");
        }
    }

    void LogStatusChanges()
    {
        if (!Accounts.LoggedIn) return;
        foreach (var (id, name) in Accounts.FriendList())
        {
            bool online = IsOnline(id);
            if (wasOnline.TryGetValue(id, out bool before) && before == online) continue;
            wasOnline[id] = online;
            Debug.Log($"[Friends] {name} is {(Accounts.HideStatus ? "hidden (your status is hidden)" : online ? "online" : "offline")}");
        }
    }

    void RunTests()
    {
        if (testHide != null)
        {
            Accounts.SetHideStatus(testHide == "on");
            Debug.Log($"[Friends] Online status {(Accounts.HideStatus ? "hidden" : "visible")}");
            testHide = null;
        }
        if (testRequest != null)
        {
            string problem = SendRequest(testRequest);
            if (problem != null) Debug.Log("[Friends] " + problem);
            testRequest = null;
        }
        if (testAccept)
            foreach (var request in Incoming.ToArray()) Accept(request);
    }
}

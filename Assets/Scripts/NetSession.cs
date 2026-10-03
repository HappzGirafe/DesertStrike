using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

/// <summary>
/// LAN multiplayer over UDP.
///
/// The host runs the whole match (bots, rounds, money, damage, the bomb) and sends every client a
/// snapshot ~30 times a second plus short events (shots, impacts, rockets) as they happen. A client
/// moves its own player, sends its position, shots and actions (reload, buy, plant...), and shows
/// everyone else from the snapshots. Hosts announce themselves on the local network every second,
/// so clients on the same WiFi can find them without typing an IP address.
/// </summary>
public class NetSession : MonoBehaviour
{
    public const int GamePort = 27015;
    public const int DiscoveryPort = 27016;
    const int ProtocolVersion = 1;
    const string BeaconTag = "DSTRIKE";
    const float SnapshotInterval = 1f / 30f;
    const float InputInterval = 1f / 60f;
    const float Timeout = 8f;

    enum Msg : byte { Hello = 1, Input, Fire, Action, Bye, Voice, Welcome = 101, Lobby, Snapshot, Events, Ended, VoiceRelay }
    enum Ev : byte { Fire = 1, Tracer, Impact, Blood, Rocket, HitMarker }

    public enum Mode { Off, Host, Client }

    /// <summary>A client, as the host sees it.</summary>
    public class Peer
    {
        public IPEndPoint EndPoint;
        public string Name;
        public Team Team;
        public Dictionary<string, string> SkinChoices;
        public float LastHeard;
        public RemotePlayerController Player;
    }

    /// <summary>A game found on the local network.</summary>
    public class FoundHost
    {
        public IPAddress Address;
        public string Name;
        public int Players;
        public bool InMatch;
        public float LastSeen;
    }

    public Mode Role { get; private set; }
    public bool IsHost => Role == Mode.Host;
    public bool IsClient => Role == Mode.Client;
    public bool Connected { get; private set; }
    public string Status { get; private set; } = "";
    public string HostName { get; private set; } = "";
    public string LobbySettings { get; private set; } = "";
    public int MyNetId { get; private set; }
    public string PlayerName = "Player";
    public Team PreferredTeam = Team.Terrorists;
    public readonly List<Peer> Peers = new List<Peer>();
    public readonly List<FoundHost> FoundHosts = new List<FoundHost>();
    public readonly List<string> LobbyPlayers = new List<string>();

    GameManager gm;
    UdpClient socket;
    UdpClient discovery;
    IPEndPoint hostEndPoint;
    readonly List<IPEndPoint> beaconTargets = new List<IPEndPoint>();
    float lastHeardFromHost, connectStarted, nextHello, nextInput, nextBeacon, nextSnapshot, nextLobby;
    readonly MemoryStream events = new MemoryStream();
    BinaryWriter eventWriter;
    int eventCount;

    bool Recording => IsHost && Peers.Count > 0;

    void Awake()
    {
        gm = GetComponent<GameManager>();
        eventWriter = new BinaryWriter(events);
    }

    void OnApplicationQuit()
    {
        Stop();
        StopDiscovery();
    }

    // ----------------------------------------------------------------- starting and stopping

    public bool StartHost()
    {
        Stop();
        try
        {
            socket = OpenSocket(GamePort);
        }
        catch (Exception e)
        {
            Status = "Could not start hosting: " + e.Message;
            return false;
        }
        Role = Mode.Host;
        Application.runInBackground = true;   // keep the game running for the others when this window is not in front
        HostName = PlayerName;
        Status = "";
        beaconTargets.Clear();
        beaconTargets.Add(new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
        foreach (var (address, mask) in LocalIPv4())
            if (mask != null) beaconTargets.Add(new IPEndPoint(BroadcastAddress(address, mask), DiscoveryPort));
        Debug.Log($"[Net] Hosting on port {GamePort} ({string.Join(", ", LocalAddresses())})");
        return true;
    }

    public void Join(string address)
    {
        if (!IPAddress.TryParse((address ?? "").Trim(), out var ip))
        {
            Status = "That is not an IP address (it looks like 192.168.1.20).";
            return;
        }
        Join(ip);
    }

    public void Join(IPAddress address)
    {
        Stop();
        try
        {
            socket = OpenSocket(0);
        }
        catch (Exception e)
        {
            Status = "Could not open the network: " + e.Message;
            return;
        }
        Role = Mode.Client;
        Application.runInBackground = true;
        Connected = false;
        hostEndPoint = new IPEndPoint(address, GamePort);
        connectStarted = lastHeardFromHost = Time.unscaledTime;
        nextHello = 0f;
        Status = $"Connecting to {address}...";
        Debug.Log($"[Net] Joining {address}");
    }

    /// <summary>Leave or stop hosting (telling the other side).</summary>
    public void Stop()
    {
        if (socket != null)
        {
            if (IsHost)
                foreach (var peer in Peers) Send(new[] { (byte)Msg.Ended }, peer.EndPoint);
            else if (IsClient && hostEndPoint != null)
                Send(new[] { (byte)Msg.Bye }, hostEndPoint);
            socket.Close();
            socket = null;
        }
        Role = Mode.Off;
        Application.runInBackground = false;  // alone, the game pauses in the background (saves battery)
        Connected = false;
        Peers.Clear();
        LobbyPlayers.Clear();
        MyNetId = 0;
        events.SetLength(0);
        eventCount = 0;
    }

    public void StartDiscovery()
    {
        if (discovery != null) return;
        try
        {
            discovery = new UdpClient();
            discovery.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            discovery.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
        }
        catch (Exception e)
        {
            Status = "Could not search the network: " + e.Message;
            discovery?.Close();
            discovery = null;
        }
    }

    public void StopDiscovery()
    {
        discovery?.Close();
        discovery = null;
        FoundHosts.Clear();
    }

    /// <summary>This PC's addresses on the local network (to type into the other PC).</summary>
    public static List<string> LocalAddresses()
    {
        var list = new List<string>();
        foreach (var (address, _) in LocalIPv4()) list.Add(address.ToString());
        return list;
    }

    static List<(IPAddress address, IPAddress mask)> LocalIPv4()
    {
        var result = new List<(IPAddress, IPAddress)>();
        try
        {
            foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (network.OperationalStatus != OperationalStatus.Up || network.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var unicast in network.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    IPAddress mask = null;
                    try { mask = unicast.IPv4Mask; } catch (Exception) { }
                    result.Add((unicast.Address, mask));
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Net] Could not list network addresses: " + e.Message);
        }
        return result;
    }

    static IPAddress BroadcastAddress(IPAddress address, IPAddress mask)
    {
        byte[] ip = address.GetAddressBytes(), bits = mask.GetAddressBytes(), broadcast = new byte[4];
        for (int i = 0; i < 4; i++) broadcast[i] = (byte)(ip[i] | ~bits[i]);
        return new IPAddress(broadcast);
    }

    static UdpClient OpenSocket(int port)
    {
        var client = new UdpClient(port) { EnableBroadcast = true };
        // Windows reports "connection reset" on a UDP socket after a packet bounced off a closed port; ignore that.
        try { client.Client.IOControl(-1744830452, new byte[] { 0 }, null); } catch (Exception) { }
        return client;
    }

    // ----------------------------------------------------------------- receiving

    void Update()
    {
        ReadDiscovery();
        while (socket != null && socket.Available > 0)
        {
            IPEndPoint from = null;
            byte[] data;
            try
            {
                data = socket.Receive(ref from);
            }
            catch (SocketException)
            {
                break;
            }
            if (data.Length == 0) continue;
            try
            {
                using (var reader = new BinaryReader(new MemoryStream(data)))
                {
                    if (IsHost) HostReceive(reader, from);
                    else if (IsClient) ClientReceive(reader, from);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Net] Bad packet: " + e.Message);
            }
        }
    }

    void LateUpdate()
    {
        if (IsHost) HostSend();
        else if (IsClient) ClientSend();
    }

    void ReadDiscovery()
    {
        if (discovery == null) return;
        try
        {
            while (discovery.Available > 0)
            {
                IPEndPoint from = null;
                string[] parts = Encoding.UTF8.GetString(discovery.Receive(ref from)).Split('|');
                if (parts.Length < 5 || parts[0] != BeaconTag || parts[1] != ProtocolVersion.ToString()) continue;
                var host = FoundHosts.Find(h => h.Address.Equals(from.Address));
                if (host == null)
                {
                    host = new FoundHost { Address = from.Address };
                    FoundHosts.Add(host);
                    Debug.Log($"[Net] Found game '{parts[2]}' at {from.Address}");
                }
                host.Name = parts[2];
                int.TryParse(parts[3], out host.Players);
                host.InMatch = parts[4] == "1";
                host.LastSeen = Time.unscaledTime;
            }
        }
        catch (SocketException)
        {
        }
        FoundHosts.RemoveAll(h => Time.unscaledTime - h.LastSeen > 4f);
    }

    // ----------------------------------------------------------------- host

    void HostReceive(BinaryReader reader, IPEndPoint from)
    {
        var msg = (Msg)reader.ReadByte();
        var peer = Peers.Find(p => p.EndPoint.Equals(from));
        if (msg == Msg.Hello)
        {
            int version = reader.ReadInt32();
            string name = CleanName(reader.ReadString());
            var team = (Team)reader.ReadByte();
            string skins = reader.ReadString();
            if (version != ProtocolVersion)
            {
                Send(new[] { (byte)Msg.Ended }, from);
                return;
            }
            if (peer == null)
            {
                peer = new Peer { EndPoint = from, Name = UniqueName(name), Team = team, SkinChoices = WeaponSkins.DecodeChoices(skins) };
                Peers.Add(peer);
                Debug.Log($"[Net] {peer.Name} joined from {from} ({TeamName(team)})");
                if (gm.State != MatchState.Menu) gm.AddRemotePlayer(peer, midRound: true);
            }
            peer.LastHeard = Time.unscaledTime;
            Send(Packet(w =>
            {
                w.Write((byte)Msg.Welcome);
                w.Write(HostName);
            }), from);
            return;
        }

        if (peer == null) return;
        peer.LastHeard = Time.unscaledTime;
        switch (msg)
        {
            case Msg.Input:
                int spawn = reader.ReadByte();
                Vector3 position = reader.ReadVector3();
                float yaw = reader.ReadSingle();
                float height = reader.ReadSingle();
                int flags = reader.ReadByte();
                if (peer.Player != null) peer.Player.ApplyInput(spawn, position, yaw, height, (flags & 1) != 0, (flags & 2) != 0);
                break;
            case Msg.Fire:
                Vector3 direction = reader.ReadVector3();
                float spread = reader.ReadSingle();
                if (peer.Player != null) peer.Player.HandleFire(direction, spread);
                break;
            case Msg.Action:
                var action = (NetAction)reader.ReadByte();
                int argument = reader.ReadInt32();
                if (peer.Player != null) peer.Player.HandleAction(action, argument);
                break;
            case Msg.Voice:
                {
                    ushort sequence = reader.ReadUInt16();
                    int offset = (int)reader.BaseStream.Position;
                    byte[] frame = ((MemoryStream)reader.BaseStream).ToArray();
                    VoiceChat.Instance?.Receive(peer.Name, frame, offset, frame.Length - offset);
                    // Pass it on to everyone else.
                    byte[] relay = VoicePacket(peer.Name, sequence, frame, offset, frame.Length - offset);
                    foreach (var other in Peers)
                        if (other != peer) Send(relay, other.EndPoint);
                }
                break;
            case Msg.Bye:
                RemovePeer(peer, "left");
                break;
        }
    }

    void RemovePeer(Peer peer, string reason)
    {
        Peers.Remove(peer);
        gm.RemoveRemotePlayer(peer);
        Debug.Log($"[Net] {peer.Name} {reason}");
    }

    void HostSend()
    {
        float now = Time.unscaledTime;
        for (int i = Peers.Count - 1; i >= 0; i--)
            if (now - Peers[i].LastHeard > Timeout) RemovePeer(Peers[i], "timed out");

        if (now >= nextBeacon)
        {
            nextBeacon = now + 1f;
            byte[] beacon = Encoding.UTF8.GetBytes($"{BeaconTag}|{ProtocolVersion}|{HostName}|{Peers.Count + 1}|{(gm.State == MatchState.Menu ? 0 : 1)}");
            foreach (var target in beaconTargets) Send(beacon, target);
        }

        if (gm.State == MatchState.Menu)
        {
            if (now >= nextLobby)
            {
                nextLobby = now + 0.5f;
                SendLobby();
            }
        }
        else if (now >= nextSnapshot)
        {
            nextSnapshot = now + SnapshotInterval;
            SendSnapshots();
        }
        FlushEvents();
    }

    void SendLobby()
    {
        var names = new List<string>();
        if (gm.Side != PlayerSide.Spectate)
            names.Add($"{HostName}  ({TeamName(gm.Side == PlayerSide.Terrorists ? Team.Terrorists : Team.Swat)}, host)");
        foreach (var peer in Peers) names.Add($"{peer.Name}  ({TeamName(peer.Team)})");
        byte[] data = Packet(w =>
        {
            w.Write((byte)Msg.Lobby);
            w.Write(HostName);
            w.Write(gm.MatchDescription());
            w.Write((byte)names.Count);
            foreach (var name in names) w.Write(name);
        });
        foreach (var peer in Peers) Send(data, peer.EndPoint);
    }

    void SendSnapshots()
    {
        if (Peers.Count == 0) return;
        byte[] body = Packet(w =>
        {
            WriteMatch(w);
            WriteBomb(w);
            WriteCombatants(w);
            WriteKillFeed(w);
        });
        foreach (var peer in Peers)
        {
            var me = peer.Player != null ? peer.Player.Self : null;
            Send(Packet(w =>
            {
                w.Write((byte)Msg.Snapshot);
                w.Write((ushort)(me != null ? me.NetId : 0));
                w.Write(me != null ? me.LastResupply ?? "" : "");
                w.Write(body);
            }), peer.EndPoint);
        }
    }

    void WriteMatch(BinaryWriter w)
    {
        w.Write((byte)gm.State);
        w.Write((byte)gm.Round);
        w.Write((byte)gm.TerroristWins);
        w.Write((byte)gm.SwatWins);
        w.Write((byte)gm.RoundsToWin);
        w.Write(gm.StateEndsAt - Time.time);
        w.Write(gm.BuyTimeLeft);
        w.Write(gm.Banner ?? "");
        w.Write((sbyte)(gm.LastWinner.HasValue ? (int)gm.LastWinner.Value : -1));
        w.Write(gm.FriendlyFire);
        w.Write(gm.Message ?? "");
        w.Write(gm.MessageUntil - Time.time);
    }

    void WriteBomb(BinaryWriter w)
    {
        var bomb = gm.Bomb;
        w.Write((byte)bomb.State);
        w.Write((ushort)(bomb.Carrier != null ? bomb.Carrier.NetId : 0));
        w.Write(bomb.Position);
        w.Write(bomb.TimeLeft);
        w.Write((ushort)(bomb.User != null ? bomb.User.NetId : 0));
        w.Write(bomb.UseProgress);
        w.Write(bomb.Site ?? "");
    }

    void WriteCombatants(BinaryWriter w)
    {
        w.Write((byte)gm.Combatants.Count);
        foreach (var c in gm.Combatants)
        {
            var current = c.Current;
            w.Write((ushort)c.NetId);
            w.Write(c.DisplayName ?? "");
            w.Write((byte)c.Team);
            w.Write((byte)((c.IsHuman ? 1 : 0) | (c.Helmet ? 2 : 0) | (c.HasDefuseKit ? 4 : 0) | (c.Scoped ? 8 : 0)
                           | (current != null && current.AutoMode ? 16 : 0)));
            w.Write(c.transform.position);
            w.Write(c.transform.eulerAngles.y);
            w.Write((byte)Mathf.RoundToInt(c.Height * 100f));
            w.Write((byte)Mathf.Clamp(c.Health, 0, 255));
            w.Write((byte)Mathf.Clamp(c.Armor, 0, 255));
            w.Write((ushort)Mathf.Clamp(c.Money, 0, 65535));
            w.Write((byte)Mathf.Clamp(c.Kills, 0, 255));
            w.Write((byte)Mathf.Clamp(c.Deaths, 0, 255));
            w.Write((byte)WeaponData.IndexOf(c.Primary?.Data));
            w.Write((byte)WeaponData.IndexOf(c.Secondary?.Data));
            w.Write((byte)(current != null ? current.Data.Slot : WeaponSlot.Knife));
            w.Write((byte)Mathf.Clamp(current?.Mag ?? 0, 0, 255));
            w.Write((ushort)Mathf.Clamp(current?.Reserve ?? 0, 0, 65535));
            w.Write((byte)Mathf.RoundToInt(c.ReloadProgress * 255f));
            w.Write((byte)(c.SpawnCount & 0xFF));
            w.Write((ushort)c.SkinSeed);
            string skin = null;
            if (c.SkinChoices != null && current != null) c.SkinChoices.TryGetValue(current.Data.Id, out skin);
            w.Write(skin ?? "");
        }
    }

    void WriteKillFeed(BinaryWriter w)
    {
        w.Write((byte)gm.KillFeed.Count);
        foreach (var kill in gm.KillFeed)
        {
            w.Write(kill.Killer ?? "");
            w.Write(kill.Victim ?? "");
            w.Write(kill.Weapon ?? "");
            w.Write((byte)kill.KillerTeam);
            w.Write((byte)kill.VictimTeam);
            w.Write((byte)((kill.Headshot ? 1 : 0) | (kill.Noscope ? 2 : 0)));
            w.Write(Time.time - kill.Time);
        }
    }

    // Events: things that happen once (a shot, an impact), sent right away instead of in the snapshot.

    public void RecordFire(Combatant shooter, WeaponData weapon, Vector3 muzzle)
    {
        if (!Recording) return;
        var w = BeginEvent(Ev.Fire);
        w.Write((ushort)shooter.NetId);
        w.Write((byte)WeaponData.IndexOf(weapon));
        w.Write(muzzle);
    }

    public void RecordTracer(Vector3 from, Vector3 to)
    {
        if (!Recording) return;
        var w = BeginEvent(Ev.Tracer);
        w.Write(from);
        w.Write(to);
    }

    public void RecordImpact(Vector3 point, Vector3 normal)
    {
        if (!Recording) return;
        var w = BeginEvent(Ev.Impact);
        w.Write(point);
        w.Write(normal);
    }

    public void RecordBlood(Vector3 point)
    {
        if (!Recording) return;
        BeginEvent(Ev.Blood).Write(point);
    }

    public void RecordRocket(Combatant shooter, Vector3 position, Vector3 direction, WeaponData weapon)
    {
        if (!Recording) return;
        var w = BeginEvent(Ev.Rocket);
        w.Write((ushort)(shooter != null ? shooter.NetId : 0));
        w.Write(position);
        w.Write(direction);
        w.Write((byte)WeaponData.IndexOf(weapon));
    }

    public void RecordHitMarker(Combatant shooter, bool headshot, bool kill)
    {
        if (!Recording) return;
        var w = BeginEvent(Ev.HitMarker);
        w.Write((ushort)shooter.NetId);
        w.Write(headshot);
        w.Write(kill);
    }

    BinaryWriter BeginEvent(Ev type)
    {
        if (eventCount >= 200 || events.Length > 1100) FlushEvents();
        eventWriter.Write((byte)type);
        eventCount++;
        return eventWriter;
    }

    void FlushEvents()
    {
        if (eventCount == 0) return;
        eventWriter.Flush();
        byte[] payload = events.ToArray();
        var packet = new byte[payload.Length + 2];
        packet[0] = (byte)Msg.Events;
        packet[1] = (byte)eventCount;
        Buffer.BlockCopy(payload, 0, packet, 2, payload.Length);
        foreach (var peer in Peers) Send(packet, peer.EndPoint);
        events.SetLength(0);
        eventCount = 0;
    }

    // ----------------------------------------------------------------- client

    void ClientReceive(BinaryReader reader, IPEndPoint from)
    {
        if (!from.Address.Equals(hostEndPoint.Address) || from.Port != hostEndPoint.Port) return;
        lastHeardFromHost = Time.unscaledTime;
        switch ((Msg)reader.ReadByte())
        {
            case Msg.Welcome:
                HostName = reader.ReadString();
                if (!Connected) Debug.Log($"[Net] Connected to {HostName}");
                Connected = true;
                Status = "";
                break;
            case Msg.Lobby:
                HostName = reader.ReadString();
                LobbySettings = reader.ReadString();
                LobbyPlayers.Clear();
                int count = reader.ReadByte();
                for (int i = 0; i < count; i++) LobbyPlayers.Add(reader.ReadString());
                Connected = true;
                break;
            case Msg.Snapshot:
                Connected = true;
                ReadSnapshot(reader);
                break;
            case Msg.Events:
                PlayEvents(reader);
                break;
            case Msg.Ended:
                gm.OnNetworkGameEnded("The host ended the game.");
                break;
            case Msg.VoiceRelay:
                {
                    string speaker = reader.ReadString();
                    reader.ReadUInt16();   // sequence number
                    int offset = (int)reader.BaseStream.Position;
                    byte[] frame = ((MemoryStream)reader.BaseStream).ToArray();
                    VoiceChat.Instance?.Receive(speaker, frame, offset, frame.Length - offset);
                }
                break;
        }
    }

    void ReadSnapshot(BinaryReader r)
    {
        int myId = r.ReadUInt16();
        string resupply = r.ReadString();

        var match = new NetMatchState
        {
            State = (MatchState)r.ReadByte(),
            Round = r.ReadByte(),
            TerroristWins = r.ReadByte(),
            SwatWins = r.ReadByte(),
            RoundsToWin = r.ReadByte(),
            Remaining = r.ReadSingle(),
            BuyTimeLeft = r.ReadSingle(),
            Banner = r.ReadString(),
        };
        sbyte winner = r.ReadSByte();
        match.LastWinner = winner >= 0 ? (Team)winner : (Team?)null;
        match.FriendlyFire = r.ReadBoolean();
        match.Message = r.ReadString();
        match.MessageLeft = r.ReadSingle();

        var bombState = (BombState)r.ReadByte();
        int carrierId = r.ReadUInt16();
        Vector3 bombPosition = r.ReadVector3();
        float bombTimeLeft = r.ReadSingle();
        int userId = r.ReadUInt16();
        float useProgress = r.ReadSingle();
        string site = r.ReadString();

        int count = r.ReadByte();
        var states = new List<NetCombatantState>(count);
        for (int i = 0; i < count; i++)
        {
            var s = new NetCombatantState
            {
                NetId = r.ReadUInt16(),
                Name = r.ReadString(),
                Team = (Team)r.ReadByte(),
            };
            int flags = r.ReadByte();
            s.IsHuman = (flags & 1) != 0;
            s.Helmet = (flags & 2) != 0;
            s.HasKit = (flags & 4) != 0;
            s.Scoped = (flags & 8) != 0;
            s.AutoMode = (flags & 16) != 0;
            s.Position = r.ReadVector3();
            s.Yaw = r.ReadSingle();
            s.Height = r.ReadByte() / 100f;
            s.Health = r.ReadByte();
            s.Armor = r.ReadByte();
            s.Money = r.ReadUInt16();
            s.Kills = r.ReadByte();
            s.Deaths = r.ReadByte();
            s.Primary = WeaponData.FromIndex(r.ReadByte());
            s.Secondary = WeaponData.FromIndex(r.ReadByte());
            s.Slot = (WeaponSlot)r.ReadByte();
            s.Mag = r.ReadByte();
            s.Reserve = r.ReadUInt16();
            s.Reload = r.ReadByte() / 255f;
            s.SpawnCount = r.ReadByte();
            s.SkinSeed = r.ReadUInt16();
            s.Skin = r.ReadString();
            states.Add(s);
        }

        int feedCount = r.ReadByte();
        var feed = new List<KillFeedEntry>(feedCount);
        for (int i = 0; i < feedCount; i++)
        {
            var kill = new KillFeedEntry
            {
                Killer = r.ReadString(),
                Victim = r.ReadString(),
                Weapon = r.ReadString(),
                KillerTeam = (Team)r.ReadByte(),
                VictimTeam = (Team)r.ReadByte(),
            };
            int flags = r.ReadByte();
            kill.Headshot = (flags & 1) != 0;
            kill.Noscope = (flags & 2) != 0;
            kill.Time = Time.time - r.ReadSingle();
            feed.Add(kill);
        }

        MyNetId = myId;
        if (!gm.IsClient) gm.BeginClientMatch();
        gm.ApplyNetMatch(match, resupply);
        var ids = new HashSet<int>();
        foreach (var state in states)
        {
            ids.Add(state.NetId);
            gm.ApplyNetCombatant(state, myId);
        }
        gm.RemoveNetCombatantsExcept(ids);
        gm.ApplyNetKillFeed(feed);
        gm.Bomb.ApplyNet(bombState, gm.FindByNetId(carrierId), bombPosition, bombTimeLeft, gm.FindByNetId(userId), useProgress, site);
    }

    void PlayEvents(BinaryReader r)
    {
        int count = r.ReadByte();
        for (int i = 0; i < count; i++)
        {
            switch ((Ev)r.ReadByte())
            {
                case Ev.Fire:
                {
                    int id = r.ReadUInt16();
                    var weapon = WeaponData.FromIndex(r.ReadByte());
                    Vector3 muzzle = r.ReadVector3();
                    if (id == MyNetId || weapon == null) break;   // our own shots already played
                    SoundFX.PlayShot(weapon, muzzle, false);
                    if (!weapon.IsMelee) Effects.MuzzleFlash(muzzle);
                    break;
                }
                case Ev.Tracer:
                {
                    Vector3 from = r.ReadVector3();
                    Vector3 to = r.ReadVector3();
                    Effects.Tracer(from, to);
                    break;
                }
                case Ev.Impact:
                {
                    Vector3 point = r.ReadVector3();
                    Vector3 normal = r.ReadVector3();
                    Effects.BulletHole(point, normal);
                    break;
                }
                case Ev.Blood:
                    Effects.Blood(r.ReadVector3());
                    break;
                case Ev.Rocket:
                {
                    var shooter = gm.FindByNetId(r.ReadUInt16());
                    Vector3 position = r.ReadVector3();
                    Vector3 direction = r.ReadVector3();
                    var weapon = WeaponData.FromIndex(r.ReadByte());
                    if (weapon != null) Rocket.Launch(shooter, position, direction, weapon, visualOnly: true);
                    break;
                }
                case Ev.HitMarker:
                {
                    int id = r.ReadUInt16();
                    bool headshot = r.ReadBoolean();
                    bool kill = r.ReadBoolean();
                    if (id == MyNetId) gm.ShowHitMarker(headshot, kill);
                    break;
                }
                default:
                    return;   // unknown event: the rest of the packet cannot be read
            }
        }
    }

    void ClientSend()
    {
        float now = Time.unscaledTime;
        if (!Connected)
        {
            if (now >= nextHello)
            {
                nextHello = now + 0.5f;
                SendHello();
            }
            if (now - connectStarted > 6f)
            {
                string target = hostEndPoint.Address.ToString();
                Stop();
                Status = $"No answer from {target}. Is a game hosted there, and did that PC allow Desert Strike through its firewall?";
            }
            return;
        }
        if (now - lastHeardFromHost > Timeout)
        {
            gm.OnNetworkGameEnded("Lost the connection to the host.");
            return;
        }

        var player = gm.IsClient ? gm.Player : null;
        if (player != null)
        {
            if (now >= nextInput)
            {
                nextInput = now + InputInterval;
                SendInput(player);
            }
        }
        else if (now >= nextHello)
        {
            nextHello = now + 1f;
            SendHello();   // keeps the connection alive in the lobby
        }
    }

    void SendHello()
    {
        Send(Packet(w =>
        {
            w.Write((byte)Msg.Hello);
            w.Write(ProtocolVersion);
            w.Write(PlayerName);
            w.Write((byte)PreferredTeam);
            w.Write(WeaponSkins.EncodeChoices(WeaponSkins.EquippedChoices()));
        }), hostEndPoint);
    }

    void SendInput(PlayerController player)
    {
        var self = player.Self;
        Send(Packet(w =>
        {
            w.Write((byte)Msg.Input);
            w.Write((byte)(self.SpawnCount & 0xFF));
            w.Write(player.transform.position);
            w.Write(player.transform.eulerAngles.y);
            w.Write(self.Height);
            w.Write((byte)((player.UseHeld ? 1 : 0) | (player.IsScoped ? 2 : 0)));
        }), hostEndPoint);
    }

    public void SendFire(Vector3 direction, float spread)
    {
        if (!IsClient) return;
        Send(Packet(w =>
        {
            w.Write((byte)Msg.Fire);
            w.Write(direction);
            w.Write(spread);
        }), hostEndPoint);
    }

    public void SendAction(NetAction action, int argument)
    {
        if (!IsClient) return;
        Send(Packet(w =>
        {
            w.Write((byte)Msg.Action);
            w.Write((byte)action);
            w.Write(argument);
        }), hostEndPoint);
    }

    /// <summary>One 20 ms voice frame from this player: a client sends it to the host, the host to every client.</summary>
    public void SendVoice(ushort sequence, byte[] frame)
    {
        if (IsClient && Connected)
        {
            Send(Packet(w =>
            {
                w.Write((byte)Msg.Voice);
                w.Write(sequence);
                w.Write(frame);
            }), hostEndPoint);
        }
        else if (IsHost && Peers.Count > 0)
        {
            byte[] packet = VoicePacket(HostName, sequence, frame, 0, frame.Length);
            foreach (var peer in Peers) Send(packet, peer.EndPoint);
        }
    }

    static byte[] VoicePacket(string speaker, ushort sequence, byte[] frame, int offset, int count) => Packet(w =>
    {
        w.Write((byte)Msg.VoiceRelay);
        w.Write(speaker);
        w.Write(sequence);
        w.Write(frame, offset, count);
    });

    // ----------------------------------------------------------------- helpers

    void Send(byte[] data, IPEndPoint to)
    {
        if (socket == null || to == null) return;
        try
        {
            socket.Send(data, data.Length, to);
        }
        catch (SocketException)
        {
            // Unreachable for a moment (e.g. a broadcast address without a route): the next send retries.
        }
    }

    static byte[] Packet(Action<BinaryWriter> write)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            write(writer);
            writer.Flush();
            return stream.ToArray();
        }
    }

    public static string TeamName(Team team) => team == Team.Terrorists ? "Terrorists" : "SWAT";

    static string CleanName(string name)
    {
        name = (name ?? "").Replace("|", "").Trim();
        if (name.Length > 16) name = name.Substring(0, 16);
        return name.Length > 0 ? name : "Player";
    }

    string UniqueName(string name)
    {
        string unique = name;
        for (int n = 2; unique == HostName || Peers.Exists(p => p.Name == unique); n++) unique = $"{name} {n}";
        return unique;
    }
}

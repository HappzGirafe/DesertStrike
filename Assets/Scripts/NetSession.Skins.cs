using System;
using System.Collections.Generic;
using System.Net;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Skin sharing: players whose games have different skins still see each other's. Every snapshot names each gun's
/// skin; a game that does not have one asks for it (<see cref="WeaponSkins.Wanted"/>) and gets its
/// <see cref="SkinPack"/> in ~1 KB pieces. A client asks the host. The host sends its own pack of the skin, else first
/// gets it from a player's game that has it (whoever chose it, then anyone) and passes it on. Lost pieces are simply
/// asked for again; the default skin shows until the whole pack is there.
/// </summary>
public partial class NetSession
{
    const int SkinPiece = 1100;            // bytes of a pack per packet
    const int SkinWindow = 30;             // pieces a client asks the host for at once (every SkinAskInterval)
    const float SkinAskInterval = 0.15f;
    // The host asks a player's game for fewer (that game also sends its moves and voice, and the host lets each
    // player send PacketRate packets a second).
    const int SkinWindowFromPlayer = 15;
    const float SkinAskPlayerInterval = 0.2f;
    const float SkinGiveUp = 45f;          // seconds without a new piece (the host may be getting it from a player)
    const float SkinRetry = 20f;           // after nobody had a skin, or the download stalled
    const float SkinChunkRate = 220f;      // pieces a second the host sends one client
    const float SkinChunkBurst = 60f;
    const int MaxSkinDownloads = 3;
    const int MaxKeptPacks = 24;           // packs from other games kept to pass on

    // A pack on its way here.
    class SkinDownload
    {
        public string Weapon, Skin;
        public byte[][] Pieces;            // null until the first piece says how many
        public int Received, Length;
        public float NextAsk, LastProgress;
        public List<Peer> Sources;         // host: the players' games to ask, in turn
        public int Source;
        public Peer From => Sources != null && Source < Sources.Count ? Sources[Source] : null;
    }

    readonly Dictionary<(string, string), SkinDownload> skinDownloads = new Dictionary<(string, string), SkinDownload>();
    readonly Dictionary<(string, string), float> skinRetryAt = new Dictionary<(string, string), float>();
    readonly Dictionary<(string, string), byte[]> keptPacks = new Dictionary<(string, string), byte[]>();

    // A game without graphics (the online game server) only passes skins on.
    static bool ShowsSkins => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

    void StopSkinSharing()
    {
        skinDownloads.Clear();
        skinRetryAt.Clear();
        WeaponSkins.Wanted.Clear();   // the next game's guns ask again for what they need
    }

    byte[] PackOf(string weapon, string skin) =>
        keptPacks.TryGetValue((weapon, skin), out var pack) ? pack : SkinPack.Local(WeaponData.Find(weapon), skin);

    void UpdateSkinSharing()
    {
        float now = Time.unscaledTime;
        if (ShowsSkins && (IsHost ? Peers.Count > 0 : Connected))
            foreach (var key in WeaponSkins.Wanted)
            {
                if (skinDownloads.Count >= MaxSkinDownloads) break;
                if (skinDownloads.ContainsKey(key) || (skinRetryAt.TryGetValue(key, out float retry) && now < retry)) continue;
                if (PackOf(key.Weapon, key.Skin) is byte[] pack)
                {
                    // A pack passed on earlier (or one of ours that did not load): no need to ask.
                    if (!SkinPack.Load(pack, key.Weapon, key.Skin)) skinRetryAt[key] = now + 600f;
                    break;   // Wanted changed
                }
                StartSkinDownload(key.Weapon, key.Skin, null);
            }

        foreach (var d in new List<SkinDownload>(skinDownloads.Values))
        {
            var key = (d.Weapon, d.Skin);
            if (IsHost && (d.From == null || d.From.Away || !Peers.Contains(d.From)))
            {
                NextSkinSource(d);
                continue;
            }
            if (now - d.LastProgress > SkinGiveUp)
            {
                Debug.Log($"[Skins] Gave up on {d.Weapon}/{d.Skin} for now (no answer)");
                skinDownloads.Remove(key);
                skinRetryAt[key] = now + SkinRetry;
                continue;
            }
            if (now < d.NextAsk) continue;
            d.NextAsk = now + (IsHost ? SkinAskPlayerInterval : SkinAskInterval);
            AskForSkinPieces(d);
        }
    }

    // Host: asks the players' games that may have it (whoever chose it first), except the one that wants it.
    void StartSkinDownload(string weapon, string skin, Peer except)
    {
        var key = (weapon, skin);
        var d = new SkinDownload { Weapon = weapon, Skin = skin, LastProgress = Time.unscaledTime };
        if (IsHost)
        {
            d.Sources = new List<Peer>();
            foreach (var peer in Peers)
                if (peer != except && !peer.Away && peer.SkinChoices != null
                    && peer.SkinChoices.TryGetValue(weapon, out var chosen) && chosen == skin)
                    d.Sources.Add(peer);
            foreach (var peer in Peers)
                if (peer != except && !peer.Away && !d.Sources.Contains(peer)) d.Sources.Add(peer);
            if (d.Sources.Count == 0)
            {
                skinRetryAt[key] = Time.unscaledTime + SkinRetry;
                return;
            }
        }
        skinDownloads[key] = d;
    }

    // Host: the game asked did not have it (or left): the next one, or nobody has it.
    void NextSkinSource(SkinDownload d)
    {
        d.Source++;
        d.Pieces = null;
        d.Received = 0;
        d.NextAsk = 0f;
        d.LastProgress = Time.unscaledTime;
        if (d.From != null) return;
        skinDownloads.Remove((d.Weapon, d.Skin));
        skinRetryAt[(d.Weapon, d.Skin)] = Time.unscaledTime + SkinRetry;
        Debug.Log($"[Skins] Nobody here has {d.Weapon}/{d.Skin}");
    }

    void AskForSkinPieces(SkinDownload d)
    {
        int window = IsHost ? SkinWindowFromPlayer : SkinWindow;
        var missing = new List<int>();
        if (d.Pieces != null)
            for (int i = 0; i < d.Pieces.Length && missing.Count < window; i++)
                if (d.Pieces[i] == null) missing.Add(i);
        byte[] packet = Packet(w =>
        {
            w.Write((byte)Msg.SkinWant);
            w.Write(d.Weapon);
            w.Write(d.Skin);
            w.Write((byte)missing.Count);   // none: the first pieces (how many there are is not known yet)
            foreach (int index in missing) w.Write((ushort)index);
        });
        Send(packet, IsHost ? d.From.EndPoint : hostEndPoint);
    }

    static List<int> ReadWantedPieces(System.IO.BinaryReader r, out string weapon, out string skin)
    {
        weapon = r.ReadString();
        skin = r.ReadString();
        int count = Math.Min((int)r.ReadByte(), SkinWindow);
        var wanted = new List<int>(count);
        for (int i = 0; i < count; i++) wanted.Add(r.ReadUInt16());
        if (count == 0)
            for (int i = 0; i < SkinWindow; i++) wanted.Add(i);
        return wanted;
    }

    // Host: a client wants a skin.
    void HostSkinWant(System.IO.BinaryReader r, Peer peer)
    {
        var wanted = ReadWantedPieces(r, out string weapon, out string skin);
        if (weapon.Length > 32 || skin.Length > 64) return;
        var pack = PackOf(weapon, skin);
        if (pack != null)
        {
            SendSkinPieces(weapon, skin, pack, wanted, SkinWindow, peer.EndPoint, peer);
            return;
        }
        var key = (weapon, skin);
        if (skinDownloads.ContainsKey(key)) return;   // on its way here: they ask again
        if (!(skinRetryAt.TryGetValue(key, out float retry) && Time.unscaledTime < retry)) StartSkinDownload(weapon, skin, peer);
        if (!skinDownloads.ContainsKey(key)) SendSkinPieces(weapon, skin, null, wanted, 0, peer.EndPoint, peer);   // nobody has it
    }

    // Client: the host wants one of our skins (another player does not have it).
    void ClientSkinWant(System.IO.BinaryReader r)
    {
        var wanted = ReadWantedPieces(r, out string weapon, out string skin);
        SendSkinPieces(weapon, skin, PackOf(weapon, skin), wanted, SkinWindowFromPlayer, hostEndPoint, null);
    }

    // Sends at most <most> of the wanted pieces; no pack: "I do not have it".
    void SendSkinPieces(string weapon, string skin, byte[] pack, List<int> wanted, int most, IPEndPoint to, Peer peer)
    {
        int count = pack == null ? 0 : (pack.Length + SkinPiece - 1) / SkinPiece;
        if (count == 0)
        {
            Send(SkinPiecePacket(weapon, skin, 0, 0, null, 0, 0), to);
            return;
        }
        int sent = 0;
        foreach (int index in wanted)
        {
            if (index < 0 || index >= count || sent >= most) continue;
            if (peer != null && !TakeSkinToken(peer)) break;
            int offset = index * SkinPiece;
            Send(SkinPiecePacket(weapon, skin, index, count, pack, offset, Math.Min(SkinPiece, pack.Length - offset)), to);
            sent++;
        }
    }

    static byte[] SkinPiecePacket(string weapon, string skin, int index, int count, byte[] pack, int offset, int length) => Packet(w =>
    {
        w.Write((byte)Msg.SkinChunk);
        w.Write(weapon);
        w.Write(skin);
        w.Write((ushort)index);
        w.Write((ushort)count);
        w.Write(pack != null ? pack.Length : 0);
        w.Write((ushort)length);
        if (length > 0) w.Write(pack, offset, length);
    });

    // The host sends each client at most SkinChunkRate pieces a second.
    bool TakeSkinToken(Peer peer)
    {
        float now = Time.unscaledTime;
        peer.SkinTokens = Mathf.Min(SkinChunkBurst, peer.SkinTokens + (now - peer.SkinTokensAt) * SkinChunkRate);
        peer.SkinTokensAt = now;
        if (peer.SkinTokens < 1f) return false;
        peer.SkinTokens -= 1f;
        return true;
    }

    // A piece of a pack we asked for (from the host, or on the host from the player's game it asked).
    void ReceiveSkinChunk(System.IO.BinaryReader r, Peer from)
    {
        string weapon = r.ReadString();
        string skin = r.ReadString();
        int index = r.ReadUInt16();
        int count = r.ReadUInt16();
        int total = r.ReadInt32();
        int length = r.ReadUInt16();
        var key = (weapon, skin);
        if (!skinDownloads.TryGetValue(key, out var d)) return;   // not asked for
        if (IsHost && d.From != from) return;
        float now = Time.unscaledTime;
        if (count == 0)
        {
            if (IsHost) NextSkinSource(d);
            else
            {
                skinDownloads.Remove(key);
                skinRetryAt[key] = now + SkinRetry;
            }
            return;
        }
        if (total <= 0 || total > SkinPack.MaxBytes || count != (total + SkinPiece - 1) / SkinPiece || index >= count) return;
        if (d.Pieces == null)
        {
            d.Pieces = new byte[count][];
            d.Length = total;
        }
        if (d.Pieces.Length != count || d.Length != total || d.Pieces[index] != null) return;
        if (length != Math.Min(SkinPiece, total - index * SkinPiece)) return;
        byte[] piece = r.ReadBytes(length);
        if (piece.Length != length) return;
        d.Pieces[index] = piece;
        d.Received++;
        d.LastProgress = now;
        if (d.Received < count) return;

        skinDownloads.Remove(key);
        var pack = new byte[total];
        for (int i = 0; i < count; i++) Buffer.BlockCopy(d.Pieces[i], 0, pack, i * SkinPiece, d.Pieces[i].Length);
        Debug.Log($"[Skins] Received {weapon}/{skin} ({total / 1024} KB)");
        var weaponData = WeaponData.Find(weapon);
        if (weaponData == null || (ShowsSkins && !SkinPack.Load(pack, weapon, skin) && WeaponSkins.Find(weaponData, skin) == null))
        {
            skinRetryAt[key] = now + 600f;   // a bad pack: not again soon
            return;
        }
        if (keptPacks.Count < MaxKeptPacks) keptPacks[key] = pack;   // to pass on
    }
}

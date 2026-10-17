using System;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// The ticket the online API gives a player for one room: "1|room|slot|account|nickname|team|expires|nonce" in
/// Base64url, a dot, and its HMAC-SHA256 made with GAME_JOIN_SECRET. Only the game hosts the API starts know that
/// secret, so only they can check a ticket; the player's game just passes it on in its Hello.
/// </summary>
public struct JoinTicket
{
    public string RoomId, SlotId, AccountId, Nick, Team, Nonce;
    public long Expires;   // Unix time, seconds

    public static bool TryRead(string text, string secret, out JoinTicket ticket)
    {
        ticket = default;
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(secret) || text.Length > 800) return false;
        int dot = text.IndexOf('.');
        if (dot <= 0 || dot == text.Length - 1) return false;
        string body = text.Substring(0, dot);
        byte[] given = FromBase64Url(text.Substring(dot + 1));
        if (given == null || !SameBytes(given, Sign(secret, body))) return false;
        byte[] payload = FromBase64Url(body);
        if (payload == null) return false;
        string[] parts = Encoding.UTF8.GetString(payload).Split('|');
        if (parts.Length != 8 || parts[0] != "1" || !long.TryParse(parts[6], out long expires)) return false;
        ticket = new JoinTicket
        {
            RoomId = parts[1], SlotId = parts[2], AccountId = parts[3], Nick = parts[4], Team = parts[5], Expires = expires, Nonce = parts[7],
        };
        return true;
    }

    /// <summary>What a game host sends with its reports to the API's internal address (made from the same secret).</summary>
    public static string InternalKey(string secret)
    {
        using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret)))
        {
            byte[] hash = hmac.ComputeHash(Encoding.ASCII.GetBytes("lowstrike-internal"));
            var hex = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) hex.Append(b.ToString("x2"));
            return hex.ToString();
        }
    }

    static byte[] Sign(string secret, string body)
    {
        using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret)))
            return hmac.ComputeHash(Encoding.ASCII.GetBytes(body));
    }

    static byte[] FromBase64Url(string text)
    {
        string s = text.Replace('-', '+').Replace('_', '/');
        s += new string('=', (4 - s.Length % 4) % 4);
        try
        {
            return Convert.FromBase64String(s);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    // Compares every byte, so how long it takes says nothing about where they differ.
    static bool SameBytes(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        int difference = 0;
        for (int i = 0; i < a.Length; i++) difference |= a[i] ^ b[i];
        return difference == 0;
    }
}

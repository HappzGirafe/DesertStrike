using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Player accounts, kept on this computer in accounts.json in the game's data folder (which updates leave alone):
/// each has a nickname no other account here has, a password (only a salted PBKDF2 hash of it is stored), and
/// the skins equipped with it, its friends (see <see cref="Friends"/>) and whether its online status is hidden. Logged
/// in, a player can choose the Halloween map, the RPG's Web skin and Extreme bots. The game asks to log in or sign up
/// at every start; playing as a guest stays possible.
///
/// An online account (see <see cref="OnlineClient"/>) is checked by the online server, which also keeps its skins and
/// settings. Logged in online, this file keeps a copy of it without any password (<see cref="BeginOnline"/>), so
/// its skins and LAN friends work here like a local account's.
/// </summary>
public static class Accounts
{
    [Serializable]
    class SkinChoice
    {
        public string weapon;
        public string skin;
    }

    [Serializable]
    class Friend
    {
        public string id;
        public string name;
    }

    [Serializable]
    class Account
    {
        public string id;        // random, so friends on other computers can tell two players with one nickname apart
        public string name;
        public string salt;      // Base64, 16 random bytes
        public string hash;      // Base64 PBKDF2-SHA256 of the password, 32 bytes
        public int iterations;
        public string created;   // ISO date
        public List<SkinChoice> skins = new List<SkinChoice>();
        public List<Friend> friends = new List<Friend>();
        public bool hideStatus;  // nobody sees this player online, and it sees nobody
        public bool online;      // a copy of an online account (id = its online id, no password here)
    }

    [Serializable]
    class AccountFile
    {
        public int version = 1;
        public string lastName;
        public List<Account> accounts = new List<Account>();
    }

    const int Iterations = 20000;
    public const int MinPasswordLength = 4;
    public const int MaxFriends = 50;
    static readonly Regex NamePattern = new Regex("^[A-Za-z0-9_-]{3,16}$");

    /// <summary>The logged-in account's nickname, or null for a guest.</summary>
    public static string Current => current?.name;
    public static string CurrentId => current?.id;
    public static bool LoggedIn => current != null;
    /// <summary>Logged in with an online account (not one kept only on this computer).</summary>
    public static bool IsOnline => current != null && current.online;

    /// <summary>The nickname that logged in last on this computer (filled in on the log-in screen).</summary>
    public static string LastName => Data.lastName ?? "";

    /// <summary>Raised after logging in or out.</summary>
    public static event Action Changed;

    static AccountFile data;
    static Account current;
    static string filePath;

    // -ds-accounts-file <file> keeps tests away from the player's own accounts.
    static string FilePath
    {
        get
        {
            if (filePath != null) return filePath;
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-ds-accounts-file");
            filePath = index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.Combine(Application.persistentDataPath, "accounts.json");
            return filePath;
        }
    }

    static AccountFile Data
    {
        get
        {
            if (data == null) Load();
            return data;
        }
    }

    static void Load()
    {
        data = new AccountFile();
        try
        {
            if (File.Exists(FilePath)) data = JsonUtility.FromJson<AccountFile>(File.ReadAllText(FilePath)) ?? new AccountFile();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Accounts] Could not read " + FilePath + ": " + e.Message);
        }
        if (data.accounts == null) data.accounts = new List<Account>();
        // Accounts from before friends get their id now.
        bool added = false;
        foreach (var account in data.accounts)
        {
            if (account.friends == null) account.friends = new List<Friend>();
            if (string.IsNullOrEmpty(account.id))
            {
                account.id = NewId();
                added = true;
            }
        }
        if (added) Save();
    }

    static string NewId()
    {
        var bytes = new byte[6];
        using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
        return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }

    public static bool ValidName(string name) => NamePattern.IsMatch(name ?? "");

    static bool Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            // Written next to it first, then swapped in, so a crash never leaves half a file.
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(Data, true));
            if (File.Exists(FilePath)) File.Delete(FilePath);
            File.Move(temp, FilePath);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Accounts] Could not save " + FilePath + ": " + e.Message);
            return false;
        }
    }

    // A local account first: an online account's copy may have the same nickname.
    static Account Find(string name) =>
        Data.accounts.Find(a => !a.online && string.Equals(a.name, name?.Trim(), StringComparison.OrdinalIgnoreCase)) ??
        Data.accounts.Find(a => string.Equals(a.name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Why a nickname cannot be used for a new account, or null when it can.</summary>
    public static string NameProblem(string name)
    {
        name = name?.Trim() ?? "";
        if (!NamePattern.IsMatch(name)) return "Nickname: 3 to 16 letters, digits, _ or -";
        if (Find(name) != null) return "This nickname is already taken";
        if (GameManager.IsBotName(name)) return "This nickname belongs to a bot";
        return null;
    }

    /// <summary>Creates an account and logs in with it; returns what is wrong, or null on success. The skins
    /// equipped as a guest come along.</summary>
    public static string SignUp(string name, string password, string repeat)
    {
        name = name?.Trim() ?? "";
        string problem = NameProblem(name);
        if (problem != null) return problem;
        if ((password ?? "").Length < MinPasswordLength) return $"Password: at least {MinPasswordLength} characters";
        if (password != repeat) return "The two passwords are not the same";

        var salt = new byte[16];
        using (var random = RandomNumberGenerator.Create()) random.GetBytes(salt);
        var account = new Account
        {
            id = NewId(),
            name = name,
            salt = Convert.ToBase64String(salt),
            hash = Convert.ToBase64String(Hash(password, salt, Iterations)),
            iterations = Iterations,
            created = DateTime.UtcNow.ToString("yyyy-MM-dd"),
        };
        foreach (var pair in WeaponSkins.GuestChoices()) account.skins.Add(new SkinChoice { weapon = pair.Key, skin = pair.Value });
        Data.accounts.Add(account);
        if (!Save())
        {
            Data.accounts.Remove(account);
            return "Could not save the account on this computer";
        }
        Begin(account);
        Debug.Log($"[Accounts] Account {name} created");
        return null;
    }

    /// <summary>Logs in; returns what is wrong, or null on success.</summary>
    public static string LogIn(string name, string password)
    {
        var account = Find(name);
        if (account != null && account.online) return "This is an online account: choose ONLINE above to log in";
        if (account == null || string.IsNullOrEmpty(password)) return "Wrong nickname or password";
        byte[] expected, salt;
        try
        {
            expected = Convert.FromBase64String(account.hash);
            salt = Convert.FromBase64String(account.salt);
        }
        catch (FormatException)
        {
            return "This account's file is damaged";
        }
        if (!SameBytes(Hash(password, salt, account.iterations > 0 ? account.iterations : Iterations), expected))
            return "Wrong nickname or password";
        Begin(account);
        Debug.Log($"[Accounts] {account.name} logged in");
        return null;
    }

    public static void LogOut()
    {
        if (current == null) return;
        current = null;
        Changed?.Invoke();
    }

    /// <summary>Logged in online: uses this computer's copy of the account (made the first time), with the skins the
    /// server keeps for it (when it keeps any) and its online status setting.</summary>
    public static void BeginOnline(string id, string name, List<(string Weapon, string Skin)> skins, bool hideStatus)
    {
        var account = Data.accounts.Find(a => a.online && a.id == id);
        if (account == null)
        {
            account = new Account { id = id, online = true, created = DateTime.UtcNow.ToString("yyyy-MM-dd") };
            Data.accounts.Add(account);
        }
        account.name = name;
        account.salt = account.hash = "";
        if (skins != null && skins.Count > 0) SetSkins(account, skins);
        account.hideStatus = hideStatus;
        Begin(account);
        Debug.Log($"[Accounts] {name} logged in online");
    }

    /// <summary>The online account was deleted: its copy on this computer goes as well.</summary>
    public static void RemoveOnlineCopy(string id)
    {
        if (id == null || Data.accounts.RemoveAll(a => a.online && a.id == id) == 0) return;
        if (current != null && current.online && current.id == id) current = null;
        Save();
        Changed?.Invoke();
    }

    /// <summary>The skins equipped with the logged-in account (weapon id, skin id).</summary>
    public static List<(string Weapon, string Skin)> Skins() =>
        current == null ? new List<(string, string)>() : current.skins.ConvertAll(s => (s.weapon, s.skin));

    /// <summary>The skins of a local (not online) account on this computer, or the guest's choices for null.</summary>
    public static List<(string Weapon, string Skin)> LocalSkins(string name)
    {
        if (name == null)
        {
            var list = new List<(string, string)>();
            foreach (var pair in WeaponSkins.GuestChoices()) list.Add((pair.Key, pair.Value));
            return list;
        }
        var account = Data.accounts.Find(a => !a.online && string.Equals(a.name, name, StringComparison.OrdinalIgnoreCase));
        return account == null ? new List<(string, string)>() : account.skins.ConvertAll(s => (s.weapon, s.skin));
    }

    /// <summary>The local (not online) account used last on this computer, or null.</summary>
    public static string LastLocalName =>
        Data.accounts.Find(a => !a.online && string.Equals(a.name, Data.lastName, StringComparison.OrdinalIgnoreCase))?.name
        ?? Data.accounts.Find(a => !a.online)?.name;

    /// <summary>Replaces the logged-in account's skins (online: what the server keeps, or what the player brought along).</summary>
    public static void ReplaceSkins(List<(string Weapon, string Skin)> skins)
    {
        if (current == null) return;
        SetSkins(current, skins);
        Save();
        Changed?.Invoke();
    }

    static void SetSkins(Account account, List<(string Weapon, string Skin)> skins)
    {
        account.skins = new List<SkinChoice>();
        foreach (var (weapon, skin) in skins)
            if (!string.IsNullOrEmpty(weapon) && !string.IsNullOrEmpty(skin)) account.skins.Add(new SkinChoice { weapon = weapon, skin = skin });
    }

    static void Begin(Account account)
    {
        current = account;
        Data.lastName = account.name;
        Save();
        Changed?.Invoke();
    }

    static byte[] Hash(string password, byte[] salt, int iterations)
    {
        using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256))
            return pbkdf2.GetBytes(32);
    }

    // Compares every byte, so how long it takes says nothing about where they differ.
    static bool SameBytes(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        int difference = 0;
        for (int i = 0; i < a.Length; i++) difference |= a[i] ^ b[i];
        return difference == 0;
    }

    // ----------------------------------------------------------------- the account's skins

    /// <summary>The skin id equipped on a weapon with this account, or null (the weapon's default).</summary>
    public static string EquippedSkin(string weaponId) => current?.skins.Find(s => s.weapon == weaponId)?.skin;

    public static void SetEquippedSkin(string weaponId, string skinId)
    {
        if (current == null) return;
        var choice = current.skins.Find(s => s.weapon == weaponId);
        if (choice == null) current.skins.Add(new SkinChoice { weapon = weaponId, skin = skinId });
        else choice.skin = skinId;
        Save();
    }

    // ----------------------------------------------------------------- the account's friends

    /// <summary>The logged-in account's friends (id, nickname); empty for a guest.</summary>
    public static List<(string Id, string Name)> FriendList() =>
        current == null ? new List<(string, string)>() : current.friends.ConvertAll(f => (f.id, f.name));

    public static bool IsFriend(string id) => current != null && current.friends.Exists(f => f.id == id);

    public static string FriendIdNamed(string name) =>
        current?.friends.Find(f => string.Equals(f.name, name?.Trim(), StringComparison.OrdinalIgnoreCase))?.id;

    /// <summary>Adds a friend (or renames one already there); false when the list is full.</summary>
    public static bool AddFriend(string id, string name)
    {
        if (current == null || string.IsNullOrEmpty(id) || id == current.id) return false;
        var friend = current.friends.Find(f => f.id == id);
        if (friend == null)
        {
            if (current.friends.Count >= MaxFriends) return false;
            current.friends.Add(new Friend { id = id, name = name });
        }
        else friend.name = name;
        Save();
        return true;
    }

    public static void RemoveFriend(string id)
    {
        if (current == null || current.friends.RemoveAll(f => f.id == id) == 0) return;
        Save();
    }

    /// <summary>Hidden: friends do not see this player online, and the player does not see them.</summary>
    public static bool HideStatus => current != null && current.hideStatus;

    public static void SetHideStatus(bool hide)
    {
        if (current == null || current.hideStatus == hide) return;
        current.hideStatus = hide;
        Save();
    }
}

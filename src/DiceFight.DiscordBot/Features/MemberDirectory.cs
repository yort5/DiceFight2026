using System.Collections.Concurrent;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DiceFight.DiscordBot.Features;

// Who the members of a server really are (2026-11-23, user, for the church
// bot: "get their real name and set that as their server nickname, so
// people can know who we are... collect their email and do a verification
// code to validate that they are who they say they are, so that they could
// then potentially access private channels"). One directory per server,
// one entry per member. The rules, apart from Discord.
public sealed partial class MemberDirectory
{
    public List<MemberInfo> Members { get; set; } = [];

    public sealed class MemberInfo
    {
        public ulong UserId { get; set; }
        // Their Discord username, so the saved file is readable on its own.
        public string Username { get; set; } = "";
        public string RealName { get; set; } = "";
        // The address they last gave, and the one they proved is theirs
        // (they differ while a change of address waits for its code).
        public string? Email { get; set; }
        public string? VerifiedEmail { get; set; }
        public DateTime? VerifiedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public bool Verified => VerifiedEmail is not null;
    }

    public MemberInfo? Find(ulong userId) => Members.FirstOrDefault(m => m.UserId == userId);

    // Saves what they entered. Already-cleaned values (CleanName, CleanEmail).
    public MemberInfo Introduce(ulong userId, string username, string realName, string? email, DateTime now)
    {
        var member = Find(userId);
        if (member is null)
        {
            member = new MemberInfo { UserId = userId };
            Members.Add(member);
        }
        member.Username = username;
        member.RealName = realName;
        member.Email = email;
        member.UpdatedAt = now;
        return member;
    }

    public bool MarkVerified(ulong userId, string email, DateTime now)
    {
        if (Find(userId) is not { } member) return false;
        member.VerifiedEmail = email;
        member.VerifiedAt = now;
        member.UpdatedAt = now;
        return true;
    }

    // Discord nicknames are 1-32 characters.
    public const int MaxNameLength = 32;

    // A real name as typed, tidied ("  troy   smith " -> "troy smith"), or why it won't do.
    public static (string? Name, string? Problem) CleanName(string? typed)
    {
        var name = Spaces().Replace(typed ?? "", " ").Trim();
        if (name.Length < 2) return (null, "Please enter your first and last name.");
        if (name.Length > MaxNameLength) return (null, $"Discord nicknames can be at most {MaxNameLength} characters - try a shorter form of your name.");
        if (name.Any(char.IsControl)) return (null, "That name has characters Discord won't show.");
        return (name, null);
    }

    // An email address as typed, lower-cased, or why it won't do. Blank is "no email".
    public static (string? Email, string? Problem) CleanEmail(string? typed)
    {
        var email = (typed ?? "").Trim();
        if (email.Length == 0) return (null, null);
        if (email.Length > 254 || !MailAddress.TryCreate(email, out var address) || address.Address != email
            || !address.Host.Contains('.') || email.Contains(' '))
            return (null, $"\"{email}\" doesn't look like an email address.");
        return (email.ToLowerInvariant(), null);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}

// Codes sent by email, waiting to be typed back. Kept in memory only - a
// restart just means asking for a new code.
public sealed class VerificationCodes(TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan ResendAfter = TimeSpan.FromSeconds(60);
    public const int MaxTries = 5;

    private sealed record Pending(string Email, string Code, DateTimeOffset SentAt, int Tries);

    public enum Check { Ok, Wrong, Expired, NoCode, TooManyTries }

    private readonly ConcurrentDictionary<(ulong Guild, ulong User), Pending> _pending = new();

    // A fresh code for this person and address, or how long until they may
    // ask again (so nobody can make the bot spam an inbox).
    public (string? Code, TimeSpan Wait) Issue(ulong guildId, ulong userId, string email)
    {
        var now = clock.GetUtcNow();
        if (_pending.TryGetValue((guildId, userId), out var last) && now - last.SentAt < ResendAfter)
            return (null, ResendAfter - (now - last.SentAt));
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        _pending[(guildId, userId)] = new Pending(email, code, now, 0);
        return (code, TimeSpan.Zero);
    }

    // The address a code is waiting for, if one is.
    public string? PendingEmail(ulong guildId, ulong userId) =>
        _pending.TryGetValue((guildId, userId), out var p) && clock.GetUtcNow() - p.SentAt <= Lifetime ? p.Email : null;

    // Whether `typed` is their code; on Ok, the address it proves.
    public (Check Result, string? Email) Try(ulong guildId, ulong userId, string? typed)
    {
        var key = (guildId, userId);
        if (!_pending.TryGetValue(key, out var pending)) return (Check.NoCode, null);
        if (clock.GetUtcNow() - pending.SentAt > Lifetime)
        {
            _pending.TryRemove(key, out _);
            return (Check.Expired, null);
        }
        if (pending.Tries >= MaxTries) return (Check.TooManyTries, null);
        var digits = new string((typed ?? "").Where(char.IsDigit).ToArray());
        if (!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(digits), System.Text.Encoding.ASCII.GetBytes(pending.Code)))
        {
            _pending[key] = pending with { Tries = pending.Tries + 1 };
            return (pending.Tries + 1 >= MaxTries ? Check.TooManyTries : Check.Wrong, null);
        }
        _pending.TryRemove(key, out _);
        return (Check.Ok, pending.Email);
    }
}

// Each server's directory, saved under discord/members/<server id>.json.
public sealed class MemberDirectoryStore(IBotDataStore store)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly ConcurrentDictionary<string, MemberDirectory> _loaded = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();

    public bool Durable => store.Durable;

    public static string FileName(string directoryId) => $"discord/members/{directoryId}.json";

    // Runs `use` on the directory and saves it if it says so.
    public async Task<T> UseAsync<T>(string directoryId, Func<MemberDirectory, (T Result, bool Changed)> use)
    {
        var gate = _gates.GetOrAdd(directoryId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (!_loaded.TryGetValue(directoryId, out var directory))
            {
                var data = await store.ReadAsync(FileName(directoryId));
                directory = data is null ? new MemberDirectory() : JsonSerializer.Deserialize<MemberDirectory>(data, Json) ?? new MemberDirectory();
                _loaded[directoryId] = directory;
            }
            var (result, changed) = use(directory);
            if (changed) await store.WriteAsync(FileName(directoryId), JsonSerializer.SerializeToUtf8Bytes(directory, Json));
            return result;
        }
        finally
        {
            gate.Release();
        }
    }
}

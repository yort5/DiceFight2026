using System.Text.RegularExpressions;

namespace DiceFight.Engine.TeamBuilding;

// Shared logic for the team-share URL format also implemented in
// web/src/TeamBuilderPage.tsx (encodeTeam/decodeTeam/toOurId) - kept as a
// second, independent port rather than shared across languages, but the
// two must stay in sync by hand; see that file's own comments for the
// origin of the format (matching the old community Teambuilder tool's
// own `?cards=<count>x<slug>;...` style so real links people already made
// with it still resolve).
public static partial class TeamLinkCodec
{
    // The old tool's slug is "<1-based position within its per-set array>
    // <lowercase set code>" e.g. "18msw" for the 18th card in MSW.
    // Verified (see DESIGN_LOG.md) that this position lines up exactly
    // with our own sheet-derived SET+number ids, so this is a pure string
    // transform, not a lookup table.
    [GeneratedRegex(@"^(\d+)([a-zA-Z]+)$")]
    private static partial Regex OldSlugRegex();

    // "<count>x<id>" - matched with a regex rather than Split('x'), since
    // some set codes contain their own "x" (AvX, XFC, XMF, XFO) that a
    // naive split would cut on too.
    [GeneratedRegex(@"^(\d+)x(.+)$")]
    private static partial Regex TeamEntryRegex();

    // Already one of our own ids (or unresolvable - caller drops it) if
    // this doesn't match.
    public static string ToOurId(string rawId)
    {
        var match = OldSlugRegex().Match(rawId);
        if (!match.Success)
        {
            return rawId;
        }

        var number = match.Groups[1].Value;
        var setCode = match.Groups[2].Value;
        return $"{setCode.ToUpperInvariant()}{number.PadLeft(3, '0')}";
    }

    // Accepts either a full URL or a bare query string; reads the "cards"
    // param either way. Malformed entries are skipped, not thrown on.
    public static IReadOnlyList<(string CardId, int Count)> Decode(string urlOrQuery)
    {
        var raw = ExtractCardsParam(urlOrQuery);

        var team = new List<(string CardId, int Count)>();
        if (string.IsNullOrEmpty(raw))
        {
            return team;
        }

        foreach (var entry in raw.Split(';'))
        {
            var match = TeamEntryRegex().Match(entry);
            if (!match.Success)
            {
                continue;
            }

            if (!int.TryParse(match.Groups[1].Value, out var count) || count <= 0)
            {
                continue;
            }

            team.Add((ToOurId(match.Groups[2].Value), count));
        }

        return team;
    }

    public static string Encode(IReadOnlyList<(string CardId, int Count)> team) =>
        string.Join(';', team.Select(entry => $"{entry.Count}x{entry.CardId}"));

    // No dependency on System.Web/ASP.NET Core query-string helpers here -
    // this is a plain class library, and the "cards" value never contains
    // characters that need escaping in practice (letters, digits, "x",
    // ";"), so a minimal manual parse avoids pulling in a web framework
    // reference just for this.
    private static string? ExtractCardsParam(string urlOrQuery)
    {
        var queryStart = urlOrQuery.IndexOf('?');
        var query = queryStart >= 0 ? urlOrQuery[(queryStart + 1)..] : urlOrQuery;

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var key = eq >= 0 ? pair[..eq] : pair;
            if (key != "cards")
            {
                continue;
            }

            var value = eq >= 0 ? pair[(eq + 1)..] : string.Empty;
            return Uri.UnescapeDataString(value);
        }

        return null;
    }
}

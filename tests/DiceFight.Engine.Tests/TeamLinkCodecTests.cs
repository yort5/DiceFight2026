using DiceFight.Engine.TeamBuilding;
using Xunit;

namespace DiceFight.Engine.Tests;

public class TeamLinkCodecTests
{
    [Theory]
    [InlineData("1msw", "MSW001")]
    [InlineData("18msw", "MSW018")]
    [InlineData("1skc", "SKC001")]
    [InlineData("1bat", "BAT001")]
    public void ToOurId_TranslatesOldStyleSlugs(string oldSlug, string expected)
    {
        Assert.Equal(expected, TeamLinkCodec.ToOurId(oldSlug));
    }

    [Fact]
    public void ToOurId_PassesThroughOurOwnIds()
    {
        Assert.Equal("MSW018", TeamLinkCodec.ToOurId("MSW018"));
    }

    [Fact]
    public void Decode_TranslatesOldStyleLinkEntries()
    {
        var team = TeamLinkCodec.Decode("?cards=1x1msw;2x4msw;1x1skc");

        Assert.Equal(
            [("MSW001", 1), ("MSW004", 2), ("SKC001", 1)],
            team);
    }

    [Fact]
    public void Decode_HandlesSetCodesContainingX()
    {
        // Old-style slug "4xfc" = 4th card in the XFC set. Full entry is
        // "1x4xfc" (count=1, rawId="4xfc") - a naive entry.split("x")
        // would wrongly cut this into three pieces ("1", "4", "fc")
        // instead of two; the anchored TeamEntryRegex must instead see
        // count=1, rawId="4xfc", which ToOurId then resolves to XFC004.
        var team = TeamLinkCodec.Decode("?cards=1x4xfc");

        Assert.Equal([("XFC004", 1)], team);
    }

    [Fact]
    public void Decode_AcceptsOurOwnIdsDirectly()
    {
        var team = TeamLinkCodec.Decode("?cards=3xMSW018");

        Assert.Equal([("MSW018", 3)], team);
    }

    [Fact]
    public void Decode_SkipsMalformedEntriesInsteadOfThrowing()
    {
        var team = TeamLinkCodec.Decode("?cards=not-an-entry;2xMSW018;0xMSW002;-1xMSW003");

        Assert.Equal([("MSW018", 2)], team);
    }

    [Fact]
    public void Decode_ReturnsEmptyWhenNoCardsParam()
    {
        Assert.Empty(TeamLinkCodec.Decode("https://example.com/teambuilder"));
    }

    [Fact]
    public void Decode_AcceptsBareQueryStringAsWellAsFullUrl()
    {
        var fromUrl = TeamLinkCodec.Decode("https://example.com/teambuilder?cards=2xMSW018");
        var fromQuery = TeamLinkCodec.Decode("cards=2xMSW018");

        Assert.Equal(fromQuery, fromUrl);
        Assert.Equal([("MSW018", 2)], fromUrl);
    }

    [Fact]
    public void Encode_DecodeRoundTrips()
    {
        var original = new List<(string CardId, int Count)> { ("MSW018", 4), ("SKC001", 1) };

        var encoded = TeamLinkCodec.Encode(original);
        var decoded = TeamLinkCodec.Decode($"?cards={encoded}");

        Assert.Equal(original, decoded);
    }
}

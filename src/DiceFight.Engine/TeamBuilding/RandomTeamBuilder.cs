using DiceFight.Engine.Model;

namespace DiceFight.Engine.TeamBuilding;

// Generates a legal-ish opposing roster for "start a game against a
// random team" (GamesController.Create) - picks from IsImplemented
// catalog cards only, so the AI-less opponent never rolls a die with no
// scripted behavior. Mirrors the same team-construction shape the web
// Team Builder's own "Strict rules" checkbox enforces (rule 2.1.1/2.1.3-
// 2.1.5: up to 8 unique-named Character/Action cards summing to at most
// 20 dice, exactly 2 Basic Action cards) - see TeamBuilderPage.tsx's
// MAX_UNIQUE_CARDS/MAX_DICE/MAX_BASIC_ACTIONS. Like TeamSetup itself,
// this counts each card's full DieLimit toward the 20-dice cap rather
// than a partial per-card count - Player.TeamCardIds has no concept of
// "how many of this card's dice" today (see TeamSetup.cs's own remarks),
// so a random team can't ask for fewer than the max either.
public static class RandomTeamBuilder
{
    private const int MaxUniqueCards = 8;
    private const int MaxDice = 20;
    private const int MaxBasicActions = 2;

    public static List<string> Build(IReadOnlyDictionary<string, CardDef> catalog, Random random)
    {
        var implemented = catalog.Values.Where(c => c.IsImplemented && c.Type != CardType.Token).ToList();

        var characterPool = Shuffle(implemented.Where(IsCharacterOrAction).ToList(), random);
        var basicActionPool = Shuffle(implemented.Where(IsBasicActionFamily).ToList(), random);

        var team = new List<string>();
        var usedNames = new HashSet<string>();
        var diceUsed = 0;
        foreach (var card in characterPool)
        {
            if (usedNames.Count >= MaxUniqueCards) break;
            if (usedNames.Contains(card.Name)) continue; // rule 2.1.1 - no duplicate-named cards
            if (diceUsed + card.DieLimit > MaxDice) continue;

            team.Add(card.Id);
            usedNames.Add(card.Name);
            diceUsed += card.DieLimit;
        }

        team.AddRange(basicActionPool.Take(MaxBasicActions).Select(c => c.Id));
        return team;
    }

    private static bool IsCharacterOrAction(CardDef card) => card.Type is CardType.Character or CardType.Action;

    private static bool IsBasicActionFamily(CardDef card) =>
        card.Type is CardType.BasicAction or CardType.EpicBasicAction;

    private static List<CardDef> Shuffle(List<CardDef> cards, Random random)
    {
        for (var i = cards.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (cards[i], cards[j]) = (cards[j], cards[i]);
        }
        return cards;
    }
}

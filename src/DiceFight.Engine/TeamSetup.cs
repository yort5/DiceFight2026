using DiceFight.Engine.Model;

namespace DiceFight.Engine;

// Rule 2.1 - Set Up. Instantiates the dice sitting unpurchased on cards
// (Zone.Unpurchased), separate from the Sidekick dice GameState.NewGame
// seeds directly into the bag.
//
// Two kinds of card, set up differently:
//
//   Character/Action cards belong to the player who brought them. Each
//   gets its own Die Limit in dice (rule 2.1.3), and only that player may
//   purchase them (rule 2.6.2.2).
//
//   Basic Action cards are COMMUNITY PROPERTY (rule 2.1.2) - they sit in
//   the centre of the table and either player may purchase from them, as
//   TurnEngine.Purchase already allows. So they are instantiated ONCE per
//   distinct card across both teams, not once per player: if both players
//   bring the same Basic Action, that is one card in the centre with one
//   set of dice to contend over, not two independent piles.
//
// NOTE: rule 2.1.3's 20-dice team cap and 2.1.1's "up to 8 unique cards"
// are team-*construction* legality rules (whether a given TeamCardIds list
// is even a legal team to bring), not something this instantiation step
// should silently enforce - truncating dice for cards near the end of the
// list would produce cards that exist on the roster but can never be
// purchased, with no visible error. That validation belongs in a separate
// team-builder/validator, not built yet.
public static class TeamSetup
{
    // Rule 1.2.11 - "All Basic Action cards have a 'Use 3' die limit. Each
    // Basic Action card will always use this fixed number of Basic Action
    // dice in every game." Fixed here rather than trusted from
    // CardDef.DieLimit, which is imported reference data and has 14 Basic
    // Actions recorded as 1.
    public const int BasicActionDiceCount = 3;

    public static void SetupDice(GameState state, IReadOnlyDictionary<string, CardDef> catalog)
    {
        var communityCardsDone = new HashSet<string>();

        foreach (var player in new[] { state.PlayerOne, state.PlayerTwo })
        {
            foreach (var cardId in player.TeamCardIds)
            {
                if (!catalog.TryGetValue(cardId, out var card)) continue;

                var isCommunity = card.Type is CardType.BasicAction or CardType.EpicBasicAction;
                if (isCommunity && !communityCardsDone.Add(cardId)) continue;

                var dieCount = isCommunity ? BasicActionDiceCount : card.DieLimit;
                for (var i = 0; i < dieCount; i++)
                {
                    state.Dice.Add(new DieInstance
                    {
                        Id = $"{player.Id}-{cardId}-{i + 1}",
                        CardId = cardId,
                        // Rule 1.1.4 - Owner is whoever brought the card,
                        // and stays so even for a community Basic Action
                        // the other player buys; Controller moves to the
                        // purchaser (see TurnEngine.Purchase).
                        OwnerId = player.Id,
                        ControllerId = player.Id,
                        Zone = Zone.Unpurchased
                    });
                }
            }
        }
    }

    /// <summary>
    /// True for a card that sits in the centre of the table rather than on
    /// either player's roster (rule 2.1.2).
    /// </summary>
    public static bool IsCommunityCard(CardDef card) =>
        card.Type is CardType.BasicAction or CardType.EpicBasicAction;
}

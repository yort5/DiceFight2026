using DiceFight.Engine.Model;

namespace DiceFight.Engine;

// Rule 2.1 - Set Up. Instantiates each player's Character/Action/Basic
// Action dice sitting unpurchased on their cards (Zone.Unpurchased),
// separate from the Sidekick dice GameState.NewGame seeds directly into
// the bag.
//
// NOTE: rule 2.1.3's 20-dice team cap and 2.1.1's "up to 8 unique cards"
// are team-*construction* legality rules (whether a given TeamCardIds list
// is even a legal team to bring), not something this instantiation step
// should silently enforce - truncating dice for cards near the end of the
// list would produce cards that exist on the roster but can never be
// purchased, with no visible error. That validation belongs in a separate
// team-builder/validator, not built yet; every card here gets its full
// Die Limit in dice regardless of team size.
public static class TeamSetup
{
    public static void SetupTeamDice(GameState state, Player player, IReadOnlyDictionary<string, CardDef> catalog)
    {
        foreach (var cardId in player.TeamCardIds)
        {
            if (!catalog.TryGetValue(cardId, out var card)) continue;

            for (var i = 0; i < card.DieLimit; i++)
            {
                state.Dice.Add(new DieInstance
                {
                    Id = $"{player.Id}-{cardId}-{i + 1}",
                    CardId = cardId,
                    OwnerId = player.Id,
                    ControllerId = player.Id,
                    Zone = Zone.Unpurchased
                });
            }
        }
    }
}

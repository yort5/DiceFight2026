using DiceFight.V2.Model;

namespace DiceFight.V2;

// Rule 2.1 - Set Up. Callers build each Player with TeamCardIds already
// populated (matching v1's TeamSetup.SetupTeamDice reading it pre-set).
// Team-construction legality (die-limit-sum-to-20, unique-card-name
// checks) is NOT enforced here, same deliberate scope line v1's own
// TeamSetup drew - that belongs to a separate team-builder/validator, not
// game setup; every card gets its full DieLimit regardless of team size.
public static class GameSetup
{
    public static GameState NewGame(
        GameConfig config,
        IReadOnlyDictionary<string, CardDef> catalog,
        Player playerOne,
        Player playerTwo)
    {
        var state = new GameState
        {
            Config = config,
            CardCatalog = catalog,
            PlayerOne = playerOne,
            PlayerTwo = playerTwo,
            ActivePlayerId = playerOne.Id,
        };

        playerOne.Life = config.Rules.StartingLife;
        playerTwo.Life = config.Rules.StartingLife;

        foreach (var player in new[] { playerOne, playerTwo })
        {
            SeedBasicDicePool(state, player);
            SeedTeamDice(state, player, catalog);
        }

        // Phase 6 - compile every card's Continuous defs into the
        // registries QueryEngine/EffectInterpreter read. Once per game,
        // here, not per-card-load, since the modifier objects themselves
        // are what re-evaluate "is the source active" live on every query
        // (ContinuousRegistry's own remarks).
        ContinuousRegistry.RegisterAll(state);

        return state;
    }

    // This is where Direction C's "8 identical Sidekick dice -> two 4-die
    // sets" becomes pure data (ARCHITECTURE_REVIEW.md Part 3) - the loop
    // just walks however many BasicDicePoolEntry rows the config declares.
    private static void SeedBasicDicePool(GameState state, Player player)
    {
        foreach (var entry in state.Config.BasicDicePool)
        {
            for (var i = 0; i < entry.Count; i++)
            {
                state.Dice.Add(new DieInstance
                {
                    Id = $"{player.Id}-{entry.Die.Id}-{i}",
                    PoolDieId = entry.Die.Id,
                    OwnerId = player.Id,
                    ControllerId = player.Id,
                    Zone = Zone.Bag,
                });
            }
        }
    }

    // Rule 2.1.3 - each team's Character/Action/Basic Action dice sit
    // unpurchased on their cards until bought (v1's TeamSetup.SetupTeamDice,
    // same shape).
    private static void SeedTeamDice(GameState state, Player player, IReadOnlyDictionary<string, CardDef> catalog)
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
                    Zone = Zone.Unpurchased,
                });
            }
        }
    }
}

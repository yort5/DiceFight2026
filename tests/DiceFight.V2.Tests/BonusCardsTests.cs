using DiceFight.V2.Data;
using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Tests;

// BonusCards.cs - cards migrated one-off, outside the DPS batch sweep.
// Same real-firing-path convention as DpsCardsTests.cs (ground rule 6).
public class BonusCardsTests
{
    private sealed class FixedRoller(int index) : IDiceRoller
    {
        public int Roll(DieDefinition die) => index;
    }

    private static GameState NewGame()
    {
        var catalog = BonusCards.All.ToDictionary(c => c.Id);
        var state = GameSetup.NewGame(DiceFightClassicConfig.Config, catalog,
            new Player { Id = "p1", Name = "One" }, new Player { Id = "p2", Name = "Two" });
        state.CurrentStep = TurnStep.Main;
        return state;
    }

    // Energy faces (0-2) come before character faces (Domino's die is the
    // real six MigrationDice.Character builds) - index 0 is a double.
    private static DieInstance Energized(GameState state, CardDef card, string controllerId)
    {
        var die = new DieInstance { Id = $"{controllerId}-{card.Id}-energized", CardId = card.Id, OwnerId = controllerId, ControllerId = controllerId, Zone = Zone.ReservePool, CurrentFaceIndex = 0 };
        state.Dice.Add(die);
        return die;
    }

    private static void FireEnergize(GameState state, AbilityQueue queue) =>
        EventBus.Fire(state, queue, new GameEvent(TriggerKind.TurnStepEntered, null, state.ActivePlayerId, StepIds.Main));

    [Fact]
    public void The_Bonus_Catalog_Is_Valid_Against_The_Classic_Config()
    {
        Assert.Empty(DiceFightClassicConfig.Config.ValidateCatalog([.. BonusCards.All]));
    }

    [Fact]
    public void Domino_Energize_Deals_1_To_The_Opponent_And_Rerolls_Herself()
    {
        var state = NewGame();
        var domino = Energized(state, BonusCards.DominoNotReallyAPartyGirl, "p1");
        var queue = new AbilityQueue();

        FireEnergize(state, queue);
        // Roll to index 2 (single energy), not another double - a reroll
        // landing back on double energy now correctly re-triggers
        // Energize immediately (Part 30), which a real random roller
        // just makes an increasingly-unlikely chain but this FixedRoller
        // would loop forever.
        EffectInterpreter.DrainQueue(state, queue, new FixedRoller(2), new Random(1));

        Assert.Equal(19, state.PlayerTwo.Life); // no target choice - "opponent" is the only candidate
        Assert.Equal(2, domino.CurrentFaceIndex); // rerolled, off double energy
    }
}

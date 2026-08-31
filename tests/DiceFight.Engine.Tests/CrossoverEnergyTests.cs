using DiceFight.Engine;
using DiceFight.Engine.Model;
using Xunit;

namespace DiceFight.Engine.Tests;

// Crossover characters - "two or more types of energy. At least one of
// each type of energy they require must be spent to purchase these
// Character dice" (Crossover glossary entry).
//
// Their double-energy face is one of EACH type rather than two of one,
// and clause 2 of that entry says half-spending it spins the die down to
// its single face - which for a Crossover is GENERIC, and for a card
// costing all four types is WILD. That is a real decision: spending half
// a four-energy die is a way to turn it into a wild.
public class CrossoverEnergyTests
{
    private static CardDef Crossover(string id, string name, params EnergyType[] types) => new()
    {
        Id = id, Name = name, Type = CardType.Character, PurchaseCost = 3, DieLimit = 4,
        EnergyTypes = types,
        Levels = [new CharacterFace(0, 1, 1), new CharacterFace(1, 2, 2), new CharacterFace(1, 3, 3)],
    };

    private static readonly CardDef Target = new()
    {
        Id = "target", Name = "Target", Type = CardType.Character, PurchaseCost = 1, DieLimit = 4,
        EnergyTypes = [EnergyType.Fist],
        Levels = [new CharacterFace(0, 1, 1), new CharacterFace(1, 2, 2), new CharacterFace(1, 3, 3)],
    };

    private static (GameState State, DieInstance Split) GameWithSplitDie(params EnergyType[] types)
    {
        var cross = Crossover("cross", types.Length >= 4 ? "White Lantern Aquaman" : "Cross", types);
        var p1 = new Player { Id = "p1", Name = "P1" };
        var p2 = new Player { Id = "p2", Name = "P2" };
        p1.TeamCardIds.AddRange([cross.Id, Target.Id]);
        var state = GameState.NewGame(
            new Dictionary<string, CardDef> { [cross.Id] = cross, [Target.Id] = Target }, p1, p2);
        // Roll one of its dice onto its split double, through the real
        // roll path, so the face is one DieFaces actually produces.
        state.CurrentStep = TurnStep.RollAndReroll;
        var die = state.DiceIn("p1", Zone.Unpurchased).First(d => d.CardId == cross.Id);
        die.Zone = Zone.DiceFromBag;
        TurnEngine.Roll(state, FaceRoller.Energy(EnergyKind.Specific, types[0], amount: 2));
        state.CurrentStep = TurnStep.Main;
        return (state, die);
    }

    [Fact]
    public void SplitDouble_ProvidesOneOfEachType()
    {
        var (_, die) = GameWithSplitDie(EnergyType.Fist, EnergyType.Mask);

        Assert.Equal(2, die.EnergyAmount);
        Assert.Equal(EnergyType.Fist, die.ProvidedEnergyType);
        Assert.Equal(EnergyType.Mask, die.SecondProvidedEnergyType);
    }

    // Both halves spent on a card needing both types - one die satisfies
    // two requirements, because it really is two different energies.
    [Fact]
    public void SplitDouble_SpentInFull_SatisfiesBothRequiredTypes()
    {
        var (state, die) = GameWithSplitDie(EnergyType.Fist, EnergyType.Mask);
        var buying = state.DiceIn("p1", Zone.Unpurchased).First(d => d.CardId == "cross");
        // Costs 3: the split face is two of that, a Sidekick the third.
        var extra = state.DiceIn("p1", Zone.Bag).First();
        extra.Zone = Zone.ReservePool; extra.Status = DieStatus.Energy;
        extra.EnergyKind = EnergyKind.Specific; extra.ProvidedEnergyType = EnergyType.Bolt;

        TurnEngine.Purchase(state, buying.Id, [die.Id, extra.Id]);

        Assert.Equal(Zone.UsedPile, buying.Zone);
        Assert.Equal(Zone.OutOfPlay, die.Zone); // fully spent - it covered both required types
    }

    // The interaction: pay for a 1-cost Fist card with half a fist/mask
    // die and the die spins down to GENERIC - the mask half is no longer
    // spendable as a mask.
    [Fact]
    public void SplitDouble_HalfSpent_SpinsDownToGenericNotToTheOtherType()
    {
        var (state, die) = GameWithSplitDie(EnergyType.Fist, EnergyType.Mask);
        var target = state.DiceIn("p1", Zone.Unpurchased).First(d => d.CardId == Target.Id);

        TurnEngine.Purchase(state, target.Id, [die.Id]);

        Assert.Equal(Zone.ReservePool, die.Zone); // stayed - only half was needed
        Assert.Equal(1, die.EnergyAmount);
        Assert.Equal(EnergyKind.Generic, die.EnergyKind);
        Assert.Null(die.ProvidedEnergyType);
        Assert.Null(die.SecondProvidedEnergyType);
    }

    // Same shape for a card costing all four types, except the face it
    // lands on is Wild - which is strictly more flexible than the fist it
    // gave up, and so worth doing on purpose.
    [Fact]
    public void FourEnergyDouble_HalfSpent_SpinsDownToWild()
    {
        var (state, die) = GameWithSplitDie(
            EnergyType.Fist, EnergyType.Shield, EnergyType.Bolt, EnergyType.Mask);
        var target = state.DiceIn("p1", Zone.Unpurchased).First(d => d.CardId == Target.Id);

        TurnEngine.Purchase(state, target.Id, [die.Id]);

        Assert.Equal(1, die.EnergyAmount);
        Assert.Equal(EnergyKind.Wild, die.EnergyKind);
    }

    // Crossover glossary - "Their cost cannot be reduced to avoid paying
    // each type of energy" - and rule 2.6.2.3's example: a 3-cost
    // bolt-fist Crossover reduced to 1 still costs a bolt AND a fist.
    // This used to make a discounted Crossover unbuyable outright.
    [Fact]
    public void DiscountedCrossover_StillCostsOneOfEachType()
    {
        var (state, _) = GameWithSplitDie(EnergyType.Fist, EnergyType.Mask);
        state.PendingPurchaseDiscount = new PendingPurchaseDiscount(2, null);

        var sidekicks = state.DiceIn("p1", Zone.Bag).Take(2).ToList();
        sidekicks[0].Zone = Zone.ReservePool; sidekicks[0].Status = DieStatus.Energy;
        sidekicks[0].EnergyKind = EnergyKind.Specific; sidekicks[0].ProvidedEnergyType = EnergyType.Fist;
        sidekicks[1].Zone = Zone.ReservePool; sidekicks[1].Status = DieStatus.Energy;
        sidekicks[1].EnergyKind = EnergyKind.Specific; sidekicks[1].ProvidedEnergyType = EnergyType.Mask;

        var buying = state.DiceIn("p1", Zone.Unpurchased).First(d => d.CardId == "cross");
        TurnEngine.Purchase(state, buying.Id, sidekicks.Select(d => d.Id).ToList());

        Assert.Equal(Zone.UsedPile, buying.Zone);
        Assert.All(sidekicks, d => Assert.Equal(Zone.OutOfPlay, d.Zone)); // both were needed
    }

    // A Wild should not be spent covering a type something else could
    // have covered, leaving a type uncovered that only it could serve.
    [Fact]
    public void WildEnergy_IsNotWastedOnATypeAnotherDieCouldCover()
    {
        var (state, _) = GameWithSplitDie(EnergyType.Fist, EnergyType.Mask);
        var dice = state.DiceIn("p1", Zone.Bag).Take(2).ToList();
        // The Wild comes FIRST, so a naive matcher would spend it on Fist
        // and then have nothing left for Mask.
        dice[0].Zone = Zone.ReservePool; dice[0].Status = DieStatus.Energy;
        dice[0].EnergyKind = EnergyKind.Wild;
        dice[1].Zone = Zone.ReservePool; dice[1].Status = DieStatus.Energy;
        dice[1].EnergyKind = EnergyKind.Specific; dice[1].ProvidedEnergyType = EnergyType.Fist;

        var buying = state.DiceIn("p1", Zone.Unpurchased).First(d => d.CardId == "cross");
        state.PendingPurchaseDiscount = new PendingPurchaseDiscount(1, null);

        TurnEngine.Purchase(state, buying.Id, dice.Select(d => d.Id).ToList());

        Assert.Equal(Zone.UsedPile, buying.Zone);
    }
}

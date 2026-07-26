using DiceFight.Engine.Data;
using DiceFight.Engine.Effects;
using DiceFight.Engine.Model;
using Xunit;

namespace DiceFight.Engine.Tests;

// Exercises EffectInterpreter directly against the scripted sample cards'
// real AbilityDefs (SampleCards.Dazzler, .CosmicCube, .ShockingGrasp,
// .CasketOfAncientWinters), independent of the turn/combat engines.
public class EffectInterpreterTests
{
    private static GameState CreateState(IReadOnlyDictionary<string, CardDef>? catalog = null) =>
        GameState.NewGame(
            catalog ?? SampleCards.BuildCatalog(),
            new Player { Id = "p1", Name = "Player One" },
            new Player { Id = "p2", Name = "Player Two" });

    [Fact]
    public void DealDamage_KOsDieWhenDamageReachesDefense()
    {
        var state = CreateState();
        var target = state.DiceFor("p2").First(); // Sidekick, 1D by default

        EffectInterpreter.Execute(
            new DealDamage(4, new TargetSpec("t")),
            new EffectContext(state, "p1", SourceDieId: null, _ => [target.Id]));

        Assert.Equal(Zone.PrepArea, target.Zone);
        Assert.Equal(DieStatus.Unrolled, target.Status);
    }

    [Fact]
    public void Dazzler_WhenFielded_Deals4DamageToChosenTarget()
    {
        var state = CreateState();
        var target = state.DiceFor("p2").First();

        var ability = SampleCards.Dazzler.Abilities.Single(a => a.Trigger == TriggerType.WhenFielded);
        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "p1", "p1-dazzler-1", _ => [target.Id]));

        Assert.Equal(Zone.PrepArea, target.Zone); // 4 damage vs 1D Sidekick
    }

    [Fact]
    public void CosmicCube_SwapsLifeTotals()
    {
        var state = CreateState();
        state.PlayerOne.Life = 15;
        state.PlayerTwo.Life = 20;

        var ability = SampleCards.CosmicCube.Abilities.Single();
        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "p1", "p1-cosmiccube-1", _ => []));

        Assert.Equal(20, state.PlayerOne.Life);
        Assert.Equal(15, state.PlayerTwo.Life);
    }

    [Fact]
    public void ShockingGrasp_KOingTarget_PrepsTheActionDieItself()
    {
        var state = CreateState();
        var target = state.DiceFor("p2").First(); // 1D - lethal to 1 damage? No, need >=1: 1 damage vs 1D is lethal.
        var sourceDie = new DieInstance
        {
            Id = "p1-shockinggrasp-1", CardId = SampleCards.ShockingGrasp.Id,
            OwnerId = "p1", ControllerId = "p1", Zone = Zone.OutOfPlay
        };
        state.Dice.Add(sourceDie);

        var ability = SampleCards.ShockingGrasp.Abilities.Single();
        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "p1", sourceDie.Id, _ => [target.Id]));

        Assert.Equal(Zone.PrepArea, target.Zone); // KO'd by the 1 damage
        Assert.Equal(Zone.PrepArea, sourceDie.Zone); // Conditional held -> Prep this die
    }

    [Fact]
    public void ShockingGrasp_NonLethalDamage_DoesNotPrepTheActionDie()
    {
        var toughCard = new CardDef
        {
            Id = "tough", Name = "Tough Guy", Type = CardType.Character, PurchaseCost = 3, DieLimit = 4,
            Levels = [new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 5)]
        };
        var state = CreateState(new Dictionary<string, CardDef>(SampleCards.BuildCatalog()) { [toughCard.Id] = toughCard });
        var target = new DieInstance
        {
            Id = "p2-tough-1", CardId = toughCard.Id, OwnerId = "p2", ControllerId = "p2",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1
        };
        state.Dice.Add(target);
        var sourceDie = new DieInstance
        {
            Id = "p1-shockinggrasp-1", CardId = SampleCards.ShockingGrasp.Id,
            OwnerId = "p1", ControllerId = "p1", Zone = Zone.OutOfPlay
        };
        state.Dice.Add(sourceDie);

        var ability = SampleCards.ShockingGrasp.Abilities.Single();
        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "p1", sourceDie.Id, _ => [target.Id]));

        Assert.Equal(1, target.Damage); // damaged but not KO'd (1 vs 5D)
        Assert.Equal(Zone.FieldZone, target.Zone);
        Assert.Equal(Zone.OutOfPlay, sourceDie.Zone); // Conditional did not hold - not Prepped
    }

    [Fact]
    public void CasketOfAncientWinters_KOsAndMovesThreeDiceFromEachZone()
    {
        var state = CreateState();
        var opponentDice = state.DiceFor("p2").ToList(); // 8 Sidekicks
        var fieldTargets = opponentDice.Take(3).ToList();
        var reserveTargets = opponentDice.Skip(3).Take(3).ToList();
        var prepTargets = opponentDice.Skip(6).Take(2).ToList(); // only 2 left; exercises "as many as available"
        foreach (var die in fieldTargets) die.Zone = Zone.FieldZone;
        foreach (var die in reserveTargets) die.Zone = Zone.ReservePool;
        foreach (var die in prepTargets) die.Zone = Zone.PrepArea;

        var ability = SampleCards.CasketOfAncientWinters.Abilities.Single();
        IReadOnlyList<string> Resolve(TargetSpec spec) => spec.Description switch
        {
            "opponent's 3 character dice" => fieldTargets.Select(d => d.Id).ToList(),
            "opponent's 3 reserve pool dice" => reserveTargets.Select(d => d.Id).ToList(),
            "opponent's 3 prep area dice" => prepTargets.Select(d => d.Id).ToList(),
            _ => []
        };

        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "p1", "p1-casket-1", Resolve));

        Assert.All(fieldTargets, d => Assert.Equal(Zone.PrepArea, d.Zone)); // KO'd
        Assert.All(reserveTargets, d => Assert.Equal(Zone.Bag, d.Zone));
        Assert.All(prepTargets, d => Assert.Equal(Zone.UsedPile, d.Zone));
    }
}

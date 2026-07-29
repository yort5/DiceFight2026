using DiceFight.Engine;
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
    // Lets a Regenerate test control exactly what a KO'd die rerolls to,
    // mirroring CombatEngineTests' FixedRoller.
    private sealed class FixedRoller(DieStatus status, int level) : IDiceRoller
    {
        public RolledFace Roll(DieInstance die, CardDef? card) => new(status, level);
    }

    private static GameState CreateState(IReadOnlyDictionary<string, CardDef>? catalog = null) =>
        GameState.NewGame(
            catalog ?? SampleCards.BuildCatalog(),
            new Player { Id = "p1", Name = "Player One" },
            new Player { Id = "p2", Name = "Player Two" });

    // A legal target for most of these specs: in the Field Zone, on a
    // character face - Sidekicks satisfy the default TargetSpec.CharacterDie
    // (rule 3.3.4/1.6.6) but not one with a RequiredEnergyType, since
    // Sidekicks have no energy type at all (rule 1.3.10).
    private static DieInstance FieldSidekickTarget(GameState state, string playerId)
    {
        var die = state.DiceFor(playerId).First();
        die.Zone = Zone.FieldZone;
        die.Status = DieStatus.SidekickCharacter;
        return die;
    }

    [Fact]
    public void DealDamage_KOsDieWhenDamageReachesDefense()
    {
        var state = CreateState();
        var target = FieldSidekickTarget(state, "p2");

        EffectInterpreter.Execute(
            new DealDamage(4, TargetSpec.CharacterDie("t")),
            new EffectContext(state, "p1", SourceDieId: null, _ => [target.Id]));

        Assert.Equal(Zone.PrepArea, target.Zone);
        Assert.Equal(DieStatus.Unrolled, target.Status);
    }

    // Ability-driven KOs (DealDamage KO'ing its target, or a direct Ko node
    // like Casket of Ancient Winters) go through DieStats.ForceKO just like
    // combat KOs do, so a Regenerate target survives here too - locking in
    // that behavior independent of CombatEngine.
    [Fact]
    public void DealDamage_RespectsRegenerate_WhenRollerSuppliedAndFaceIsCharacter()
    {
        var regenCard = new CardDef
        {
            Id = "regen-target", Name = "Regen Target", Type = CardType.Character,
            PurchaseCost = 2, DieLimit = 4,
            Levels = [new CharacterFace(FieldingCost: 1, Attack: 1, Defense: 1)],
            Keywords = [new KeywordInstance("Regenerate")],
        };
        var catalog = new Dictionary<string, CardDef> { [regenCard.Id] = regenCard };
        var state = CreateState(catalog);
        var target = new DieInstance
        {
            Id = "p2-regen-target-1", CardId = regenCard.Id, OwnerId = "p2", ControllerId = "p2",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(target);

        var roller = new FixedRoller(DieStatus.Character, 2);
        EffectInterpreter.Execute(
            new DealDamage(1, TargetSpec.CharacterDie("t")),
            new EffectContext(state, "p1", SourceDieId: null, _ => [target.Id], Roller: roller));

        Assert.Equal(Zone.FieldZone, target.Zone); // regenerated, not KO'd
        Assert.Equal(2, target.Level);
        Assert.Equal(0, target.Damage);
    }

    [Fact]
    public void Dazzler_WhenFielded_Deals4DamageToChosenTarget()
    {
        var state = CreateState();
        // Dazzler's spec requires Mask energy type, which Sidekicks don't
        // have (rule 1.3.10) - needs an opposing Mask-type character die,
        // e.g. any of the sample Characters (all placeholder Mask type).
        var target = new DieInstance
        {
            Id = "p2-captain-marvel-1", CardId = SampleCards.CaptainMarvel.Id, OwnerId = "p2", ControllerId = "p2",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1
        };
        state.Dice.Add(target);

        var ability = SampleCards.Dazzler.Abilities.Single(a => a.Trigger == TriggerType.WhenFielded);
        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "p1", "p1-dazzler-1", _ => [target.Id]));

        Assert.Equal(Zone.PrepArea, target.Zone); // 4 damage vs 2D at level 1
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
        var target = FieldSidekickTarget(state, "p2"); // 1D - lethal to 1 damage
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
        foreach (var die in fieldTargets)
        {
            die.Zone = Zone.FieldZone;
            die.Status = DieStatus.SidekickCharacter; // the Ko clause requires a character face (rule 1.6.6)
        }
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

    [Fact]
    public void NeedsTarget_IsTrueForAGlobalWithARealTarget_FalseForOneWithout()
    {
        // Distraction's Global picks a specific attacking character die -
        // a real target the caller has to choose.
        var distractionGlobal = SampleCards.Distraction.Abilities.Single(a => a.Trigger == TriggerType.Global);
        Assert.True(EffectInterpreter.NeedsTarget(distractionGlobal.Effect));

        // Falcon's Global ("each player must field a Sidekick... if able")
        // has no chooser-selected target at all - see FieldSidekickForEachPlayer.
        var falconGlobal = SampleCards.Falcon.Abilities.Single(a => a.Trigger == TriggerType.Global);
        Assert.False(EffectInterpreter.NeedsTarget(falconGlobal.Effect));
    }
}

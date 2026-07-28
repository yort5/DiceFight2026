using DiceFight.Engine.Combat;
using DiceFight.Engine.Data;
using DiceFight.Engine.Effects;
using DiceFight.Engine.Model;
using DiceFight.Engine.Queueing;
using Xunit;

namespace DiceFight.Engine.Tests;

// End-to-end proof that the two curated sample teams actually play against
// each other through the real engine: Purchase -> Field -> Attack, with
// WhenFielded/WhenUsed/Global abilities running through the real
// AbilityQueue + EffectInterpreter. This bypasses actual dice rolling
// (GameState.NewGame/TeamSetup already provisions real team dice into
// Zone.Unpurchased; this file just marks a handful of Sidekicks as
// "already rolled onto an energy face" to pay for things, since Roll and
// Reroll's IDiceRoller-driven randomness isn't the thing being tested
// here) - everything downstream (Purchase, Field, DeclareAttackers/
// Blockers, AssignCombatDamage, UseActionDie, UseGlobalAbility,
// AbilityQueue) is the real engine path.
public class TwoTeamsDemoTests
{
    private static GameState BuildTwoTeamGame()
    {
        var catalog = SampleCards.BuildCatalog();
        var teamA = new Player { Id = "teamA", Name = "Team A" };
        teamA.TeamCardIds.AddRange(SampleCards.TeamACharacterIds);
        teamA.TeamCardIds.AddRange(SampleCards.TeamABasicActionIds);

        var teamB = new Player { Id = "teamB", Name = "Team B" };
        teamB.TeamCardIds.AddRange(SampleCards.TeamBCharacterIds);
        teamB.TeamCardIds.AddRange(SampleCards.TeamBBasicActionIds);

        var state = GameState.NewGame(catalog, teamA, teamB);
        state.CurrentStep = TurnStep.Main; // bypass Clear/Draw/Roll for this focused scenario
        return state;
    }

    // Converts N of a player's own Sidekick dice into ready Wild energy
    // sitting in the Reserve Pool - Sidekick energy faces satisfy any
    // required type (rule 1.3.10), which keeps these tests focused on the
    // mechanics being exercised rather than energy-type bookkeeping.
    private static List<DieInstance> GiveWildEnergy(GameState state, string playerId, int count)
    {
        var dice = state.DiceIn(playerId, Zone.Bag).Take(count).ToList();
        foreach (var die in dice)
        {
            die.Zone = Zone.ReservePool;
            die.Status = DieStatus.Energy;
            die.EnergyKind = EnergyKind.Wild;
        }
        return dice;
    }

    private static DieInstance FindUnpurchased(GameState state, string playerId, string cardId) =>
        state.DiceIn(playerId, Zone.Unpurchased).First(d => d.CardId == cardId);

    // A "double" energy face (rulebook's Doubles rule) - worth 2 when
    // spent, either wholesale or partially ("spun down" - see
    // TurnEngine.SpendEnergy) depending how much of it a payment needs.
    private static List<DieInstance> GiveDoubleEnergy(
        GameState state, string playerId, int count, EnergyKind kind, EnergyType? providedType = null)
    {
        var dice = state.DiceIn(playerId, Zone.Bag).Take(count).ToList();
        foreach (var die in dice)
        {
            die.Zone = Zone.ReservePool;
            die.Status = DieStatus.Energy;
            die.EnergyKind = kind;
            die.ProvidedEnergyType = providedType;
            die.EnergyAmount = 2;
        }
        return dice;
    }

    [Fact]
    public void PurchasingAndFieldingDazzler_TriggersWhenFieldedAbility_ThroughTheRealQueueAndInterpreter()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";
        var dazzlerDie = FindUnpurchased(state, "teamA", SampleCards.Dazzler.Id);
        // Dazzler's spec requires Mask energy type, which Sidekicks don't
        // have (rule 1.3.10) - use a real fielded opposing character.
        var opposingTarget = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        opposingTarget.Zone = Zone.FieldZone;
        opposingTarget.Status = DieStatus.Character;

        var purchaseEnergy = GiveWildEnergy(state, "teamA", SampleCards.Dazzler.PurchaseCost);
        TurnEngine.Purchase(state, dazzlerDie.Id, purchaseEnergy.Select(d => d.Id).ToList());
        Assert.Equal(Zone.UsedPile, dazzlerDie.Zone);

        // Rule 2.4 bypass: mark the purchased die as already rolled to its
        // level-1 character face, ready to field.
        dazzlerDie.Zone = Zone.ReservePool;
        dazzlerDie.Status = DieStatus.Character;
        dazzlerDie.Level = 1; // fielding cost 0 at level 1

        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, dazzlerDie.Id, energyDieIdsToSpend: []);

        Assert.Equal(Zone.FieldZone, dazzlerDie.Zone);
        Assert.False(queue.IsEmpty);

        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [opposingTarget.Id])));

        Assert.Equal(Zone.PrepArea, opposingTarget.Zone); // KO'd by Dazzler's 4 damage
        Assert.True(queue.IsEmpty);
    }

    [Fact]
    public void PurchasingAndFieldingApocalypse_ThenAttackingUnblocked_DealsRealCombatDamage()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";
        var apocalypseDie = FindUnpurchased(state, "teamA", SampleCards.Apocalypse.Id);

        var purchaseEnergy = GiveWildEnergy(state, "teamA", SampleCards.Apocalypse.PurchaseCost);
        TurnEngine.Purchase(state, apocalypseDie.Id, purchaseEnergy.Select(d => d.Id).ToList());
        Assert.Equal(Zone.UsedPile, apocalypseDie.Zone);
        Assert.Equal("teamA", apocalypseDie.ControllerId);

        apocalypseDie.Zone = Zone.ReservePool;
        apocalypseDie.Status = DieStatus.Character;
        apocalypseDie.Level = 2; // placeholder level-2 face: fielding cost 1, 2A/3D

        var fieldEnergy = GiveWildEnergy(state, "teamA", 1);
        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, apocalypseDie.Id, energyDieIdsToSpend: [fieldEnergy[0].Id]);

        Assert.Equal(Zone.FieldZone, apocalypseDie.Zone);
        Assert.Equal(Zone.OutOfPlay, fieldEnergy[0].Zone);

        TurnEngine.EnterAttackStep(state);
        CombatEngine.DeclareAttackers(state, queue, [apocalypseDie.Id]);
        CombatEngine.DeclareBlockers(state, new CombatAssignment(), blockerDieIds: []); // Team B chooses not to block
        var result = CombatEngine.AssignCombatDamage(
            state, queue, new CombatAssignment(),
            attackerDamageSplits: new Dictionary<string, IReadOnlyDictionary<string, int>>());

        Assert.Equal(Player.StartingLife - 2, state.PlayerTwo.Life); // Apocalypse's level-2 attack (2)
        Assert.Equal(Zone.OutOfPlay, apocalypseDie.Zone); // unblocked attacker leaves play
        Assert.Empty(result.KOdDieIds);
        Assert.Equal(TurnStep.CleanUp, state.CurrentStep);
    }

    [Fact]
    public void UsingShockingGraspActionDie_KOsTargetAndPrepsItself_ThroughTheRealQueue()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";
        var shockingGraspDie = FindUnpurchased(state, "teamA", SampleCards.ShockingGrasp.Id);
        var target = state.DiceFor("teamB").First(); // Sidekick, 1D - lethal to 1 damage
        target.Zone = Zone.FieldZone; // legal targets must be in the Field Zone (rule 3.3.4)
        target.Status = DieStatus.SidekickCharacter;

        var purchaseEnergy = GiveWildEnergy(state, "teamA", SampleCards.ShockingGrasp.PurchaseCost);
        TurnEngine.Purchase(state, shockingGraspDie.Id, purchaseEnergy.Select(d => d.Id).ToList());

        // Rule 2.4 bypass: mark as already rolled to its action face.
        shockingGraspDie.Zone = Zone.ReservePool;
        shockingGraspDie.Status = DieStatus.Action;

        var queue = new AbilityQueue();
        TurnEngine.UseActionDie(state, queue, shockingGraspDie.Id);

        Assert.Equal(Zone.OutOfPlay, shockingGraspDie.Zone); // rule 2.6.4.1, before the ability resolves
        Assert.False(queue.IsEmpty);

        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [target.Id])));

        Assert.Equal(Zone.PrepArea, target.Zone); // KO'd
        Assert.Equal(Zone.PrepArea, shockingGraspDie.Zone); // its own Conditional Prepped it

        // Prep Area is a dormant zone (rulebook's "unrolled dice" -
        // doesn't matter what face was showing) - the die's own Action
        // status from being used shouldn't linger now that it's Prepped.
        Assert.Equal(DieStatus.Unrolled, shockingGraspDie.Status);
    }

    [Fact]
    public void UsingCosmicCube_EpicBasicAction_ReturnsToItsCardInsteadOfOutOfPlay_AndIsOncePerTurn()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";
        // Rule 1.2.3(4) - needs an active Character die with purchase cost 4+.
        var qualifyingCharacter = FindUnpurchased(state, "teamA", SampleCards.CaptainMarvel.Id);
        qualifyingCharacter.Zone = Zone.FieldZone;
        qualifyingCharacter.Status = DieStatus.Character;

        var cosmicCubeDie = FindUnpurchased(state, "teamA", SampleCards.CosmicCube.Id);
        var purchaseEnergy = GiveWildEnergy(state, "teamA", SampleCards.CosmicCube.PurchaseCost);
        TurnEngine.Purchase(state, cosmicCubeDie.Id, purchaseEnergy.Select(d => d.Id).ToList());
        cosmicCubeDie.Zone = Zone.ReservePool;
        cosmicCubeDie.Status = DieStatus.Action;

        state.PlayerOne.Life = 15;
        state.PlayerTwo.Life = 20;
        var queue = new AbilityQueue();
        TurnEngine.UseActionDie(state, queue, cosmicCubeDie.Id);

        Assert.Equal(Zone.Unpurchased, cosmicCubeDie.Zone); // rule 1.2.3(2), not Out of Play
        Assert.True(state.EpicBasicActionUsedThisTurn);

        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [])));
        Assert.Equal(20, state.PlayerOne.Life);
        Assert.Equal(15, state.PlayerTwo.Life);

        // A second Epic Basic Action this turn is rejected (rule 1.2.3(3)).
        var secondEpicDie = FindUnpurchased(state, "teamB", SampleCards.CasketOfAncientWinters.Id);
        secondEpicDie.ControllerId = "teamA"; // pretend it was also purchased
        secondEpicDie.Zone = Zone.ReservePool;
        secondEpicDie.Status = DieStatus.Action;
        Assert.Throws<InvalidOperationException>(() => TurnEngine.UseActionDie(state, queue, secondEpicDie.Id));
    }

    [Fact]
    public void UsingDistractionGlobalAbility_RemovesAttackerFromCombat_PaidByTheInactivePlayer()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";
        var attacker = FindUnpurchased(state, "teamA", SampleCards.Apocalypse.Id);
        attacker.Zone = Zone.FieldZone;
        attacker.Status = DieStatus.Character;
        attacker.Level = 1;

        state.CurrentStep = TurnStep.Attack;
        state.AttackSubStep = AttackSubStep.DeclareAttackers;
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        CombatEngine.DeclareBlockers(state, new CombatAssignment(), blockerDieIds: []);
        Assert.Equal(Zone.AttackZone, attacker.Zone);

        // Team B (Inactive player) pays for Distraction's Global ability
        // using their own energy - printed on a card from Team A's pool,
        // which rule 2.6.5.2 explicitly allows.
        var teamBEnergy = GiveWildEnergy(state, "teamB", 1);
        TurnEngine.UseGlobalAbility(
            state, queue, SampleCards.Distraction.Id, "teamB", teamBEnergy.Select(d => d.Id).ToList());

        Assert.Equal(Zone.UsedPile, teamBEnergy[0].Zone); // rule 2.6.1.2 - Inactive player's spend, not Out of Play

        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [attacker.Id])));

        Assert.Equal(Zone.FieldZone, attacker.Zone); // removed from combat

        var result = CombatEngine.AssignCombatDamage(
            state, queue, new CombatAssignment(), new Dictionary<string, IReadOnlyDictionary<string, int>>());

        Assert.Equal(Player.StartingLife, state.PlayerTwo.Life); // no damage - attacker never made it to the Attack Zone at resolution time
        Assert.Empty(result.KOdDieIds);
    }

    [Fact]
    public void UsingFalconGlobalAbility_FieldsASidekickForEachPlayer_IfAble_AndOnlyOncePerTurn()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamB";

        // Team B has a Sidekick sitting in their Used Pile to be fielded;
        // Team A has none, exercising the "if able" no-op half at once.
        // Rule 1.6.8 - a Sidekick in the Used Pile is unrolled, not on any
        // particular face (matches DieInstance.ResetToUnrolled's default),
        // so this deliberately does NOT set a rolled Status - finding it
        // has to work off IsSidekick alone.
        var teamBSidekick = state.DiceIn("teamB", Zone.Bag).First();
        teamBSidekick.Zone = Zone.UsedPile;

        var energy = GiveWildEnergy(state, "teamB", 1);
        var queue = new AbilityQueue();
        TurnEngine.UseGlobalAbility(state, queue, SampleCards.Falcon.Id, "teamB", energy.Select(d => d.Id).ToList());
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [])));

        Assert.Equal(Zone.FieldZone, teamBSidekick.Zone);
        Assert.DoesNotContain(state.DiceIn("teamA", Zone.FieldZone), d => d.Status == DieStatus.SidekickCharacter);

        // Once during your turn - a second activation this turn fails even with fresh energy.
        var moreEnergy = GiveWildEnergy(state, "teamB", 1);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            TurnEngine.UseGlobalAbility(state, queue, SampleCards.Falcon.Id, "teamB", moreEnergy.Select(d => d.Id).ToList()));
        Assert.Contains("once per turn", ex.Message);
    }

    [Fact]
    public void UsingInvisibleWomanGlobalAbility_ForcesTargetToBlock_EnforcedAtDeclareBlockers()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";

        var teamBBlocker = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        teamBBlocker.Zone = Zone.FieldZone;
        teamBBlocker.Status = DieStatus.Character;
        teamBBlocker.Level = 1;

        var energy = GiveWildEnergy(state, "teamA", 1);
        var queue = new AbilityQueue();
        TurnEngine.UseGlobalAbility(
            state, queue, SampleCards.InvisibleWoman.Id, "teamA", energy.Select(d => d.Id).ToList());
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [teamBBlocker.Id])));

        Assert.Contains(teamBBlocker.Id, state.MustBlockThisTurn);

        var attacker = FindUnpurchased(state, "teamA", SampleCards.BigBarda.Id);
        attacker.Zone = Zone.FieldZone;
        attacker.Status = DieStatus.Character;
        attacker.Level = 1;

        state.CurrentStep = TurnStep.Attack;
        state.AttackSubStep = AttackSubStep.DeclareAttackers;
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CombatEngine.DeclareBlockers(state, new CombatAssignment(), blockerDieIds: []));
        Assert.Contains("must block", ex.Message);

        CombatEngine.DeclareBlockers(state, new CombatAssignment(), blockerDieIds: [teamBBlocker.Id]);
        Assert.Equal(Zone.AttackZone, teamBBlocker.Zone);
    }

    [Fact]
    public void UsingStarfireGlobalAbility_PrepsADieFromBag_IfYouPurchasedADieThisTurn()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamB";

        var purchaseEnergy = GiveWildEnergy(state, "teamB", 3);
        var toBuy = FindUnpurchased(state, "teamB", SampleCards.GodEmperorDoom.Id);
        TurnEngine.Purchase(state, toBuy.Id, purchaseEnergy.Select(d => d.Id).ToList());
        Assert.True(state.PlayerTwo.PurchasedDieThisTurn);

        var globalEnergy = GiveWildEnergy(state, "teamB", 1);
        var bagCountBefore = state.DiceIn("teamB", Zone.Bag).Count();
        var queue = new AbilityQueue();
        TurnEngine.UseGlobalAbility(
            state, queue, SampleCards.Starfire.Id, "teamB", globalEnergy.Select(d => d.Id).ToList());
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [])));

        Assert.Single(state.DiceIn("teamB", Zone.PrepArea));
        Assert.Equal(bagCountBefore - 1, state.DiceIn("teamB", Zone.Bag).Count());
    }

    [Fact]
    public void UsingStarfireGlobalAbility_IsANoOp_WithoutAPurchaseThisTurn_AndOnlyOncePerTurn()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamB";

        var energy = GiveWildEnergy(state, "teamB", 2);
        var queue = new AbilityQueue();
        TurnEngine.UseGlobalAbility(state, queue, SampleCards.Starfire.Id, "teamB", [energy[0].Id]);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [])));

        Assert.Empty(state.DiceIn("teamB", Zone.PrepArea));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            TurnEngine.UseGlobalAbility(state, queue, SampleCards.Starfire.Id, "teamB", [energy[1].Id]));
        Assert.Contains("once per turn", ex.Message);
    }

    [Fact]
    public void Fielding_WithATypedDoubleEnergyDie_SpinsDownTheLeftoverInsteadOfSpendingTheWholeDie()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";
        var bigBardaDie = FindUnpurchased(state, "teamA", SampleCards.BigBarda.Id);
        bigBardaDie.Zone = Zone.ReservePool;
        bigBardaDie.Status = DieStatus.Character;
        bigBardaDie.Level = 1; // fielding cost 1

        var doubleFist = GiveDoubleEnergy(state, "teamA", 1, EnergyKind.Specific, EnergyType.Fist)[0];

        TurnEngine.Field(state, new AbilityQueue(), bigBardaDie.Id, [doubleFist.Id]);

        Assert.Equal(Zone.FieldZone, bigBardaDie.Zone);
        Assert.Equal(Zone.ReservePool, doubleFist.Zone); // only half was needed - stays, doesn't move out
        Assert.Equal(1, doubleFist.EnergyAmount); // "spun down" to its single-energy face
        Assert.Equal(EnergyKind.Specific, doubleFist.EnergyKind);
        Assert.Equal(EnergyType.Fist, doubleFist.ProvidedEnergyType);
    }

    [Fact]
    public void Fielding_WithATypedDoubleEnergyDie_SpendsTheWholeDieWhenExactlyEnoughIsNeeded()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";
        var bigBardaDie = FindUnpurchased(state, "teamA", SampleCards.BigBarda.Id);
        bigBardaDie.Zone = Zone.ReservePool;
        bigBardaDie.Status = DieStatus.Character;
        bigBardaDie.Level = 3; // fielding cost 2 - exactly what a double provides

        var doubleFist = GiveDoubleEnergy(state, "teamA", 1, EnergyKind.Specific, EnergyType.Fist)[0];

        TurnEngine.Field(state, new AbilityQueue(), bigBardaDie.Id, [doubleFist.Id]);

        Assert.Equal(Zone.FieldZone, bigBardaDie.Zone);
        Assert.Equal(Zone.OutOfPlay, doubleFist.Zone); // fully spent - no leftover to spin down
        Assert.Equal(2, doubleFist.EnergyAmount); // untouched
    }

    [Fact]
    public void Fielding_WithAGenericDoubleEnergyDie_SpendsTheWholeDieAndBanksTheLeftoverAsVirtualEnergy()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";
        var bigBardaDie = FindUnpurchased(state, "teamA", SampleCards.BigBarda.Id);
        bigBardaDie.Zone = Zone.ReservePool;
        bigBardaDie.Status = DieStatus.Character;
        bigBardaDie.Level = 1; // fielding cost 1

        // A Generic double (e.g. a Basic Action die) has no single-energy
        // face to "spin down" to - unlike a typed double, so it moves out
        // fully and the unspent half becomes tracked virtual energy.
        var doubleGeneric = GiveDoubleEnergy(state, "teamA", 1, EnergyKind.Generic)[0];

        TurnEngine.Field(state, new AbilityQueue(), bigBardaDie.Id, [doubleGeneric.Id]);

        Assert.Equal(Zone.FieldZone, bigBardaDie.Zone);
        Assert.Equal(Zone.OutOfPlay, doubleGeneric.Zone);

        // Rule 1.4.4 - banked as a real spendable die in the Reserve Pool,
        // not a separate counter (see TurnEngine.AddVirtualGenericEnergy).
        var virtualDie = Assert.Single(state.DiceIn("teamA", Zone.ReservePool));
        Assert.True(virtualDie.IsVirtualEnergy);
        Assert.Equal(1, virtualDie.EnergyAmount);
    }

    [Fact]
    public void VirtualGenericEnergy_IsActuallySpendable_LikeAnyOtherEnergyDie()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";

        // Bank 1 virtual energy: field a cost-1 Big Barda using a Generic
        // double - only 1 of its 2 is actually needed.
        var firstBigBarda = FindUnpurchased(state, "teamA", SampleCards.BigBarda.Id);
        firstBigBarda.Zone = Zone.ReservePool;
        firstBigBarda.Status = DieStatus.Character;
        firstBigBarda.Level = 1; // fielding cost 1
        var doubleGeneric = GiveDoubleEnergy(state, "teamA", 1, EnergyKind.Generic)[0];
        TurnEngine.Field(state, new AbilityQueue(), firstBigBarda.Id, [doubleGeneric.Id]);
        var virtualDie = Assert.Single(state.Dice, d => d.IsVirtualEnergy);
        Assert.Equal(1, virtualDie.EnergyAmount);

        // Now spend it, same as any other energy die, to field a second
        // Big Barda (dieLimit 4, so a second unpurchased copy exists).
        var secondBigBarda = state.DiceIn("teamA", Zone.Unpurchased).First(d => d.CardId == SampleCards.BigBarda.Id);
        secondBigBarda.Zone = Zone.ReservePool;
        secondBigBarda.Status = DieStatus.Character;
        secondBigBarda.Level = 1; // fielding cost 1

        TurnEngine.Field(state, new AbilityQueue(), secondBigBarda.Id, [virtualDie.Id]);

        Assert.Equal(Zone.FieldZone, secondBigBarda.Zone);
        // Fully consumed - it isn't a real die, so instead of moving to a
        // zone it just vanishes once used up.
        Assert.DoesNotContain(virtualDie, state.Dice);
    }

    [Fact]
    public void VirtualGenericEnergy_DoesNotCarryPastCleanUp()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";
        var bigBardaDie = FindUnpurchased(state, "teamA", SampleCards.BigBarda.Id);
        bigBardaDie.Zone = Zone.ReservePool;
        bigBardaDie.Status = DieStatus.Character;
        bigBardaDie.Level = 1; // fielding cost 1
        var doubleGeneric = GiveDoubleEnergy(state, "teamA", 1, EnergyKind.Generic)[0];
        TurnEngine.Field(state, new AbilityQueue(), bigBardaDie.Id, [doubleGeneric.Id]);
        Assert.Single(state.Dice, d => d.IsVirtualEnergy);

        state.CurrentStep = TurnStep.CleanUp;
        TurnEngine.CleanUp(state);

        // Gone outright, unlike a real Reserve Pool die - it never gets
        // carried to the Used Pile for a future Clear & Draw to sweep.
        Assert.DoesNotContain(state.Dice, d => d.IsVirtualEnergy);
    }
}

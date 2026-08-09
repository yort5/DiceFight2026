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
// A fake roller lets the Energize/DrawDice tests be deterministic without
// modeling real physical die face tables (see TurnEngine.RolledFace
// remarks) - same shape as TurnEngineTests' own file-scoped FixedRoller.
file sealed class FixedRoller(DieStatus status, int level) : IDiceRoller
{
    public RolledFace Roll(DieInstance die, CardDef? card) => new(status, level);
}

public class TwoTeamsDemoTests
{
    // extraTeamACardIds/extraTeamBCardIds: lets a test pull a real card
    // that isn't on the live roster (e.g. because it's IsImplemented:
    // false and was intentionally left off - see SampleCards.cs's own
    // remarks) into its own local game, without touching the production
    // roster the deployed app actually uses.
    private static GameState BuildTwoTeamGame(
        IReadOnlyList<string>? extraTeamACardIds = null, IReadOnlyList<string>? extraTeamBCardIds = null)
    {
        var catalog = SampleCards.BuildCatalog();
        var teamA = new Player { Id = "teamA", Name = "Team A" };
        teamA.TeamCardIds.AddRange(SampleCards.TeamACharacterIds);
        teamA.TeamCardIds.AddRange(SampleCards.TeamABasicActionIds);
        teamA.TeamCardIds.AddRange(extraTeamACardIds ?? []);

        var teamB = new Player { Id = "teamB", Name = "Team B" };
        teamB.TeamCardIds.AddRange(SampleCards.TeamBCharacterIds);
        teamB.TeamCardIds.AddRange(SampleCards.TeamBBasicActionIds);
        teamB.TeamCardIds.AddRange(extraTeamBCardIds ?? []);

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
        // BigBarda isn't on the live teamA roster (IsImplemented: false) -
        // pulled in as an extra card just for this test's attacker.
        // InvisibleWoman's own Global works by CardId against the shared
        // catalog regardless of roster membership (rule 2.6.5.2), so it
        // doesn't need to be added here too.
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.BigBarda.Id]);
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
    public void FieldingDeathbird_TargetedDieCantBlockThisTurn_EnforcedAtDeclareBlockers()
    {
        // The restriction mirror of the InvisibleWoman test above: CantBlock
        // (new this pass) instead of ForceBlock - real firing path through
        // TurnEngine.Field, real enforcement through CombatEngine.
        // DeclareBlockers actually rejecting the die as a blocker, not just
        // GameState.CantBlockThisTurn getting populated.
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.DeathbirdWarOfKings.Id]);
        state.ActivePlayerId = "teamA";

        var opposingTarget = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        opposingTarget.Zone = Zone.FieldZone;
        opposingTarget.Status = DieStatus.Character;
        opposingTarget.Level = 1;

        var deathbirdDie = FindUnpurchased(state, "teamA", SampleCards.DeathbirdWarOfKings.Id);
        var purchaseEnergy = GiveWildEnergy(state, "teamA", SampleCards.DeathbirdWarOfKings.PurchaseCost);
        TurnEngine.Purchase(state, deathbirdDie.Id, purchaseEnergy.Select(d => d.Id).ToList());
        deathbirdDie.Zone = Zone.ReservePool;
        deathbirdDie.Status = DieStatus.Character;
        deathbirdDie.Level = 1; // fielding cost 0 at level 1

        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, deathbirdDie.Id, energyDieIdsToSpend: []);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [opposingTarget.Id])));

        Assert.Contains(opposingTarget.Id, state.CantBlockThisTurn);

        // What's attacking doesn't matter for this test - a bare Sidekick
        // stands in so Declare Attackers has something legal to work with.
        var attacker = state.DiceIn("teamA", Zone.Bag).First();
        attacker.Zone = Zone.FieldZone;
        attacker.Status = DieStatus.SidekickCharacter;
        attacker.Level = 1;

        state.CurrentStep = TurnStep.Attack;
        state.AttackSubStep = AttackSubStep.DeclareAttackers;
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CombatEngine.DeclareBlockers(state, new CombatAssignment(), blockerDieIds: [opposingTarget.Id]));
        Assert.Contains("not an eligible blocker", ex.Message);

        // Not a "must block" in reverse - declaring no blockers at all
        // (the die simply sitting out) is perfectly legal.
        CombatEngine.DeclareBlockers(state, new CombatAssignment(), blockerDieIds: []);
    }

    [Fact]
    public void DeadpoolAttacking_DealsDamageStraightToOpponent()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.DeadpoolMoreThanAChumpBlocker.Id]);
        state.ActivePlayerId = "teamA";

        var deadpoolDie = FindUnpurchased(state, "teamA", SampleCards.DeadpoolMoreThanAChumpBlocker.Id);
        deadpoolDie.Zone = Zone.FieldZone;
        deadpoolDie.Status = DieStatus.Character;
        deadpoolDie.Level = 1;

        var opponentLifeBefore = state.PlayerTwo.Life;

        state.CurrentStep = TurnStep.Attack;
        state.AttackSubStep = AttackSubStep.DeclareAttackers;
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [deadpoolDie.Id]);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [state.PlayerTwo.Id])));

        Assert.Equal(opponentLifeBefore - 1, state.PlayerTwo.Life);
    }

    [Fact]
    public void FieldingRonanTheAccuser_BothPlayersLoseLife()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.RonanTheAccuserNoExceptions.Id]);
        state.ActivePlayerId = "teamA";

        var ronanDie = FindUnpurchased(state, "teamA", SampleCards.RonanTheAccuserNoExceptions.Id);
        var purchaseEnergy = GiveWildEnergy(state, "teamA", SampleCards.RonanTheAccuserNoExceptions.PurchaseCost);
        TurnEngine.Purchase(state, ronanDie.Id, purchaseEnergy.Select(d => d.Id).ToList());
        ronanDie.Zone = Zone.ReservePool;
        ronanDie.Status = DieStatus.Character;
        ronanDie.Level = 2; // fielding cost 1 at level 1; skip straight to level 2's own 1-cost face

        var fieldEnergy = GiveWildEnergy(state, "teamA", 1);
        var controllerLifeBefore = state.PlayerOne.Life;
        var opponentLifeBefore = state.PlayerTwo.Life;

        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, ronanDie.Id, energyDieIdsToSpend: [fieldEnergy[0].Id]);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [])));

        Assert.Equal(controllerLifeBefore - 3, state.PlayerOne.Life);
        Assert.Equal(opponentLifeBefore - 3, state.PlayerTwo.Life);
    }

    [Fact]
    public void FieldingGambit_RerollsOpposingDie_MovesToUsedPileIfNotCharacter()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.GambitUnlessIGotSomeoneToPlayWith.Id]);
        state.ActivePlayerId = "teamA";

        var opposingTarget = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        opposingTarget.Zone = Zone.FieldZone;
        opposingTarget.Status = DieStatus.Character;
        opposingTarget.Level = 1;

        var gambitDie = FindUnpurchased(state, "teamA", SampleCards.GambitUnlessIGotSomeoneToPlayWith.Id);
        var purchaseEnergy = GiveWildEnergy(state, "teamA", SampleCards.GambitUnlessIGotSomeoneToPlayWith.PurchaseCost);
        TurnEngine.Purchase(state, gambitDie.Id, purchaseEnergy.Select(d => d.Id).ToList());
        gambitDie.Zone = Zone.ReservePool;
        gambitDie.Status = DieStatus.Character;
        gambitDie.Level = 1; // fielding cost 1

        var fieldEnergy = GiveWildEnergy(state, "teamA", 1);
        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, gambitDie.Id, energyDieIdsToSpend: [fieldEnergy[0].Id]);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(
                state, ability.ControllerId, ability.SourceDieId, _ => [opposingTarget.Id],
                Roller: new FixedRoller(DieStatus.Energy, 1))));

        Assert.Equal(Zone.UsedPile, opposingTarget.Zone);
    }

    [Fact]
    public void FieldingGambit_RerollsOpposingDie_StaysPutIfItRollsAnotherCharacterFace()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.GambitUnlessIGotSomeoneToPlayWith.Id]);
        state.ActivePlayerId = "teamA";

        var opposingTarget = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        opposingTarget.Zone = Zone.FieldZone;
        opposingTarget.Status = DieStatus.Character;
        opposingTarget.Level = 1;

        var gambitDie = FindUnpurchased(state, "teamA", SampleCards.GambitUnlessIGotSomeoneToPlayWith.Id);
        var purchaseEnergy = GiveWildEnergy(state, "teamA", SampleCards.GambitUnlessIGotSomeoneToPlayWith.PurchaseCost);
        TurnEngine.Purchase(state, gambitDie.Id, purchaseEnergy.Select(d => d.Id).ToList());
        gambitDie.Zone = Zone.ReservePool;
        gambitDie.Status = DieStatus.Character;
        gambitDie.Level = 1;

        var fieldEnergy = GiveWildEnergy(state, "teamA", 1);
        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, gambitDie.Id, energyDieIdsToSpend: [fieldEnergy[0].Id]);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(
                state, ability.ControllerId, ability.SourceDieId, _ => [opposingTarget.Id],
                Roller: new FixedRoller(DieStatus.Character, 2))));

        Assert.Equal(Zone.FieldZone, opposingTarget.Zone);
        Assert.Equal(2, opposingTarget.Level); // actually rerolled, just landed on a character face again
    }

    [Fact]
    public void FieldingPsylocke_DealsDamageToOpponent_ScaledByHowManyDiceActuallyMoved()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.PsylockeAdvancedTelekineticCombatant.Id]);
        state.ActivePlayerId = "teamA";

        var firstTarget = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        firstTarget.Zone = Zone.FieldZone;
        firstTarget.Status = DieStatus.Character;
        firstTarget.Level = 1;
        var secondTarget = FindUnpurchased(state, "teamB", SampleCards.Groot.Id);
        secondTarget.Zone = Zone.FieldZone;
        secondTarget.Status = DieStatus.Character;
        secondTarget.Level = 1;

        var psylockeDie = FindUnpurchased(state, "teamA", SampleCards.PsylockeAdvancedTelekineticCombatant.Id);
        var purchaseEnergy = GiveWildEnergy(state, "teamA", SampleCards.PsylockeAdvancedTelekineticCombatant.PurchaseCost);
        TurnEngine.Purchase(state, psylockeDie.Id, purchaseEnergy.Select(d => d.Id).ToList());
        psylockeDie.Zone = Zone.ReservePool;
        psylockeDie.Status = DieStatus.Character;
        psylockeDie.Level = 1; // fielding cost 0 at level 1

        var opponentLifeBefore = state.PlayerTwo.Life;
        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, psylockeDie.Id, energyDieIdsToSpend: []);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(
                state, ability.ControllerId, ability.SourceDieId, _ => [firstTarget.Id, secondTarget.Id],
                Roller: new FixedRoller(DieStatus.Energy, 1))));

        Assert.Equal(Zone.UsedPile, firstTarget.Zone);
        Assert.Equal(Zone.UsedPile, secondTarget.Zone);
        Assert.Equal(opponentLifeBefore - 4, state.PlayerTwo.Life); // 2 damage x 2 dice moved
    }

    [Fact]
    public void StormQueenEnergize_FiresOffRealEnergizeGate_RerollsTargetOpposingDie()
    {
        // Same "test the gate, not just the effect" bar the Kitty Pryde/
        // Phoenix Awaken/Energize bug established - TurnEngine.Reroll's
        // real post-roll Energize scan, not a manually-enqueued trigger,
        // is what has to actually find Storm's own KeywordInstance here.
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.StormQueen.Id]);
        state.ActivePlayerId = "teamA";
        state.CurrentStep = TurnStep.RollAndReroll;

        var stormDie = FindUnpurchased(state, "teamA", SampleCards.StormQueen.Id);
        stormDie.Zone = Zone.ReservePool;
        stormDie.Status = DieStatus.Energy;
        stormDie.EnergyKind = EnergyKind.Generic;
        stormDie.EnergyAmount = 2; // double energy - Energize's own trigger condition

        var opposingTarget = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        opposingTarget.Zone = Zone.FieldZone;
        opposingTarget.Status = DieStatus.Character;
        opposingTarget.Level = 1;

        var queue = new AbilityQueue();
        TurnEngine.Reroll(state, queue, new FixedRoller(DieStatus.Energy, 1), []);

        Assert.Equal(1, queue.Count);
        Assert.Equal(TriggerType.Energize, queue.Pending[0].Trigger);

        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [opposingTarget.Id],
                Roller: new FixedRoller(DieStatus.Energy, 1))));

        Assert.Equal(DieStatus.Energy, opposingTarget.Status); // rerolled off its character face
    }

    [Fact]
    public void FieldingMagik_GrantsOvercrushAndAttackBuff_ButOnlyUntilEndOfTurn()
    {
        // Rule 3.4.3.9 - an Applied ability (Magik's own text has no
        // duration language at all) defaults to "until end of turn," the
        // same lifetime as a numeric Applied stat modifier - not
        // permanent just because the card text doesn't say "until end of
        // turn" itself.
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.MagikSorceressOfLimbo.Id]);
        state.ActivePlayerId = "teamA";

        var target = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        target.Zone = Zone.FieldZone;
        target.Status = DieStatus.Character;
        target.Level = 1;
        var attackBefore = DieStats.EffectiveAttack(state, target);

        var magikDie = FindUnpurchased(state, "teamA", SampleCards.MagikSorceressOfLimbo.Id);
        var purchaseEnergy = GiveWildEnergy(state, "teamA", SampleCards.MagikSorceressOfLimbo.PurchaseCost);
        TurnEngine.Purchase(state, magikDie.Id, purchaseEnergy.Select(d => d.Id).ToList());
        magikDie.Zone = Zone.ReservePool;
        magikDie.Status = DieStatus.Character;
        magikDie.Level = 1; // fielding cost 0 at level 1

        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, magikDie.Id, energyDieIdsToSpend: []);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [target.Id])));

        Assert.True(DieStats.HasKeyword(state, target, "Overcrush"));
        Assert.Equal(attackBefore + 2, DieStats.EffectiveAttack(state, target));

        state.CurrentStep = TurnStep.CleanUp;
        TurnEngine.CleanUp(state, new FixedRoller(DieStatus.Energy, 1), queue);

        Assert.False(DieStats.HasKeyword(state, target, "Overcrush"));
        Assert.Equal(attackBefore, DieStats.EffectiveAttack(state, target));
    }

    [Fact]
    public void FieldingPsylockeTelepath_GrantsOvercrushToTargetDie()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.PsylockeTelepath.Id]);
        state.ActivePlayerId = "teamA";

        var target = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        target.Zone = Zone.FieldZone;
        target.Status = DieStatus.Character;
        target.Level = 1;
        Assert.False(DieStats.HasKeyword(state, target, "Overcrush"));

        var psylockeDie = FindUnpurchased(state, "teamA", SampleCards.PsylockeTelepath.Id);
        var purchaseEnergy = GiveWildEnergy(state, "teamA", SampleCards.PsylockeTelepath.PurchaseCost);
        TurnEngine.Purchase(state, psylockeDie.Id, purchaseEnergy.Select(d => d.Id).ToList());
        psylockeDie.Zone = Zone.ReservePool;
        psylockeDie.Status = DieStatus.Character;
        psylockeDie.Level = 1;

        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, psylockeDie.Id, energyDieIdsToSpend: []);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [target.Id])));

        Assert.True(DieStats.HasKeyword(state, target, "Overcrush"));
    }

    [Fact]
    public void FieldingStormCloudCover_OnlyAcceptsATargetWith3AttackOrLess()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.StormCloudCover.Id]);
        state.ActivePlayerId = "teamA";

        var lowAttackTarget = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        lowAttackTarget.Zone = Zone.FieldZone;
        lowAttackTarget.Status = DieStatus.Character;
        lowAttackTarget.Level = 1; // PlaceholderLevels: 1A

        var highAttackTarget = FindUnpurchased(state, "teamB", SampleCards.Groot.Id);
        highAttackTarget.Zone = Zone.FieldZone;
        highAttackTarget.Status = DieStatus.Character;
        highAttackTarget.Level = 3; // PlaceholderLevels: 4A - over the "3A or less" threshold

        var ability = SampleCards.StormCloudCover.Abilities.Single(a => a.Trigger == TriggerType.WhenFielded);

        var ex = Assert.Throws<InvalidOperationException>(() => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, "teamA", SourceDieId: null, _ => [highAttackTarget.Id])));
        Assert.Contains("not legal", ex.Message);

        EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, "teamA", SourceDieId: null, _ => [lowAttackTarget.Id]));
        Assert.Contains(lowAttackTarget.Id, state.CantBlockThisTurn);
    }

    [Fact]
    public void FieldingMasterMoldTargetingMutants_OnlyAcceptsABrotherhoodOfMutantsTarget()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.MasterMoldTargetingMutants.Id],
            extraTeamBCardIds: [SampleCards.Magneto.Id]); // Brotherhood of Mutants

        var legalTarget = FindUnpurchased(state, "teamB", SampleCards.Magneto.Id);
        legalTarget.Zone = Zone.FieldZone;
        legalTarget.Status = DieStatus.Character;
        legalTarget.Level = 1;

        var illegalTarget = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id); // no affiliation
        illegalTarget.Zone = Zone.FieldZone;
        illegalTarget.Status = DieStatus.Character;
        illegalTarget.Level = 1;

        var ability = SampleCards.MasterMoldTargetingMutants.Abilities.Single(a => a.Trigger == TriggerType.WhenFielded);

        var ex = Assert.Throws<InvalidOperationException>(() => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, "teamA", SourceDieId: null, _ => [illegalTarget.Id])));
        Assert.Contains("not legal", ex.Message);

        EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, "teamA", SourceDieId: null, _ => [legalTarget.Id]));
        Assert.Equal(Zone.PrepArea, legalTarget.Zone); // KO'd
    }

    [Fact]
    public void FieldingMasterMoldUntoldElectronicExpertise_OnlyAcceptsAnXMenTarget()
    {
        // GambitUnlessIGotSomeoneToPlayWith is X-Men-affiliated and doesn't
        // need to be on either live roster to serve as a target here.
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.MasterMoldUntoldElectronicExpertise.Id],
            extraTeamBCardIds: [SampleCards.GambitUnlessIGotSomeoneToPlayWith.Id]);

        var legalTarget = FindUnpurchased(state, "teamB", SampleCards.GambitUnlessIGotSomeoneToPlayWith.Id);
        legalTarget.Zone = Zone.FieldZone;
        legalTarget.Status = DieStatus.Character;
        legalTarget.Level = 3; // 6D - survives being KO'd trivially, just proving legality here

        var illegalTarget = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id); // no affiliation
        illegalTarget.Zone = Zone.FieldZone;
        illegalTarget.Status = DieStatus.Character;
        illegalTarget.Level = 1;

        var ability = SampleCards.MasterMoldUntoldElectronicExpertise.Abilities.Single(a => a.Trigger == TriggerType.WhenFielded);

        var ex = Assert.Throws<InvalidOperationException>(() => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, "teamA", SourceDieId: null, _ => [illegalTarget.Id])));
        Assert.Contains("not legal", ex.Message);

        EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, "teamA", SourceDieId: null, _ => [legalTarget.Id]));
        Assert.Equal(Zone.PrepArea, legalTarget.Zone); // KO'd - a real Ko despite 6D, since abilities aren't blocked by defense
    }

    [Fact]
    public void FieldingMasterMoldInexplicableDurability_DamagesEveryMatchingDie_NoChoiceNeeded()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.MasterMoldInexplicableDurability.Id],
            extraTeamBCardIds: [SampleCards.Magneto.Id, SampleCards.GambitUnlessIGotSomeoneToPlayWith.Id]);

        var brotherhoodDie = FindUnpurchased(state, "teamB", SampleCards.Magneto.Id);
        brotherhoodDie.Zone = Zone.FieldZone;
        brotherhoodDie.Status = DieStatus.Character;
        brotherhoodDie.Level = 1; // 4D - survives 2 damage

        var xMenDie = FindUnpurchased(state, "teamB", SampleCards.GambitUnlessIGotSomeoneToPlayWith.Id);
        xMenDie.Zone = Zone.FieldZone;
        xMenDie.Status = DieStatus.Character;
        xMenDie.Level = 3; // 6D - survives 2 damage

        var unaffiliatedDie = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        unaffiliatedDie.Zone = Zone.FieldZone;
        unaffiliatedDie.Status = DieStatus.Character;
        unaffiliatedDie.Level = 3; // 4D - survives, but shouldn't be hit at all

        var ability = SampleCards.MasterMoldInexplicableDurability.Abilities.Single(a => a.Trigger == TriggerType.WhenFielded);
        // MatchAll bypasses target resolution entirely - a resolver that
        // throws proves nothing was ever asked to choose.
        EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(state, "teamA", SourceDieId: null, _ => throw new InvalidOperationException("MatchAll shouldn't ask for a choice")));

        Assert.Equal(2, brotherhoodDie.Damage);
        Assert.Equal(2, xMenDie.Damage);
        Assert.Equal(0, unaffiliatedDie.Damage);
    }

    [Fact]
    public void PhoenixEternalFlameAttacking_BarsOnlyLowAttackOpposingDice_EnforcedAtDeclareBlockers()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.PhoenixEternalFlame.Id]);
        state.ActivePlayerId = "teamA";

        var lowAttackOpponent = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        lowAttackOpponent.Zone = Zone.FieldZone;
        lowAttackOpponent.Status = DieStatus.Character;
        lowAttackOpponent.Level = 1; // PlaceholderLevels: 1A

        var highAttackOpponent = FindUnpurchased(state, "teamB", SampleCards.Groot.Id);
        highAttackOpponent.Zone = Zone.FieldZone;
        highAttackOpponent.Status = DieStatus.Character;
        highAttackOpponent.Level = 3; // PlaceholderLevels: 4A - over the "less than 4A" threshold

        var phoenixDie = FindUnpurchased(state, "teamA", SampleCards.PhoenixEternalFlame.Id);
        phoenixDie.Zone = Zone.FieldZone;
        phoenixDie.Status = DieStatus.Character;
        phoenixDie.Level = 1;

        state.CurrentStep = TurnStep.Attack;
        state.AttackSubStep = AttackSubStep.DeclareAttackers;
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [phoenixDie.Id]);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => throw new InvalidOperationException("MatchAll shouldn't ask for a choice"))));

        Assert.Contains(lowAttackOpponent.Id, state.CantBlockThisTurn);
        Assert.DoesNotContain(highAttackOpponent.Id, state.CantBlockThisTurn);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CombatEngine.DeclareBlockers(state, new CombatAssignment(), blockerDieIds: [lowAttackOpponent.Id]));
        Assert.Contains("not an eligible blocker", ex.Message);

        // The high-attack die is unaffected - still a legal blocker.
        CombatEngine.DeclareBlockers(state, new CombatAssignment(), blockerDieIds: [highAttackOpponent.Id]);
        Assert.Equal(Zone.AttackZone, highAttackOpponent.Zone);
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

    // Regression: PrepFromBagIfPurchasedThisTurn/PrepFromBag used to pick
    // straight from Zone.Bag without TurnEngine.DrawFromBag's own "refill
    // from the Used Pile when the Bag is empty" step, so the ability
    // silently no-op'd (not even an error) whenever the Bag itself was
    // empty but the Used Pile had dice - exactly what a real game hits
    // once a player's first lap through their own dice finishes.
    [Fact]
    public void UsingStarfireGlobalAbility_RecyclesUsedPileIntoBag_WhenBagIsEmpty()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamB";

        // Pull out the energy this test needs first (GiveWildEnergy sources
        // from Zone.Bag), then dump whatever's left of the Bag into the
        // Used Pile - same shape a real game reaches once a player's first
        // lap through their own dice finishes and the Bag runs dry.
        var purchaseEnergy = GiveWildEnergy(state, "teamB", 3);
        var globalEnergy = GiveWildEnergy(state, "teamB", 1);
        foreach (var die in state.DiceIn("teamB", Zone.Bag).ToList()) die.Zone = Zone.UsedPile;
        Assert.True(state.DiceIn("teamB", Zone.UsedPile).Count() > 0);
        Assert.Empty(state.DiceIn("teamB", Zone.Bag));

        var toBuy = FindUnpurchased(state, "teamB", SampleCards.GodEmperorDoom.Id);
        TurnEngine.Purchase(state, toBuy.Id, purchaseEnergy.Select(d => d.Id).ToList());
        Assert.True(state.PlayerTwo.PurchasedDieThisTurn);

        var queue = new AbilityQueue();
        TurnEngine.UseGlobalAbility(
            state, queue, SampleCards.Starfire.Id, "teamB", globalEnergy.Select(d => d.Id).ToList());
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [])));

        Assert.Single(state.DiceIn("teamB", Zone.PrepArea));
    }

    [Fact]
    public void Fielding_WithATypedDoubleEnergyDie_SpinsDownTheLeftoverInsteadOfSpendingTheWholeDie()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.BigBarda.Id]);
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
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.BigBarda.Id]);
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
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.BigBarda.Id]);
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
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.BigBarda.Id]);
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
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.BigBarda.Id]);
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

    [Fact]
    public void UseGlobalAbility_InactivePlayerSpendingAGenericDouble_LosesTheLeftoverInsteadOfBankingIt()
    {
        // None of the scripted sample Globals accept plain generic energy
        // (they all require a specific type) - inject one that does, since
        // that's the only way a Generic double die can pay for a Global at
        // all (rule 2.6.2.3-style type matching applies the same way here).
        // Has to go in before GameState.NewGame - CardCatalog is read-only
        // once the game exists.
        var anyEnergyGlobal = new CardDef
        {
            Id = "test-any-energy-global", Name = "Test Any-Energy Global",
            Type = CardType.BasicAction, PurchaseCost = 1, DieLimit = 3,
            Abilities = [new AbilityDef(TriggerType.Global, Cost: null, Effect: new GainLife(0),
                EnergyCost: new EnergyCost(Amount: 1, RequiredType: null))],
        };
        var catalog = new Dictionary<string, CardDef>(SampleCards.BuildCatalog()) { [anyEnergyGlobal.Id] = anyEnergyGlobal };
        var teamA = new Player { Id = "teamA", Name = "Team A" };
        teamA.TeamCardIds.AddRange(SampleCards.TeamACharacterIds);
        teamA.TeamCardIds.AddRange(SampleCards.TeamABasicActionIds);
        var teamB = new Player { Id = "teamB", Name = "Team B" };
        teamB.TeamCardIds.AddRange(SampleCards.TeamBCharacterIds);
        teamB.TeamCardIds.AddRange(SampleCards.TeamBBasicActionIds);
        var state = GameState.NewGame(catalog, teamA, teamB);
        state.CurrentStep = TurnStep.Main;
        state.ActivePlayerId = "teamA"; // teamB is the Inactive player here

        var doubleGeneric = GiveDoubleEnergy(state, "teamB", 1, EnergyKind.Generic)[0];

        var queue = new AbilityQueue();
        TurnEngine.UseGlobalAbility(state, queue, anyEnergyGlobal.Id, "teamB", [doubleGeneric.Id]);

        // Rule 2.6.1.2 - the Inactive player's spent energy goes straight
        // to the Used Pile. Rule 2.6.1.6 only grants virtual-energy banking
        // to the Active player, so the unspent half of teamB's Generic
        // double is simply lost here, not tracked as virtual energy.
        Assert.Equal(Zone.UsedPile, doubleGeneric.Zone);
        Assert.DoesNotContain(state.Dice, d => d.IsVirtualEnergy);
    }

    [Fact]
    public void AntManAmplify_UsingAnActionDie_SpinsOwnAmplifyDieUpOneLevel_ButNotOpponents()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";

        // Real card on both sides - proves Amplify only reacts to its own
        // controller's Action-die usage, not "any Action die in the game."
        var ownAmplify = new DieInstance
        {
            Id = "teamA-antman-1", CardId = SampleCards.AntManAmplify.Id, OwnerId = "teamA", ControllerId = "teamA",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        var opponentAmplify = new DieInstance
        {
            Id = "teamB-antman-1", CardId = SampleCards.AntManAmplify.Id, OwnerId = "teamB", ControllerId = "teamB",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(ownAmplify);
        state.Dice.Add(opponentAmplify);

        var shockingGraspDie = FindUnpurchased(state, "teamA", SampleCards.ShockingGrasp.Id);
        var purchaseEnergy = GiveWildEnergy(state, "teamA", SampleCards.ShockingGrasp.PurchaseCost);
        TurnEngine.Purchase(state, shockingGraspDie.Id, purchaseEnergy.Select(d => d.Id).ToList());
        shockingGraspDie.Zone = Zone.ReservePool;
        shockingGraspDie.Status = DieStatus.Action;

        var target = state.DiceFor("teamB").First(d => d.Zone == Zone.Bag);
        target.Zone = Zone.FieldZone;
        target.Status = DieStatus.SidekickCharacter;

        var queue = new AbilityQueue();
        TurnEngine.UseActionDie(state, queue, shockingGraspDie.Id);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [target.Id])));

        Assert.Equal(2, ownAmplify.Level); // spun up by teamA's own Action-die use
        Assert.Equal(1, opponentAmplify.Level); // untouched - not teamB's turn to act
    }

    [Fact]
    public void AntManAmplify_AtMaxLevel_DoesNotSpinPastIt()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";

        var maxedAmplify = new DieInstance
        {
            Id = "teamA-antman-1", CardId = SampleCards.AntManAmplify.Id, OwnerId = "teamA", ControllerId = "teamA",
            Zone = Zone.FieldZone, Status = DieStatus.Character,
            Level = SampleCards.AntManAmplify.Levels.Count, // already at max
        };
        state.Dice.Add(maxedAmplify);

        var shockingGraspDie = FindUnpurchased(state, "teamA", SampleCards.ShockingGrasp.Id);
        var purchaseEnergy = GiveWildEnergy(state, "teamA", SampleCards.ShockingGrasp.PurchaseCost);
        TurnEngine.Purchase(state, shockingGraspDie.Id, purchaseEnergy.Select(d => d.Id).ToList());
        shockingGraspDie.Zone = Zone.ReservePool;
        shockingGraspDie.Status = DieStatus.Action;

        var queue = new AbilityQueue();
        TurnEngine.UseActionDie(state, queue, shockingGraspDie.Id); // Amplify's spin happens regardless of draining ShockingGrasp's own queued ability

        Assert.Equal(SampleCards.AntManAmplify.Levels.Count, maxedAmplify.Level); // unchanged - "if able"
    }

    [Fact]
    public void BlackPantherEnergize_RolledOnDoubleEnergy_TriggersAndRollsTwoFreshDiceFromBag()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";
        state.CurrentStep = TurnStep.RollAndReroll;

        var blackPanther = FindUnpurchased(state, "teamA", SampleCards.BlackPanther.Id);
        blackPanther.Zone = Zone.ReservePool;
        blackPanther.Status = DieStatus.Energy;
        blackPanther.EnergyKind = EnergyKind.Generic;
        blackPanther.EnergyAmount = 2;

        var bagCountBefore = state.DiceIn("teamA", Zone.Bag).Count();

        var queue = new AbilityQueue();
        TurnEngine.Reroll(state, queue, new FixedRoller(DieStatus.Energy, 1), []);

        Assert.Equal(1, queue.Count);
        Assert.Equal(TriggerType.Energize, queue.Pending[0].Trigger);

        var drawRoller = new FixedRoller(DieStatus.SidekickCharacter, 3);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [], Roller: drawRoller, Queue: queue)));

        Assert.Equal(bagCountBefore - 2, state.DiceIn("teamA", Zone.Bag).Count());
        Assert.Equal(2, state.DiceIn("teamA", Zone.ReservePool).Count(d => d.Status == DieStatus.SidekickCharacter));
    }

    private static DieInstance AddWasp(GameState state, string playerId, string suffix, Zone zone = Zone.FieldZone)
    {
        var die = new DieInstance
        {
            Id = $"{playerId}-wasp-{suffix}", CardId = SampleCards.Wasp.Id, OwnerId = playerId, ControllerId = playerId,
            Zone = zone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(die);
        return die;
    }

    [Fact]
    public void WaspAttune_UsingAnActionDie_DamagesChosenTargetAndBoostsWaspsStats()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";
        var wasp = AddWasp(state, "teamA", "1");

        var shockingGraspDie = FindUnpurchased(state, "teamA", SampleCards.ShockingGrasp.Id);
        var purchaseEnergy = GiveWildEnergy(state, "teamA", SampleCards.ShockingGrasp.PurchaseCost);
        TurnEngine.Purchase(state, shockingGraspDie.Id, purchaseEnergy.Select(d => d.Id).ToList());
        shockingGraspDie.Zone = Zone.ReservePool;
        shockingGraspDie.Status = DieStatus.Action;

        var queue = new AbilityQueue();
        TurnEngine.UseActionDie(state, queue, shockingGraspDie.Id);

        // ShockingGrasp's own WhenUsed + Attune's built-in damage + Wasp's
        // stat-boost follow-up, all queued from the one Action-die use.
        Assert.Equal(3, queue.Count);
        Assert.Contains(queue.Pending, a => a.Trigger == TriggerType.Attune && a.SourceDieId == wasp.Id);

        // ShockingGrasp's own spec only allows a character die, not a
        // player - so unlike Attune's, it needs a real die target here;
        // this drain's single flat resolver (the same simplification
        // GamesController.Drain uses) picks one per spec based on shape.
        IReadOnlyList<string> ResolveTarget(TargetSpec spec) =>
            spec.PlayersAllowed ? [state.OpponentOf("teamA")] : LegalTargets.Query(state, "teamA", spec).Take(1).ToList();

        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(state, ability.ControllerId, ability.SourceDieId, ResolveTarget)));

        Assert.Equal(Player.StartingLife - 1, state.PlayerTwo.Life); // Attune's 1 damage, targeted at the opponent
        Assert.Equal(3, DieStats.EffectiveAttack(state, wasp)); // 2 base + 1 from her own Attune follow-up
        Assert.Equal(3, DieStats.EffectiveDefense(state, wasp)); // 2 base + 1
    }

    // Rule 3.4.3.9 - "until end of turn" means exactly that: Wasp's own
    // Attune buff expires at Clean Up even though she never left the
    // Field Zone (previously a real bug - TurnEngine.CleanUp never
    // cleared a survivor's AppliedModifiers at all).
    [Fact]
    public void WaspAttune_StatBoost_ExpiresAtCleanUpEvenThoughSheNeverLeftTheField()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";
        var wasp = AddWasp(state, "teamA", "1");

        var shockingGraspDie = FindUnpurchased(state, "teamA", SampleCards.ShockingGrasp.Id);
        var purchaseEnergy = GiveWildEnergy(state, "teamA", SampleCards.ShockingGrasp.PurchaseCost);
        TurnEngine.Purchase(state, shockingGraspDie.Id, purchaseEnergy.Select(d => d.Id).ToList());
        shockingGraspDie.Zone = Zone.ReservePool;
        shockingGraspDie.Status = DieStatus.Action;

        var queue = new AbilityQueue();
        TurnEngine.UseActionDie(state, queue, shockingGraspDie.Id);
        IReadOnlyList<string> ResolveTarget(TargetSpec spec) =>
            spec.PlayersAllowed ? [state.OpponentOf("teamA")] : LegalTargets.Query(state, "teamA", spec).Take(1).ToList();
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, ResolveTarget)));

        Assert.Equal(3, DieStats.EffectiveAttack(state, wasp)); // boosted, same as the test above

        TurnEngine.SkipAttackStep(state);
        TurnEngine.CleanUp(state);

        Assert.Equal(Zone.FieldZone, wasp.Zone); // never left the field
        Assert.Equal(2, DieStats.EffectiveAttack(state, wasp)); // back to base - the buff expired
        Assert.Equal(2, DieStats.EffectiveDefense(state, wasp));
    }

    [Fact]
    public void WaspAttune_CanTargetACharacterDieInstead()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";
        AddWasp(state, "teamA", "1");
        // 2D at level 1 (placeholder stats) - survives ShockingGrasp's own
        // 1 damage so it's still a legal target when Attune's own 1 damage
        // resolves next, KO'ing it on the combined total.
        var opposingTarget = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        opposingTarget.Zone = Zone.FieldZone;
        opposingTarget.Status = DieStatus.Character;
        opposingTarget.Level = 1;

        var shockingGraspDie = FindUnpurchased(state, "teamA", SampleCards.ShockingGrasp.Id);
        var purchaseEnergy = GiveWildEnergy(state, "teamA", SampleCards.ShockingGrasp.PurchaseCost);
        TurnEngine.Purchase(state, shockingGraspDie.Id, purchaseEnergy.Select(d => d.Id).ToList());
        shockingGraspDie.Zone = Zone.ReservePool;
        shockingGraspDie.Status = DieStatus.Action;

        var queue = new AbilityQueue();
        TurnEngine.UseActionDie(state, queue, shockingGraspDie.Id);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [opposingTarget.Id])));

        // ShockingGrasp's own damage (1) alone isn't lethal (2D); Attune's
        // follow-up 1 damage on the same die pushes it to exactly 2 and KOs it.
        Assert.Equal(Zone.PrepArea, opposingTarget.Zone);
    }

    [Fact]
    public void WaspAttune_TwoActiveDiceOfTheSameCharacter_EachTriggersItsOwnInstance()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";
        var wasp1 = AddWasp(state, "teamA", "1");
        var wasp2 = AddWasp(state, "teamA", "2", Zone.AttackZone);

        var shockingGraspDie = FindUnpurchased(state, "teamA", SampleCards.ShockingGrasp.Id);
        var purchaseEnergy = GiveWildEnergy(state, "teamA", SampleCards.ShockingGrasp.PurchaseCost);
        TurnEngine.Purchase(state, shockingGraspDie.Id, purchaseEnergy.Select(d => d.Id).ToList());
        shockingGraspDie.Zone = Zone.ReservePool;
        shockingGraspDie.Status = DieStatus.Action;

        var queue = new AbilityQueue();
        TurnEngine.UseActionDie(state, queue, shockingGraspDie.Id);

        // "No matter how many of that Character's dice are active" - two
        // active Wasps means two Attune damage instances and two of her
        // own stat-boost follow-ups, plus ShockingGrasp's own WhenUsed.
        Assert.Equal(5, queue.Count);
        Assert.Equal(2, queue.Pending.Count(a => a.Trigger == TriggerType.Attune && a.SourceDieId == wasp1.Id));
        Assert.Equal(2, queue.Pending.Count(a => a.Trigger == TriggerType.Attune && a.SourceDieId == wasp2.Id));
    }

    [Fact]
    public void WaspAttune_DoesNotFireForAnInactiveDieOrTheOpponentsDie()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";
        AddWasp(state, "teamA", "reserve", Zone.ReservePool); // not active - Attune requires the Field/Attack Zone
        AddWasp(state, "teamB", "opponent"); // active, but not this player's turn to trigger it

        var shockingGraspDie = FindUnpurchased(state, "teamA", SampleCards.ShockingGrasp.Id);
        var purchaseEnergy = GiveWildEnergy(state, "teamA", SampleCards.ShockingGrasp.PurchaseCost);
        TurnEngine.Purchase(state, shockingGraspDie.Id, purchaseEnergy.Select(d => d.Id).ToList());
        shockingGraspDie.Zone = Zone.ReservePool;
        shockingGraspDie.Status = DieStatus.Action;

        var queue = new AbilityQueue();
        TurnEngine.UseActionDie(state, queue, shockingGraspDie.Id);

        Assert.DoesNotContain(queue.Pending, a => a.Trigger == TriggerType.Attune);
    }

    [Fact]
    public void BlackWidowCallOut_RestrictsBlockingToHerChosenTarget()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";
        var blackWidow = new DieInstance
        {
            Id = "teamA-blackwidow-1", CardId = SampleCards.BlackWidow.Id, OwnerId = "teamA", ControllerId = "teamA",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(blackWidow);

        var target = state.DiceFor("teamB").First(d => d.Zone == Zone.Bag);
        target.Zone = Zone.FieldZone;
        target.Status = DieStatus.SidekickCharacter;
        var otherBlocker = state.DiceFor("teamB").Skip(1).First(d => d.Zone == Zone.Bag);
        otherBlocker.Zone = Zone.FieldZone;
        otherBlocker.Status = DieStatus.SidekickCharacter;

        TurnEngine.EnterAttackStep(state);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [blackWidow.Id]);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [target.Id])));

        var illegalAssignment = new CombatAssignment();
        illegalAssignment.AssignBlocker(blackWidow.Id, otherBlocker.Id); // not her Call Out target
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CombatEngine.DeclareBlockers(state, illegalAssignment, [otherBlocker.Id]));
        Assert.Contains("Called Out", ex.Message);

        var legalAssignment = new CombatAssignment();
        legalAssignment.AssignBlocker(blackWidow.Id, target.Id);
        CombatEngine.DeclareBlockers(state, legalAssignment, [target.Id]); // her actual target blocks legally
        Assert.Equal(Zone.AttackZone, target.Zone);
    }

    [Fact]
    public void PolarisCorrupt_WhenFielded_DrawsFromOpponentsBagAndSendsOneToUsedPile()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";
        var polaris = new DieInstance
        {
            Id = "teamA-polaris-1", CardId = SampleCards.Polaris.Id, OwnerId = "teamA", ControllerId = "teamA",
            Zone = Zone.ReservePool, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(polaris);

        var fieldEnergy = GiveWildEnergy(state, "teamA", 1); // level-1 fielding cost is 1
        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, polaris.Id, energyDieIdsToSpend: [fieldEnergy[0].Id]);

        Assert.Equal(1, queue.Count);

        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(state, ability.ControllerId, ability.SourceDieId, spec => spec.PlayersAllowed ? ["teamB"] : [])));

        // The post-draw "which one" choice always pauses via
        // PendingChoice now (see EffectInterpreter's Corrupt case).
        Assert.NotNull(state.PendingChoice);
        var chosenId = state.PendingChoice!.CandidateDieIds[0];
        state.PendingChoice.Resolve([chosenId]);

        var chosen = state.Dice.Single(d => d.Id == chosenId);
        Assert.Equal(Zone.UsedPile, chosen.Zone);
        Assert.Single(state.DiceIn("teamB", Zone.UsedPile));
    }

    [Fact]
    public void DeathbirdDeadly_BlockerSurvivesCombatDamage_ButIsStillKOdAtCleanUp()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";
        var deathbird = new DieInstance
        {
            Id = "teamA-deathbird-1", CardId = SampleCards.Deathbird.Id, OwnerId = "teamA", ControllerId = "teamA",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1, // 1A/1D
        };
        state.Dice.Add(deathbird);

        // Falcon's own team-setup die (placeholder stats: 1A/2D at level
        // 1) - reused rather than constructed fresh, to avoid colliding
        // with the id TeamSetup already gave it.
        var blocker = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        blocker.Zone = Zone.FieldZone;
        blocker.Status = DieStatus.Character;
        blocker.Level = 1;

        TurnEngine.EnterAttackStep(state);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [deathbird.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(deathbird.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, assignment, [blocker.Id]);

        var splits = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [deathbird.Id] = new Dictionary<string, int> { [blocker.Id] = 1 }, // Deathbird's full 1A
        };
        var result = CombatEngine.AssignCombatDamage(state, queue, assignment, splits);

        // Blocker's 2D easily absorbs 1 damage - survives combat outright.
        Assert.DoesNotContain(blocker.Id, result.KOdDieIds);
        Assert.Equal(Zone.FieldZone, blocker.Zone);
        Assert.Contains(blocker.Id, state.DeadlyEngagedDieIds); // recorded at Declare Blockers regardless

        TurnEngine.CleanUp(state);

        // Deadly still gets it at Clean Up, despite surviving combat outright.
        Assert.Equal(Zone.PrepArea, blocker.Zone);
    }

    [Fact]
    public void WaspPixieFast_KOsBlockerBeforeBlockerCanDealDamageBack()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";
        var wasp = new DieInstance
        {
            Id = "teamA-wasp-pixie-1", CardId = SampleCards.WaspPixie.Id, OwnerId = "teamA", ControllerId = "teamA",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 3, // 4A/3D
        };
        state.Dice.Add(wasp);

        var blocker = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        blocker.Zone = Zone.FieldZone;
        blocker.Status = DieStatus.Character;
        blocker.Level = 2; // placeholder stats: 2A/3D

        TurnEngine.EnterAttackStep(state);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [wasp.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(wasp.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, assignment, [blocker.Id]);

        var splits = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [wasp.Id] = new Dictionary<string, int> { [blocker.Id] = 4 },
        };
        var result = CombatEngine.AssignCombatDamage(state, queue, assignment, splits);

        Assert.Contains(blocker.Id, result.KOdDieIds);
        Assert.Equal(0, wasp.Damage); // Fast - the blocker never got to strike back
    }

    [Fact]
    public void MadalynePryorEnergyDrain_SpinsDownHerEngagedAttacker()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";

        var attacker = FindUnpurchased(state, "teamA", SampleCards.Apocalypse.Id);
        attacker.Zone = Zone.FieldZone;
        attacker.Status = DieStatus.Character;
        attacker.Level = 3;

        var madalyne = new DieInstance
        {
            Id = "teamB-madalyne-1", CardId = SampleCards.MadalynePryor.Id, OwnerId = "teamB", ControllerId = "teamB",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(madalyne);

        TurnEngine.EnterAttackStep(state);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, madalyne.Id);
        CombatEngine.DeclareBlockers(state, assignment, [madalyne.Id]);

        Assert.Equal(2, attacker.Level); // spun down 1 the moment blockers were assigned
        Assert.Equal(1, madalyne.Level); // Madalyne herself is untouched by her own keyword
    }

    [Fact]
    public void TheSpotInfiltrates_WithRicochetActive_DamagesOpponentAndDrawsRicochetADie()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";

        var theSpot = new DieInstance
        {
            Id = "teamA-thespot-1", CardId = SampleCards.TheSpot.Id, OwnerId = "teamA", ControllerId = "teamA",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        var ricochet = new DieInstance
        {
            Id = "teamA-ricochet-1", CardId = SampleCards.Ricochet.Id, OwnerId = "teamA", ControllerId = "teamA",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(theSpot);
        state.Dice.Add(ricochet);
        var prepAreaCountBefore = state.DiceIn("teamA", Zone.PrepArea).Count();

        TurnEngine.EnterAttackStep(state);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [theSpot.Id]);
        var assignment = new CombatAssignment();
        CombatEngine.DeclareBlockers(state, assignment, []); // teamB chooses not to block

        Assert.Equal(AttackSubStep.InfiltrateWindow, state.AttackSubStep);
        CombatEngine.ResolveInfiltrate(state, queue, assignment, [theSpot.Id]);

        Assert.Equal(Player.StartingLife - 1, state.PlayerTwo.Life);
        Assert.Equal(Zone.FieldZone, theSpot.Zone);

        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [])));

        // Ricochet's own reactive follow-up drew a die into the Prep Area.
        Assert.Equal(prepAreaCountBefore + 1, state.DiceIn("teamA", Zone.PrepArea).Count());
    }

    [Fact]
    public void ScarletSpiderIntimidate_RemovesOpposingDieUntilCleanUp()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";

        var scarletSpider = new DieInstance
        {
            Id = "teamA-scarletspider-1", CardId = SampleCards.ScarletSpider.Id, OwnerId = "teamA", ControllerId = "teamA",
            Zone = Zone.ReservePool, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(scarletSpider);

        var opposingTarget = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        opposingTarget.Zone = Zone.FieldZone;
        opposingTarget.Status = DieStatus.Character;
        opposingTarget.Level = 1;

        var fieldEnergy = GiveWildEnergy(state, "teamA", 1); // level-1 fielding cost is 1
        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, scarletSpider.Id, energyDieIdsToSpend: [fieldEnergy[0].Id]);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [opposingTarget.Id])));

        Assert.Equal(Zone.Intimidated, opposingTarget.Zone);

        // While Intimidated, it's simply not in the Field Zone - not a
        // legal blocker for this combat at all.
        TurnEngine.EnterAttackStep(state);
        var attacker = FindUnpurchased(state, "teamA", SampleCards.Apocalypse.Id);
        attacker.Zone = Zone.FieldZone;
        attacker.Status = DieStatus.Character;
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var illegalAssignment = new CombatAssignment();
        illegalAssignment.AssignBlocker(attacker.Id, opposingTarget.Id);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CombatEngine.DeclareBlockers(state, illegalAssignment, [opposingTarget.Id]));
        Assert.Contains("not an eligible blocker", ex.Message);

        // Combat proceeds normally otherwise; Clean Up returns it.
        var noBlockAssignment = new CombatAssignment();
        CombatEngine.DeclareBlockers(state, noBlockAssignment, []);
        CombatEngine.AssignCombatDamage(
            state, queue, noBlockAssignment, new Dictionary<string, IReadOnlyDictionary<string, int>>());
        TurnEngine.CleanUp(state);

        Assert.Equal(Zone.FieldZone, opposingTarget.Zone);
    }

    // Icons' Drow Mercenary, "Hired Blade" printing - pure Obscure. "When
    // you use an Action die" fires from ANY Action die (same shape as
    // AntManAmplify above), not just a Drow Mercenary die's own use, and
    // affects every die from that CardId until Clean Up.
    [Fact]
    public void DrowMercenaryObscure_UsingAnyActionDie_MakesItUnblockableUntilCleanUp()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";

        var drow = new DieInstance
        {
            Id = "teamA-drow-1", CardId = SampleCards.DrowMercenary.Id, OwnerId = "teamA", ControllerId = "teamA",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(drow);

        var opposingBlocker = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        opposingBlocker.Zone = Zone.FieldZone;
        opposingBlocker.Status = DieStatus.Character;

        var shockingGraspDie = FindUnpurchased(state, "teamA", SampleCards.ShockingGrasp.Id);
        var purchaseEnergy = GiveWildEnergy(state, "teamA", SampleCards.ShockingGrasp.PurchaseCost);
        TurnEngine.Purchase(state, shockingGraspDie.Id, purchaseEnergy.Select(d => d.Id).ToList());
        shockingGraspDie.Zone = Zone.ReservePool;
        shockingGraspDie.Status = DieStatus.Action;

        var queue = new AbilityQueue();
        TurnEngine.UseActionDie(state, queue, shockingGraspDie.Id); // an unrelated Action die - not Drow Mercenary's own
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [opposingBlocker.Id])));

        Assert.Contains(SampleCards.DrowMercenary.Id, state.ObscuredCardIds);

        TurnEngine.EnterAttackStep(state);
        CombatEngine.DeclareAttackers(state, queue, [drow.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(drow.Id, opposingBlocker.Id);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CombatEngine.DeclareBlockers(state, assignment, [opposingBlocker.Id]));
        Assert.Contains("unblockable", ex.Message);

        // Unblocked, it resolves as an ordinary attacker; Clean Up expires the effect.
        var noBlockAssignment = new CombatAssignment();
        CombatEngine.DeclareBlockers(state, noBlockAssignment, []);
        CombatEngine.AssignCombatDamage(
            state, queue, noBlockAssignment, new Dictionary<string, IReadOnlyDictionary<string, int>>());
        TurnEngine.CleanUp(state);

        Assert.Empty(state.ObscuredCardIds);
    }

    // Justice League's Black Manta, "Deep Sea Deviant" printing - Retaliation
    // with its own base amount entirely redefined by card text ("for each
    // of your active Villains" instead of the keyword's default 1). Three
    // Black Manta dice (same CardId - clarification 2's "only once per
    // unique character" dedup applies even to itself) stand in for a small
    // Legion of Doom board: one dies in combat, and the other two (still
    // active afterward) are what the surviving Retaliator counts.
    [Fact]
    public void BlackMantaRetaliation_TriggersOnceScaledByActiveVillainsRemainingAfterTheKO()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamB";

        var mantaA = new DieInstance
        {
            Id = "teamA-manta-1", CardId = SampleCards.BlackMantaDeepSeaDeviant.Id, OwnerId = "teamA", ControllerId = "teamA",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        var mantaB = new DieInstance
        {
            Id = "teamA-manta-2", CardId = SampleCards.BlackMantaDeepSeaDeviant.Id, OwnerId = "teamA", ControllerId = "teamA",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        var mantaC = new DieInstance
        {
            Id = "teamA-manta-3", CardId = SampleCards.BlackMantaDeepSeaDeviant.Id, OwnerId = "teamA", ControllerId = "teamA",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.AddRange([mantaA, mantaB, mantaC]);

        var attacker = new DieInstance
        {
            Id = "teamB-drow-1", CardId = SampleCards.DrowMercenary.Id, OwnerId = "teamB", ControllerId = "teamB",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(attacker);

        TurnEngine.EnterAttackStep(state);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, mantaC.Id);
        CombatEngine.DeclareBlockers(state, assignment, [mantaC.Id]);

        var splits = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [attacker.Id] = new Dictionary<string, int> { [mantaC.Id] = 3 }, // Drow's 3A - exactly Black Manta L1's 3D
        };
        CombatEngine.AssignCombatDamage(state, queue, assignment, splits);

        Assert.Equal(Zone.PrepArea, mantaC.Zone); // KO'd
        Assert.Equal(1, queue.Count); // mantaA and mantaB are the same character - one trigger, not two
        Assert.Equal(TriggerType.Retaliation, queue.Pending[0].Trigger);

        var opponentLifeBefore = state.GetPlayer("teamB").Life;
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => ["teamB"])));

        // 2 active Villains remain on teamA's side (mantaA + mantaB) once mantaC is gone.
        Assert.Equal(opponentLifeBefore - 2, state.GetPlayer("teamB").Life);
    }

    // Justice League's Bizarro, "More Than a Monster" printing - pure
    // Strike. No AbilityDef to drain here at all - DieStats.HasStrikeBonus
    // is a live check against GameState.FieldedThisTurn (populated by the
    // real TurnEngine.Field call below), not a triggered effect.
    [Fact]
    public void BizarroStrike_SoleCharacterFieldedThisTurn_GetsOvercrushAndStatBonus()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";

        var bizarro = new DieInstance
        {
            Id = "teamA-bizarro-1", CardId = SampleCards.BizarroMoreThanAMonster.Id, OwnerId = "teamA", ControllerId = "teamA",
            Zone = Zone.ReservePool, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(bizarro);

        var fieldEnergy = GiveWildEnergy(state, "teamA", 1); // level-1 fielding cost is 1
        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, bizarro.Id, energyDieIdsToSpend: [fieldEnergy[0].Id]);

        Assert.Equal(7, DieStats.EffectiveAttack(state, bizarro)); // base 5 + Strike's +2
        Assert.Equal(8, DieStats.EffectiveDefense(state, bizarro)); // base 6 + Strike's +2
        Assert.True(DieStats.HasKeyword(state, bizarro, "Overcrush"));
    }

    [Fact]
    public void BizarroStrike_AnotherCharacterAlsoFieldedThisTurn_BonusDoesNotApply()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";

        var bizarro = new DieInstance
        {
            Id = "teamA-bizarro-1", CardId = SampleCards.BizarroMoreThanAMonster.Id, OwnerId = "teamA", ControllerId = "teamA",
            Zone = Zone.ReservePool, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(bizarro);

        var bizarroEnergy = GiveWildEnergy(state, "teamA", 1);
        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, bizarro.Id, energyDieIdsToSpend: [bizarroEnergy[0].Id]);

        // A second, unrelated character die fielded later in the same turn.
        var apocalypseDie = FindUnpurchased(state, "teamA", SampleCards.Apocalypse.Id);
        var purchaseEnergy = GiveWildEnergy(state, "teamA", SampleCards.Apocalypse.PurchaseCost);
        TurnEngine.Purchase(state, apocalypseDie.Id, purchaseEnergy.Select(d => d.Id).ToList());
        apocalypseDie.Zone = Zone.ReservePool;
        apocalypseDie.Status = DieStatus.Character;
        apocalypseDie.Level = 1; // placeholder level-1 face: fielding cost 0
        TurnEngine.Field(state, queue, apocalypseDie.Id, energyDieIdsToSpend: []);

        Assert.Equal(5, DieStats.EffectiveAttack(state, bizarro)); // unmodified base - no longer the sole fielded die
        Assert.False(DieStats.HasKeyword(state, bizarro, "Overcrush"));
    }

    // Real Captain Marvel, already on teamA's roster - "While Captain
    // Marvel is active, your Character dice get +1 attack and +1
    // defense." No AbilityDef to drain - DieStats.StaticTeamBonusFor is a
    // live check, same "no trigger at all" shape as Strike.
    [Fact]
    public void CaptainMarvelStaticBonus_BoostsHerOwnTeamsActiveCharacterDice_NotTheOpponents()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.BigBarda.Id]);

        var captainMarvel = FindUnpurchased(state, "teamA", SampleCards.CaptainMarvel.Id);
        captainMarvel.Zone = Zone.FieldZone;
        captainMarvel.Status = DieStatus.Character;
        captainMarvel.Level = 1;

        var ally = FindUnpurchased(state, "teamA", SampleCards.BigBarda.Id);
        ally.Zone = Zone.FieldZone;
        ally.Status = DieStatus.Character;
        ally.Level = 1;

        var opposing = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        opposing.Zone = Zone.FieldZone;
        opposing.Status = DieStatus.Character;
        opposing.Level = 1;

        var captainMarvelBaseAttack = SampleCards.CaptainMarvel.Levels[0].Attack;
        var allyBaseAttack = SampleCards.BigBarda.Levels[0].Attack;
        var opposingBaseAttack = SampleCards.Falcon.Levels[0].Attack;

        Assert.Equal(captainMarvelBaseAttack + 1, DieStats.EffectiveAttack(state, captainMarvel)); // her own aura applies to herself too
        Assert.Equal(allyBaseAttack + 1, DieStats.EffectiveAttack(state, ally));
        Assert.Equal(opposingBaseAttack, DieStats.EffectiveAttack(state, opposing)); // not "your" character dice

        // Rule 3.6.6 - the bonus disappears the instant she's no longer active.
        captainMarvel.Zone = Zone.PrepArea;
        Assert.Equal(allyBaseAttack, DieStats.EffectiveAttack(state, ally));
    }

    // Real Falcon ("Take Flight," teamB roster) and Black Panther
    // ("Clutching Reality," teamA roster) share the real "Avengers"
    // affiliation - constructed under the same controller here since
    // Teamwatch is a same-controller reaction, regardless of which
    // roster each card normally belongs to.
    [Fact]
    public void FalconTeamwatch_FieldingADifferentAffiliatedCharacter_PrepsASidekickFromUsedPile()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamB";

        var falconDie = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        falconDie.Zone = Zone.FieldZone;
        falconDie.Status = DieStatus.Character;
        falconDie.Level = 1;

        var blackPanther = new DieInstance
        {
            Id = "teamB-blackpanther-1", CardId = SampleCards.BlackPanther.Id, OwnerId = "teamB", ControllerId = "teamB",
            Zone = Zone.ReservePool, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(blackPanther);

        var usedPileSidekick = state.DiceIn("teamB", Zone.Bag).First();
        usedPileSidekick.Zone = Zone.UsedPile;

        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, blackPanther.Id, energyDieIdsToSpend: []); // placeholder level-1 fielding cost is 0

        // Black Panther's own "when fielded, roll a die from your bag" also
        // fires from this same Field call - Falcon's Teamwatch is the one
        // under test here, found alongside it rather than assumed alone.
        Assert.Contains(queue.Pending, a => a.Trigger == TriggerType.Teamwatch && a.SourceDieId == falconDie.Id);

        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [usedPileSidekick.Id])));

        Assert.Equal(Zone.PrepArea, usedPileSidekick.Zone); // Prepped from the Used Pile by Falcon's Teamwatch
    }

    // Real "Spidey's Last Stand" Basic Action - Sacrifice paired with an
    // already-buildable effect, no optional "if you do" branching (using
    // the Action die at all is the opt-in moment).
    [Fact]
    public void UsingSpideysLastStandActionDie_SacrificesACharacterAndDrawsTwoDice_ThroughTheRealQueue()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";

        // Not on either team roster - constructed directly, same pattern
        // used for other non-roster cards' end-to-end tests.
        var spideysLastStandDie = new DieInstance
        {
            Id = "teamA-spideys-last-stand-1", CardId = SampleCards.SpideysLastStand.Id,
            OwnerId = "teamA", ControllerId = "teamA", Zone = Zone.ReservePool, Status = DieStatus.Action,
        };
        state.Dice.Add(spideysLastStandDie);

        var sacrificeTarget = FindUnpurchased(state, "teamA", SampleCards.Apocalypse.Id);
        sacrificeTarget.Zone = Zone.FieldZone;
        sacrificeTarget.Status = DieStatus.Character;

        var bagCountBefore = state.DiceIn("teamA", Zone.Bag).Count();

        var queue = new AbilityQueue();
        TurnEngine.UseActionDie(state, queue, spideysLastStandDie.Id);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [sacrificeTarget.Id])));

        // Sacrificed on its own owner's turn - Out of Play, not Prep Area
        // (not a KO at all - never went through ForceKO/Regenerate).
        Assert.Equal(Zone.OutOfPlay, sacrificeTarget.Zone);
        Assert.Equal(DieStatus.Unrolled, sacrificeTarget.Status);

        Assert.Equal(bagCountBefore - 2, state.DiceIn("teamA", Zone.Bag).Count());
        Assert.Equal(2, state.DiceIn("teamA", Zone.ReservePool).Count());
    }

    // WWE's Big E, "Tag Team Champion" printing - pure Tag Out, no
    // AbilityDef to drain at all. Not on either roster - constructed
    // directly, same pattern as the other non-roster real cards.
    [Fact]
    public void BigETagOut_PrepsItselfToBuffAnAttackerUntilCleanUp()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";

        var attacker = FindUnpurchased(state, "teamA", SampleCards.Apocalypse.Id);
        attacker.Zone = Zone.FieldZone;
        attacker.Status = DieStatus.Character;

        var bigE = new DieInstance
        {
            Id = "teamA-big-e-1", CardId = SampleCards.BigE.Id, OwnerId = "teamA", ControllerId = "teamA",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(bigE);

        var attackerBaseAttack = SampleCards.Apocalypse.Levels[0].Attack;
        var attackerBaseDefense = SampleCards.Apocalypse.Levels[0].Defense;

        TurnEngine.EnterAttackStep(state);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        CombatEngine.DeclareBlockers(state, new CombatAssignment(), []);

        Assert.Equal(AttackSubStep.TagOutWindow, state.AttackSubStep);
        CombatEngine.ResolveTagOut(state, queue, [(bigE.Id, attacker.Id)]);

        Assert.Equal(Zone.PrepArea, bigE.Zone);
        Assert.Equal(attackerBaseAttack + 2, DieStats.EffectiveAttack(state, attacker));
        Assert.Equal(attackerBaseDefense + 2, DieStats.EffectiveDefense(state, attacker));

        CombatEngine.AssignCombatDamage(
            state, queue, new CombatAssignment(), new Dictionary<string, IReadOnlyDictionary<string, int>>());
        TurnEngine.CleanUp(state);

        Assert.Equal(attackerBaseAttack, DieStats.EffectiveAttack(state, attacker)); // buff expired
    }

    // Justice League set's Starfire, "Starbolts" printing - pure Range 2.
    // Not on either roster - constructed directly, same pattern as the
    // other non-roster real cards.
    [Fact]
    public void StarfireRange_AttackingOpensTheRangeWindowAndDamagesAnOpposingDie()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";

        var starfire = new DieInstance
        {
            Id = "teamA-starfire-starbolts-1", CardId = SampleCards.StarfireStarbolts.Id,
            OwnerId = "teamA", ControllerId = "teamA", Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(starfire);

        var opposingTarget = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        opposingTarget.Zone = Zone.FieldZone;
        opposingTarget.Status = DieStatus.Character;
        opposingTarget.Level = 1;

        TurnEngine.EnterAttackStep(state);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [starfire.Id]);

        Assert.Equal(AttackSubStep.RangeWindow, state.AttackSubStep);
        CombatEngine.ResolveRange(state, queue, [(starfire.Id, opposingTarget.Id)], []);

        Assert.Equal(Zone.PrepArea, opposingTarget.Zone); // Range 2 vs Falcon's placeholder 2D - KO'd
        Assert.Equal(AttackSubStep.DeclareBlockers, state.AttackSubStep); // Range resolves before blockers exist
    }

    // Real Jamilah ("Shipwrecked on Chult") and real Drow Mercenary
    // ("Hired Blade," already cataloged for Obscure and now carrying the
    // "Monster" affiliation too) - neither on either roster, constructed
    // directly under the usual teamA/teamB controllers. KO'ing the Monster
    // goes through DieStats.ForceKO directly (the same real production
    // path combat/abilities/Range all funnel through) rather than
    // needing a full combat sequence, since the point under test is the
    // Experience token-granting itself, not how the KO happened.
    [Fact]
    public void JamilahExperience_KOingAnOpposingMonsterGrantsATokenAtCleanUp()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";

        var jamilah = new DieInstance
        {
            Id = "teamA-jamilah-1", CardId = SampleCards.JamilahShipwreckedOnChult.Id,
            OwnerId = "teamA", ControllerId = "teamA", Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(jamilah);

        var drowMercenary = new DieInstance
        {
            Id = "teamB-drow-1", CardId = SampleCards.DrowMercenary.Id,
            OwnerId = "teamB", ControllerId = "teamB", Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(drowMercenary);

        var jamilahBaseAttack = SampleCards.JamilahShipwreckedOnChult.Levels[0].Attack;
        var jamilahBaseDefense = SampleCards.JamilahShipwreckedOnChult.Levels[0].Defense;

        DieStats.ForceKO(state, drowMercenary);

        Assert.Equal(Zone.PrepArea, drowMercenary.Zone);
        Assert.True(state.OpposingMonsterKOdThisTurn);

        TurnEngine.SkipAttackStep(state);
        TurnEngine.CleanUp(state);

        Assert.Equal(1, state.ExperienceTokens[SampleCards.JamilahShipwreckedOnChult.Id]);
        Assert.Equal(jamilahBaseAttack + 1, DieStats.EffectiveAttack(state, jamilah));
        Assert.Equal(jamilahBaseDefense + 1, DieStats.EffectiveDefense(state, jamilah));
    }
}

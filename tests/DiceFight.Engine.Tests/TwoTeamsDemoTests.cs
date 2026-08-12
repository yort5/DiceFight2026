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
    public void MagikBetterThanBelasco_AwakenFiresOffRealSpinUp_RollsADieFromBag()
    {
        // Same "test the gate, not just the effect" bar as every other
        // keyword-gated trigger this session - EffectInterpreter's own
        // Spin case is the real path that calls TurnEngine.CheckAwaken,
        // which itself checks DieStats.HasKeyword, so this actually
        // proves the Awaken KeywordInstance is wired, not just the
        // AbilityDef's effect.
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.MagikBetterThanBelasco.Id]);
        var magikDie = FindUnpurchased(state, "teamA", SampleCards.MagikBetterThanBelasco.Id);
        magikDie.Zone = Zone.FieldZone;
        magikDie.Status = DieStatus.Character;
        magikDie.Level = 1;

        var queue = new AbilityQueue();
        EffectInterpreter.Execute(
            new Spin(TargetSpec.Self, +1),
            new EffectContext(state, "teamA", magikDie.Id, _ => [], Queue: queue));

        Assert.Equal(2, magikDie.Level);
        Assert.Equal(1, queue.Count);
        Assert.Equal(TriggerType.Awaken, queue.Pending[0].Trigger);

        var bagCountBefore = state.DiceIn("teamA", Zone.Bag).Count();
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(
                state, ability.ControllerId, ability.SourceDieId, _ => [], Roller: new FixedRoller(DieStatus.Energy, 1))));

        Assert.Equal(bagCountBefore - 1, state.DiceIn("teamA", Zone.Bag).Count());
    }

    [Fact]
    public void FieldingProfessorX_SpinsAnyOpposingDieToItsSingleEnergyFace()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.ProfessorXUncannyLeadership.Id]);
        state.ActivePlayerId = "teamA";

        var target = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        target.Zone = Zone.FieldZone;
        target.Status = DieStatus.Character;
        target.Level = 2;

        var profXDie = FindUnpurchased(state, "teamA", SampleCards.ProfessorXUncannyLeadership.Id);
        var purchaseEnergy = GiveWildEnergy(state, "teamA", SampleCards.ProfessorXUncannyLeadership.PurchaseCost);
        TurnEngine.Purchase(state, profXDie.Id, purchaseEnergy.Select(d => d.Id).ToList());
        profXDie.Zone = Zone.ReservePool;
        profXDie.Status = DieStatus.Character;
        profXDie.Level = 1;

        var fieldEnergy = GiveWildEnergy(state, "teamA", 1);
        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, profXDie.Id, energyDieIdsToSpend: [fieldEnergy[0].Id]);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [target.Id])));

        Assert.Equal(DieStatus.Energy, target.Status);
        Assert.Equal(1, target.EnergyAmount);
        Assert.Equal(EnergyKind.Specific, target.EnergyKind);
        Assert.Equal(SampleCards.Falcon.EnergyTypes[0], target.ProvidedEnergyType);
    }

    [Fact]
    public void ProfessorXUncannyLeadershipEnergize_FiresOffRealGate_MovesAnXMenDieFromUsedPileToPrepArea()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds:
            [SampleCards.ProfessorXUncannyLeadership.Id, SampleCards.GambitUnlessIGotSomeoneToPlayWith.Id]);
        state.ActivePlayerId = "teamA";
        state.CurrentStep = TurnStep.RollAndReroll;

        var profXDie = FindUnpurchased(state, "teamA", SampleCards.ProfessorXUncannyLeadership.Id);
        profXDie.Zone = Zone.ReservePool;
        profXDie.Status = DieStatus.Energy;
        profXDie.EnergyKind = EnergyKind.Generic;
        profXDie.EnergyAmount = 2; // double energy - Energize's own trigger condition

        var xMenDie = FindUnpurchased(state, "teamA", SampleCards.GambitUnlessIGotSomeoneToPlayWith.Id);
        xMenDie.Zone = Zone.UsedPile;

        var queue = new AbilityQueue();
        TurnEngine.Reroll(state, queue, new FixedRoller(DieStatus.Energy, 1), []);

        Assert.Equal(1, queue.Count);
        Assert.Equal(TriggerType.Energize, queue.Pending[0].Trigger);

        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [xMenDie.Id])));

        Assert.Equal(Zone.PrepArea, xMenDie.Zone);
    }

    [Fact]
    public void IcemanIcyInterference_OnlyAcceptsALevel1OpposingCharacterDie()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.IcemanIcyInterference.Id]);
        state.ActivePlayerId = "teamA";

        var level1Target = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        level1Target.Zone = Zone.FieldZone;
        level1Target.Status = DieStatus.Character;
        level1Target.Level = 1;

        var level2Target = FindUnpurchased(state, "teamB", SampleCards.Groot.Id);
        level2Target.Zone = Zone.FieldZone;
        level2Target.Status = DieStatus.Character;
        level2Target.Level = 2;

        var icemanDie = FindUnpurchased(state, "teamA", SampleCards.IcemanIcyInterference.Id);
        icemanDie.Zone = Zone.FieldZone;
        icemanDie.Status = DieStatus.Character;
        icemanDie.Level = 1;

        state.CurrentStep = TurnStep.Attack;
        state.AttackSubStep = AttackSubStep.DeclareAttackers;
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [icemanDie.Id]);
        Assert.Equal(1, queue.Count);
        Assert.Equal(TriggerType.WhenAttacks, queue.Pending[0].Trigger);

        var ability = SampleCards.IcemanIcyInterference.Abilities.Single(a => a.Trigger == TriggerType.WhenAttacks);

        var ex = Assert.Throws<InvalidOperationException>(() => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, "teamA", icemanDie.Id, _ => [level2Target.Id])));
        Assert.Contains("not legal", ex.Message);

        EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, "teamA", icemanDie.Id, _ => [level1Target.Id]));
        Assert.Equal(DieStatus.Energy, level1Target.Status);
    }

    [Fact]
    public void CyclopsDefendingThePhoenixEnergize_FiresOffRealGate_DamagesTargetAndRerollsSelf()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.CyclopsDefendingThePhoenix.Id]);
        state.ActivePlayerId = "teamA";
        state.CurrentStep = TurnStep.RollAndReroll;

        var cyclopsDie = FindUnpurchased(state, "teamA", SampleCards.CyclopsDefendingThePhoenix.Id);
        cyclopsDie.Zone = Zone.ReservePool;
        cyclopsDie.Status = DieStatus.Energy;
        cyclopsDie.EnergyKind = EnergyKind.Generic;
        cyclopsDie.EnergyAmount = 2; // double energy - Energize's own trigger condition

        var target = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        target.Zone = Zone.FieldZone;
        target.Status = DieStatus.Character;
        target.Level = 1;

        var queue = new AbilityQueue();
        TurnEngine.Reroll(state, queue, new FixedRoller(DieStatus.Energy, 1), []);

        Assert.Equal(1, queue.Count);
        Assert.Equal(TriggerType.Energize, queue.Pending[0].Trigger);

        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(
                state, ability.ControllerId, ability.SourceDieId, _ => [target.Id],
                Roller: new FixedRoller(DieStatus.Character, 3))));

        Assert.Equal(1, target.Damage);
        Assert.Equal(DieStatus.Character, cyclopsDie.Status); // rerolled itself
        Assert.Equal(3, cyclopsDie.Level);
    }

    [Fact]
    public void RogueStrengthAbsorptionEnergize_SetsTargetAttackToZero_ButOnlyUntilEndOfTurn()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.RogueStrengthAbsorption.Id]);
        state.ActivePlayerId = "teamA";
        state.CurrentStep = TurnStep.RollAndReroll;

        var rogueDie = FindUnpurchased(state, "teamA", SampleCards.RogueStrengthAbsorption.Id);
        rogueDie.Zone = Zone.ReservePool;
        rogueDie.Status = DieStatus.Energy;
        rogueDie.EnergyKind = EnergyKind.Generic;
        rogueDie.EnergyAmount = 2;

        var target = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        target.Zone = Zone.FieldZone;
        target.Status = DieStatus.Character;
        target.Level = 3; // PlaceholderLevels: 4A
        var attackBefore = DieStats.EffectiveAttack(state, target);
        Assert.NotEqual(0, attackBefore);

        var queue = new AbilityQueue();
        TurnEngine.Reroll(state, queue, new FixedRoller(DieStatus.Energy, 1), []);
        Assert.Equal(1, queue.Count);
        Assert.Equal(TriggerType.Energize, queue.Pending[0].Trigger);

        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [target.Id])));

        Assert.Equal(0, DieStats.EffectiveAttack(state, target));

        state.CurrentStep = TurnStep.CleanUp;
        TurnEngine.CleanUp(state, new FixedRoller(DieStatus.Energy, 1));

        Assert.Equal(attackBefore, DieStats.EffectiveAttack(state, target));
    }

    [Fact]
    public void MoiraIfItsReal_GetsPlusOneDefense_OnlyWhileAWolverineDieIsActive()
    {
        // ASM074 is a bulk-only "Wolverine" card (no AbilityDef, but a
        // real Name) - all GrantsSelfStatBonusWhileNamedCardActive needs
        // is a die whose card is named "Wolverine" active somewhere.
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.MoiraIfItsReal.Id, "ASM074"]);

        var moiraDie = FindUnpurchased(state, "teamA", SampleCards.MoiraIfItsReal.Id);
        moiraDie.Zone = Zone.FieldZone;
        moiraDie.Status = DieStatus.Character;
        moiraDie.Level = 1;
        var baseDefense = DieStats.EffectiveDefense(state, moiraDie);

        var wolverineDie = FindUnpurchased(state, "teamA", "ASM074");
        wolverineDie.Zone = Zone.FieldZone;
        wolverineDie.Status = DieStatus.Character;
        wolverineDie.Level = 1;

        Assert.Equal(baseDefense + 1, DieStats.EffectiveDefense(state, moiraDie));
    }

    [Fact]
    public void FieldingMoira_GivesAllXMenDiceAnAttackBuff_ButNotOthers()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.MoiraIfItsReal.Id, SampleCards.GambitUnlessIGotSomeoneToPlayWith.Id]);
        state.ActivePlayerId = "teamA";

        var xMenDie = FindUnpurchased(state, "teamA", SampleCards.GambitUnlessIGotSomeoneToPlayWith.Id);
        xMenDie.Zone = Zone.FieldZone;
        xMenDie.Status = DieStatus.Character;
        xMenDie.Level = 1;
        var xMenAttackBefore = DieStats.EffectiveAttack(state, xMenDie);

        var nonXMenDie = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id); // no affiliation
        nonXMenDie.Zone = Zone.FieldZone;
        nonXMenDie.Status = DieStatus.Character;
        nonXMenDie.Level = 1;
        var nonXMenAttackBefore = DieStats.EffectiveAttack(state, nonXMenDie);

        var moiraDie = FindUnpurchased(state, "teamA", SampleCards.MoiraIfItsReal.Id);
        moiraDie.Zone = Zone.ReservePool;
        moiraDie.Status = DieStatus.Character;
        moiraDie.Level = 1; // fielding cost 0

        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, moiraDie.Id, energyDieIdsToSpend: []);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(
                state, ability.ControllerId, ability.SourceDieId,
                _ => throw new InvalidOperationException("MatchAll shouldn't ask for a choice"))));

        Assert.Equal(xMenAttackBefore + 1, DieStats.EffectiveAttack(state, xMenDie));
        Assert.Equal(nonXMenAttackBefore, DieStats.EffectiveAttack(state, nonXMenDie));
    }

    [Fact]
    public void KittyPrydeExperiencedLeader_BuffsOnlyActiveXMenDice()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.KittyPrydeExperiencedLeader.Id, SampleCards.GambitUnlessIGotSomeoneToPlayWith.Id]);

        var kittyDie = FindUnpurchased(state, "teamA", SampleCards.KittyPrydeExperiencedLeader.Id);
        kittyDie.Zone = Zone.FieldZone;
        kittyDie.Status = DieStatus.Character;
        kittyDie.Level = 1;

        var xMenDie = FindUnpurchased(state, "teamA", SampleCards.GambitUnlessIGotSomeoneToPlayWith.Id);
        xMenDie.Zone = Zone.FieldZone;
        xMenDie.Status = DieStatus.Character;
        xMenDie.Level = 1;
        var xMenAttackWithKitty = DieStats.EffectiveAttack(state, xMenDie);
        var xMenDefenseWithKitty = DieStats.EffectiveDefense(state, xMenDie);

        var nonXMenDie = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        nonXMenDie.Zone = Zone.FieldZone;
        nonXMenDie.Status = DieStatus.Character;
        nonXMenDie.Level = 1;
        var nonXMenAttack = DieStats.EffectiveAttack(state, nonXMenDie);

        kittyDie.Zone = Zone.Unpurchased; // Kitty leaves the field - the bonus should vanish
        Assert.Equal(xMenAttackWithKitty - 1, DieStats.EffectiveAttack(state, xMenDie));
        Assert.Equal(xMenDefenseWithKitty - 1, DieStats.EffectiveDefense(state, xMenDie));
        Assert.Equal(nonXMenAttack, DieStats.EffectiveAttack(state, nonXMenDie)); // never affected
    }

    [Fact]
    public void SabretoothDoISmellWeakness_AttackScalesWithLowDefenseOpposingDice()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.SabretoothDoISmellWeakness.Id]);

        var sabretoothDie = FindUnpurchased(state, "teamA", SampleCards.SabretoothDoISmellWeakness.Id);
        sabretoothDie.Zone = Zone.FieldZone;
        sabretoothDie.Status = DieStatus.Character;
        sabretoothDie.Level = 1;
        var baseAttack = DieStats.EffectiveAttack(state, sabretoothDie);

        var lowDefenseTarget = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        lowDefenseTarget.Zone = Zone.FieldZone;
        lowDefenseTarget.Status = DieStatus.Character;
        lowDefenseTarget.Level = 1; // PlaceholderLevels: 2D
        Assert.Equal(baseAttack + 1, DieStats.EffectiveAttack(state, sabretoothDie));

        var highDefenseTarget = FindUnpurchased(state, "teamB", SampleCards.Groot.Id);
        highDefenseTarget.Zone = Zone.FieldZone;
        highDefenseTarget.Status = DieStatus.Character;
        highDefenseTarget.Level = 3; // PlaceholderLevels: 4D - doesn't qualify
        Assert.Equal(baseAttack + 1, DieStats.EffectiveAttack(state, sabretoothDie)); // unchanged
    }

    [Fact]
    public void PsylockeHeiress_AttackScalesWithOwnXMenDiceInPrepArea()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.PsylockeHeiress.Id, SampleCards.GambitUnlessIGotSomeoneToPlayWith.Id]);

        var psylockeDie = FindUnpurchased(state, "teamA", SampleCards.PsylockeHeiress.Id);
        psylockeDie.Zone = Zone.FieldZone;
        psylockeDie.Status = DieStatus.Character;
        psylockeDie.Level = 1;
        var baseAttack = DieStats.EffectiveAttack(state, psylockeDie);

        var xMenPrepDie = FindUnpurchased(state, "teamA", SampleCards.GambitUnlessIGotSomeoneToPlayWith.Id);
        xMenPrepDie.Zone = Zone.PrepArea;

        Assert.Equal(baseAttack + 2, DieStats.EffectiveAttack(state, psylockeDie));
    }

    [Fact]
    public void PsylockeHeiressEnergize_FiresOffRealGate_SpinsTargetUpOneLevel()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.PsylockeHeiress.Id]);
        state.ActivePlayerId = "teamA";
        state.CurrentStep = TurnStep.RollAndReroll;

        var psylockeDie = FindUnpurchased(state, "teamA", SampleCards.PsylockeHeiress.Id);
        psylockeDie.Zone = Zone.ReservePool;
        psylockeDie.Status = DieStatus.Energy;
        psylockeDie.EnergyKind = EnergyKind.Generic;
        psylockeDie.EnergyAmount = 2;

        var target = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        target.Zone = Zone.FieldZone;
        target.Status = DieStatus.Character;
        target.Level = 1;

        var queue = new AbilityQueue();
        TurnEngine.Reroll(state, queue, new FixedRoller(DieStatus.Energy, 1), []);
        Assert.Equal(1, queue.Count);
        Assert.Equal(TriggerType.Energize, queue.Pending[0].Trigger);

        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [target.Id])));

        Assert.Equal(2, target.Level);
    }

    [Fact]
    public void SabretoothYouReadyToPartyAttacking_BuffsOnlyBrotherhoodOfMutantsDice()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.SabretoothYouReadyToParty.Id, SampleCards.MagnetoFounderOfTheBrotherhood.Id]);
        state.ActivePlayerId = "teamA";

        var brotherhoodDie = FindUnpurchased(state, "teamA", SampleCards.MagnetoFounderOfTheBrotherhood.Id);
        brotherhoodDie.Zone = Zone.FieldZone;
        brotherhoodDie.Status = DieStatus.Character;
        brotherhoodDie.Level = 1;
        var brotherhoodAttackBefore = DieStats.EffectiveAttack(state, brotherhoodDie);

        var nonBrotherhoodDie = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        nonBrotherhoodDie.Zone = Zone.FieldZone;
        nonBrotherhoodDie.Status = DieStatus.Character;
        nonBrotherhoodDie.Level = 1;
        var nonBrotherhoodAttackBefore = DieStats.EffectiveAttack(state, nonBrotherhoodDie);

        var sabretoothDie = FindUnpurchased(state, "teamA", SampleCards.SabretoothYouReadyToParty.Id);
        sabretoothDie.Zone = Zone.FieldZone;
        sabretoothDie.Status = DieStatus.Character;
        sabretoothDie.Level = 1;

        state.CurrentStep = TurnStep.Attack;
        state.AttackSubStep = AttackSubStep.DeclareAttackers;
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [sabretoothDie.Id]);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(
                state, ability.ControllerId, ability.SourceDieId,
                _ => throw new InvalidOperationException("MatchAll shouldn't ask for a choice"))));

        Assert.Equal(brotherhoodAttackBefore + 2, DieStats.EffectiveAttack(state, brotherhoodDie));
        Assert.Equal(nonBrotherhoodAttackBefore, DieStats.EffectiveAttack(state, nonBrotherhoodDie));
    }

    [Fact]
    public void SabretoothYouReadyToPartyTeamwatch_FiresOffRealFieldScan_TargetCantBlock()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.SabretoothYouReadyToParty.Id, SampleCards.MagnetoFounderOfTheBrotherhood.Id]);
        state.ActivePlayerId = "teamA";

        var sabretoothDie = FindUnpurchased(state, "teamA", SampleCards.SabretoothYouReadyToParty.Id);
        sabretoothDie.Zone = Zone.FieldZone;
        sabretoothDie.Status = DieStatus.Character;
        sabretoothDie.Level = 1;

        var opposingTarget = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        opposingTarget.Zone = Zone.FieldZone;
        opposingTarget.Status = DieStatus.Character;
        opposingTarget.Level = 1;

        // Fielding a different Brotherhood of Mutants die is what fires
        // Sabretooth's own Teamwatch (TurnEngine.Field's real scan).
        var magnetoDie = FindUnpurchased(state, "teamA", SampleCards.MagnetoFounderOfTheBrotherhood.Id);
        var purchaseEnergy = GiveWildEnergy(state, "teamA", SampleCards.MagnetoFounderOfTheBrotherhood.PurchaseCost);
        TurnEngine.Purchase(state, magnetoDie.Id, purchaseEnergy.Select(d => d.Id).ToList());
        magnetoDie.Zone = Zone.ReservePool;
        magnetoDie.Status = DieStatus.Character;
        magnetoDie.Level = 1;

        var fieldEnergy = GiveWildEnergy(state, "teamA", 1);
        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, magnetoDie.Id, energyDieIdsToSpend: [fieldEnergy[0].Id]);

        Assert.Contains(queue.Pending, a => a.Trigger == TriggerType.Teamwatch);

        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [opposingTarget.Id])));

        Assert.Contains(opposingTarget.Id, state.CantBlockThisTurn);
    }

    [Fact]
    public void ToadJourneyIntoMiseryTeamwatch_FiresOffRealFieldScan_MovesOpponentPrepAreaDieToBag()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.ToadJourneyIntoMisery.Id, SampleCards.MagnetoFounderOfTheBrotherhood.Id]);
        state.ActivePlayerId = "teamA";

        var toadDie = FindUnpurchased(state, "teamA", SampleCards.ToadJourneyIntoMisery.Id);
        toadDie.Zone = Zone.FieldZone;
        toadDie.Status = DieStatus.Character;
        toadDie.Level = 1;

        var opponentPrepDie = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        opponentPrepDie.Zone = Zone.PrepArea;

        var magnetoDie = FindUnpurchased(state, "teamA", SampleCards.MagnetoFounderOfTheBrotherhood.Id);
        var purchaseEnergy = GiveWildEnergy(state, "teamA", SampleCards.MagnetoFounderOfTheBrotherhood.PurchaseCost);
        TurnEngine.Purchase(state, magnetoDie.Id, purchaseEnergy.Select(d => d.Id).ToList());
        magnetoDie.Zone = Zone.ReservePool;
        magnetoDie.Status = DieStatus.Character;
        magnetoDie.Level = 1;

        var fieldEnergy = GiveWildEnergy(state, "teamA", 1);
        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, magnetoDie.Id, energyDieIdsToSpend: [fieldEnergy[0].Id]);

        Assert.Contains(queue.Pending, a => a.Trigger == TriggerType.Teamwatch);

        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [opponentPrepDie.Id])));

        Assert.Equal(Zone.Bag, opponentPrepDie.Zone);
    }

    [Fact]
    public void JubileeRebelliousNatureEnergize_WhenLifeIsLower_FieldsItselfForFreeAtLevel2()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.JubileeRebelliousNature.Id]);
        state.ActivePlayerId = "teamA";
        state.CurrentStep = TurnStep.RollAndReroll;
        state.PlayerOne.Life = 10;
        state.PlayerTwo.Life = 20;

        var jubileeDie = FindUnpurchased(state, "teamA", SampleCards.JubileeRebelliousNature.Id);
        jubileeDie.Zone = Zone.ReservePool;
        jubileeDie.Status = DieStatus.Energy;
        jubileeDie.EnergyKind = EnergyKind.Generic;
        jubileeDie.EnergyAmount = 2;

        var queue = new AbilityQueue();
        TurnEngine.Reroll(state, queue, new FixedRoller(DieStatus.Energy, 1), []);
        Assert.Equal(1, queue.Count);
        Assert.Equal(TriggerType.Energize, queue.Pending[0].Trigger);

        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [])));

        // Status was Energy going in - FieldDie's own fix (Sidekick-aware
        // Status, a configurable Level) is what makes this land correctly.
        Assert.Equal(Zone.FieldZone, jubileeDie.Zone);
        Assert.Equal(DieStatus.Character, jubileeDie.Status);
        Assert.Equal(2, jubileeDie.Level);
    }

    [Fact]
    public void JubileeRebelliousNatureEnergize_WhenLifeIsNotLower_DoesNothing()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.JubileeRebelliousNature.Id]);
        state.ActivePlayerId = "teamA";
        state.CurrentStep = TurnStep.RollAndReroll;
        state.PlayerOne.Life = 20;
        state.PlayerTwo.Life = 20;

        var jubileeDie = FindUnpurchased(state, "teamA", SampleCards.JubileeRebelliousNature.Id);
        jubileeDie.Zone = Zone.ReservePool;
        jubileeDie.Status = DieStatus.Energy;
        jubileeDie.EnergyKind = EnergyKind.Generic;
        jubileeDie.EnergyAmount = 2;

        var queue = new AbilityQueue();
        TurnEngine.Reroll(state, queue, new FixedRoller(DieStatus.Energy, 1), []);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [])));

        Assert.Equal(Zone.ReservePool, jubileeDie.Zone); // unchanged - condition false
        Assert.Equal(DieStatus.Energy, jubileeDie.Status);
    }

    [Fact]
    public void CyclopsFirstClass_ReactsOnlyToFieldingAFounderKeywordDie()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds:
            [SampleCards.CyclopsFirstClass.Id, SampleCards.JeanGreyPeacefulCoexistence.Id,
             SampleCards.GambitUnlessIGotSomeoneToPlayWith.Id]);
        state.ActivePlayerId = "teamA";

        var cyclopsDie = FindUnpurchased(state, "teamA", SampleCards.CyclopsFirstClass.Id);
        cyclopsDie.Zone = Zone.FieldZone;
        cyclopsDie.Status = DieStatus.Character;
        cyclopsDie.Level = 1;

        var opposingTarget = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        opposingTarget.Zone = Zone.FieldZone;
        opposingTarget.Status = DieStatus.Character;
        opposingTarget.Level = 3; // PlaceholderLevels: 4D - survives 2 damage so Damage is observable

        // Fielding a non-Founder die does NOT trigger Cyclops. Rule 2.4
        // bypass: mark the die as already purchased and rolled onto its
        // level-1 character face, ready to field - purchase mechanics
        // aren't what this test is about.
        var gambitDie = FindUnpurchased(state, "teamA", SampleCards.GambitUnlessIGotSomeoneToPlayWith.Id);
        gambitDie.Zone = Zone.ReservePool;
        gambitDie.Status = DieStatus.Character;
        gambitDie.Level = 1; // fielding cost 1
        var gambitFieldEnergy = GiveWildEnergy(state, "teamA", 1);
        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, gambitDie.Id, energyDieIdsToSpend: [gambitFieldEnergy[0].Id]);
        Assert.DoesNotContain(queue.Pending, a => a.Trigger == TriggerType.WhenAnotherDieFielded);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [opposingTarget.Id])));

        // Fielding a Founder die DOES trigger Cyclops.
        var jeanGreyDie = FindUnpurchased(state, "teamA", SampleCards.JeanGreyPeacefulCoexistence.Id);
        jeanGreyDie.Zone = Zone.ReservePool;
        jeanGreyDie.Status = DieStatus.Character;
        jeanGreyDie.Level = 1; // fielding cost 1
        var jeanGreyFieldEnergy = GiveWildEnergy(state, "teamA", 1);
        TurnEngine.Field(state, queue, jeanGreyDie.Id, energyDieIdsToSpend: [jeanGreyFieldEnergy[0].Id]);
        Assert.Contains(queue.Pending, a => a.Trigger == TriggerType.WhenAnotherDieFielded);

        var damageBefore = opposingTarget.Damage;
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [opposingTarget.Id])));

        Assert.Equal(damageBefore + 2, opposingTarget.Damage);
    }

    [Fact]
    public void JubileeXMenFieldLeader_ReactsToFieldingAnyOfYourOwnDice()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.JubileeXMenFieldLeader.Id, SampleCards.GambitUnlessIGotSomeoneToPlayWith.Id]);
        state.ActivePlayerId = "teamA";

        var jubileeDie = FindUnpurchased(state, "teamA", SampleCards.JubileeXMenFieldLeader.Id);
        jubileeDie.Zone = Zone.FieldZone;
        jubileeDie.Status = DieStatus.Character;
        jubileeDie.Level = 1;

        var opposingTarget = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        opposingTarget.Zone = Zone.FieldZone;
        opposingTarget.Status = DieStatus.Character;
        opposingTarget.Level = 1;
        var opponentLifeBefore = state.PlayerTwo.Life;

        var gambitDie = FindUnpurchased(state, "teamA", SampleCards.GambitUnlessIGotSomeoneToPlayWith.Id);
        var purchaseEnergy = GiveWildEnergy(state, "teamA", SampleCards.GambitUnlessIGotSomeoneToPlayWith.PurchaseCost);
        TurnEngine.Purchase(state, gambitDie.Id, purchaseEnergy.Select(d => d.Id).ToList());
        gambitDie.Zone = Zone.ReservePool;
        gambitDie.Status = DieStatus.Character;
        gambitDie.Level = 1; // fielding cost 1
        var gambitFieldEnergy = GiveWildEnergy(state, "teamA", 1);

        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, gambitDie.Id, energyDieIdsToSpend: [gambitFieldEnergy[0].Id]);
        Assert.Contains(queue.Pending, a => a.Trigger == TriggerType.WhenAnotherDieFielded);

        // Two different TargetSpecs in the same Sequence (a fixed Player
        // target, a chosen CharacterDie target) - the resolver has to
        // discriminate by spec shape, unlike every single-target-spec
        // resolver elsewhere in this file.
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(
                state, ability.ControllerId, ability.SourceDieId,
                spec => spec.CharacterDiceOnly ? [opposingTarget.Id] : [state.PlayerTwo.Id])));

        Assert.Equal(opponentLifeBefore - 1, state.PlayerTwo.Life);
        Assert.Equal(1, opposingTarget.Damage);
    }

    [Fact]
    public void JubileeThingsNeverChange_GetsPlusOneAttack_OnlyWhileWolverineActive()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.JubileeThingsNeverChange.Id, "ASM074"]);

        var jubileeDie = FindUnpurchased(state, "teamA", SampleCards.JubileeThingsNeverChange.Id);
        jubileeDie.Zone = Zone.FieldZone;
        jubileeDie.Status = DieStatus.Character;
        jubileeDie.Level = 1;
        var baseAttack = DieStats.EffectiveAttack(state, jubileeDie);

        var wolverineDie = FindUnpurchased(state, "teamA", "ASM074"); // bulk-only "Wolverine" - Name is all that matters
        wolverineDie.Zone = Zone.FieldZone;
        wolverineDie.Status = DieStatus.Character;
        wolverineDie.Level = 1;

        Assert.Equal(baseAttack + 1, DieStats.EffectiveAttack(state, jubileeDie));
    }

    [Fact]
    public void KittyPrydeHeadmistress_CannotBeTargetedByOpponent_ButOwnControllerCanStill_WhileWolverineActive()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.KittyPrydeHeadmistress.Id, "ASM074"]);

        var kittyDie = FindUnpurchased(state, "teamA", SampleCards.KittyPrydeHeadmistress.Id);
        kittyDie.Zone = Zone.FieldZone;
        kittyDie.Status = DieStatus.Character;
        kittyDie.Level = 1;
        var baseAttack = DieStats.EffectiveAttack(state, kittyDie);

        var anyTarget = TargetSpec.CharacterDie("target character die");
        Assert.Contains(kittyDie.Id, LegalTargets.Query(state, "teamB", anyTarget));

        var wolverineDie = FindUnpurchased(state, "teamA", "ASM074");
        wolverineDie.Zone = Zone.FieldZone;
        wolverineDie.Status = DieStatus.Character;
        wolverineDie.Level = 1;

        Assert.Equal(baseAttack + 1, DieStats.EffectiveAttack(state, kittyDie));
        Assert.DoesNotContain(kittyDie.Id, LegalTargets.Query(state, "teamB", anyTarget)); // opponent can't target her
        Assert.Contains(kittyDie.Id, LegalTargets.Query(state, "teamA", anyTarget)); // her own controller still can
    }

    [Fact]
    public void FieldingCorsairCriminalRecord_KOsTwoVillainsDice_WhenOpponentHasFourOrMoreFieldedCharacters()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.CorsairCriminalRecord.Id],
            extraTeamBCardIds: [SampleCards.MasterMoldTargetingMutants.Id, SampleCards.MasterMoldUntoldElectronicExpertise.Id]);
        state.ActivePlayerId = "teamA";

        var villain1 = FindUnpurchased(state, "teamB", SampleCards.MasterMoldTargetingMutants.Id);
        villain1.Zone = Zone.FieldZone; villain1.Status = DieStatus.Character; villain1.Level = 1;
        var villain2 = FindUnpurchased(state, "teamB", SampleCards.MasterMoldUntoldElectronicExpertise.Id);
        villain2.Zone = Zone.FieldZone; villain2.Status = DieStatus.Character; villain2.Level = 1;
        var pad1 = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        pad1.Zone = Zone.FieldZone; pad1.Status = DieStatus.Character; pad1.Level = 1;
        var pad2 = FindUnpurchased(state, "teamB", SampleCards.Groot.Id);
        pad2.Zone = Zone.FieldZone; pad2.Status = DieStatus.Character; pad2.Level = 1;

        var corsairDie = FindUnpurchased(state, "teamA", SampleCards.CorsairCriminalRecord.Id);
        corsairDie.Zone = Zone.FieldZone;
        corsairDie.Status = DieStatus.Character;
        corsairDie.Level = 1;

        var ability = SampleCards.CorsairCriminalRecord.Abilities.Single(a => a.Trigger == TriggerType.WhenFielded);
        EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, "teamA", corsairDie.Id, _ => [villain1.Id, villain2.Id]));

        Assert.Equal(Zone.PrepArea, villain1.Zone);
        Assert.Equal(Zone.PrepArea, villain2.Zone);
    }

    [Fact]
    public void FieldingCorsairCriminalRecord_KOsOnlyOneVillainsDie_WhenOpponentHasFewerThanFourFieldedCharacters()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.CorsairCriminalRecord.Id],
            extraTeamBCardIds: [SampleCards.MasterMoldTargetingMutants.Id]);
        state.ActivePlayerId = "teamA";

        var villain = FindUnpurchased(state, "teamB", SampleCards.MasterMoldTargetingMutants.Id);
        villain.Zone = Zone.FieldZone; villain.Status = DieStatus.Character; villain.Level = 1;

        var corsairDie = FindUnpurchased(state, "teamA", SampleCards.CorsairCriminalRecord.Id);
        corsairDie.Zone = Zone.FieldZone;
        corsairDie.Status = DieStatus.Character;
        corsairDie.Level = 1;

        var ability = SampleCards.CorsairCriminalRecord.Abilities.Single(a => a.Trigger == TriggerType.WhenFielded);
        EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, "teamA", corsairDie.Id, _ => [villain.Id]));

        Assert.Equal(Zone.PrepArea, villain.Zone);
    }

    [Fact]
    public void PhoenixPsionicMaelstromAttacking_DealsSecondDamage_OnlyIfFirstTargetIsVillains()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.PhoenixPsionicMaelstrom.Id, SampleCards.MasterMoldTargetingMutants.Id]);
        state.ActivePlayerId = "teamA";

        var villainTarget = FindUnpurchased(state, "teamA", SampleCards.MasterMoldTargetingMutants.Id);
        villainTarget.Zone = Zone.FieldZone; villainTarget.Status = DieStatus.Character; villainTarget.Level = 3;

        var secondTarget = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        secondTarget.Zone = Zone.FieldZone; secondTarget.Status = DieStatus.Character; secondTarget.Level = 3;

        var phoenixDie = FindUnpurchased(state, "teamA", SampleCards.PhoenixPsionicMaelstrom.Id);
        phoenixDie.Zone = Zone.FieldZone; phoenixDie.Status = DieStatus.Character; phoenixDie.Level = 1;

        state.CurrentStep = TurnStep.Attack;
        state.AttackSubStep = AttackSubStep.DeclareAttackers;
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [phoenixDie.Id]);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(
                state, ability.ControllerId, ability.SourceDieId,
                spec => spec.Description == "another target character die" ? [secondTarget.Id] : [villainTarget.Id])));

        Assert.Equal(3, villainTarget.Damage);
        Assert.Equal(3, secondTarget.Damage);
    }

    [Fact]
    public void PhoenixPsionicMaelstromAttacking_DoesNotDealSecondDamage_IfFirstTargetIsNotVillains()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.PhoenixPsionicMaelstrom.Id]);
        state.ActivePlayerId = "teamA";

        var nonVillainTarget = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        nonVillainTarget.Zone = Zone.FieldZone; nonVillainTarget.Status = DieStatus.Character; nonVillainTarget.Level = 3;

        var phoenixDie = FindUnpurchased(state, "teamA", SampleCards.PhoenixPsionicMaelstrom.Id);
        phoenixDie.Zone = Zone.FieldZone; phoenixDie.Status = DieStatus.Character; phoenixDie.Level = 1;

        state.CurrentStep = TurnStep.Attack;
        state.AttackSubStep = AttackSubStep.DeclareAttackers;
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [phoenixDie.Id]);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [nonVillainTarget.Id])));

        Assert.Equal(3, nonVillainTarget.Damage); // only the first DealDamage landed
    }

    [Fact]
    public void FieldingDarkPhoenixEnemyOfTheShiar_OnlyAcceptsShiarOrXMenTarget()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.DarkPhoenixEnemyOfTheShiar.Id],
            extraTeamBCardIds: [SampleCards.GambitUnlessIGotSomeoneToPlayWith.Id]); // X-Men

        var xMenTarget = FindUnpurchased(state, "teamB", SampleCards.GambitUnlessIGotSomeoneToPlayWith.Id);
        xMenTarget.Zone = Zone.FieldZone; xMenTarget.Status = DieStatus.Character; xMenTarget.Level = 3;

        var illegalTarget = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id); // no affiliation
        illegalTarget.Zone = Zone.FieldZone; illegalTarget.Status = DieStatus.Character; illegalTarget.Level = 1;

        var ability = SampleCards.DarkPhoenixEnemyOfTheShiar.Abilities.Single(a => a.Trigger == TriggerType.WhenFielded);

        var ex = Assert.Throws<InvalidOperationException>(() => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, "teamA", SourceDieId: null, _ => [illegalTarget.Id])));
        Assert.Contains("not legal", ex.Message);

        EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, "teamA", SourceDieId: null, _ => [xMenTarget.Id]));
        Assert.Equal(Zone.PrepArea, xMenTarget.Zone);
    }

    [Fact]
    public void DarkPhoenixEnemyOfTheShiarGlobal_KOsOwnDieAndDiscountsNextPurchaseByTwo()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.DarkPhoenixEnemyOfTheShiar.Id]);
        state.ActivePlayerId = "teamA";

        var sacrificeDie = state.DiceIn("teamA", Zone.Bag).First();
        sacrificeDie.Zone = Zone.FieldZone;
        sacrificeDie.Status = DieStatus.SidekickCharacter;
        sacrificeDie.Level = 1;

        var globalEnergy = GiveWildEnergy(state, "teamA", 1);
        var queue = new AbilityQueue();
        TurnEngine.UseGlobalAbility(
            state, queue, SampleCards.DarkPhoenixEnemyOfTheShiar.Id, "teamA", globalEnergy.Select(d => d.Id).ToList());
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [sacrificeDie.Id])));

        Assert.Equal(Zone.PrepArea, sacrificeDie.Zone); // the Global's own cost, KO'd
        Assert.NotNull(state.PendingPurchaseDiscount);

        var toBuy = FindUnpurchased(state, "teamA", SampleCards.DarkPhoenixEnemyOfTheShiar.Id);
        var purchaseEnergy = GiveWildEnergy(state, "teamA", SampleCards.DarkPhoenixEnemyOfTheShiar.PurchaseCost - 2);
        TurnEngine.Purchase(state, toBuy.Id, purchaseEnergy.Select(d => d.Id).ToList());

        Assert.Equal(Zone.UsedPile, toBuy.Zone);
        Assert.Null(state.PendingPurchaseDiscount); // consumed
    }

    [Fact]
    public void FieldingMagikWielderOfTheSoulsword_DiscountsOnlyTheNextActionDiePurchase()
    {
        // AI004 is a bulk-only real Action-type card - no hand-curated
        // Action card exists yet to prove the RequiredType filter with.
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.MagikWielderOfTheSoulsword.Id, "AI004"]);
        state.ActivePlayerId = "teamA";

        var magikDie = FindUnpurchased(state, "teamA", SampleCards.MagikWielderOfTheSoulsword.Id);
        magikDie.Zone = Zone.FieldZone;
        magikDie.Status = DieStatus.Character;
        magikDie.Level = 1;

        var ability = SampleCards.MagikWielderOfTheSoulsword.Abilities.Single(a => a.Trigger == TriggerType.WhenFielded);
        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "teamA", magikDie.Id, _ => []));
        Assert.NotNull(state.PendingPurchaseDiscount);

        // A Character die purchase first doesn't consume it (Action-only).
        var characterDie = FindUnpurchased(state, "teamA", SampleCards.MagikWielderOfTheSoulsword.Id);
        var characterEnergy = GiveWildEnergy(state, "teamA", SampleCards.MagikWielderOfTheSoulsword.PurchaseCost);
        TurnEngine.Purchase(state, characterDie.Id, characterEnergy.Select(d => d.Id).ToList());
        Assert.NotNull(state.PendingPurchaseDiscount);

        // An Action die purchase next DOES consume it (cost 2 - 1 = 1).
        var actionDie = FindUnpurchased(state, "teamA", "AI004");
        var actionEnergy = GiveWildEnergy(state, "teamA", 1);
        TurnEngine.Purchase(state, actionDie.Id, actionEnergy.Select(d => d.Id).ToList());

        Assert.Equal(Zone.UsedPile, actionDie.Zone);
        Assert.Null(state.PendingPurchaseDiscount);
    }

    [Fact]
    public void UsingTakeCover_BuffsAllOwnDice_PlusABurstBonusOnASingleTarget()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.TakeCover.Id]);
        state.ActivePlayerId = "teamA";

        var ownDie = state.DiceIn("teamA", Zone.Bag).First();
        ownDie.Zone = Zone.FieldZone;
        ownDie.Status = DieStatus.SidekickCharacter;
        ownDie.Level = 1;
        var ownDefenseBefore = DieStats.EffectiveDefense(state, ownDie);

        var burstTarget = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        burstTarget.Zone = Zone.FieldZone;
        burstTarget.Status = DieStatus.Character;
        burstTarget.Level = 1;
        var burstTargetDefenseBefore = DieStats.EffectiveDefense(state, burstTarget);

        var takeCoverDie = FindUnpurchased(state, "teamA", SampleCards.TakeCover.Id);
        takeCoverDie.Zone = Zone.ReservePool;
        takeCoverDie.Status = DieStatus.Action;
        takeCoverDie.BurstStars = 1; // single burst face

        var queue = new AbilityQueue();
        TurnEngine.UseActionDie(state, queue, takeCoverDie.Id);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [burstTarget.Id])));

        Assert.Equal(ownDefenseBefore + 2, DieStats.EffectiveDefense(state, ownDie)); // team-wide +2D, own dice only
        Assert.Equal(burstTargetDefenseBefore + 3, DieStats.EffectiveDefense(state, burstTarget)); // burst-only bonus, not the team one (opposing)
    }

    [Fact]
    public void UsingTakeCoverGlobal_GivesTargetPlusOneDefense()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.TakeCover.Id]);
        state.ActivePlayerId = "teamA";

        var target = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        target.Zone = Zone.FieldZone;
        target.Status = DieStatus.Character;
        target.Level = 1;
        var defenseBefore = DieStats.EffectiveDefense(state, target);

        var energy = GiveWildEnergy(state, "teamA", 1);
        var queue = new AbilityQueue();
        TurnEngine.UseGlobalAbility(state, queue, SampleCards.TakeCover.Id, "teamA", energy.Select(d => d.Id).ToList());
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [target.Id])));

        Assert.Equal(defenseBefore + 1, DieStats.EffectiveDefense(state, target));
    }

    [Fact]
    public void DeadpoolCollectThis_MakesFieldingCost2DiceFree()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.DeadpoolCollectThis.Id, SampleCards.DarkPhoenixEnemyOfTheShiar.Id]);
        state.ActivePlayerId = "teamA";

        var deadpoolDie = FindUnpurchased(state, "teamA", SampleCards.DeadpoolCollectThis.Id);
        deadpoolDie.Zone = Zone.FieldZone;
        deadpoolDie.Status = DieStatus.Character;
        deadpoolDie.Level = 1;

        var costTwoDie = FindUnpurchased(state, "teamA", SampleCards.DarkPhoenixEnemyOfTheShiar.Id);
        costTwoDie.Zone = Zone.ReservePool;
        costTwoDie.Status = DieStatus.Character;
        costTwoDie.Level = 2; // fielding cost 2

        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, costTwoDie.Id, energyDieIdsToSpend: []); // would throw if not actually free

        Assert.Equal(Zone.FieldZone, costTwoDie.Zone);
    }

    [Fact]
    public void FieldingACostTwoDie_WithoutDeadpoolActive_StillRequiresEnergy()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.DarkPhoenixEnemyOfTheShiar.Id]);
        state.ActivePlayerId = "teamA";

        var costTwoDie = FindUnpurchased(state, "teamA", SampleCards.DarkPhoenixEnemyOfTheShiar.Id);
        costTwoDie.Zone = Zone.ReservePool;
        costTwoDie.Status = DieStatus.Character;
        costTwoDie.Level = 2;

        var queue = new AbilityQueue();
        Assert.Throws<InvalidOperationException>(() =>
            TurnEngine.Field(state, queue, costTwoDie.Id, energyDieIdsToSpend: []));
    }

    [Fact]
    public void MystiqueTaughtByMagneto_MakesBrotherhoodOfMutantsDiceFreeToField()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.MystiqueTaughtByMagneto.Id, SampleCards.MagnetoFounderOfTheBrotherhood.Id]);
        state.ActivePlayerId = "teamA";

        var mystiqueDie = FindUnpurchased(state, "teamA", SampleCards.MystiqueTaughtByMagneto.Id);
        mystiqueDie.Zone = Zone.FieldZone;
        mystiqueDie.Status = DieStatus.Character;
        mystiqueDie.Level = 3;

        var magnetoDie = FindUnpurchased(state, "teamA", SampleCards.MagnetoFounderOfTheBrotherhood.Id);
        magnetoDie.Zone = Zone.ReservePool;
        magnetoDie.Status = DieStatus.Character;
        magnetoDie.Level = 1; // fielding cost 1

        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, magnetoDie.Id, energyDieIdsToSpend: []); // would throw if not actually free

        Assert.Equal(Zone.FieldZone, magnetoDie.Zone);
    }

    [Fact]
    public void MystiqueTaughtByMagnetoEnergize_FiresOffRealGate_FieldsABrotherhoodDieForFree()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.MystiqueTaughtByMagneto.Id, SampleCards.MagnetoFounderOfTheBrotherhood.Id]);
        state.ActivePlayerId = "teamA";
        state.CurrentStep = TurnStep.RollAndReroll;

        var mystiqueDie = FindUnpurchased(state, "teamA", SampleCards.MystiqueTaughtByMagneto.Id);
        mystiqueDie.Zone = Zone.ReservePool;
        mystiqueDie.Status = DieStatus.Energy;
        mystiqueDie.EnergyKind = EnergyKind.Generic;
        mystiqueDie.EnergyAmount = 2;

        var magnetoDie = FindUnpurchased(state, "teamA", SampleCards.MagnetoFounderOfTheBrotherhood.Id);
        magnetoDie.Zone = Zone.ReservePool;
        magnetoDie.Status = DieStatus.Character;
        magnetoDie.Level = 2;

        var queue = new AbilityQueue();
        TurnEngine.Reroll(state, queue, new FixedRoller(DieStatus.Energy, 1), []);
        Assert.Equal(1, queue.Count);
        Assert.Equal(TriggerType.Energize, queue.Pending[0].Trigger);

        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [magnetoDie.Id])));

        Assert.Equal(Zone.FieldZone, magnetoDie.Zone);
    }

    [Fact]
    public void IcemanFrozenFistsOfFuryAttacking_DealsDamage_OnlyWhileWolverineActive()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.IcemanFrozenFistsOfFury.Id, "ASM074"]);
        state.ActivePlayerId = "teamA";

        var target = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        target.Zone = Zone.FieldZone; target.Status = DieStatus.Character; target.Level = 3;

        var icemanDie = FindUnpurchased(state, "teamA", SampleCards.IcemanFrozenFistsOfFury.Id);
        icemanDie.Zone = Zone.FieldZone; icemanDie.Status = DieStatus.Character; icemanDie.Level = 1;

        var ability = SampleCards.IcemanFrozenFistsOfFury.Abilities.Single(a => a.Trigger == TriggerType.WhenAttacks);

        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "teamA", icemanDie.Id, _ => [target.Id]));
        Assert.Equal(0, target.Damage);

        var wolverineDie = FindUnpurchased(state, "teamA", "ASM074");
        wolverineDie.Zone = Zone.FieldZone; wolverineDie.Status = DieStatus.Character; wolverineDie.Level = 1;

        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "teamA", icemanDie.Id, _ => [target.Id]));
        Assert.Equal(3, target.Damage);
    }

    [Fact]
    public void FieldingRonanTheAccuserNoMercy_BothPlayersKOOwnDie_OpponentViaPendingChoice()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.RonanTheAccuserNoMercy.Id]);
        state.ActivePlayerId = "teamA";

        var ownDie = state.DiceIn("teamA", Zone.Bag).First();
        ownDie.Zone = Zone.FieldZone;
        ownDie.Status = DieStatus.SidekickCharacter;
        ownDie.Level = 1;

        var opponentDie1 = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        opponentDie1.Zone = Zone.FieldZone; opponentDie1.Status = DieStatus.Character; opponentDie1.Level = 1;
        var opponentDie2 = FindUnpurchased(state, "teamB", SampleCards.Groot.Id);
        opponentDie2.Zone = Zone.FieldZone; opponentDie2.Status = DieStatus.Character; opponentDie2.Level = 1;

        var ronanDie = FindUnpurchased(state, "teamA", SampleCards.RonanTheAccuserNoMercy.Id);
        ronanDie.Zone = Zone.FieldZone;
        ronanDie.Status = DieStatus.Character;
        ronanDie.Level = 1;

        var ability = SampleCards.RonanTheAccuserNoMercy.Abilities.Single(a => a.Trigger == TriggerType.WhenFielded);
        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "teamA", ronanDie.Id, _ => [ownDie.Id]));

        Assert.Equal(Zone.PrepArea, ownDie.Zone); // the controller's own half already happened

        var pending = state.PendingChoice;
        Assert.NotNull(pending);
        Assert.Equal("teamB", pending!.ControllerId); // the OPPONENT answers this one
        Assert.Contains(opponentDie1.Id, pending.CandidateDieIds);
        Assert.Contains(opponentDie2.Id, pending.CandidateDieIds);

        pending.Resolve([opponentDie2.Id]);

        Assert.Equal(Zone.PrepArea, opponentDie2.Zone); // the opponent's own chosen die
        Assert.Equal(Zone.FieldZone, opponentDie1.Zone); // untouched
    }

    [Fact]
    public void FieldingRonanTheAccuserNoMercy_OpponentHasNoCharacterDice_SkipsSilently()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.RonanTheAccuserNoMercy.Id]);
        state.ActivePlayerId = "teamA";

        var ownDie = state.DiceIn("teamA", Zone.Bag).First();
        ownDie.Zone = Zone.FieldZone;
        ownDie.Status = DieStatus.SidekickCharacter;
        ownDie.Level = 1;

        var ronanDie = FindUnpurchased(state, "teamA", SampleCards.RonanTheAccuserNoMercy.Id);
        ronanDie.Zone = Zone.FieldZone;
        ronanDie.Status = DieStatus.Character;
        ronanDie.Level = 1;

        var ability = SampleCards.RonanTheAccuserNoMercy.Abilities.Single(a => a.Trigger == TriggerType.WhenFielded);
        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "teamA", ronanDie.Id, _ => [ownDie.Id]));

        Assert.Equal(Zone.PrepArea, ownDie.Zone);
        Assert.Null(state.PendingChoice); // "if able" - opponent had nothing to KO
    }

    [Fact]
    public void FieldingRonanTheAccuserNoMercy_OpponentHasExactlyOneDie_ResolvesImmediately()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.RonanTheAccuserNoMercy.Id]);
        state.ActivePlayerId = "teamA";

        var ownDie = state.DiceIn("teamA", Zone.Bag).First();
        ownDie.Zone = Zone.FieldZone;
        ownDie.Status = DieStatus.SidekickCharacter;
        ownDie.Level = 1;

        var opponentDie = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        opponentDie.Zone = Zone.FieldZone;
        opponentDie.Status = DieStatus.Character;
        opponentDie.Level = 1;

        var ronanDie = FindUnpurchased(state, "teamA", SampleCards.RonanTheAccuserNoMercy.Id);
        ronanDie.Zone = Zone.FieldZone;
        ronanDie.Status = DieStatus.Character;
        ronanDie.Level = 1;

        var ability = SampleCards.RonanTheAccuserNoMercy.Abilities.Single(a => a.Trigger == TriggerType.WhenFielded);
        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "teamA", ronanDie.Id, _ => [ownDie.Id]));

        Assert.Equal(Zone.PrepArea, ownDie.Zone);
        Assert.Equal(Zone.PrepArea, opponentDie.Zone); // no real choice among one - resolves immediately
        Assert.Null(state.PendingChoice);
    }

    [Fact]
    public void EmmaFrostManipulative_FiresOffRealEnterAttackStepGate_RerollsOpponentTarget()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.EmmaFrostManipulative.Id]);
        state.ActivePlayerId = "teamB"; // teamB is about to attack; Emma Frost's controller (teamA) reacts

        var emmaDie = FindUnpurchased(state, "teamA", SampleCards.EmmaFrostManipulative.Id);
        emmaDie.Zone = Zone.FieldZone;
        emmaDie.Status = DieStatus.Character;
        emmaDie.Level = 1;

        var target = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        target.Zone = Zone.FieldZone;
        target.Status = DieStatus.Character;
        target.Level = 1;

        var queue = new AbilityQueue();
        TurnEngine.EnterAttackStep(state, queue);

        Assert.Equal(1, queue.Count);
        Assert.Equal(TriggerType.StartOfOpponentsAttackStep, queue.Pending[0].Trigger);

        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(
                state, ability.ControllerId, ability.SourceDieId, _ => [target.Id],
                Roller: new FixedRoller(DieStatus.Energy, 1))));

        Assert.Equal(DieStatus.Energy, target.Status); // rerolled off its character face
    }

    [Fact]
    public void EmmaFrostFinesse_RerollsAFistTarget_SendingAnEnergyLanderToTheReservePool()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.EmmaFrostFinesse.Id],
            extraTeamBCardIds: [SampleCards.SabretoothDoISmellWeakness.Id]);
        state.ActivePlayerId = "teamB";

        var emmaDie = FindUnpurchased(state, "teamA", SampleCards.EmmaFrostFinesse.Id);
        emmaDie.Zone = Zone.FieldZone;
        emmaDie.Status = DieStatus.Character;
        emmaDie.Level = 1;

        var fistTarget = FindUnpurchased(state, "teamB", SampleCards.SabretoothDoISmellWeakness.Id); // Fist energy type
        fistTarget.Zone = Zone.FieldZone;
        fistTarget.Status = DieStatus.Character;
        fistTarget.Level = 1;

        var queue = new AbilityQueue();
        TurnEngine.EnterAttackStep(state, queue);
        Assert.Equal(1, queue.Count);

        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(
                state, ability.ControllerId, ability.SourceDieId, _ => [fistTarget.Id],
                Roller: new FixedRoller(DieStatus.Energy, 1))));

        Assert.Equal(Zone.ReservePool, fistTarget.Zone); // landed on an energy face
    }

    [Fact]
    public void FieldingMasterMoldEndlessSentinels_PlacesARealSentinelToken()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.MasterMoldEndlessSentinels.Id]);
        state.ActivePlayerId = "teamA";

        var masterMoldDie = FindUnpurchased(state, "teamA", SampleCards.MasterMoldEndlessSentinels.Id);
        masterMoldDie.Zone = Zone.FieldZone;
        masterMoldDie.Status = DieStatus.Character;
        masterMoldDie.Level = 1;

        var ability = SampleCards.MasterMoldEndlessSentinels.Abilities.Single(a => a.Trigger == TriggerType.WhenFielded);
        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "teamA", masterMoldDie.Id, _ => []));

        var token = state.DiceIn("teamA", Zone.FieldZone).Single(d => d.VirtualCardId == SampleCards.SentinelToken.Id);
        Assert.Equal(DieStatus.Character, token.Status);
        Assert.Equal(5, DieStats.EffectiveAttack(state, token));
        Assert.Equal(5, DieStats.EffectiveDefense(state, token));
    }

    [Fact]
    public void MasterMoldEndlessSentinels_AlsoPlacesTokens_WhenItAttacksAndWhenItsKOd()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.MasterMoldEndlessSentinels.Id]);
        state.ActivePlayerId = "teamA";

        var masterMoldDie = FindUnpurchased(state, "teamA", SampleCards.MasterMoldEndlessSentinels.Id);
        masterMoldDie.Zone = Zone.FieldZone;
        masterMoldDie.Status = DieStatus.Character;
        masterMoldDie.Level = 1;

        state.CurrentStep = TurnStep.Attack;
        state.AttackSubStep = AttackSubStep.DeclareAttackers;
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [masterMoldDie.Id]);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [])));

        Assert.Single(state.DiceIn("teamA", Zone.FieldZone), d => d.VirtualCardId == SampleCards.SentinelToken.Id);

        // Ko's own case already funnels through TurnEngine.ResolveKOReactions
        // internally - going through the public EffectInterpreter/Ko path
        // here instead of calling that internal method directly.
        var koQueue = new AbilityQueue();
        EffectInterpreter.Execute(
            new Ko(TargetSpec.Self), new EffectContext(state, "teamA", masterMoldDie.Id, _ => [], Queue: koQueue));
        koQueue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [])));

        Assert.Equal(2, state.DiceIn("teamA", Zone.FieldZone).Count(d => d.VirtualCardId == SampleCards.SentinelToken.Id));
    }

    [Fact]
    public void VulcanAggession_DebuffsOnlyOpponentsNonFistCharacters()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.VulcanAggession.Id],
            extraTeamBCardIds: [SampleCards.SabretoothDoISmellWeakness.Id]); // Fist energy type

        var nonFistOpponent = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id); // PlaceholderEnergy: Mask
        nonFistOpponent.Zone = Zone.FieldZone;
        nonFistOpponent.Status = DieStatus.Character;
        nonFistOpponent.Level = 1;
        var nonFistDefenseBefore = DieStats.EffectiveDefense(state, nonFistOpponent); // Vulcan not fielded yet

        var fistOpponent = FindUnpurchased(state, "teamB", SampleCards.SabretoothDoISmellWeakness.Id);
        fistOpponent.Zone = Zone.FieldZone;
        fistOpponent.Status = DieStatus.Character;
        fistOpponent.Level = 1;
        var fistDefenseBefore = DieStats.EffectiveDefense(state, fistOpponent);

        var ownNonFistDie = state.DiceIn("teamA", Zone.Bag).First();
        ownNonFistDie.Zone = Zone.FieldZone;
        ownNonFistDie.Status = DieStatus.SidekickCharacter;
        ownNonFistDie.Level = 1;
        var ownDefenseBefore = DieStats.EffectiveDefense(state, ownNonFistDie);

        var vulcanDie = FindUnpurchased(state, "teamA", SampleCards.VulcanAggession.Id);
        vulcanDie.Zone = Zone.FieldZone;
        vulcanDie.Status = DieStatus.Character;
        vulcanDie.Level = 1;

        Assert.Equal(nonFistDefenseBefore - 2, DieStats.EffectiveDefense(state, nonFistOpponent)); // debuffed
        Assert.Equal(fistDefenseBefore, DieStats.EffectiveDefense(state, fistOpponent)); // excluded - Fist
        Assert.Equal(ownDefenseBefore, DieStats.EffectiveDefense(state, ownNonFistDie)); // own side, unaffected
    }

    [Fact]
    public void UsingVulcanAggessionGlobal_ForcesTargetToAttack()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.VulcanAggession.Id]);
        state.ActivePlayerId = "teamA";

        var target = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        target.Zone = Zone.FieldZone;
        target.Status = DieStatus.Character;
        target.Level = 1;

        var energy = GiveWildEnergy(state, "teamA", 1);
        var queue = new AbilityQueue();
        TurnEngine.UseGlobalAbility(state, queue, SampleCards.VulcanAggession.Id, "teamA", energy.Select(d => d.Id).ToList());
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [target.Id])));

        Assert.Contains(target.Id, state.MustAttackThisTurn);
    }

    [Fact]
    public void ColossusOrganicSteel_RedirectsFirstDamageThisTurn_ToItself()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.ColossusOrganicSteel.Id]);

        var colossusDie = FindUnpurchased(state, "teamA", SampleCards.ColossusOrganicSteel.Id);
        colossusDie.Zone = Zone.FieldZone;
        colossusDie.Status = DieStatus.Character;
        colossusDie.Level = 1; // no burst mark - a real redirect, not a prevention

        var otherDie = state.DiceIn("teamA", Zone.Bag).First();
        otherDie.Zone = Zone.FieldZone;
        otherDie.Status = DieStatus.SidekickCharacter;
        otherDie.Level = 1;

        EffectInterpreter.Execute(
            new DealDamage(2, TargetSpec.Self), new EffectContext(state, "teamA", otherDie.Id, _ => []));

        Assert.Equal(0, otherDie.Damage); // redirected away
        Assert.Equal(2, colossusDie.Damage); // Colossus took it instead
    }

    [Fact]
    public void ColossusOrganicSteel_OnlyRedirectsTheFirstDamageEachTurn()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.ColossusOrganicSteel.Id]);

        var colossusDie = FindUnpurchased(state, "teamA", SampleCards.ColossusOrganicSteel.Id);
        colossusDie.Zone = Zone.FieldZone;
        colossusDie.Status = DieStatus.Character;
        colossusDie.Level = 1;

        var die1 = state.DiceIn("teamA", Zone.Bag).First();
        die1.Zone = Zone.FieldZone; die1.Status = DieStatus.SidekickCharacter; die1.Level = 1;
        var die2 = state.DiceIn("teamA", Zone.Bag).First();
        die2.Zone = Zone.FieldZone; die2.Status = DieStatus.SidekickCharacter; die2.Level = 1;

        EffectInterpreter.Execute(new DealDamage(1, TargetSpec.Self), new EffectContext(state, "teamA", die1.Id, _ => []));
        EffectInterpreter.Execute(new DealDamage(1, TargetSpec.Self), new EffectContext(state, "teamA", die2.Id, _ => []));

        Assert.Equal(0, die1.Damage);
        Assert.Equal(1, colossusDie.Damage);
        // die2's own damage was NOT redirected (the "first time" already
        // happened) - it took the hit directly and, being a bare 1A/1D
        // Sidekick, was KO'd by it (which resets Damage to 0 via the
        // normal KO cleanup - Zone is the real proof here, not Damage).
        Assert.Equal(Zone.PrepArea, die2.Zone);
    }

    [Fact]
    public void ColossusOrganicSteel_PreventsDamageEntirely_WhenOnItsSingleBurstFace()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.ColossusOrganicSteel.Id]);

        var colossusDie = FindUnpurchased(state, "teamA", SampleCards.ColossusOrganicSteel.Id);
        colossusDie.Zone = Zone.FieldZone;
        colossusDie.Status = DieStatus.Character;
        colossusDie.Level = 2; // single-burst face

        var otherDie = state.DiceIn("teamA", Zone.Bag).First();
        otherDie.Zone = Zone.FieldZone;
        otherDie.Status = DieStatus.SidekickCharacter;
        otherDie.Level = 1;

        EffectInterpreter.Execute(
            new DealDamage(2, TargetSpec.Self), new EffectContext(state, "teamA", otherDie.Id, _ => []));

        Assert.Equal(0, otherDie.Damage);
        Assert.Equal(0, colossusDie.Damage); // prevented entirely - nobody takes it
    }

    [Fact]
    public void ColossusOrganicSteel_RedirectUsageResets_AtCleanUp()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.ColossusOrganicSteel.Id]);
        state.ActivePlayerId = "teamA";

        var colossusDie = FindUnpurchased(state, "teamA", SampleCards.ColossusOrganicSteel.Id);
        colossusDie.Zone = Zone.FieldZone;
        colossusDie.Status = DieStatus.Character;
        colossusDie.Level = 1;

        var die1 = state.DiceIn("teamA", Zone.Bag).First();
        die1.Zone = Zone.FieldZone; die1.Status = DieStatus.SidekickCharacter; die1.Level = 1;

        EffectInterpreter.Execute(new DealDamage(1, TargetSpec.Self), new EffectContext(state, "teamA", die1.Id, _ => []));
        Assert.Contains("teamA", state.UsedDamageRedirectThisTurn);

        state.CurrentStep = TurnStep.CleanUp;
        TurnEngine.CleanUp(state, new FixedRoller(DieStatus.Energy, 1));

        Assert.DoesNotContain("teamA", state.UsedDamageRedirectThisTurn);
    }

    [Fact]
    public void ColossusOrganicSteel_RedirectsCombatDamage_EvenWhileSittingOutOfCombat()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.ColossusOrganicSteel.Id]);
        state.ActivePlayerId = "teamB";

        var colossusDie = FindUnpurchased(state, "teamA", SampleCards.ColossusOrganicSteel.Id);
        colossusDie.Zone = Zone.FieldZone;
        colossusDie.Status = DieStatus.Character;
        colossusDie.Level = 1; // 4D - not participating in this combat at all

        var blockerDie = state.DiceIn("teamA", Zone.Bag).First();
        blockerDie.Zone = Zone.FieldZone;
        blockerDie.Status = DieStatus.SidekickCharacter;
        blockerDie.Level = 1;

        var attackerDie = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        attackerDie.Zone = Zone.FieldZone;
        attackerDie.Status = DieStatus.Character;
        attackerDie.Level = 2; // PlaceholderLevels: 2A

        TurnEngine.EnterAttackStep(state);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [attackerDie.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attackerDie.Id, blockerDie.Id);
        CombatEngine.DeclareBlockers(state, assignment, [blockerDie.Id]);

        var splits = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [attackerDie.Id] = new Dictionary<string, int> { [blockerDie.Id] = 2 }, // must match Falcon L2's full 2A
        };
        CombatEngine.AssignCombatDamage(state, queue, assignment, splits);

        Assert.Equal(0, blockerDie.Damage); // redirected away from the actual blocker
        Assert.Equal(2, colossusDie.Damage); // Colossus (Field Zone, not blocking) took it instead - survives (4D)
        Assert.Equal(Zone.FieldZone, blockerDie.Zone); // survived combat (never took damage, never KO'd)
    }

    [Fact]
    public void FieldingMisterSinisterMutantSupremacist_BlanksTheWholeOpposingTeamsText()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.MisterSinisterMutantSupremacist.Id],
            extraTeamBCardIds: [SampleCards.Apocalypse.Id]);
        state.ActivePlayerId = "teamA";

        var opposingDie = FindUnpurchased(state, "teamB", SampleCards.Apocalypse.Id); // printed Overcrush
        opposingDie.Zone = Zone.FieldZone;
        opposingDie.Status = DieStatus.Character;
        opposingDie.Level = 1;
        Assert.True(DieStats.HasKeyword(state, opposingDie, "Overcrush"));

        var sinisterDie = FindUnpurchased(state, "teamA", SampleCards.MisterSinisterMutantSupremacist.Id);
        sinisterDie.Zone = Zone.FieldZone;
        sinisterDie.Status = DieStatus.Character;
        sinisterDie.Level = 1;

        var ability = SampleCards.MisterSinisterMutantSupremacist.Abilities.Single(a => a.Trigger == TriggerType.WhenFielded);
        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "teamA", sinisterDie.Id, _ => []));

        Assert.False(DieStats.HasKeyword(state, opposingDie, "Overcrush")); // text ignored
    }

    [Fact]
    public void MisterSinisterMutantSupremacist_DoesNotBlankItsOwnSidesText()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.MisterSinisterMutantSupremacist.Id]);
        state.ActivePlayerId = "teamA";

        var ownDie = FindUnpurchased(state, "teamA", SampleCards.Apocalypse.Id); // own roster's own Overcrush card
        ownDie.Zone = Zone.FieldZone;
        ownDie.Status = DieStatus.Character;
        ownDie.Level = 1;

        var sinisterDie = FindUnpurchased(state, "teamA", SampleCards.MisterSinisterMutantSupremacist.Id);
        sinisterDie.Zone = Zone.FieldZone;
        sinisterDie.Status = DieStatus.Character;
        sinisterDie.Level = 1;

        var ability = SampleCards.MisterSinisterMutantSupremacist.Abilities.Single(a => a.Trigger == TriggerType.WhenFielded);
        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "teamA", sinisterDie.Id, _ => []));

        Assert.True(DieStats.HasKeyword(state, ownDie, "Overcrush")); // own side, unaffected
    }

    [Fact]
    public void MisterSinisterMutantSupremacistGlobal_BlanksOnlyTheTargetedAttacker()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.MisterSinisterMutantSupremacist.Id],
            extraTeamBCardIds: [SampleCards.Apocalypse.Id, SampleCards.Beast.Id]);
        state.ActivePlayerId = "teamB";

        var attacker1 = FindUnpurchased(state, "teamB", SampleCards.Apocalypse.Id); // Overcrush
        attacker1.Zone = Zone.FieldZone; attacker1.Status = DieStatus.Character; attacker1.Level = 1;
        var attacker2 = FindUnpurchased(state, "teamB", SampleCards.Beast.Id); // Regenerate
        attacker2.Zone = Zone.FieldZone; attacker2.Status = DieStatus.Character; attacker2.Level = 1;

        TurnEngine.EnterAttackStep(state);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [attacker1.Id, attacker2.Id]);

        var ability = SampleCards.MisterSinisterMutantSupremacist.Abilities.Single(a => a.Trigger == TriggerType.Global);
        EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, "teamA", SourceDieId: null, _ => [attacker1.Id]));

        Assert.False(DieStats.HasKeyword(state, attacker1, "Overcrush")); // targeted - blanked
        Assert.True(DieStats.HasKeyword(state, attacker2, "Regenerate")); // not targeted - unaffected
    }

    [Fact]
    public void UsingGlobalAbility_IsBlocked_WhileControllersTextIsBlanked()
    {
        var state = BuildTwoTeamGame();
        state.BlankedControllerIds.Add("teamB");

        var energy = GiveWildEnergy(state, "teamB", 1);
        var queue = new AbilityQueue();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            TurnEngine.UseGlobalAbility(state, queue, SampleCards.Falcon.Id, "teamB", energy.Select(d => d.Id).ToList()));
        Assert.Contains("ignored", ex.Message);
    }

    [Fact]
    public void VulcanPowerSuppression_BlanksAbilities_OnlyForDiceEngagedWithIt()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.VulcanPowerSuppression.Id],
            extraTeamBCardIds: [SampleCards.Apocalypse.Id, SampleCards.Beast.Id]);
        state.ActivePlayerId = "teamB";

        var vulcanDie = FindUnpurchased(state, "teamA", SampleCards.VulcanPowerSuppression.Id);
        vulcanDie.Zone = Zone.FieldZone;
        vulcanDie.Status = DieStatus.Character;
        vulcanDie.Level = 1;

        var otherBlocker = state.DiceIn("teamA", Zone.Bag).First(); // not engaged with Vulcan at all
        otherBlocker.Zone = Zone.FieldZone;
        otherBlocker.Status = DieStatus.SidekickCharacter;
        otherBlocker.Level = 1;

        var blockedByVulcan = FindUnpurchased(state, "teamB", SampleCards.Apocalypse.Id); // Overcrush
        blockedByVulcan.Zone = Zone.FieldZone; blockedByVulcan.Status = DieStatus.Character; blockedByVulcan.Level = 1;

        var notEngagedWithVulcan = FindUnpurchased(state, "teamB", SampleCards.Beast.Id); // Regenerate
        notEngagedWithVulcan.Zone = Zone.FieldZone; notEngagedWithVulcan.Status = DieStatus.Character; notEngagedWithVulcan.Level = 1;

        TurnEngine.EnterAttackStep(state);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [blockedByVulcan.Id, notEngagedWithVulcan.Id]);

        var assignment = new CombatAssignment();
        assignment.AssignBlocker(blockedByVulcan.Id, vulcanDie.Id);
        assignment.AssignBlocker(notEngagedWithVulcan.Id, otherBlocker.Id);
        CombatEngine.DeclareBlockers(state, assignment, [vulcanDie.Id, otherBlocker.Id]);

        Assert.False(DieStats.HasKeyword(state, blockedByVulcan, "Overcrush")); // blocked BY Vulcan - blanked
        Assert.True(DieStats.HasKeyword(state, notEngagedWithVulcan, "Regenerate")); // unaffected
    }

    [Fact]
    public void BlankedText_ClearsAtCleanUp()
    {
        var state = BuildTwoTeamGame();
        state.BlankedDieIds.Add("some-die-id");
        state.BlankedControllerIds.Add("teamB");
        state.ActivePlayerId = "teamA";
        state.CurrentStep = TurnStep.CleanUp;

        TurnEngine.CleanUp(state, new FixedRoller(DieStatus.Energy, 1));

        Assert.Empty(state.BlankedDieIds);
        Assert.Empty(state.BlankedControllerIds);
    }

    [Fact]
    public void GladiatorGlobal_ProtectsControllersCharacterDice_FromARealGlobalTargetingAttempt()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.GladiatorMajestorKallark.Id],
            extraTeamBCardIds: [SampleCards.MisterSinisterMutantSupremacist.Id]);
        state.ActivePlayerId = "teamA";

        var gladiatorDie = FindUnpurchased(state, "teamA", SampleCards.GladiatorMajestorKallark.Id);
        gladiatorDie.Zone = Zone.FieldZone;
        gladiatorDie.Status = DieStatus.Character;
        gladiatorDie.Level = 1;

        TurnEngine.EnterAttackStep(state);
        var combatQueue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, combatQueue, [gladiatorDie.Id]);
        CombatEngine.DeclareBlockers(state, new CombatAssignment(), []); // no blockers - reach the Action/Global window

        var sinisterGlobal = SampleCards.MisterSinisterMutantSupremacist.Abilities.Single(a => a.Trigger == TriggerType.Global);
        var sinisterTarget = ((BlankTargetText)sinisterGlobal.Effect).Target;

        // Before Gladiator's own Global is used, its attacking die is a
        // perfectly ordinary legal target for another Global.
        Assert.Contains(gladiatorDie.Id, LegalTargets.Query(state, "teamB", sinisterTarget, TriggerType.Global));

        // Team A activates Gladiator's own Global for real, through the
        // same TurnEngine.UseGlobalAbility gate/AbilityQueue.Drain path
        // production uses (GamesController.Drain), Trigger included.
        var globalEnergy = GiveWildEnergy(state, "teamA", 1);
        var globalQueue = new AbilityQueue();
        TurnEngine.UseGlobalAbility(state, globalQueue, SampleCards.GladiatorMajestorKallark.Id, "teamA", globalEnergy.Select(d => d.Id).ToList());
        globalQueue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [], Trigger: ability.Trigger)));

        Assert.Contains("teamA", state.ImmuneToActionAndGlobalTargetingControllerIds);

        // Now the same query, with the same Global trigger, excludes it.
        Assert.DoesNotContain(gladiatorDie.Id, LegalTargets.Query(state, "teamB", sinisterTarget, TriggerType.Global));

        // And a real attempt to resolve Mister Sinister's own Global
        // choosing Gladiator's die as the target is rejected as illegal,
        // the same path/exception every other "chosen target isn't
        // legal" case goes through.
        var ex = Assert.Throws<InvalidOperationException>(() => EffectInterpreter.Execute(
            sinisterGlobal.Effect,
            new EffectContext(state, "teamB", SourceDieId: null, _ => [gladiatorDie.Id], Trigger: TriggerType.Global)));
        Assert.Contains("not legal", ex.Message);
    }

    [Fact]
    public void GladiatorGlobal_DoesNotProtectAgainst_TriggersOtherThanGlobalOrActionDie()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.GladiatorMajestorKallark.Id]);
        state.ActivePlayerId = "teamA";
        state.ImmuneToActionAndGlobalTargetingControllerIds.Add("teamA");

        var gladiatorDie = FindUnpurchased(state, "teamA", SampleCards.GladiatorMajestorKallark.Id);
        gladiatorDie.Zone = Zone.FieldZone;
        gladiatorDie.Status = DieStatus.Character;
        gladiatorDie.Level = 1;

        var spec = TargetSpec.CharacterDie("target character die", TargetOwnership.Opposing);

        // A WhenFielded/WhenAttacks/etc-triggered targeting attempt (or a
        // caller that doesn't pass a trigger at all) isn't what Gladiator's
        // text protects against - only Global and WhenUsed (Action Die) are.
        Assert.Contains(gladiatorDie.Id, LegalTargets.Query(state, "teamB", spec, TriggerType.WhenFielded));
        Assert.Contains(gladiatorDie.Id, LegalTargets.Query(state, "teamB", spec));
    }

    [Fact]
    public void GladiatorGlobal_TargetingImmunity_ClearsAtCleanUp()
    {
        var state = BuildTwoTeamGame();
        state.ImmuneToActionAndGlobalTargetingControllerIds.Add("teamA");
        state.ActivePlayerId = "teamA";
        state.CurrentStep = TurnStep.CleanUp;

        TurnEngine.CleanUp(state, new FixedRoller(DieStatus.Energy, 1));

        Assert.Empty(state.ImmuneToActionAndGlobalTargetingControllerIds);
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
        Assert.Contains(blocker.Id, state.DeadlyEngagedDieIds.Keys); // recorded at Declare Blockers regardless

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

    [Fact]
    public void WolverinePureOfHeart_IsFreeToField_WhenTeamHasNoVillains()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.WolverinePureOfHeart.Id]);
        state.ActivePlayerId = "teamA";

        var wolverineDie = FindUnpurchased(state, "teamA", SampleCards.WolverinePureOfHeart.Id);
        wolverineDie.Zone = Zone.ReservePool;
        wolverineDie.Status = DieStatus.Character;
        wolverineDie.Level = 1; // printed fielding cost 1 - would normally require energy

        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, wolverineDie.Id, energyDieIdsToSpend: []); // no exception - free

        Assert.Equal(Zone.FieldZone, wolverineDie.Zone);
    }

    [Fact]
    public void WolverinePureOfHeart_CostsNormally_WhenTeamHasAVillain()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.WolverinePureOfHeart.Id, SampleCards.MisterSinisterMutantSupremacist.Id]);
        state.ActivePlayerId = "teamA";

        var wolverineDie = FindUnpurchased(state, "teamA", SampleCards.WolverinePureOfHeart.Id);
        wolverineDie.Zone = Zone.ReservePool;
        wolverineDie.Status = DieStatus.Character;
        wolverineDie.Level = 1;

        var queue = new AbilityQueue();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            TurnEngine.Field(state, queue, wolverineDie.Id, energyDieIdsToSpend: []));
        Assert.Contains("Not enough energy", ex.Message);
    }

    [Fact]
    public void CorsairRecruitingACrew_SendsOnlyTheNextPurchase_ToTheBagInsteadOfTheUsedPile()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.CorsairRecruitingACrew.Id]);
        state.ActivePlayerId = "teamA";

        var corsairDie = FindUnpurchased(state, "teamA", SampleCards.CorsairRecruitingACrew.Id);
        corsairDie.Zone = Zone.FieldZone;
        corsairDie.Status = DieStatus.Character;
        corsairDie.Level = 1;

        var queue = new AbilityQueue();
        var ability = SampleCards.CorsairRecruitingACrew.Abilities.Single(a => a.Trigger == TriggerType.WhenFielded);
        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "teamA", corsairDie.Id, _ => []));
        Assert.True(state.PendingNextPurchaseGoesToBag);

        var energy = GiveWildEnergy(state, "teamA", SampleCards.Dazzler.PurchaseCost);
        var dazzlerDie = FindUnpurchased(state, "teamA", SampleCards.Dazzler.Id);
        TurnEngine.Purchase(state, dazzlerDie.Id, energy.Select(d => d.Id).ToList());

        Assert.Equal(Zone.Bag, dazzlerDie.Zone);
        Assert.False(state.PendingNextPurchaseGoesToBag); // consumed

        var secondEnergy = GiveWildEnergy(state, "teamA", SampleCards.BlackWidow.PurchaseCost);
        var secondDie = FindUnpurchased(state, "teamA", SampleCards.BlackWidow.Id);
        TurnEngine.Purchase(state, secondDie.Id, secondEnergy.Select(d => d.Id).ToList());

        Assert.Equal(Zone.UsedPile, secondDie.Zone); // back to normal
    }

    [Fact]
    public void RogueSurveillanceImmunity_SendsTargetActionDie_ToOpponentsUsedPile()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.RogueSurveillanceImmunity.Id]);
        state.ActivePlayerId = "teamA";

        var opposingActionDie = new DieInstance
        {
            Id = "teamB-lab-test-1", CardId = SampleCards.LabTest.Id,
            OwnerId = "teamB", ControllerId = "teamB", Zone = Zone.FieldZone, Status = DieStatus.Action,
        };
        state.Dice.Add(opposingActionDie);

        var opposingCharacter = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        opposingCharacter.Zone = Zone.FieldZone;
        opposingCharacter.Status = DieStatus.Character;
        opposingCharacter.Level = 1;

        var rogueDie = FindUnpurchased(state, "teamA", SampleCards.RogueSurveillanceImmunity.Id);
        rogueDie.Zone = Zone.FieldZone;
        rogueDie.Status = DieStatus.Character;
        rogueDie.Level = 1;

        var ability = SampleCards.RogueSurveillanceImmunity.Abilities.Single(a => a.Trigger == TriggerType.WhenFielded);
        var spec = ((MoveDie)ability.Effect).Target;

        // Test the gate: a character die is never a legal Action-die target.
        var legal = LegalTargets.Query(state, "teamA", spec);
        Assert.DoesNotContain(opposingCharacter.Id, legal);
        Assert.Contains(opposingActionDie.Id, legal);

        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "teamA", rogueDie.Id, _ => [opposingActionDie.Id]));

        Assert.Equal(Zone.UsedPile, opposingActionDie.Zone);
        Assert.Equal("teamB", opposingActionDie.ControllerId);
    }

    [Fact]
    public void RogueMrsX_SwapsItsOwnAttack_WithTheTargetsAttack()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.RogueMrsX.Id]);
        state.ActivePlayerId = "teamA";

        var rogueDie = FindUnpurchased(state, "teamA", SampleCards.RogueMrsX.Id);
        rogueDie.Zone = Zone.FieldZone;
        rogueDie.Status = DieStatus.Character;
        rogueDie.Level = 1; // real printed 2A

        var opposingTarget = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id); // placeholder level-3 face: 4A
        opposingTarget.Zone = Zone.FieldZone;
        opposingTarget.Status = DieStatus.Character;
        opposingTarget.Level = 3;

        var rogueAttackBefore = DieStats.EffectiveAttack(state, rogueDie);
        var targetAttackBefore = DieStats.EffectiveAttack(state, opposingTarget);
        Assert.NotEqual(rogueAttackBefore, targetAttackBefore); // otherwise the swap would be a no-op test

        var ability = SampleCards.RogueMrsX.Abilities.Single(a => a.Trigger == TriggerType.WhenFielded);
        EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, "teamA", rogueDie.Id, _ => [opposingTarget.Id]));

        Assert.Equal(targetAttackBefore, DieStats.EffectiveAttack(state, rogueDie));
        Assert.Equal(rogueAttackBefore, DieStats.EffectiveAttack(state, opposingTarget));
    }

    [Fact]
    public void AngelJeanGreysSchool_BoostsOtherFounderDice_ButNotItself_OrNonFounders()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.AngelJeanGreysSchool.Id, SampleCards.JeanGreyMarvelGirl.Id]);
        state.ActivePlayerId = "teamA";

        var angelDie = FindUnpurchased(state, "teamA", SampleCards.AngelJeanGreysSchool.Id);
        angelDie.Zone = Zone.FieldZone; angelDie.Status = DieStatus.Character; angelDie.Level = 1;
        var angelBaseAttack = DieStats.EffectiveAttack(state, angelDie);

        var otherFounder = FindUnpurchased(state, "teamA", SampleCards.JeanGreyMarvelGirl.Id); // also Founder
        otherFounder.Zone = Zone.FieldZone; otherFounder.Status = DieStatus.Character; otherFounder.Level = 1;
        var otherFounderBaseAttack = SampleCards.JeanGreyMarvelGirl.Levels[0].Attack;

        var nonFounder = FindUnpurchased(state, "teamA", SampleCards.BlackWidow.Id);
        nonFounder.Zone = Zone.FieldZone; nonFounder.Status = DieStatus.Character; nonFounder.Level = 1;
        var nonFounderBaseAttack = DieStats.EffectiveAttack(state, nonFounder);

        Assert.Equal(angelBaseAttack, DieStats.EffectiveAttack(state, angelDie)); // "other" - excludes itself
        Assert.Equal(otherFounderBaseAttack + 1, DieStats.EffectiveAttack(state, otherFounder));
        Assert.Equal(nonFounderBaseAttack, DieStats.EffectiveAttack(state, nonFounder)); // not a Founder - unaffected
    }

    [Fact]
    public void MystiqueRelentless_GetsPlus2Attack_WhileWolverineIsActive()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.MystiqueRelentless.Id, SampleCards.WolverinePureOfHeart.Id]);
        state.ActivePlayerId = "teamA";

        var mystiqueDie = FindUnpurchased(state, "teamA", SampleCards.MystiqueRelentless.Id);
        mystiqueDie.Zone = Zone.FieldZone; mystiqueDie.Status = DieStatus.Character; mystiqueDie.Level = 1;
        var baseAttack = DieStats.EffectiveAttack(state, mystiqueDie);

        var wolverineDie = FindUnpurchased(state, "teamA", SampleCards.WolverinePureOfHeart.Id);
        wolverineDie.Zone = Zone.FieldZone; wolverineDie.Status = DieStatus.Character; wolverineDie.Level = 1;

        Assert.Equal(baseAttack + 2, DieStats.EffectiveAttack(state, mystiqueDie));
    }

    [Fact]
    public void MystiqueRelentlessGlobal_PreventsBlocking_OnlyForADieSharingATeamAffiliation()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.MystiqueRelentless.Id], // Team A's roster now includes an X-Men card
            extraTeamBCardIds: [SampleCards.RogueMrsX.Id]); // X-Men, so it shares an affiliation with Team A's roster

        var sharesAffiliation = FindUnpurchased(state, "teamB", SampleCards.RogueMrsX.Id);
        sharesAffiliation.Zone = Zone.FieldZone; sharesAffiliation.Status = DieStatus.Character; sharesAffiliation.Level = 1;

        var doesNotShare = FindUnpurchased(state, "teamB", SampleCards.Groot.Id); // no affiliation at all
        doesNotShare.Zone = Zone.FieldZone; doesNotShare.Status = DieStatus.Character; doesNotShare.Level = 1;

        var mystiqueGlobal = SampleCards.MystiqueRelentless.Abilities.Single(a => a.Trigger == TriggerType.Global);
        var spec = ((CantBlock)mystiqueGlobal.Effect).Target;

        // Test the gate directly first: LegalTargets.Query itself excludes
        // the non-matching die, not just "nothing happened to choose it."
        var legal = LegalTargets.Query(state, "teamA", spec);
        Assert.Contains(sharesAffiliation.Id, legal);
        Assert.DoesNotContain(doesNotShare.Id, legal);

        var energy = GiveWildEnergy(state, "teamA", 2);
        var queue = new AbilityQueue();
        TurnEngine.UseGlobalAbility(state, queue, SampleCards.MystiqueRelentless.Id, "teamA", energy.Select(d => d.Id).ToList());
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [sharesAffiliation.Id])));

        Assert.Contains(sharesAffiliation.Id, state.CantBlockThisTurn);
        Assert.DoesNotContain(doesNotShare.Id, state.CantBlockThisTurn);
    }

    [Fact]
    public void CableBosomBuddies_DiscountsAndBuffs_YourDeadpool()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.CableBosomBuddies.Id, SampleCards.DeadpoolDraftPick.Id]);
        state.ActivePlayerId = "teamA";

        var cableDie = FindUnpurchased(state, "teamA", SampleCards.CableBosomBuddies.Id);
        cableDie.Zone = Zone.FieldZone; cableDie.Status = DieStatus.Character; cableDie.Level = 1;

        var deadpoolDie = FindUnpurchased(state, "teamA", SampleCards.DeadpoolDraftPick.Id);
        var energy = GiveWildEnergy(state, "teamA", SampleCards.DeadpoolDraftPick.PurchaseCost - 1);
        TurnEngine.Purchase(state, deadpoolDie.Id, energy.Select(d => d.Id).ToList()); // 1 less than printed cost

        Assert.Equal(Zone.UsedPile, deadpoolDie.Zone); // succeeded - the discount really applied

        deadpoolDie.Zone = Zone.ReservePool;
        deadpoolDie.Status = DieStatus.Character;
        deadpoolDie.Level = 1;
        var fieldQueue = new AbilityQueue();
        TurnEngine.Field(state, fieldQueue, deadpoolDie.Id, energyDieIdsToSpend: []); // level-1 fielding cost 0
        Assert.Equal(SampleCards.DeadpoolDraftPick.Levels[0].Attack + 2, DieStats.EffectiveAttack(state, deadpoolDie));
    }

    [Fact]
    public void BeastXaviersDream_GetsPlus1Attack_OnlyWhileAnOwnSidekickIsActive()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.BeastXaviersDream.Id]);
        state.ActivePlayerId = "teamA";

        var beastDie = FindUnpurchased(state, "teamA", SampleCards.BeastXaviersDream.Id);
        beastDie.Zone = Zone.FieldZone; beastDie.Status = DieStatus.Character; beastDie.Level = 1;
        var baseAttack = DieStats.EffectiveAttack(state, beastDie);

        var sidekick = state.DiceIn("teamA", Zone.Bag).First();
        sidekick.Zone = Zone.FieldZone;
        sidekick.Status = DieStatus.SidekickCharacter;
        sidekick.Level = 1;

        Assert.Equal(baseAttack + 1, DieStats.EffectiveAttack(state, beastDie));

        sidekick.Zone = Zone.PrepArea; // no longer active
        Assert.Equal(baseAttack, DieStats.EffectiveAttack(state, beastDie));
    }

    [Fact]
    public void ForgeSupportTechnician_SurchargesOpponentsCheapPurchases_ButNotExpensiveOnes()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.ForgeSupportTechnician.Id], // Forge's controller is teamA
            extraTeamBCardIds: [SampleCards.HarleyQuinn.Id, SampleCards.CaptainMarvel.Id]);
        state.ActivePlayerId = "teamB"; // teamB is Forge's controller's "opponent"

        var forgeDie = FindUnpurchased(state, "teamA", SampleCards.ForgeSupportTechnician.Id);
        forgeDie.Zone = Zone.FieldZone; forgeDie.Status = DieStatus.Character; forgeDie.Level = 1;

        var cheapDie = FindUnpurchased(state, "teamB", SampleCards.HarleyQuinn.Id); // purchase cost 1
        Assert.True(SampleCards.HarleyQuinn.PurchaseCost <= 2);
        var exactEnergy = GiveWildEnergy(state, "teamB", SampleCards.HarleyQuinn.PurchaseCost);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            TurnEngine.Purchase(state, cheapDie.Id, exactEnergy.Select(d => d.Id).ToList()));
        Assert.Contains("Not enough energy", ex.Message);

        var extraEnergy = GiveWildEnergy(state, "teamB", 1);
        TurnEngine.Purchase(state, cheapDie.Id, exactEnergy.Concat(extraEnergy).Select(d => d.Id).ToList());
        Assert.Equal(Zone.UsedPile, cheapDie.Zone); // the +1 surcharge really was required

        var expensiveDie = FindUnpurchased(state, "teamB", SampleCards.CaptainMarvel.Id);
        Assert.True(SampleCards.CaptainMarvel.PurchaseCost > 2);
        var normalEnergy = GiveWildEnergy(state, "teamB", SampleCards.CaptainMarvel.PurchaseCost);
        TurnEngine.Purchase(state, expensiveDie.Id, normalEnergy.Select(d => d.Id).ToList()); // no surcharge
        Assert.Equal(Zone.UsedPile, expensiveDie.Zone);
    }

    [Fact]
    public void JeanGreyXaviersDream_SurchargesOpponentsGlobalUse_OnlyWithAnActiveSidekick()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.JeanGreyXaviersDream.Id]);
        state.ActivePlayerId = "teamB"; // Jean Grey's controller is teamA, so teamB is "your opponent"

        var jeanGreyDie = FindUnpurchased(state, "teamA", SampleCards.JeanGreyXaviersDream.Id);
        jeanGreyDie.Zone = Zone.FieldZone; jeanGreyDie.Status = DieStatus.Character; jeanGreyDie.Level = 1;

        var exactEnergy = GiveWildEnergy(state, "teamB", 1); // Falcon's Global costs 1, no Jean Grey Sidekick yet
        var queue = new AbilityQueue();
        TurnEngine.UseGlobalAbility(state, queue, SampleCards.Falcon.Id, "teamB", exactEnergy.Select(d => d.Id).ToList());
        Assert.Contains(SampleCards.Falcon.Id, state.GlobalsUsedThisTurn); // succeeded with no surcharge

        // Reset and prove the surcharge kicks in once Jean Grey's own side has an active Sidekick.
        state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.JeanGreyXaviersDream.Id]);
        state.ActivePlayerId = "teamB";
        jeanGreyDie = FindUnpurchased(state, "teamA", SampleCards.JeanGreyXaviersDream.Id);
        jeanGreyDie.Zone = Zone.FieldZone; jeanGreyDie.Status = DieStatus.Character; jeanGreyDie.Level = 1;
        var teamASidekick = state.DiceIn("teamA", Zone.Bag).First();
        teamASidekick.Zone = Zone.FieldZone; teamASidekick.Status = DieStatus.SidekickCharacter; teamASidekick.Level = 1;

        var insufficientEnergy = GiveWildEnergy(state, "teamB", 1);
        var queue2 = new AbilityQueue();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            TurnEngine.UseGlobalAbility(state, queue2, SampleCards.Falcon.Id, "teamB", insufficientEnergy.Select(d => d.Id).ToList()));
        Assert.Contains("Not enough energy", ex.Message);

        var extra = GiveWildEnergy(state, "teamB", 1);
        TurnEngine.UseGlobalAbility(
            state, queue2, SampleCards.Falcon.Id, "teamB", insufficientEnergy.Concat(extra).Select(d => d.Id).ToList());
        Assert.False(queue2.IsEmpty); // the Global really went through with the surcharge paid
    }

    [Fact]
    public void JeanGreyMarvelGirl_SurchargesOpponentsGlobalUse_UnconditionallyWhileActive()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.JeanGreyMarvelGirl.Id]);
        state.ActivePlayerId = "teamB";

        var jeanGreyDie = FindUnpurchased(state, "teamA", SampleCards.JeanGreyMarvelGirl.Id);
        jeanGreyDie.Zone = Zone.FieldZone; jeanGreyDie.Status = DieStatus.Character; jeanGreyDie.Level = 1;

        var insufficientEnergy = GiveWildEnergy(state, "teamB", 1); // Falcon's Global normally costs 1
        var queue = new AbilityQueue();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            TurnEngine.UseGlobalAbility(state, queue, SampleCards.Falcon.Id, "teamB", insufficientEnergy.Select(d => d.Id).ToList()));
        Assert.Contains("Not enough energy", ex.Message);
    }

    [Fact]
    public void JeanGreyMarvelGirl_IsFreeToField_WhileADifferentXMenDieIsActive()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.JeanGreyMarvelGirl.Id, SampleCards.WolverinePureOfHeart.Id]);
        state.ActivePlayerId = "teamA";

        var otherXMen = FindUnpurchased(state, "teamA", SampleCards.WolverinePureOfHeart.Id);
        otherXMen.Zone = Zone.FieldZone; otherXMen.Status = DieStatus.Character; otherXMen.Level = 1;

        var jeanGreyDie = FindUnpurchased(state, "teamA", SampleCards.JeanGreyMarvelGirl.Id);
        jeanGreyDie.Zone = Zone.ReservePool;
        jeanGreyDie.Status = DieStatus.Character;
        jeanGreyDie.Level = 1; // printed fielding cost 1 - would normally require energy

        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, jeanGreyDie.Id, energyDieIdsToSpend: []); // no exception - free

        Assert.Equal(Zone.FieldZone, jeanGreyDie.Zone);
    }

    [Fact]
    public void AngelXaviersDream_ProtectsOwnSidekicks_FromOpponentGlobalTargeting_ButNotOtherTriggers()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.AngelXaviersDream.Id]);
        state.ActivePlayerId = "teamA";

        var angelDie = FindUnpurchased(state, "teamA", SampleCards.AngelXaviersDream.Id);
        angelDie.Zone = Zone.FieldZone; angelDie.Status = DieStatus.Character; angelDie.Level = 1;

        var sidekick = state.DiceIn("teamA", Zone.Bag).First();
        sidekick.Zone = Zone.FieldZone; sidekick.Status = DieStatus.SidekickCharacter; sidekick.Level = 1;

        var spec = TargetSpec.Sidekick("target Sidekick", TargetOwnership.Opposing);

        Assert.DoesNotContain(sidekick.Id, LegalTargets.Query(state, "teamB", spec, TriggerType.Global));
        Assert.Contains(sidekick.Id, LegalTargets.Query(state, "teamB", spec, TriggerType.WhenFielded));
        Assert.Contains(sidekick.Id, LegalTargets.Query(state, "teamB", spec)); // no trigger info at all
    }

    [Fact]
    public void MoiraStrengthOfForesight_GrantsALoyaltyCounter_OnlyForACostlyXMenFielding()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.MoiraStrengthOfForesight.Id, SampleCards.WolverinePureOfHeart.Id]);
        state.ActivePlayerId = "teamA";

        var moiraDie = FindUnpurchased(state, "teamA", SampleCards.MoiraStrengthOfForesight.Id);
        moiraDie.Zone = Zone.FieldZone; moiraDie.Status = DieStatus.Character; moiraDie.Level = 1;

        var costlyXMen = FindUnpurchased(state, "teamA", SampleCards.WolverinePureOfHeart.Id); // X-Men, cost 4
        Assert.True(SampleCards.WolverinePureOfHeart.PurchaseCost >= 3);
        costlyXMen.Zone = Zone.ReservePool; costlyXMen.Status = DieStatus.Character; costlyXMen.Level = 1;

        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, costlyXMen.Id, energyDieIdsToSpend: [.. GiveWildEnergy(state, "teamA", 2).Select(d => d.Id)]);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [])));

        Assert.Equal(1, state.LoyaltyCounters.GetValueOrDefault(SampleCards.MoiraStrengthOfForesight.Id));

        var nonXMen = FindUnpurchased(state, "teamA", SampleCards.BlackWidow.Id);
        nonXMen.Zone = Zone.ReservePool; nonXMen.Status = DieStatus.Character; nonXMen.Level = 1;
        var queue2 = new AbilityQueue();
        TurnEngine.Field(state, queue2, nonXMen.Id, energyDieIdsToSpend: [.. GiveWildEnergy(state, "teamA", 2).Select(d => d.Id)]);
        queue2.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [])));

        Assert.Equal(1, state.LoyaltyCounters.GetValueOrDefault(SampleCards.MoiraStrengthOfForesight.Id)); // unchanged
    }

    [Fact]
    public void RogueUnitySquad_ReducesFieldingCost_ForXMenDice()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.RogueUnitySquad.Id, SampleCards.RogueMrsX.Id]);
        state.ActivePlayerId = "teamA";

        var rogueDie = FindUnpurchased(state, "teamA", SampleCards.RogueUnitySquad.Id);
        rogueDie.Zone = Zone.FieldZone; rogueDie.Status = DieStatus.Character; rogueDie.Level = 1;

        var xMenDie = FindUnpurchased(state, "teamA", SampleCards.RogueMrsX.Id);
        xMenDie.Zone = Zone.ReservePool; xMenDie.Status = DieStatus.Character; xMenDie.Level = 1; // printed fielding cost 1

        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, xMenDie.Id, energyDieIdsToSpend: []); // free with the -1 reduction

        Assert.Equal(Zone.FieldZone, xMenDie.Zone);
    }

    [Fact]
    public void RogueUnitySquadTeamwatch_FiresOffRealFieldingGate_GetsPlus2AttackUntilEndOfTurn()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.RogueUnitySquad.Id, SampleCards.RogueMrsX.Id]);
        state.ActivePlayerId = "teamA";

        var rogueDie = FindUnpurchased(state, "teamA", SampleCards.RogueUnitySquad.Id);
        rogueDie.Zone = Zone.FieldZone; rogueDie.Status = DieStatus.Character; rogueDie.Level = 1;
        var baseAttack = DieStats.EffectiveAttack(state, rogueDie);

        var otherXMen = FindUnpurchased(state, "teamA", SampleCards.RogueMrsX.Id); // shares X-Men with Rogue
        otherXMen.Zone = Zone.ReservePool; otherXMen.Status = DieStatus.Character; otherXMen.Level = 1;

        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, otherXMen.Id, energyDieIdsToSpend: []);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [])));

        Assert.Equal(baseAttack + 2, DieStats.EffectiveAttack(state, rogueDie));
    }

    [Fact]
    public void MagnetoVisionary_RequiresAtLeastTwoBlockers_ForItsOwnBrotherhoodAttacker()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.MagnetoVisionary.Id]);
        state.ActivePlayerId = "teamA";

        var magnetoDie = FindUnpurchased(state, "teamA", SampleCards.MagnetoVisionary.Id);
        magnetoDie.Zone = Zone.FieldZone; magnetoDie.Status = DieStatus.Character; magnetoDie.Level = 1;

        var blocker1 = state.DiceIn("teamB", Zone.Bag).First();
        blocker1.Zone = Zone.FieldZone; blocker1.Status = DieStatus.SidekickCharacter; blocker1.Level = 1;
        var blocker2 = state.DiceIn("teamB", Zone.Bag).Skip(1).First();
        blocker2.Zone = Zone.FieldZone; blocker2.Status = DieStatus.SidekickCharacter; blocker2.Level = 1;

        TurnEngine.EnterAttackStep(state);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [magnetoDie.Id]);

        var oneBlockerAssignment = new CombatAssignment();
        oneBlockerAssignment.AssignBlocker(magnetoDie.Id, blocker1.Id);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CombatEngine.DeclareBlockers(state, oneBlockerAssignment, [blocker1.Id]));
        Assert.Contains("2 or more", ex.Message);

        var twoBlockerAssignment = new CombatAssignment();
        twoBlockerAssignment.AssignBlocker(magnetoDie.Id, blocker1.Id);
        twoBlockerAssignment.AssignBlocker(magnetoDie.Id, blocker2.Id);
        CombatEngine.DeclareBlockers(state, twoBlockerAssignment, [blocker1.Id, blocker2.Id]); // succeeds
    }

    [Fact]
    public void MagnetoVisionary_LeavingAnAttackerUnblocked_IsStillLegal()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.MagnetoVisionary.Id]);
        state.ActivePlayerId = "teamA";

        var magnetoDie = FindUnpurchased(state, "teamA", SampleCards.MagnetoVisionary.Id);
        magnetoDie.Zone = Zone.FieldZone; magnetoDie.Status = DieStatus.Character; magnetoDie.Level = 1;

        TurnEngine.EnterAttackStep(state);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [magnetoDie.Id]);
        CombatEngine.DeclareBlockers(state, new CombatAssignment(), []); // no exception - unblocked is fine
    }

    [Fact]
    public void MagnetoVisionaryGlobal_IsANoOp_WhenPrepAreaIsEmpty()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.MagnetoVisionary.Id]);
        state.ActivePlayerId = "teamA";

        var magnetoDie = FindUnpurchased(state, "teamA", SampleCards.MagnetoVisionary.Id);
        magnetoDie.Zone = Zone.FieldZone; magnetoDie.Status = DieStatus.Character; magnetoDie.Level = 1;

        var energy = GiveWildEnergy(state, "teamA", 1);
        var queue = new AbilityQueue();
        TurnEngine.UseGlobalAbility(state, queue, SampleCards.MagnetoVisionary.Id, "teamA", energy.Select(d => d.Id).ToList());
        var bagCountBefore = state.DiceIn("teamA", Zone.Bag).Count();
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [])));

        Assert.Empty(state.DiceIn("teamA", Zone.PrepArea));
        Assert.Equal(bagCountBefore, state.DiceIn("teamA", Zone.Bag).Count());
    }

    [Fact]
    public void MagnetoVisionaryGlobal_DrawsADie_WhenPrepAreaAlreadyHasOne()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.MagnetoVisionary.Id]);
        state.ActivePlayerId = "teamA";

        var magnetoDie = FindUnpurchased(state, "teamA", SampleCards.MagnetoVisionary.Id);
        magnetoDie.Zone = Zone.FieldZone; magnetoDie.Status = DieStatus.Character; magnetoDie.Level = 1;

        var alreadyInPrep = state.DiceIn("teamA", Zone.Bag).First();
        alreadyInPrep.Zone = Zone.PrepArea;

        var energy = GiveWildEnergy(state, "teamA", 1);
        var bagCountBefore = state.DiceIn("teamA", Zone.Bag).Count(); // after GiveWildEnergy's own removal
        var queue = new AbilityQueue();
        TurnEngine.UseGlobalAbility(state, queue, SampleCards.MagnetoVisionary.Id, "teamA", energy.Select(d => d.Id).ToList());
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [])));

        Assert.Equal(2, state.DiceIn("teamA", Zone.PrepArea).Count());
        Assert.Equal(bagCountBefore - 1, state.DiceIn("teamA", Zone.Bag).Count());
    }

    // A pre-existing bug caught while authoring Magneto Visionary's own
    // Global above: EffectContext.SourceDieId is always null for a Global
    // ability (rule 3.1.5 - the source is the paying player, not a die),
    // and TargetSpec.Self's own Resolve used to return an EMPTY list
    // whenever SourceDieId was null - which made `Resolve(...).Any(...)`
    // always false for ANY Conditional keyed on TargetSpec.Self inside a
    // Global, regardless of the real condition. Magneto ("Idealist",
    // DPS041)'s own Global ("if you have no dice in your Prep Area, [...]")
    // was already using exactly that shape and had never been exercised by
    // a test - fixed by falling back to ctx.ControllerId when SourceDieId
    // is null (see EffectInterpreter.Resolve's own remarks).
    [Fact]
    public void MagnetoIdealistGlobal_PrepsADie_WhenPrepAreaIsEmpty()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.Magneto.Id]);
        state.ActivePlayerId = "teamA";

        var magnetoDie = FindUnpurchased(state, "teamA", SampleCards.Magneto.Id);
        magnetoDie.Zone = Zone.FieldZone; magnetoDie.Status = DieStatus.Character; magnetoDie.Level = 1;

        var energy = GiveWildEnergy(state, "teamA", 1);
        var queue = new AbilityQueue();
        TurnEngine.UseGlobalAbility(state, queue, SampleCards.Magneto.Id, "teamA", energy.Select(d => d.Id).ToList());
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [])));

        Assert.Single(state.DiceIn("teamA", Zone.PrepArea));
    }

    [Fact]
    public void MagnetoIdealistGlobal_IsANoOp_WhenPrepAreaAlreadyHasADie()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.Magneto.Id]);
        state.ActivePlayerId = "teamA";

        var magnetoDie = FindUnpurchased(state, "teamA", SampleCards.Magneto.Id);
        magnetoDie.Zone = Zone.FieldZone; magnetoDie.Status = DieStatus.Character; magnetoDie.Level = 1;

        var alreadyInPrep = state.DiceIn("teamA", Zone.Bag).First();
        alreadyInPrep.Zone = Zone.PrepArea;

        var energy = GiveWildEnergy(state, "teamA", 1);
        var queue = new AbilityQueue();
        TurnEngine.UseGlobalAbility(state, queue, SampleCards.Magneto.Id, "teamA", energy.Select(d => d.Id).ToList());
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [])));

        Assert.Single(state.DiceIn("teamA", Zone.PrepArea)); // unchanged - still just the one already there
    }

    [Fact]
    public void WolverineHardenedByMadripoorEnergize_FiresOffRealGate_SpinsToLevel1_WithThreeActiveXMen()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [
            SampleCards.WolverineHardenedByMadripoor.Id, SampleCards.RogueMrsX.Id, SampleCards.WolverinePureOfHeart.Id,
            SampleCards.AngelJeanGreysSchool.Id]);
        state.ActivePlayerId = "teamA";
        state.CurrentStep = TurnStep.RollAndReroll;

        var wolverineDie = FindUnpurchased(state, "teamA", SampleCards.WolverineHardenedByMadripoor.Id);
        wolverineDie.Zone = Zone.ReservePool;
        wolverineDie.Status = DieStatus.Energy;
        wolverineDie.EnergyKind = EnergyKind.Generic;
        wolverineDie.EnergyAmount = 2; // double energy - Energize's own trigger condition

        // 3 active X-Men dice, none of them Wolverine himself (he's on an
        // energy face right now, not active).
        foreach (var cardId in new[] { SampleCards.RogueMrsX.Id, SampleCards.WolverinePureOfHeart.Id, SampleCards.AngelJeanGreysSchool.Id })
        {
            var xMenDie = FindUnpurchased(state, "teamA", cardId);
            xMenDie.Zone = Zone.FieldZone; xMenDie.Status = DieStatus.Character; xMenDie.Level = 1;
        }

        var queue = new AbilityQueue();
        TurnEngine.Reroll(state, queue, new FixedRoller(DieStatus.Energy, 1), []);

        Assert.Equal(1, queue.Count);
        Assert.Equal(TriggerType.Energize, queue.Pending[0].Trigger);

        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [])));

        Assert.Equal(DieStatus.Character, wolverineDie.Status);
        Assert.Equal(1, wolverineDie.Level);
    }

    [Fact]
    public void WolverineHardenedByMadripoorEnergize_DoesNotSpin_WithFewerThanThreeActiveXMen()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [
            SampleCards.WolverineHardenedByMadripoor.Id, SampleCards.RogueMrsX.Id]);
        state.ActivePlayerId = "teamA";
        state.CurrentStep = TurnStep.RollAndReroll;

        var wolverineDie = FindUnpurchased(state, "teamA", SampleCards.WolverineHardenedByMadripoor.Id);
        wolverineDie.Zone = Zone.ReservePool;
        wolverineDie.Status = DieStatus.Energy;
        wolverineDie.EnergyKind = EnergyKind.Generic;
        wolverineDie.EnergyAmount = 2;

        var onlyXMen = FindUnpurchased(state, "teamA", SampleCards.RogueMrsX.Id);
        onlyXMen.Zone = Zone.FieldZone; onlyXMen.Status = DieStatus.Character; onlyXMen.Level = 1;

        var queue = new AbilityQueue();
        TurnEngine.Reroll(state, queue, new FixedRoller(DieStatus.Energy, 1), []);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [])));

        Assert.Equal(DieStatus.Energy, wolverineDie.Status); // condition not met - stayed on its energy face
    }

    [Fact]
    public void BeastCombatReadyWhenAttacks_FiresOffRealAttackGate_PrepsADieFromBag()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.BeastCombatReady.Id]);
        state.ActivePlayerId = "teamA";

        var beastDie = FindUnpurchased(state, "teamA", SampleCards.BeastCombatReady.Id);
        beastDie.Zone = Zone.FieldZone; beastDie.Status = DieStatus.Character; beastDie.Level = 1;

        TurnEngine.EnterAttackStep(state);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [beastDie.Id]);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [], Random: new Random(1))));

        Assert.Single(state.DiceIn("teamA", Zone.PrepArea));
    }

    [Fact]
    public void BeastCombatReady_SurchargesOnlyTheFirstPurchase_EachGame()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.BeastCombatReady.Id]);
        state.ActivePlayerId = "teamA";
        Assert.True(SampleCards.BeastCombatReady.DieLimit >= 2);

        var firstBeast = state.DiceIn("teamA", Zone.Unpurchased).Where(d => d.CardId == SampleCards.BeastCombatReady.Id).ElementAt(0);
        var secondBeast = state.DiceIn("teamA", Zone.Unpurchased).Where(d => d.CardId == SampleCards.BeastCombatReady.Id).ElementAt(1);

        var exactEnergy = GiveWildEnergy(state, "teamA", SampleCards.BeastCombatReady.PurchaseCost);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            TurnEngine.Purchase(state, firstBeast.Id, exactEnergy.Select(d => d.Id).ToList()));
        Assert.Contains("Not enough energy", ex.Message);

        var extra = GiveWildEnergy(state, "teamA", 1);
        TurnEngine.Purchase(state, firstBeast.Id, exactEnergy.Concat(extra).Select(d => d.Id).ToList());
        Assert.Equal(Zone.UsedPile, firstBeast.Zone); // the +1 surcharge really was required

        var secondExactEnergy = GiveWildEnergy(state, "teamA", SampleCards.BeastCombatReady.PurchaseCost);
        TurnEngine.Purchase(state, secondBeast.Id, secondExactEnergy.Select(d => d.Id).ToList()); // no surcharge this time
        Assert.Equal(Zone.UsedPile, secondBeast.Zone);
    }

    [Fact]
    public void DarkPhoenixMalevolent_CostsOneLess_WhenOpponentHasAnXMenCharacterOnTheirTeam()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.DarkPhoenixMalevolent.Id],
            extraTeamBCardIds: [SampleCards.RogueMrsX.Id]); // X-Men on teamB's roster
        state.ActivePlayerId = "teamA";

        var dpDie = FindUnpurchased(state, "teamA", SampleCards.DarkPhoenixMalevolent.Id);
        var discountedEnergy = GiveWildEnergy(state, "teamA", SampleCards.DarkPhoenixMalevolent.PurchaseCost - 1);
        TurnEngine.Purchase(state, dpDie.Id, discountedEnergy.Select(d => d.Id).ToList());

        Assert.Equal(Zone.UsedPile, dpDie.Zone); // succeeded 1 short of full price - the discount applied
    }

    [Fact]
    public void DarkPhoenixMalevolent_CostsFullPrice_WhenOpponentHasNoXMen()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.DarkPhoenixMalevolent.Id]);
        state.ActivePlayerId = "teamA";

        var dpDie = FindUnpurchased(state, "teamA", SampleCards.DarkPhoenixMalevolent.Id);
        var tooLittleEnergy = GiveWildEnergy(state, "teamA", SampleCards.DarkPhoenixMalevolent.PurchaseCost - 1);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            TurnEngine.Purchase(state, dpDie.Id, tooLittleEnergy.Select(d => d.Id).ToList()));
        Assert.Contains("Not enough energy", ex.Message);
    }

    [Fact]
    public void DarkPhoenixMalevolentWhenFielded_DamagesOpponent_OnlyIfTheKOdDieWasXMen()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.DarkPhoenixMalevolent.Id, SampleCards.RogueMrsX.Id]);
        state.ActivePlayerId = "teamA";

        var xMenVictim = FindUnpurchased(state, "teamA", SampleCards.RogueMrsX.Id); // targetable by "target character die" (any ownership)
        xMenVictim.Zone = Zone.FieldZone; xMenVictim.Status = DieStatus.Character; xMenVictim.Level = 1;

        var dpDie = FindUnpurchased(state, "teamA", SampleCards.DarkPhoenixMalevolent.Id);
        dpDie.Zone = Zone.FieldZone; dpDie.Status = DieStatus.Character; dpDie.Level = 1;

        var ability = SampleCards.DarkPhoenixMalevolent.Abilities.Single(a => a.Trigger == TriggerType.WhenFielded);
        var opponentLifeBefore = state.PlayerTwo.Life;
        EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(state, "teamA", dpDie.Id, spec => spec.PlayersAllowed ? [state.OpponentOf("teamA")] : [xMenVictim.Id]));

        Assert.Equal(Zone.PrepArea, xMenVictim.Zone); // KO'd
        Assert.Equal(opponentLifeBefore - 1, state.PlayerTwo.Life); // it was X-Men - damage dealt
    }

    [Fact]
    public void DarkPhoenixMalevolentWhenFielded_DoesNotDamageOpponent_WhenTheKOdDieIsNotXMen()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.DarkPhoenixMalevolent.Id]);
        state.ActivePlayerId = "teamA";

        var nonXMenVictim = FindUnpurchased(state, "teamA", SampleCards.BlackWidow.Id); // no affiliation
        nonXMenVictim.Zone = Zone.FieldZone; nonXMenVictim.Status = DieStatus.Character; nonXMenVictim.Level = 1;

        var dpDie = FindUnpurchased(state, "teamA", SampleCards.DarkPhoenixMalevolent.Id);
        dpDie.Zone = Zone.FieldZone; dpDie.Status = DieStatus.Character; dpDie.Level = 1;

        var ability = SampleCards.DarkPhoenixMalevolent.Abilities.Single(a => a.Trigger == TriggerType.WhenFielded);
        var opponentLifeBefore = state.PlayerTwo.Life;
        EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(state, "teamA", dpDie.Id, spec => spec.PlayersAllowed ? [state.OpponentOf("teamA")] : [nonXMenVictim.Id]));

        Assert.Equal(Zone.PrepArea, nonXMenVictim.Zone); // still KO'd
        Assert.Equal(opponentLifeBefore, state.PlayerTwo.Life); // but not X-Men - no damage
    }

    [Fact]
    public void CableHighStakesWhenAttacks_FiresOffRealAttackGate_DoublesOtherDiceButNotSelfOrOpponents()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.CableHighStakes.Id]);
        state.ActivePlayerId = "teamA";

        var cableDie = FindUnpurchased(state, "teamA", SampleCards.CableHighStakes.Id);
        cableDie.Zone = Zone.FieldZone; cableDie.Status = DieStatus.Character; cableDie.Level = 1;
        var cableAttackBefore = DieStats.EffectiveAttack(state, cableDie);

        var otherOwnDie = state.DiceIn("teamA", Zone.Bag).First();
        otherOwnDie.Zone = Zone.FieldZone; otherOwnDie.Status = DieStatus.SidekickCharacter; otherOwnDie.Level = 1;
        var otherOwnAttackBefore = DieStats.EffectiveAttack(state, otherOwnDie);

        var opposingDie = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        opposingDie.Zone = Zone.FieldZone; opposingDie.Status = DieStatus.Character; opposingDie.Level = 1;
        var opposingAttackBefore = DieStats.EffectiveAttack(state, opposingDie);

        TurnEngine.EnterAttackStep(state);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [cableDie.Id]);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [])));

        Assert.Equal(cableAttackBefore, DieStats.EffectiveAttack(state, cableDie)); // "other" - excludes itself
        Assert.Equal(otherOwnAttackBefore * 2, DieStats.EffectiveAttack(state, otherOwnDie));
        Assert.Equal(opposingAttackBefore, DieStats.EffectiveAttack(state, opposingDie)); // "your" - not opponent's
    }

    [Fact]
    public void GambitILikeSolitaire_FiresOffRealFieldingGate_WhenItsTheOnlyCharacterFieldedThisTurn()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.GambitILikeSolitaire.Id]);
        state.ActivePlayerId = "teamA";

        var opposingDie = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        opposingDie.Zone = Zone.FieldZone; opposingDie.Status = DieStatus.Character; opposingDie.Level = 1;

        var gambitDie = FindUnpurchased(state, "teamA", SampleCards.GambitILikeSolitaire.Id);
        gambitDie.Zone = Zone.ReservePool; gambitDie.Status = DieStatus.Character; gambitDie.Level = 1;

        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, gambitDie.Id, energyDieIdsToSpend: [.. GiveWildEnergy(state, "teamA", 2).Select(d => d.Id)]);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [],
                Roller: new FixedRoller(DieStatus.Energy, 1))));

        Assert.Equal(DieStatus.Energy, opposingDie.Status); // rerolled off its character face
        Assert.Contains("teamA", state.CantFieldCharacterDiceThisTurn);

        // The real gate this restriction is enforced through:
        var anotherGambit = state.DiceIn("teamA", Zone.Unpurchased)
            .Where(d => d.CardId == SampleCards.GambitILikeSolitaire.Id).ElementAtOrDefault(1);
        if (anotherGambit is not null)
        {
            anotherGambit.Zone = Zone.ReservePool; anotherGambit.Status = DieStatus.Character; anotherGambit.Level = 1;
            var ex = Assert.Throws<InvalidOperationException>(() =>
                TurnEngine.Field(state, new AbilityQueue(), anotherGambit.Id, energyDieIdsToSpend: []));
            Assert.Contains("can't field any more character dice", ex.Message);
        }
    }

    [Fact]
    public void GambitILikeSolitaire_DoesNothing_WhenAnotherCharacterDieWasAlreadyFieldedThisTurn()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.GambitILikeSolitaire.Id]);
        state.ActivePlayerId = "teamA";

        var opposingDie = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        opposingDie.Zone = Zone.FieldZone; opposingDie.Status = DieStatus.Character; opposingDie.Level = 1;

        var alreadyFielded = state.DiceIn("teamA", Zone.Bag).First();
        alreadyFielded.Zone = Zone.FieldZone; alreadyFielded.Status = DieStatus.SidekickCharacter; alreadyFielded.Level = 1;
        state.FieldedThisTurn.Add(alreadyFielded.Id);

        var gambitDie = FindUnpurchased(state, "teamA", SampleCards.GambitILikeSolitaire.Id);
        gambitDie.Zone = Zone.ReservePool; gambitDie.Status = DieStatus.Character; gambitDie.Level = 1;

        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, gambitDie.Id, energyDieIdsToSpend: [.. GiveWildEnergy(state, "teamA", 2).Select(d => d.Id)]);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [],
                Roller: new FixedRoller(DieStatus.Energy, 1))));

        Assert.Equal(DieStatus.Character, opposingDie.Status); // untouched - condition wasn't met
        Assert.DoesNotContain("teamA", state.CantFieldCharacterDiceThisTurn);
    }

    [Fact]
    public void CyclopsUtopiaRealizedWhenAttacks_DealsNoDamage_WithFewerThanTwoOwnFieldZoneCharacterDice()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.CyclopsUtopiaRealized.Id]);
        state.ActivePlayerId = "teamA";

        var cyclopsDie = FindUnpurchased(state, "teamA", SampleCards.CyclopsUtopiaRealized.Id);
        cyclopsDie.Zone = Zone.FieldZone; cyclopsDie.Status = DieStatus.Character; cyclopsDie.Level = 1;

        var opposingTarget = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        opposingTarget.Zone = Zone.FieldZone; opposingTarget.Status = DieStatus.Character; opposingTarget.Level = 1;

        TurnEngine.EnterAttackStep(state);
        var queue = new AbilityQueue();
        // Cyclops alone - by the time WhenAttacks resolves he's already
        // left the Field Zone for the Attack Zone (DeclareAttackers'
        // own zone move happens before enqueueing), leaving 0 OTHER
        // dice in the Field Zone.
        CombatEngine.DeclareAttackers(state, queue, [cyclopsDie.Id]);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [opposingTarget.Id])));

        Assert.Equal(0, opposingTarget.Damage); // condition not met - the Conditional's Then never ran
    }

    [Fact]
    public void CyclopsUtopiaRealizedWhenAttacks_FiresOffRealAttackGate_DealsDamage_WithTwoOrMoreOwnFieldZoneCharacterDice()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.CyclopsUtopiaRealized.Id]);
        state.ActivePlayerId = "teamA";

        var cyclopsDie = FindUnpurchased(state, "teamA", SampleCards.CyclopsUtopiaRealized.Id);
        cyclopsDie.Zone = Zone.FieldZone; cyclopsDie.Status = DieStatus.Character; cyclopsDie.Level = 1;

        var ownDie1 = state.DiceIn("teamA", Zone.Bag).First();
        ownDie1.Zone = Zone.FieldZone; ownDie1.Status = DieStatus.SidekickCharacter; ownDie1.Level = 1;
        var ownDie2 = state.DiceIn("teamA", Zone.Bag).Skip(1).First();
        ownDie2.Zone = Zone.FieldZone; ownDie2.Status = DieStatus.SidekickCharacter; ownDie2.Level = 1;

        // Level 3 (placeholder 4D) so 3 damage doesn't KO it - a KO'd die's
        // Damage resets to 0 (DieStats.ForceKO's own ResetToUnrolled), which
        // would make a bare Damage-field assertion indistinguishable from
        // "no damage was ever dealt" - the same class of mistake this
        // project already caught once before (Colossus's own redirect
        // tests).
        var opposingTarget = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        opposingTarget.Zone = Zone.FieldZone; opposingTarget.Status = DieStatus.Character; opposingTarget.Level = 3;

        TurnEngine.EnterAttackStep(state);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [cyclopsDie.Id]); // 2 other dice remain in the Field Zone
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [opposingTarget.Id])));

        Assert.Equal(3, opposingTarget.Damage);
    }

    [Fact]
    public void MutantResearchProgram_DrawsThreeDice_WithAtLeastTwoActiveFounders()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.AngelJeanGreysSchool.Id, SampleCards.JeanGreyXaviersDream.Id]);
        state.ActivePlayerId = "teamA";

        foreach (var cardId in new[] { SampleCards.AngelJeanGreysSchool.Id, SampleCards.JeanGreyXaviersDream.Id })
        {
            var founder = FindUnpurchased(state, "teamA", cardId);
            founder.Zone = Zone.FieldZone; founder.Status = DieStatus.Character; founder.Level = 1;
        }

        var ability = SampleCards.MutantResearchProgram.Abilities.Single(a => a.Trigger == TriggerType.WhenUsed);
        var reservePoolCountBefore = state.DiceIn("teamA", Zone.ReservePool).Count();
        EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(state, "teamA", SourceDieId: null, _ => [],
                Random: new Random(1), Roller: new FixedRoller(DieStatus.Energy, 1)));

        Assert.Equal(reservePoolCountBefore + 3, state.DiceIn("teamA", Zone.ReservePool).Count());
    }

    [Fact]
    public void MutantResearchProgram_DrawsOneDie_WithFewerThanTwoActiveFounders()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";

        var ability = SampleCards.MutantResearchProgram.Abilities.Single(a => a.Trigger == TriggerType.WhenUsed);
        var reservePoolCountBefore = state.DiceIn("teamA", Zone.ReservePool).Count();
        EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(state, "teamA", SourceDieId: null, _ => [],
                Random: new Random(1), Roller: new FixedRoller(DieStatus.Energy, 1)));

        Assert.Equal(reservePoolCountBefore + 1, state.DiceIn("teamA", Zone.ReservePool).Count());
    }

    [Fact]
    public void LivingTheDream_BuffsTheWholeTeam_WithAtLeastThreeTeamWideLoyaltyCounters()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";
        state.LoyaltyCounters[SampleCards.Apocalypse.Id] = 2;
        state.LoyaltyCounters[SampleCards.Beast.Id] = 1;

        var ownDie = FindUnpurchased(state, "teamA", SampleCards.BlackWidow.Id);
        ownDie.Zone = Zone.FieldZone; ownDie.Status = DieStatus.Character; ownDie.Level = 1;
        var baseAttack = DieStats.EffectiveAttack(state, ownDie);
        Assert.False(DieStats.HasKeyword(state, ownDie, "Overcrush"));

        var ability = SampleCards.LivingTheDream.Abilities.Single(a => a.Trigger == TriggerType.ContinuousResolve);
        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "teamA", SourceDieId: null, _ => []));

        Assert.Equal(baseAttack + 1, DieStats.EffectiveAttack(state, ownDie));
        Assert.True(DieStats.HasKeyword(state, ownDie, "Overcrush"));
    }

    [Fact]
    public void LivingTheDream_DoesNothing_WithFewerThanThreeTeamWideLoyaltyCounters()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";
        state.LoyaltyCounters[SampleCards.Apocalypse.Id] = 2;

        var ownDie = FindUnpurchased(state, "teamA", SampleCards.BlackWidow.Id);
        ownDie.Zone = Zone.FieldZone; ownDie.Status = DieStatus.Character; ownDie.Level = 1;
        var baseAttack = DieStats.EffectiveAttack(state, ownDie);

        var ability = SampleCards.LivingTheDream.Abilities.Single(a => a.Trigger == TriggerType.ContinuousResolve);
        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "teamA", SourceDieId: null, _ => []));

        Assert.Equal(baseAttack, DieStats.EffectiveAttack(state, ownDie));
        Assert.False(DieStats.HasKeyword(state, ownDie, "Overcrush"));
    }

    [Fact]
    public void DampeningCollar_PreventsOpposingCharacterDice_FromSpinningUp_ButNotOwnDice()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.DampeningCollar.Id],
            extraTeamBCardIds: [SampleCards.BlackWidow.Id]);

        var collarDie = FindUnpurchased(state, "teamA", SampleCards.DampeningCollar.Id);
        collarDie.Zone = Zone.FieldZone; collarDie.Status = DieStatus.Action;

        var opposingDie = FindUnpurchased(state, "teamB", SampleCards.BlackWidow.Id);
        opposingDie.Zone = Zone.FieldZone; opposingDie.Status = DieStatus.Character; opposingDie.Level = 1;
        Assert.Equal(0, DieStats.SpinLevel(state, opposingDie, +1));
        Assert.Equal(1, opposingDie.Level);

        var ownDie = FindUnpurchased(state, "teamA", SampleCards.BlackWidow.Id);
        ownDie.Zone = Zone.FieldZone; ownDie.Status = DieStatus.Character; ownDie.Level = 1;
        Assert.Equal(1, DieStats.SpinLevel(state, ownDie, +1)); // not opposing Dampening Collar's own controller
        Assert.Equal(2, ownDie.Level);

        // Spin-DOWN is unaffected either way - only "spin up" is blocked.
        // (opposingDie is already floored at level 1, so spin the level-2
        // ownDie back down instead to prove the block is direction-specific,
        // not a blanket freeze.)
        Assert.Equal(-1, DieStats.SpinLevel(state, ownDie, -1));
        Assert.Equal(1, ownDie.Level);
    }

    [Fact]
    public void OpponentResolveContinuousDie_RemovesDampeningCollar_ByReturningAnXMenDieToItsCard()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.DampeningCollar.Id],
            extraTeamBCardIds: [SampleCards.GambitUnlessIGotSomeoneToPlayWith.Id, SampleCards.BlackWidow.Id]);
        state.CurrentStep = TurnStep.Main;
        state.ActivePlayerId = "teamA"; // opponent (teamB) can still act - no priority-passing modeled yet

        var collarDie = FindUnpurchased(state, "teamA", SampleCards.DampeningCollar.Id);
        collarDie.Zone = Zone.FieldZone; collarDie.Status = DieStatus.Action;

        var xMenDie = FindUnpurchased(state, "teamB", SampleCards.GambitUnlessIGotSomeoneToPlayWith.Id);
        xMenDie.Zone = Zone.FieldZone; xMenDie.Status = DieStatus.Character; xMenDie.Level = 1;

        TurnEngine.OpponentResolveContinuousDie(state, collarDie.Id, xMenDie.Id);

        Assert.Equal(Zone.Unpurchased, collarDie.Zone);
        Assert.Equal(Zone.Unpurchased, xMenDie.Zone);
        Assert.Equal(DieStatus.Unrolled, xMenDie.Status);

        // The prevention is gone along with the die.
        var stillOpposing = FindUnpurchased(state, "teamB", SampleCards.BlackWidow.Id);
        stillOpposing.Zone = Zone.FieldZone; stillOpposing.Status = DieStatus.Character; stillOpposing.Level = 1;
        Assert.Equal(1, DieStats.SpinLevel(state, stillOpposing, +1));
    }

    [Fact]
    public void OpponentResolveContinuousDie_RejectsANonXMenAffiliate()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.DampeningCollar.Id],
            extraTeamBCardIds: [SampleCards.BlackWidow.Id]);
        state.CurrentStep = TurnStep.Main;

        var collarDie = FindUnpurchased(state, "teamA", SampleCards.DampeningCollar.Id);
        collarDie.Zone = Zone.FieldZone; collarDie.Status = DieStatus.Action;

        var nonXMenDie = FindUnpurchased(state, "teamB", SampleCards.BlackWidow.Id); // no affiliations at all
        nonXMenDie.Zone = Zone.FieldZone; nonXMenDie.Status = DieStatus.Character; nonXMenDie.Level = 1;

        var ex = Assert.Throws<InvalidOperationException>(
            () => TurnEngine.OpponentResolveContinuousDie(state, collarDie.Id, nonXMenDie.Id));
        Assert.Contains("X-Men", ex.Message);
        Assert.Equal(Zone.FieldZone, collarDie.Zone); // untouched - the attempt was rejected
    }

    [Fact]
    public void OpponentResolveContinuousDie_RejectsADieControlledByTheSamePlayer()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.DampeningCollar.Id, SampleCards.GambitUnlessIGotSomeoneToPlayWith.Id]);
        state.CurrentStep = TurnStep.Main;

        var collarDie = FindUnpurchased(state, "teamA", SampleCards.DampeningCollar.Id);
        collarDie.Zone = Zone.FieldZone; collarDie.Status = DieStatus.Action;

        var ownXMenDie = FindUnpurchased(state, "teamA", SampleCards.GambitUnlessIGotSomeoneToPlayWith.Id);
        ownXMenDie.Zone = Zone.FieldZone; ownXMenDie.Status = DieStatus.Character; ownXMenDie.Level = 1;

        var ex = Assert.Throws<InvalidOperationException>(
            () => TurnEngine.OpponentResolveContinuousDie(state, collarDie.Id, ownXMenDie.Id));
        Assert.Contains("opponent", ex.Message);
    }

    [Fact]
    public void CorsairBackFromOuterSpace_WhenKOd_MayPrepAnotherCorsairDie_IfFourOrMoreCharacterDiceKOdThisTurn()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.CorsairBackFromOuterSpace.Id]);

        // Real KO count via the real choke point (DieStats.ForceKO), not
        // a manually-set counter - matches "test the gate, not just the
        // effect."
        foreach (var filler in state.DiceIn("teamA", Zone.Bag).Take(4).ToList())
        {
            filler.Zone = Zone.FieldZone; filler.Status = DieStatus.SidekickCharacter; filler.Level = 1;
            DieStats.ForceKO(state, filler);
        }
        Assert.Equal(4, state.CharacterDiceKOdThisTurnByController["teamA"]);

        var corsairDie = FindUnpurchased(state, "teamA", SampleCards.CorsairBackFromOuterSpace.Id);
        corsairDie.Zone = Zone.FieldZone; corsairDie.Status = DieStatus.Character; corsairDie.Level = 1;
        // A second, still-dormant Corsair copy (dieLimit 4) - the real
        // "from this card" target once the first copy is KO'd below.
        var dormantCorsair = FindUnpurchased(state, "teamA", SampleCards.CorsairBackFromOuterSpace.Id);
        Assert.Equal(Zone.Unpurchased, dormantCorsair.Zone);

        var queue = new AbilityQueue();
        // A real Ko effect (not a manually-enqueued trigger) - exercises
        // the actual TurnEngine.ResolveKOReactions scan that fires
        // WhenKOd for real.
        EffectInterpreter.Execute(
            new Ko(TargetSpec.Self), new EffectContext(state, "teamA", corsairDie.Id, _ => [], Queue: queue));

        Assert.Contains(queue.Pending, a => a.Trigger == TriggerType.WhenKOd && a.SourceDieId == corsairDie.Id);

        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [dormantCorsair.Id], Queue: queue)));

        Assert.Equal(Zone.PrepArea, dormantCorsair.Zone);
    }

    [Fact]
    public void CorsairBackFromOuterSpace_WhenKOd_DoesNothing_WithFewerThanFourCharacterDiceKOdThisTurn()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.CorsairBackFromOuterSpace.Id]);

        var corsairDie = FindUnpurchased(state, "teamA", SampleCards.CorsairBackFromOuterSpace.Id);
        corsairDie.Zone = Zone.FieldZone; corsairDie.Status = DieStatus.Character; corsairDie.Level = 1;
        var dormantCorsair = FindUnpurchased(state, "teamA", SampleCards.CorsairBackFromOuterSpace.Id);

        var queue = new AbilityQueue();
        EffectInterpreter.Execute(
            new Ko(TargetSpec.Self), new EffectContext(state, "teamA", corsairDie.Id, _ => [], Queue: queue));
        // Rule 3.2.5 - every branch's targets are resolved upfront
        // regardless of which one the condition actually picks (see
        // EffectInterpreter.Execute's own remarks), so the resolver still
        // gets asked here; the condition being false just means the
        // resolved choice is never acted on.
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [dormantCorsair.Id], Queue: queue)));

        Assert.Equal(Zone.Unpurchased, dormantCorsair.Zone); // untouched - condition was false
    }

    [Fact]
    public void OrganicSteelPreventDamage_PreventsUpToTwoDamage_ToTargetDie()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";

        var target = FindUnpurchased(state, "teamA", SampleCards.BlackWidow.Id);
        target.Zone = Zone.FieldZone; target.Status = DieStatus.Character; target.Level = 1;

        var ability = SampleCards.OrganicSteelPreventDamage.Abilities.Single(a => a.Trigger == TriggerType.ContinuousResolve);
        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "teamA", SourceDieId: null, _ => [target.Id]));

        // 3 damage against a 2-damage shield - 2 is prevented, 1 gets through.
        DieStats.ApplyDamage(state, target, 3);
        Assert.Equal(1, target.Damage);
        Assert.Equal(0, target.PendingDamagePrevention);
    }

    [Fact]
    public void OrganicSteelPreventDamage_ShieldIsSingleUse_NotARunningTotal()
    {
        var state = BuildTwoTeamGame();

        var target = FindUnpurchased(state, "teamA", SampleCards.BlackWidow.Id);
        target.Zone = Zone.FieldZone; target.Status = DieStatus.Character; target.Level = 1;

        var ability = SampleCards.OrganicSteelPreventDamage.Abilities.Single(a => a.Trigger == TriggerType.ContinuousResolve);
        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "teamA", SourceDieId: null, _ => [target.Id]));

        DieStats.ApplyDamage(state, target, 1); // fully absorbed by the shield (up to 2 available)
        Assert.Equal(0, target.Damage);

        DieStats.ApplyDamage(state, target, 1); // shield is already spent - this one lands for real
        Assert.Equal(1, target.Damage);
    }

    [Fact]
    public void OrganicSteelPreventDamage_GainsLife_OnlyWithAnActiveXMenCharacter()
    {
        var withXMen = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.GambitUnlessIGotSomeoneToPlayWith.Id]);
        withXMen.ActivePlayerId = "teamA";
        var xMenDie = FindUnpurchased(withXMen, "teamA", SampleCards.GambitUnlessIGotSomeoneToPlayWith.Id);
        xMenDie.Zone = Zone.FieldZone; xMenDie.Status = DieStatus.Character; xMenDie.Level = 1;
        var target = FindUnpurchased(withXMen, "teamA", SampleCards.BlackWidow.Id);
        target.Zone = Zone.FieldZone; target.Status = DieStatus.Character; target.Level = 1;
        withXMen.GetPlayer("teamA").Life -= 5; // room to gain back to - GainLife caps at StartingLife
        var lifeBefore = withXMen.GetPlayer("teamA").Life;

        var ability = SampleCards.OrganicSteelPreventDamage.Abilities.Single(a => a.Trigger == TriggerType.ContinuousResolve);
        EffectInterpreter.Execute(ability.Effect, new EffectContext(withXMen, "teamA", SourceDieId: null, _ => [target.Id]));
        Assert.Equal(lifeBefore + 1, withXMen.GetPlayer("teamA").Life);

        var withoutXMen = BuildTwoTeamGame();
        withoutXMen.ActivePlayerId = "teamA";
        var target2 = FindUnpurchased(withoutXMen, "teamA", SampleCards.BlackWidow.Id);
        target2.Zone = Zone.FieldZone; target2.Status = DieStatus.Character; target2.Level = 1;
        withoutXMen.GetPlayer("teamA").Life -= 5;
        var lifeBefore2 = withoutXMen.GetPlayer("teamA").Life;

        EffectInterpreter.Execute(ability.Effect, new EffectContext(withoutXMen, "teamA", SourceDieId: null, _ => [target2.Id]));
        Assert.Equal(lifeBefore2, withoutXMen.GetPlayer("teamA").Life);
    }

    [Fact]
    public void ColossusPiotr_DealsDamagePerLevel2Or3CharacterDie_AtEndOfTurn()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.ColossusPiotr.Id]);
        state.ActivePlayerId = "teamA";

        var colossusDie = FindUnpurchased(state, "teamA", SampleCards.ColossusPiotr.Id);
        colossusDie.Zone = Zone.FieldZone; colossusDie.Status = DieStatus.Character; colossusDie.Level = 2; // qualifies

        var lowLevelOwnDie = state.DiceIn("teamA", Zone.Bag).First();
        lowLevelOwnDie.Zone = Zone.FieldZone; lowLevelOwnDie.Status = DieStatus.SidekickCharacter; lowLevelOwnDie.Level = 1; // Sidekicks are always level 1 - doesn't qualify

        var opponentLifeBefore = state.PlayerTwo.Life;
        state.CurrentStep = TurnStep.CleanUp;
        TurnEngine.CleanUp(state, new FixedRoller(DieStatus.Energy, 1));

        Assert.Equal(opponentLifeBefore - 2, state.PlayerTwo.Life); // only Colossus itself (level 2) qualifies
    }

    [Fact]
    public void DKenShiarCivilWar_BlanksOpposingCheapDice_ButNotExpensiveOnes()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.DKenShiarCivilWar.Id],
            extraTeamBCardIds: [SampleCards.CaptainMarvel.Id, SampleCards.BlackWidow.Id]);
        state.ActivePlayerId = "teamA";

        var dKenDie = FindUnpurchased(state, "teamA", SampleCards.DKenShiarCivilWar.Id);
        dKenDie.Zone = Zone.FieldZone; dKenDie.Status = DieStatus.Character; dKenDie.Level = 1;

        var cheapDie = FindUnpurchased(state, "teamB", SampleCards.BlackWidow.Id); // cost 3, printed Call Out
        cheapDie.Zone = Zone.FieldZone; cheapDie.Status = DieStatus.Character; cheapDie.Level = 1;

        var expensiveDie = FindUnpurchased(state, "teamB", SampleCards.CaptainMarvel.Id); // cost 4, static +1/+1 to itself
        expensiveDie.Zone = Zone.FieldZone; expensiveDie.Status = DieStatus.Character; expensiveDie.Level = 1;
        var expensiveBaseAttack = SampleCards.CaptainMarvel.Levels[0].Attack;

        Assert.False(DieStats.HasKeyword(state, cheapDie, "Call Out")); // blanked
        Assert.Equal(expensiveBaseAttack + 1, DieStats.EffectiveAttack(state, expensiveDie)); // unaffected - own bonus still applies
    }

    [Fact]
    public void DKenShiarCivilWar_FreelyFields_OpposingCheapDice()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.DKenShiarCivilWar.Id],
            extraTeamBCardIds: [SampleCards.BlackWidow.Id]);
        state.ActivePlayerId = "teamB";

        var dKenDie = FindUnpurchased(state, "teamA", SampleCards.DKenShiarCivilWar.Id);
        dKenDie.Zone = Zone.FieldZone; dKenDie.Status = DieStatus.Character; dKenDie.Level = 1;

        var cheapDie = FindUnpurchased(state, "teamB", SampleCards.BlackWidow.Id);
        cheapDie.Zone = Zone.ReservePool; cheapDie.Status = DieStatus.Character; cheapDie.Level = 3; // printed fielding cost 1

        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, cheapDie.Id, energyDieIdsToSpend: []); // no exception - free

        Assert.Equal(Zone.FieldZone, cheapDie.Zone);
    }

    [Fact]
    public void BishopTimeTraveller_PrepsThePurchasedDie_WhenPaidWithOnlyBishopEnergy()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.BishopTimeTraveller.Id]);
        state.ActivePlayerId = "teamA";

        var bishopEnergy = FindUnpurchased(state, "teamA", SampleCards.BishopTimeTraveller.Id);
        bishopEnergy.Zone = Zone.ReservePool;
        bishopEnergy.Status = DieStatus.Energy;
        bishopEnergy.EnergyKind = EnergyKind.Wild;
        bishopEnergy.EnergyAmount = SampleCards.Dazzler.PurchaseCost;

        var dazzlerDie = FindUnpurchased(state, "teamA", SampleCards.Dazzler.Id);
        TurnEngine.Purchase(state, dazzlerDie.Id, [bishopEnergy.Id]);

        Assert.Equal(Zone.PrepArea, dazzlerDie.Zone);
    }

    [Fact]
    public void BishopTimeTraveller_DoesNotPrep_WhenPaidWithMixedEnergy()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.BishopTimeTraveller.Id]);
        state.ActivePlayerId = "teamA";

        var bishopEnergy = FindUnpurchased(state, "teamA", SampleCards.BishopTimeTraveller.Id);
        bishopEnergy.Zone = Zone.ReservePool;
        bishopEnergy.Status = DieStatus.Energy;
        bishopEnergy.EnergyKind = EnergyKind.Wild;
        bishopEnergy.EnergyAmount = SampleCards.Dazzler.PurchaseCost - 1;

        var otherEnergy = GiveWildEnergy(state, "teamA", 1); // a plain Sidekick energy die - not Bishop-named
        var dazzlerDie = FindUnpurchased(state, "teamA", SampleCards.Dazzler.Id);
        TurnEngine.Purchase(state, dazzlerDie.Id, [bishopEnergy.Id, .. otherEnergy.Select(d => d.Id)]);

        Assert.Equal(Zone.UsedPile, dazzlerDie.Zone); // ordinary destination - mixed energy doesn't qualify
    }

    [Fact]
    public void BlinkExilesTeamLeader_FiresOffRealAttackGate_GrantsInfiltrateToXMenInFieldZone()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [
            SampleCards.BlinkExilesTeamLeader.Id, SampleCards.RogueMrsX.Id, SampleCards.WolverinePureOfHeart.Id,
            SampleCards.AngelJeanGreysSchool.Id]);
        state.ActivePlayerId = "teamA";

        var blinkDie = FindUnpurchased(state, "teamA", SampleCards.BlinkExilesTeamLeader.Id);
        blinkDie.Zone = Zone.FieldZone; blinkDie.Status = DieStatus.Character; blinkDie.Level = 1;

        var otherXMen1 = FindUnpurchased(state, "teamA", SampleCards.RogueMrsX.Id);
        otherXMen1.Zone = Zone.FieldZone; otherXMen1.Status = DieStatus.Character; otherXMen1.Level = 1;
        var otherXMen2 = FindUnpurchased(state, "teamA", SampleCards.WolverinePureOfHeart.Id);
        otherXMen2.Zone = Zone.FieldZone; otherXMen2.Status = DieStatus.Character; otherXMen2.Level = 1;

        // Stays in the Field Zone (doesn't attack) - Blink's own grant is
        // scoped to X-Men dice "in the Field Zone," which every attacking
        // die (Blink included) has already left by the time it resolves.
        var stayingXMen = FindUnpurchased(state, "teamA", SampleCards.AngelJeanGreysSchool.Id);
        stayingXMen.Zone = Zone.FieldZone; stayingXMen.Status = DieStatus.Character; stayingXMen.Level = 1;

        TurnEngine.EnterAttackStep(state);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [blinkDie.Id, otherXMen1.Id, otherXMen2.Id]);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [])));

        Assert.True(DieStats.HasKeyword(state, stayingXMen, "Infiltrate"));
    }

    [Fact]
    public void BlinkExilesTeamLeader_DoesNotGrantInfiltrate_WithFewerThanTwoOtherXMenAttacking()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.BlinkExilesTeamLeader.Id, SampleCards.RogueMrsX.Id]);
        state.ActivePlayerId = "teamA";

        var blinkDie = FindUnpurchased(state, "teamA", SampleCards.BlinkExilesTeamLeader.Id);
        blinkDie.Zone = Zone.FieldZone; blinkDie.Status = DieStatus.Character; blinkDie.Level = 1;

        var stayingXMen = FindUnpurchased(state, "teamA", SampleCards.RogueMrsX.Id);
        stayingXMen.Zone = Zone.FieldZone; stayingXMen.Status = DieStatus.Character; stayingXMen.Level = 1;

        TurnEngine.EnterAttackStep(state);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [blinkDie.Id]); // attacking alone
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [])));

        Assert.False(DieStats.HasKeyword(state, stayingXMen, "Infiltrate"));
    }

    [Fact]
    public void Radicalization_DealsDamage_AndKOsASidekick_OnDoubleBurstFace()
    {
        var state = BuildTwoTeamGame(extraTeamBCardIds: [SampleCards.RogueMrsX.Id]);
        state.ActivePlayerId = "teamA";

        var xMenTarget = FindUnpurchased(state, "teamB", SampleCards.RogueMrsX.Id);
        xMenTarget.Zone = Zone.FieldZone; xMenTarget.Status = DieStatus.Character; xMenTarget.Level = 3; // survives 3 damage

        var sidekick = state.DiceIn("teamB", Zone.Bag).First();
        sidekick.Zone = Zone.FieldZone; sidekick.Status = DieStatus.SidekickCharacter; sidekick.Level = 1;

        var radDie = new DieInstance
        {
            Id = "teamA-rad-1", CardId = SampleCards.Radicalization.Id,
            OwnerId = "teamA", ControllerId = "teamA", Zone = Zone.ReservePool, Status = DieStatus.Action, BurstStars = 2,
        };
        state.Dice.Add(radDie);

        var ability = SampleCards.Radicalization.Abilities.Single(a => a.Trigger == TriggerType.WhenUsed);
        EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(state, "teamA", radDie.Id, spec => spec.SidekicksOnly ? [sidekick.Id] : [xMenTarget.Id]));

        Assert.Equal(3, xMenTarget.Damage);
        Assert.Equal(Zone.PrepArea, sidekick.Zone); // KO'd via the double-burst follow-up
    }

    [Fact]
    public void RadicalizationGlobal_GrantsBothAffiliations_UntilEndOfTurn()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";

        var target = FindUnpurchased(state, "teamA", SampleCards.BlackWidow.Id); // no printed affiliation
        target.Zone = Zone.FieldZone; target.Status = DieStatus.Character; target.Level = 1;
        Assert.False(DieStats.HasAffiliation(state, target, "X-Men"));

        var ability = SampleCards.Radicalization.Abilities.Single(a => a.Trigger == TriggerType.Global);
        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "teamA", SourceDieId: null, _ => [target.Id]));

        Assert.True(DieStats.HasAffiliation(state, target, "X-Men"));
        Assert.True(DieStats.HasAffiliation(state, target, "Brotherhood of Mutants"));

        state.CurrentStep = TurnStep.CleanUp;
        TurnEngine.CleanUp(state, new FixedRoller(DieStatus.Energy, 1));

        Assert.False(DieStats.HasAffiliation(state, target, "X-Men")); // expired at Clean Up
    }

    [Fact]
    public void TightRanks_KOsTarget_WhenThreeActiveDiceShareAnAffiliation()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds:
            [SampleCards.RogueMrsX.Id, SampleCards.WolverinePureOfHeart.Id, SampleCards.AngelJeanGreysSchool.Id]);
        state.ActivePlayerId = "teamA";

        foreach (var id in new[]
                 { SampleCards.RogueMrsX.Id, SampleCards.WolverinePureOfHeart.Id, SampleCards.AngelJeanGreysSchool.Id })
        {
            var d = FindUnpurchased(state, "teamA", id); // all X-Men
            d.Zone = Zone.FieldZone; d.Status = DieStatus.Character; d.Level = 1;
        }

        var victim = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        victim.Zone = Zone.FieldZone; victim.Status = DieStatus.Character; victim.Level = 1;

        var ability = SampleCards.TightRanks.Abilities.Single(a => a.Trigger == TriggerType.WhenUsed);
        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "teamA", SourceDieId: null, _ => [victim.Id]));

        Assert.Equal(Zone.PrepArea, victim.Zone);
    }

    [Fact]
    public void TightRanks_DoesNothing_WithoutThreeSharedAffiliationDice()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";

        var victim = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        victim.Zone = Zone.FieldZone; victim.Status = DieStatus.Character; victim.Level = 1;

        var ability = SampleCards.TightRanks.Abilities.Single(a => a.Trigger == TriggerType.WhenUsed);
        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "teamA", SourceDieId: null, _ => [victim.Id]));

        Assert.Equal(Zone.FieldZone, victim.Zone); // untouched
    }

    [Fact]
    public void TightRanksGlobal_OnlyTargetsDiceWithALoyaltyCounter()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";

        var withCounter = FindUnpurchased(state, "teamA", SampleCards.Apocalypse.Id);
        withCounter.Zone = Zone.FieldZone; withCounter.Status = DieStatus.Character; withCounter.Level = 1;
        state.LoyaltyCounters[SampleCards.Apocalypse.Id] = 1;

        var withoutCounter = FindUnpurchased(state, "teamA", SampleCards.Beast.Id);
        withoutCounter.Zone = Zone.FieldZone; withoutCounter.Status = DieStatus.Character; withoutCounter.Level = 1;

        var ability = SampleCards.TightRanks.Abilities.Single(a => a.Trigger == TriggerType.Global);
        var spec = ((ModifyStat)ability.Effect).Target;
        var legal = LegalTargets.Query(state, "teamA", spec);

        Assert.Contains(withCounter.Id, legal);
        Assert.DoesNotContain(withoutCounter.Id, legal);

        var baseAttack = DieStats.EffectiveAttack(state, withCounter);
        var baseDefense = DieStats.EffectiveDefense(state, withCounter);
        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "teamA", SourceDieId: null, _ => [withCounter.Id]));

        Assert.Equal(baseAttack - 2, DieStats.EffectiveAttack(state, withCounter));
        Assert.Equal(baseDefense - 2, DieStats.EffectiveDefense(state, withCounter));
    }

    [Fact]
    public void GreetingsFromKrakoa_SpinsUpAndBuffs_OnlyLoyaltyCounteredDiceThatActuallyMoved()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";
        state.LoyaltyCounters[SampleCards.Apocalypse.Id] = 1;
        state.LoyaltyCounters[SampleCards.Beast.Id] = 1;

        var canSpinUp = FindUnpurchased(state, "teamA", SampleCards.Apocalypse.Id);
        canSpinUp.Zone = Zone.FieldZone; canSpinUp.Status = DieStatus.Character; canSpinUp.Level = 1; // room to move up
        var baseAttack = DieStats.EffectiveAttack(state, canSpinUp);

        var alreadyMaxed = FindUnpurchased(state, "teamA", SampleCards.Beast.Id);
        alreadyMaxed.Zone = Zone.FieldZone; alreadyMaxed.Status = DieStatus.Character; alreadyMaxed.Level = 3; // maxed
        var maxedBaseAttack = DieStats.EffectiveAttack(state, alreadyMaxed);

        var ability = SampleCards.GreetingsFromKrakoa.Abilities.Single(a => a.Trigger == TriggerType.WhenUsed);
        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "teamA", SourceDieId: null, _ => []));

        Assert.Equal(2, canSpinUp.Level); // actually spun up
        Assert.Contains(canSpinUp.AppliedModifiers, m => m.AttackDelta == 2); // got the +2A follow-up
        Assert.Equal(3, alreadyMaxed.Level); // unchanged - already maxed, nothing to spin up to
        Assert.DoesNotContain(alreadyMaxed.AppliedModifiers, m => m.AttackDelta == 2); // no bonus - never actually moved
        Assert.Equal(maxedBaseAttack, DieStats.EffectiveAttack(state, alreadyMaxed));
    }

    [Fact]
    public void JubileeFireworks_FiresOffRealGlobalGate_WhenXMenEnergyIsSpent()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.JubileeFireworks.Id, SampleCards.RogueMrsX.Id]);
        state.ActivePlayerId = "teamA";

        var jubileeDie = FindUnpurchased(state, "teamA", SampleCards.JubileeFireworks.Id);
        jubileeDie.Zone = Zone.FieldZone; jubileeDie.Status = DieStatus.Character; jubileeDie.Level = 1;

        var xMenEnergy = FindUnpurchased(state, "teamA", SampleCards.RogueMrsX.Id);
        xMenEnergy.Zone = Zone.ReservePool; xMenEnergy.Status = DieStatus.Energy;
        xMenEnergy.EnergyKind = EnergyKind.Wild; xMenEnergy.EnergyAmount = 1;

        var opposingTarget = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        opposingTarget.Zone = Zone.FieldZone; opposingTarget.Status = DieStatus.Character; opposingTarget.Level = 1;

        var queue = new AbilityQueue();
        TurnEngine.UseGlobalAbility(state, queue, SampleCards.Falcon.Id, "teamA", [xMenEnergy.Id]);
        Assert.Contains(queue.Pending, a => a.Trigger == TriggerType.WhenXMenEnergySpentOnGlobalOrField);

        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [opposingTarget.Id])));

        Assert.Equal(1, opposingTarget.Damage);
    }

    [Fact]
    public void JubileeFireworks_DoesNotFire_WhenNonXMenEnergyIsSpent()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.JubileeFireworks.Id]);
        state.ActivePlayerId = "teamA";

        var jubileeDie = FindUnpurchased(state, "teamA", SampleCards.JubileeFireworks.Id);
        jubileeDie.Zone = Zone.FieldZone; jubileeDie.Status = DieStatus.Character; jubileeDie.Level = 1;

        var plainEnergy = GiveWildEnergy(state, "teamA", 1); // bare Sidekick - no affiliation

        var queue = new AbilityQueue();
        TurnEngine.UseGlobalAbility(state, queue, SampleCards.Falcon.Id, "teamA", plainEnergy.Select(d => d.Id).ToList());

        Assert.DoesNotContain(queue.Pending, a => a.Trigger == TriggerType.WhenXMenEnergySpentOnGlobalOrField);
    }

    [Fact]
    public void JubileeFireworks_FiresOffRealFieldingGate_WhenXMenEnergyIsSpent()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds:
            [SampleCards.JubileeFireworks.Id, SampleCards.RogueMrsX.Id, SampleCards.WolverinePureOfHeart.Id]);
        state.ActivePlayerId = "teamA";

        var jubileeDie = FindUnpurchased(state, "teamA", SampleCards.JubileeFireworks.Id);
        jubileeDie.Zone = Zone.FieldZone; jubileeDie.Status = DieStatus.Character; jubileeDie.Level = 1;

        var xMenEnergy = FindUnpurchased(state, "teamA", SampleCards.RogueMrsX.Id);
        xMenEnergy.Zone = Zone.ReservePool; xMenEnergy.Status = DieStatus.Energy;
        xMenEnergy.EnergyKind = EnergyKind.Wild; xMenEnergy.EnergyAmount = 1;

        var fieldedDie = FindUnpurchased(state, "teamA", SampleCards.WolverinePureOfHeart.Id);
        fieldedDie.Zone = Zone.ReservePool; fieldedDie.Status = DieStatus.Character; fieldedDie.Level = 1; // printed fielding cost 1

        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, fieldedDie.Id, [xMenEnergy.Id]);

        Assert.Contains(queue.Pending, a => a.Trigger == TriggerType.WhenXMenEnergySpentOnGlobalOrField);
    }

    [Fact]
    public void BeastFirstClass_ReactsToAnOwnFounderAttacker_PrepsADieFromBag()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.BeastFirstClass.Id, SampleCards.JeanGreyPeacefulCoexistence.Id]);
        state.ActivePlayerId = "teamA";

        var beastDie = FindUnpurchased(state, "teamA", SampleCards.BeastFirstClass.Id);
        beastDie.Zone = Zone.FieldZone; beastDie.Status = DieStatus.Character; beastDie.Level = 1;

        var founderAttacker = FindUnpurchased(state, "teamA", SampleCards.JeanGreyPeacefulCoexistence.Id); // printed Founder
        founderAttacker.Zone = Zone.FieldZone; founderAttacker.Status = DieStatus.Character; founderAttacker.Level = 1;

        TurnEngine.EnterAttackStep(state);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [founderAttacker.Id]);
        Assert.Contains(queue.Pending, a => a.Trigger == TriggerType.WhenAnotherDieAttacks);

        var bagCountBefore = state.DiceIn("teamA", Zone.Bag).Count();
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [], Random: new Random(1))));

        Assert.Equal(bagCountBefore - 1, state.DiceIn("teamA", Zone.Bag).Count());
    }

    [Fact]
    public void BeastFirstClass_DoesNotReact_ToANonFounderAttacker()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.BeastFirstClass.Id]);
        state.ActivePlayerId = "teamA";

        var beastDie = FindUnpurchased(state, "teamA", SampleCards.BeastFirstClass.Id);
        beastDie.Zone = Zone.FieldZone; beastDie.Status = DieStatus.Character; beastDie.Level = 1;

        var nonFounderAttacker = FindUnpurchased(state, "teamA", SampleCards.BlackWidow.Id); // no Founder
        nonFounderAttacker.Zone = Zone.FieldZone; nonFounderAttacker.Status = DieStatus.Character; nonFounderAttacker.Level = 1;

        TurnEngine.EnterAttackStep(state);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [nonFounderAttacker.Id]);

        Assert.DoesNotContain(queue.Pending, a => a.Trigger == TriggerType.WhenAnotherDieAttacks);
    }

    [Fact]
    public void BeastFirstClass_DoesNotReact_ToAnOpposingFounderAttacker()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.BeastFirstClass.Id],
            extraTeamBCardIds: [SampleCards.JeanGreyPeacefulCoexistence.Id]);
        state.ActivePlayerId = "teamB"; // the Founder die attacks for teamB, opposing Beast's own controller

        var beastDie = FindUnpurchased(state, "teamA", SampleCards.BeastFirstClass.Id);
        beastDie.Zone = Zone.FieldZone; beastDie.Status = DieStatus.Character; beastDie.Level = 1;

        var opposingFounderAttacker = FindUnpurchased(state, "teamB", SampleCards.JeanGreyPeacefulCoexistence.Id);
        opposingFounderAttacker.Zone = Zone.FieldZone; opposingFounderAttacker.Status = DieStatus.Character; opposingFounderAttacker.Level = 1;

        TurnEngine.EnterAttackStep(state);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [opposingFounderAttacker.Id]);

        Assert.DoesNotContain(queue.Pending, a => a.Trigger == TriggerType.WhenAnotherDieAttacks);
    }

    [Fact]
    public void BishopImBack_PrepsItself_WhenSpentAsFieldingEnergy()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.BishopImBack.Id, SampleCards.RogueMrsX.Id]);
        state.ActivePlayerId = "teamA";

        var bishopEnergy = FindUnpurchased(state, "teamA", SampleCards.BishopImBack.Id);
        bishopEnergy.Zone = Zone.ReservePool;
        bishopEnergy.Status = DieStatus.Energy;
        bishopEnergy.EnergyKind = EnergyKind.Wild;
        bishopEnergy.EnergyAmount = 1;

        var fieldedDie = FindUnpurchased(state, "teamA", SampleCards.RogueMrsX.Id);
        fieldedDie.Zone = Zone.ReservePool; fieldedDie.Status = DieStatus.Character; fieldedDie.Level = 1; // printed fielding cost 1

        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, fieldedDie.Id, [bishopEnergy.Id]);

        Assert.Equal(Zone.PrepArea, bishopEnergy.Zone);
    }

    [Fact]
    public void BishopImBack_DoesNotPrep_EnergyThatIsNotItself()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.BishopImBack.Id, SampleCards.RogueMrsX.Id]);
        state.ActivePlayerId = "teamA";

        var plainEnergy = GiveWildEnergy(state, "teamA", 1);

        var fieldedDie = FindUnpurchased(state, "teamA", SampleCards.RogueMrsX.Id);
        fieldedDie.Zone = Zone.ReservePool; fieldedDie.Status = DieStatus.Character; fieldedDie.Level = 1;

        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, fieldedDie.Id, plainEnergy.Select(d => d.Id).ToList());

        Assert.NotEqual(Zone.PrepArea, plainEnergy[0].Zone); // ordinary destination (Out of Play) instead
    }

    [Fact]
    public void IcemanXaviersDream_AttackEqualsDefense_OnlyWhileAnOwnSidekickIsActive()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.IcemanXaviersDream.Id]);
        state.ActivePlayerId = "teamA";

        var icemanDie = FindUnpurchased(state, "teamA", SampleCards.IcemanXaviersDream.Id);
        icemanDie.Zone = Zone.FieldZone; icemanDie.Status = DieStatus.Character; icemanDie.Level = 2; // printed 3A/6D - genuinely differ
        var defenseBefore = DieStats.EffectiveDefense(state, icemanDie);
        Assert.NotEqual(defenseBefore, DieStats.EffectiveAttack(state, icemanDie)); // sanity: differ without a Sidekick

        var sidekick = state.DiceIn("teamA", Zone.Bag).First();
        sidekick.Zone = Zone.FieldZone; sidekick.Status = DieStatus.SidekickCharacter; sidekick.Level = 1;

        Assert.Equal(defenseBefore, DieStats.EffectiveAttack(state, icemanDie));

        sidekick.Zone = Zone.PrepArea; // no longer active
        Assert.NotEqual(defenseBefore, DieStats.EffectiveAttack(state, icemanDie));
    }

    [Fact]
    public void IcemanMrIceGuy_BuffsSidekicksOnly_WhileActive()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.IcemanMrIceGuy.Id]);
        state.ActivePlayerId = "teamA";

        // Baselines captured BEFORE Iceman is active - a real mistake
        // this project has made and fixed before (measuring "before"
        // after the buff is already live just re-measures the buffed
        // value).
        var sidekick = state.DiceIn("teamA", Zone.Bag).First();
        sidekick.Zone = Zone.FieldZone; sidekick.Status = DieStatus.SidekickCharacter; sidekick.Level = 1;
        var sidekickBaseAttack = DieStats.EffectiveAttack(state, sidekick);

        var nonSidekick = FindUnpurchased(state, "teamA", SampleCards.BlackWidow.Id);
        nonSidekick.Zone = Zone.FieldZone; nonSidekick.Status = DieStatus.Character; nonSidekick.Level = 1;
        var nonSidekickBaseAttack = DieStats.EffectiveAttack(state, nonSidekick);

        var icemanDie = FindUnpurchased(state, "teamA", SampleCards.IcemanMrIceGuy.Id);
        icemanDie.Zone = Zone.FieldZone; icemanDie.Status = DieStatus.Character; icemanDie.Level = 1;

        Assert.Equal(sidekickBaseAttack + 1, DieStats.EffectiveAttack(state, sidekick));
        Assert.Equal(nonSidekickBaseAttack, DieStats.EffectiveAttack(state, nonSidekick)); // unaffected - not a Sidekick
    }

    [Fact]
    public void IcemanMrIceGuyEnergize_FiresOffRealGate_DoublesTargetsPrintedAttack()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.IcemanMrIceGuy.Id]);
        state.ActivePlayerId = "teamA";
        state.CurrentStep = TurnStep.RollAndReroll;

        var icemanDie = FindUnpurchased(state, "teamA", SampleCards.IcemanMrIceGuy.Id);
        icemanDie.Zone = Zone.ReservePool;
        icemanDie.Status = DieStatus.Energy;
        icemanDie.EnergyKind = EnergyKind.Generic;
        icemanDie.EnergyAmount = 2; // double energy - Energize's own trigger condition

        var target = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        target.Zone = Zone.FieldZone; target.Status = DieStatus.Character; target.Level = 3;
        var printedAttack = SampleCards.Falcon.Levels[2].Attack;
        var baseAttack = DieStats.EffectiveAttack(state, target);

        var queue = new AbilityQueue();
        TurnEngine.Reroll(state, queue, new FixedRoller(DieStatus.Energy, 1), []);
        Assert.Equal(1, queue.Count);
        Assert.Equal(TriggerType.Energize, queue.Pending[0].Trigger);

        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [target.Id])));

        Assert.Equal(baseAttack + printedAttack, DieStats.EffectiveAttack(state, target));
    }

    [Fact]
    public void EmmaFrostInfluential_BuffsAndGrantsAffiliation_ToSidekicksOnly_WhileActive()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.EmmaFrostInfluential.Id]);
        state.ActivePlayerId = "teamA";

        // Baselines captured BEFORE Emma Frost is active - see
        // IcemanMrIceGuy_BuffsSidekicksOnly_WhileActive's own remarks.
        var sidekick = state.DiceIn("teamA", Zone.Bag).First();
        sidekick.Zone = Zone.FieldZone; sidekick.Status = DieStatus.SidekickCharacter; sidekick.Level = 1;
        var baseAttack = DieStats.EffectiveAttack(state, sidekick);
        var baseDefense = DieStats.EffectiveDefense(state, sidekick);

        var nonSidekick = FindUnpurchased(state, "teamA", SampleCards.BlackWidow.Id);
        nonSidekick.Zone = Zone.FieldZone; nonSidekick.Status = DieStatus.Character; nonSidekick.Level = 1;

        var emmaDie = FindUnpurchased(state, "teamA", SampleCards.EmmaFrostInfluential.Id);
        emmaDie.Zone = Zone.FieldZone; emmaDie.Status = DieStatus.Character; emmaDie.Level = 1;

        Assert.Equal(baseAttack + 1, DieStats.EffectiveAttack(state, sidekick));
        Assert.Equal(baseDefense + 1, DieStats.EffectiveDefense(state, sidekick));
        Assert.True(DieStats.HasAffiliation(state, sidekick, "Hellfire Club"));
        Assert.False(DieStats.HasAffiliation(state, nonSidekick, "Hellfire Club")); // not a Sidekick
    }

    [Fact]
    public void ForgeMoreThanFirepower_FiresOffRealFieldingGate_WhenFieldedWithBoltEnergy()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.ForgeMoreThanFirepower.Id]);
        state.ActivePlayerId = "teamA";

        var forgeDie = FindUnpurchased(state, "teamA", SampleCards.ForgeMoreThanFirepower.Id);
        forgeDie.Zone = Zone.ReservePool; forgeDie.Status = DieStatus.Character; forgeDie.Level = 1; // printed fielding cost 1

        var boltEnergy = state.DiceIn("teamA", Zone.Bag).First();
        boltEnergy.Zone = Zone.ReservePool;
        boltEnergy.Status = DieStatus.Energy;
        boltEnergy.EnergyKind = EnergyKind.Specific;
        boltEnergy.ProvidedEnergyType = EnergyType.Bolt;
        boltEnergy.EnergyAmount = 1;

        var prepCountBefore = state.DiceIn("teamA", Zone.PrepArea).Count();
        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, forgeDie.Id, [boltEnergy.Id]);

        Assert.Equal(prepCountBefore + 1, state.DiceIn("teamA", Zone.PrepArea).Count());
    }

    [Fact]
    public void ForgeMoreThanFirepower_DoesNotPrep_WhenFieldedWithNonBoltEnergy()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.ForgeMoreThanFirepower.Id]);
        state.ActivePlayerId = "teamA";

        var forgeDie = FindUnpurchased(state, "teamA", SampleCards.ForgeMoreThanFirepower.Id);
        forgeDie.Zone = Zone.ReservePool; forgeDie.Status = DieStatus.Character; forgeDie.Level = 1;

        var wildEnergy = GiveWildEnergy(state, "teamA", 1); // Wild - not a real Bolt-typed die

        var prepCountBefore = state.DiceIn("teamA", Zone.PrepArea).Count();
        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, forgeDie.Id, wildEnergy.Select(d => d.Id).ToList());

        Assert.Equal(prepCountBefore, state.DiceIn("teamA", Zone.PrepArea).Count());
    }

    [Fact]
    public void ProfessorXDreamer_FiresOffRealFieldingGate_WhenFieldedWithXMenEnergy()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.ProfessorXDreamer.Id, SampleCards.RogueMrsX.Id]);
        state.ActivePlayerId = "teamA";

        var profXDie = FindUnpurchased(state, "teamA", SampleCards.ProfessorXDreamer.Id);
        profXDie.Zone = Zone.ReservePool; profXDie.Status = DieStatus.Character; profXDie.Level = 1; // printed fielding cost 1

        var xMenEnergy = FindUnpurchased(state, "teamA", SampleCards.RogueMrsX.Id);
        xMenEnergy.Zone = Zone.ReservePool;
        xMenEnergy.Status = DieStatus.Energy;
        xMenEnergy.EnergyKind = EnergyKind.Wild;
        xMenEnergy.EnergyAmount = 1;

        var prepCountBefore = state.DiceIn("teamA", Zone.PrepArea).Count();
        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, profXDie.Id, [xMenEnergy.Id]);

        Assert.Equal(prepCountBefore + 1, state.DiceIn("teamA", Zone.PrepArea).Count());
    }

    [Fact]
    public void ProfessorXDreamerEnergize_FiresOffRealGate_MovesAnXMenDieFromUsedPileToPrepArea()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.ProfessorXDreamer.Id, SampleCards.RogueMrsX.Id]);
        state.ActivePlayerId = "teamA";
        state.CurrentStep = TurnStep.RollAndReroll;

        var profXDie = FindUnpurchased(state, "teamA", SampleCards.ProfessorXDreamer.Id);
        profXDie.Zone = Zone.ReservePool;
        profXDie.Status = DieStatus.Energy;
        profXDie.EnergyKind = EnergyKind.Generic;
        profXDie.EnergyAmount = 2;

        var usedPileXMen = FindUnpurchased(state, "teamA", SampleCards.RogueMrsX.Id);
        usedPileXMen.Zone = Zone.UsedPile;

        var queue = new AbilityQueue();
        TurnEngine.Reroll(state, queue, new FixedRoller(DieStatus.Energy, 1), []);
        Assert.Equal(1, queue.Count);
        Assert.Equal(TriggerType.Energize, queue.Pending[0].Trigger);

        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [usedPileXMen.Id])));

        Assert.Equal(Zone.PrepArea, usedPileXMen.Zone);
    }

    // --- Deeper-abilities round: ability-vs-combat damage distinction,
    // the multi-block default rule, WhenDamaged-via-injection, and the
    // "who caused this KO" simplifications (Blob/Deathbird). Each test
    // exercises the real firing mechanism (CombatEngine's own
    // DeclareAttackers/DeclareBlockers/AssignCombatDamage, or a real
    // EffectInterpreter.Execute call) rather than asserting on the
    // helper methods directly.

    [Fact]
    public void MystiqueFreedomForce_ReducesOpposingAbilityDamage_ByOne_WhileActive()
    {
        var state = BuildTwoTeamGame(extraTeamBCardIds: [SampleCards.MystiqueFreedomForce.Id]);

        var mystiqueDie = FindUnpurchased(state, "teamB", SampleCards.MystiqueFreedomForce.Id);
        mystiqueDie.Zone = Zone.FieldZone;
        mystiqueDie.Status = DieStatus.Character;
        mystiqueDie.Level = 1;

        var target = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        target.Zone = Zone.FieldZone;
        target.Status = DieStatus.Character;
        target.Level = 3; // 4A/4D - survives the reduced hit

        var spec = TargetSpec.CharacterDie("target character die");
        EffectInterpreter.Execute(
            new DealDamage(4, spec), new EffectContext(state, "teamA", SourceDieId: null, _ => [target.Id]));

        Assert.Equal(3, target.Damage); // 4 - 1 reduction (teamA is opposing Mystique's controller, teamB)
    }

    [Fact]
    public void MystiqueFreedomForce_DoesNotReduce_OwnSideAbilityDamage()
    {
        var state = BuildTwoTeamGame(extraTeamBCardIds: [SampleCards.MystiqueFreedomForce.Id]);

        var mystiqueDie = FindUnpurchased(state, "teamB", SampleCards.MystiqueFreedomForce.Id);
        mystiqueDie.Zone = Zone.FieldZone;
        mystiqueDie.Status = DieStatus.Character;
        mystiqueDie.Level = 1;

        var target = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        target.Zone = Zone.FieldZone;
        target.Status = DieStatus.Character;
        target.Level = 3;

        var spec = TargetSpec.CharacterDie("target character die");
        EffectInterpreter.Execute(
            new DealDamage(3, spec), new EffectContext(state, "teamB", SourceDieId: null, _ => [target.Id]));

        Assert.Equal(3, target.Damage); // no reduction - the damage's own side, not "opposing" (3, not 4, so the 4D target survives to show a real Damage value instead of being KO'd and reset)
    }

    [Fact]
    public void MystiqueFreedomForceWhenKOd_MovesAQualifyingBrotherhoodDie_FromUsedPileToPrepArea()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds:
            [SampleCards.MystiqueFreedomForce.Id, SampleCards.MagnetoVisionary.Id, SampleCards.RogueMrsX.Id]);

        var mystiqueDie = FindUnpurchased(state, "teamA", SampleCards.MystiqueFreedomForce.Id);
        mystiqueDie.Zone = Zone.FieldZone;
        mystiqueDie.Status = DieStatus.Character;
        mystiqueDie.Level = 1;

        var qualifying = FindUnpurchased(state, "teamA", SampleCards.MagnetoVisionary.Id); // Brotherhood, cost 5
        qualifying.Zone = Zone.UsedPile;

        var nonQualifying = FindUnpurchased(state, "teamA", SampleCards.RogueMrsX.Id); // not Brotherhood
        nonQualifying.Zone = Zone.UsedPile;

        var ability = SampleCards.MystiqueFreedomForce.Abilities.Single(a => a.Trigger == TriggerType.WhenKOd);
        var legalTargets = LegalTargets.Query(state, "teamA", ((MoveDie)ability.Effect).Target);
        Assert.Contains(qualifying.Id, legalTargets);
        Assert.DoesNotContain(nonQualifying.Id, legalTargets);

        EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, "teamA", mystiqueDie.Id, _ => [qualifying.Id]));

        Assert.Equal(Zone.PrepArea, qualifying.Zone);
        Assert.Equal(Zone.UsedPile, nonQualifying.Zone); // untouched
    }

    [Fact]
    public void MisterSinisterBiologist_PreventsNonCombatDamage_ToOtherOwnDice_ButNotItself()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.MisterSinisterBiologist.Id]);

        var sinisterDie = FindUnpurchased(state, "teamA", SampleCards.MisterSinisterBiologist.Id);
        sinisterDie.Zone = Zone.FieldZone;
        sinisterDie.Status = DieStatus.Character;
        sinisterDie.Level = 3; // 6A/3D - the max face; dealt less than 3D below so it survives to show a real Damage value

        var otherOwnDie = FindUnpurchased(state, "teamA", SampleCards.BlackWidow.Id);
        otherOwnDie.Zone = Zone.FieldZone;
        otherOwnDie.Status = DieStatus.Character;
        otherOwnDie.Level = 3;

        var spec = TargetSpec.CharacterDie("target character die");
        EffectInterpreter.Execute(
            new DealDamage(3, spec), new EffectContext(state, "teamB", SourceDieId: null, _ => [otherOwnDie.Id]));
        Assert.Equal(0, otherOwnDie.Damage); // fully prevented - non-combat damage to another own die

        EffectInterpreter.Execute(
            new DealDamage(2, spec), new EffectContext(state, "teamB", SourceDieId: null, _ => [sinisterDie.Id]));
        Assert.Equal(2, sinisterDie.Damage); // NOT protected - "other" excludes Mister Sinister himself
    }

    [Fact]
    public void MisterSinisterBiologist_DoesNotPrevent_CombatDamage()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.MisterSinisterBiologist.Id]);
        state.ActivePlayerId = "teamB";

        var sinisterDie = FindUnpurchased(state, "teamA", SampleCards.MisterSinisterBiologist.Id);
        sinisterDie.Zone = Zone.FieldZone;
        sinisterDie.Status = DieStatus.Character;
        sinisterDie.Level = 1;

        var otherOwnDie = FindUnpurchased(state, "teamA", SampleCards.BlackWidow.Id);
        otherOwnDie.Zone = Zone.FieldZone;
        otherOwnDie.Status = DieStatus.Character;
        otherOwnDie.Level = 3; // enough defense to survive and show real Damage

        var attacker = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        attacker.Zone = Zone.FieldZone;
        attacker.Status = DieStatus.Character;
        attacker.Level = 2; // 2A/3D placeholder

        TurnEngine.EnterAttackStep(state);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, otherOwnDie.Id);
        CombatEngine.DeclareBlockers(state, assignment, [otherOwnDie.Id]);

        var splits = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [attacker.Id] = new Dictionary<string, int> { [otherOwnDie.Id] = 2 }
        };
        CombatEngine.AssignCombatDamage(state, queue, assignment, splits);

        Assert.Equal(2, otherOwnDie.Damage); // real combat damage - unaffected by Mister Sinister's non-combat text
    }

    [Fact]
    public void MisterSinisterBiologistGlobal_GrantsOvercrush()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.MisterSinisterBiologist.Id]);

        var target = FindUnpurchased(state, "teamA", SampleCards.BlackWidow.Id);
        target.Zone = Zone.FieldZone;
        target.Status = DieStatus.Character;
        target.Level = 1;
        Assert.False(DieStats.HasKeyword(state, target, "Overcrush"));

        var ability = SampleCards.MisterSinisterBiologist.Abilities.Single(a => a.Trigger == TriggerType.Global);
        EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, "teamA", SourceDieId: null, _ => [target.Id]));

        Assert.True(DieStats.HasKeyword(state, target, "Overcrush"));
    }

    [Fact]
    public void MisterSinisterGeneticist_WhenFielded_KOsUpToTwoTargetSidekicks()
    {
        var state = BuildTwoTeamGame();

        var sidekick1 = state.DiceIn("teamB", Zone.Bag).First();
        sidekick1.Zone = Zone.FieldZone;
        var sidekick2 = state.DiceIn("teamB", Zone.Bag).Skip(1).First();
        sidekick2.Zone = Zone.FieldZone;

        var ability = SampleCards.MisterSinisterGeneticist.Abilities.Single(a => a.Trigger == TriggerType.WhenFielded);
        EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, "teamA", SourceDieId: null, _ => [sidekick1.Id, sidekick2.Id]));

        Assert.Equal(Zone.PrepArea, sidekick1.Zone);
        Assert.Equal(Zone.PrepArea, sidekick2.Zone);
    }

    [Fact]
    public void MisterSinisterGeneticist_WhenFielded_KOingFewerThanTwo_IsAllowed()
    {
        var state = BuildTwoTeamGame();

        var sidekick = state.DiceIn("teamB", Zone.Bag).First();
        sidekick.Zone = Zone.FieldZone;

        var ability = SampleCards.MisterSinisterGeneticist.Abilities.Single(a => a.Trigger == TriggerType.WhenFielded);
        // "up to 2" - choosing zero is a legal answer (Optional: true).
        EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, "teamA", SourceDieId: null, _ => []));

        Assert.Equal(Zone.FieldZone, sidekick.Zone);
    }

    [Fact]
    public void MisterSinisterGeneticistGlobal_GrantsDeadly_ButNotToSidekicks()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.MisterSinisterGeneticist.Id]);

        var target = FindUnpurchased(state, "teamA", SampleCards.BlackWidow.Id);
        target.Zone = Zone.FieldZone;
        target.Status = DieStatus.Character;
        target.Level = 1;
        Assert.False(DieStats.HasKeyword(state, target, "Deadly"));

        var ability = SampleCards.MisterSinisterGeneticist.Abilities.Single(a => a.Trigger == TriggerType.Global);
        EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, "teamA", SourceDieId: null, _ => [target.Id]));

        Assert.True(DieStats.HasKeyword(state, target, "Deadly"));

        var sidekick = state.DiceIn("teamA", Zone.Bag).First();
        sidekick.Zone = Zone.FieldZone;
        var legalTargets = LegalTargets.Query(
            state, "teamA",
            ((GrantKeyword)ability.Effect).Target,
            TriggerType.Global);
        Assert.DoesNotContain(sidekick.Id, legalTargets);
    }

    [Fact]
    public void MisterSinisterGeneticist_FiresWhenKOsOpposingCharacter_ForARealCombatKO()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.MisterSinisterGeneticist.Id],
            extraTeamBCardIds: [SampleCards.BlackWidow.Id]);
        state.ActivePlayerId = "teamA";

        var sinisterDie = FindUnpurchased(state, "teamA", SampleCards.MisterSinisterGeneticist.Id);
        sinisterDie.Zone = Zone.FieldZone; sinisterDie.Status = DieStatus.Character; sinisterDie.Level = 3; // 6A/3D

        var blocker = FindUnpurchased(state, "teamB", SampleCards.BlackWidow.Id);
        blocker.Zone = Zone.FieldZone; blocker.Status = DieStatus.Character; blocker.Level = 1; // 3A/1D - dies to 6A

        TurnEngine.EnterAttackStep(state);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [sinisterDie.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(sinisterDie.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, assignment, [blocker.Id]);

        var splits = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [sinisterDie.Id] = new Dictionary<string, int> { [blocker.Id] = DieStats.EffectiveAttack(state, sinisterDie) }
        };
        CombatEngine.AssignCombatDamage(state, queue, assignment, splits);

        Assert.Contains(
            queue.Pending, a => a.Trigger == TriggerType.WhenKOsOpposingCharacter && a.SourceDieId == sinisterDie.Id);
    }

    [Fact]
    public void MisterSinisterGeneticist_DoesNotFireWhenKOsOpposingCharacter_ForAnOwnDieKO()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.MisterSinisterGeneticist.Id]);
        state.ActivePlayerId = "teamA";

        var sinisterDie = FindUnpurchased(state, "teamA", SampleCards.MisterSinisterGeneticist.Id);
        sinisterDie.Zone = Zone.FieldZone; sinisterDie.Status = DieStatus.Character; sinisterDie.Level = 3;

        var ownAlly = FindUnpurchased(state, "teamA", SampleCards.BlackWidow.Id);
        ownAlly.Zone = Zone.FieldZone; ownAlly.Status = DieStatus.Character; ownAlly.Level = 1;

        // Contrived (real DeclareBlockers only ever engages OPPOSING
        // dice, so this never happens via normal play) - seeded directly
        // to exercise the ownership check in isolation, same "seed
        // DeadlyEngagedDieIds directly" shape TurnEngineTests' own
        // CleanUp tests already use.
        state.DeadlyEngagedDieIds[ownAlly.Id] = [sinisterDie.Id];
        state.CurrentStep = TurnStep.CleanUp;

        var queue = new AbilityQueue();
        TurnEngine.CleanUp(state, roller: null, queue: queue);

        // Same controller (Mister Sinister's own team) - "an OPPOSING
        // character" never matches, no matter what the source map claims.
        Assert.DoesNotContain(queue.Pending, a => a.Trigger == TriggerType.WhenKOsOpposingCharacter);
    }

    [Fact]
    public void MayPayLife_AcceptingThePayment_RunsThenAndDeductsLife()
    {
        var state = BuildTwoTeamGame();
        var ability = new AbilityDef(TriggerType.WhenKOsOpposingCharacter, Cost: null,
            Effect: new MayPayLife(1, new LoseLife(1, TargetOwnership.Opposing)));

        var ownLifeBefore = state.GetPlayer("teamA").Life;
        var opponentLifeBefore = state.GetPlayer("teamB").Life;

        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "teamA", "some-source-die", _ => []));

        Assert.NotNull(state.PendingChoice);
        Assert.Equal(["some-source-die"], state.PendingChoice!.CandidateDieIds);
        Assert.True(state.PendingChoice.AllowMultiple);

        state.PendingChoice.Resolve(["some-source-die"]); // accept

        Assert.Equal(ownLifeBefore - 1, state.GetPlayer("teamA").Life);
        Assert.Equal(opponentLifeBefore - 1, state.GetPlayer("teamB").Life);
    }

    [Fact]
    public void MayPayLife_Declining_DoesNothing()
    {
        var state = BuildTwoTeamGame();
        var ability = new AbilityDef(TriggerType.WhenKOsOpposingCharacter, Cost: null,
            Effect: new MayPayLife(1, new LoseLife(1, TargetOwnership.Opposing)));

        var ownLifeBefore = state.GetPlayer("teamA").Life;
        var opponentLifeBefore = state.GetPlayer("teamB").Life;

        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "teamA", "some-source-die", _ => []));
        state.PendingChoice!.Resolve([]); // decline

        Assert.Equal(ownLifeBefore, state.GetPlayer("teamA").Life);
        Assert.Equal(opponentLifeBefore, state.GetPlayer("teamB").Life);
    }

    [Fact]
    public void DarkPhoenixDestructiveForce_RetaliatesForRealCombatDamage_FromAnOpposingCharacter()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.DarkPhoenixDestructiveForce.Id]);
        state.ActivePlayerId = "teamB";

        var dpDie = FindUnpurchased(state, "teamA", SampleCards.DarkPhoenixDestructiveForce.Id);
        dpDie.Zone = Zone.FieldZone;
        dpDie.Status = DieStatus.Character;
        dpDie.Level = 3; // 8A/8D - survives

        var attacker = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        attacker.Zone = Zone.FieldZone;
        attacker.Status = DieStatus.Character;
        attacker.Level = 3; // 4A/4D placeholder

        TurnEngine.EnterAttackStep(state);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, dpDie.Id);
        CombatEngine.DeclareBlockers(state, assignment, [dpDie.Id]);

        var teamBLifeBefore = state.GetPlayer("teamB").Life;
        var splits = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [attacker.Id] = new Dictionary<string, int> { [dpDie.Id] = 4 }
        };
        CombatEngine.AssignCombatDamage(state, queue, assignment, splits);

        Assert.Equal(4, dpDie.Damage);
        // Dark Phoenix (teamA) deals 4 to "each opponent" - teamB, the
        // attacking player who dealt the damage in the first place.
        Assert.Equal(teamBLifeBefore - 4, state.GetPlayer("teamB").Life);
    }

    [Fact]
    public void DarkPhoenixDestructiveForce_DoesNotRetaliate_ForNonCombatAbilityDamage()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.DarkPhoenixDestructiveForce.Id]);

        var dpDie = FindUnpurchased(state, "teamA", SampleCards.DarkPhoenixDestructiveForce.Id);
        dpDie.Zone = Zone.FieldZone;
        dpDie.Status = DieStatus.Character;
        dpDie.Level = 3;

        var teamBLifeBefore = state.GetPlayer("teamB").Life;
        var spec = TargetSpec.CharacterDie("target character die");
        EffectInterpreter.Execute(
            new DealDamage(3, spec), new EffectContext(state, "teamB", SourceDieId: null, _ => [dpDie.Id]));

        Assert.Equal(3, dpDie.Damage);
        Assert.Equal(teamBLifeBefore, state.GetPlayer("teamB").Life); // no retaliation - ability damage, not combat
    }

    [Fact]
    public void BlobImmovable_CanBlockThreeAttackersAtOnce()
    {
        var state = BuildTwoTeamGame(extraTeamBCardIds: [SampleCards.BlobImmovable.Id]);
        state.ActivePlayerId = "teamA";

        var blobDie = FindUnpurchased(state, "teamB", SampleCards.BlobImmovable.Id);
        blobDie.Zone = Zone.FieldZone;
        blobDie.Status = DieStatus.Character;
        blobDie.Level = 3; // 1A/8D

        var teamABag = state.DiceIn("teamA", Zone.Bag).ToList();
        var attacker1 = teamABag[0]; attacker1.Zone = Zone.FieldZone; attacker1.Status = DieStatus.SidekickCharacter;
        var attacker2 = teamABag[1]; attacker2.Zone = Zone.FieldZone; attacker2.Status = DieStatus.SidekickCharacter;
        var attacker3 = teamABag[2]; attacker3.Zone = Zone.FieldZone; attacker3.Status = DieStatus.SidekickCharacter;

        TurnEngine.EnterAttackStep(state);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [attacker1.Id, attacker2.Id, attacker3.Id]);

        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker1.Id, blobDie.Id);
        assignment.AssignBlocker(attacker2.Id, blobDie.Id);
        assignment.AssignBlocker(attacker3.Id, blobDie.Id);
        // blockerDieIds lists each distinct blocker once, not once per assignment.
        CombatEngine.DeclareBlockers(state, assignment, [blobDie.Id]);

        Assert.Equal(3, assignment.BlockersOf(attacker1.Id).Count + assignment.BlockersOf(attacker2.Id).Count +
                         assignment.BlockersOf(attacker3.Id).Count);
    }

    [Fact]
    public void NormalBlocker_CannotBlockMoreThanOneAttacker()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";

        var blocker = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        blocker.Zone = Zone.FieldZone;
        blocker.Status = DieStatus.Character;
        blocker.Level = 3;

        var teamABag = state.DiceIn("teamA", Zone.Bag).ToList();
        var attacker1 = teamABag[0]; attacker1.Zone = Zone.FieldZone; attacker1.Status = DieStatus.SidekickCharacter;
        var attacker2 = teamABag[1]; attacker2.Zone = Zone.FieldZone; attacker2.Status = DieStatus.SidekickCharacter;

        TurnEngine.EnterAttackStep(state);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [attacker1.Id, attacker2.Id]);

        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker1.Id, blocker.Id);
        assignment.AssignBlocker(attacker2.Id, blocker.Id);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CombatEngine.DeclareBlockers(state, assignment, [blocker.Id]));
        Assert.Contains("can only block", ex.Message);
    }

    [Fact]
    public void BlobImmovable_ReturnsKOdOpposingSidekick_ToItsOwnersBag()
    {
        var state = BuildTwoTeamGame(extraTeamBCardIds: [SampleCards.BlobImmovable.Id]);
        state.ActivePlayerId = "teamA";

        var blobDie = FindUnpurchased(state, "teamB", SampleCards.BlobImmovable.Id);
        blobDie.Zone = Zone.FieldZone;
        blobDie.Status = DieStatus.Character;
        blobDie.Level = 3; // 1A/8D

        var sidekickAttacker = state.DiceIn("teamA", Zone.Bag).First();
        sidekickAttacker.Zone = Zone.FieldZone;
        sidekickAttacker.Status = DieStatus.SidekickCharacter; // 1A/1D

        TurnEngine.EnterAttackStep(state);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [sidekickAttacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(sidekickAttacker.Id, blobDie.Id);
        CombatEngine.DeclareBlockers(state, assignment, [blobDie.Id]);

        var splits = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [sidekickAttacker.Id] = new Dictionary<string, int> { [blobDie.Id] = 1 }
        };
        CombatEngine.AssignCombatDamage(state, queue, assignment, splits);

        // Blob's own 1A KO's the Sidekick's 1D - returned to teamA's bag, not Prep Area.
        Assert.Equal(Zone.Bag, sidekickAttacker.Zone);
        Assert.Equal("teamA", sidekickAttacker.ControllerId);
    }

    [Fact]
    public void BlobImmovable_DoesNotReturnNonSidekickDice_ToBag()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.MystiqueFreedomForce.Id], extraTeamBCardIds: [SampleCards.BlobImmovable.Id]);
        state.ActivePlayerId = "teamA";

        var blobDie = FindUnpurchased(state, "teamB", SampleCards.BlobImmovable.Id);
        blobDie.Zone = Zone.FieldZone;
        blobDie.Status = DieStatus.Character;
        blobDie.Level = 3; // 1A/8D

        // A real character card with 1D (not a bare Sidekick) - Blob's 1A still KO's it.
        var attacker = FindUnpurchased(state, "teamA", SampleCards.MystiqueFreedomForce.Id);
        attacker.Zone = Zone.FieldZone;
        attacker.Status = DieStatus.Character;
        attacker.Level = 1; // 1A/1D

        TurnEngine.EnterAttackStep(state);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, blobDie.Id);
        CombatEngine.DeclareBlockers(state, assignment, [blobDie.Id]);

        var splits = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [attacker.Id] = new Dictionary<string, int> { [blobDie.Id] = 1 }
        };
        CombatEngine.AssignCombatDamage(state, queue, assignment, splits);

        Assert.Equal(Zone.PrepArea, attacker.Zone); // KO'd normally - not a Sidekick, no bag-return
    }

    [Fact]
    public void DeathbirdUsurper_FiresOffRealCombatGate_DealsDamage_WhenCausingAHighDefenseOpposingKO()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.DeathbirdUsurper.Id]);
        state.ActivePlayerId = "teamA";

        var deathbirdDie = FindUnpurchased(state, "teamA", SampleCards.DeathbirdUsurper.Id);
        deathbirdDie.Zone = Zone.FieldZone;
        deathbirdDie.Status = DieStatus.Character;
        deathbirdDie.Level = 3; // 3A/4D

        var opposingHighDefense = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        opposingHighDefense.Zone = Zone.FieldZone;
        opposingHighDefense.Status = DieStatus.Character;
        opposingHighDefense.Level = 2; // placeholder 2A/3D - meets "3D or greater"

        TurnEngine.EnterAttackStep(state);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [deathbirdDie.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(deathbirdDie.Id, opposingHighDefense.Id);
        CombatEngine.DeclareBlockers(state, assignment, [opposingHighDefense.Id]);

        var teamBLifeBefore = state.GetPlayer("teamB").Life;
        var splits = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [deathbirdDie.Id] = new Dictionary<string, int> { [opposingHighDefense.Id] = 3 }
        };
        CombatEngine.AssignCombatDamage(state, queue, assignment, splits);

        Assert.Equal(Zone.PrepArea, opposingHighDefense.Zone); // KO'd (3 damage >= 3D)
        Assert.Equal(teamBLifeBefore - 3, state.GetPlayer("teamB").Life);
    }

    [Fact]
    public void DeathbirdUsurper_DoesNotFire_ForALowDefenseOpposingKO()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.DeathbirdUsurper.Id]);
        state.ActivePlayerId = "teamA";

        var deathbirdDie = FindUnpurchased(state, "teamA", SampleCards.DeathbirdUsurper.Id);
        deathbirdDie.Zone = Zone.FieldZone;
        deathbirdDie.Status = DieStatus.Character;
        deathbirdDie.Level = 3; // 3A/4D

        var lowDefense = state.DiceIn("teamB", Zone.Bag).First();
        lowDefense.Zone = Zone.FieldZone;
        lowDefense.Status = DieStatus.SidekickCharacter; // 1A/1D

        TurnEngine.EnterAttackStep(state);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [deathbirdDie.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(deathbirdDie.Id, lowDefense.Id);
        CombatEngine.DeclareBlockers(state, assignment, [lowDefense.Id]);

        var teamBLifeBefore = state.GetPlayer("teamB").Life;
        var splits = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [deathbirdDie.Id] = new Dictionary<string, int> { [lowDefense.Id] = 3 }
        };
        CombatEngine.AssignCombatDamage(state, queue, assignment, splits);

        Assert.Equal(Zone.PrepArea, lowDefense.Zone); // KO'd
        Assert.Equal(teamBLifeBefore, state.GetPlayer("teamB").Life); // but defense < 3 - no Deathbird reaction
    }

    [Fact]
    public void DeathbirdUsurper_DoesNotFire_WhenNotActive()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.DeathbirdUsurper.Id]);
        state.ActivePlayerId = "teamA";

        // Deathbird's card is owned but left sitting Unpurchased - never fielded, so not active.
        var attacker = FindUnpurchased(state, "teamA", SampleCards.BlackWidow.Id);
        attacker.Zone = Zone.FieldZone;
        attacker.Status = DieStatus.Character;
        attacker.Level = 3; // 3A/3D

        var opposingHighDefense = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        opposingHighDefense.Zone = Zone.FieldZone;
        opposingHighDefense.Status = DieStatus.Character;
        opposingHighDefense.Level = 2; // placeholder 2A/3D - meets "3D or greater"

        TurnEngine.EnterAttackStep(state);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, opposingHighDefense.Id);
        CombatEngine.DeclareBlockers(state, assignment, [opposingHighDefense.Id]);

        var teamBLifeBefore = state.GetPlayer("teamB").Life;
        var splits = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [attacker.Id] = new Dictionary<string, int> { [opposingHighDefense.Id] = 3 }
        };
        CombatEngine.AssignCombatDamage(state, queue, assignment, splits);

        Assert.Equal(teamBLifeBefore, state.GetPlayer("teamB").Life); // no active Deathbird - no reaction
    }

    // --- Second deeper-abilities round: TriggerType.WhenDamaged wired
    // for real (Firestar), Action-Die usage-cost plumbing (both Lilandra
    // printings), and the Magneto/Mystique "opponent chooses the energy
    // face" simplification (always the double face).

    [Fact]
    public void FirestarAmazingFriend_WhenDamagedByRealCombat_FiresARealChoiceOfTargetPlayer()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.FirestarAmazingFriend.Id]);
        state.ActivePlayerId = "teamB";

        var firestarDie = FindUnpurchased(state, "teamA", SampleCards.FirestarAmazingFriend.Id);
        firestarDie.Zone = Zone.FieldZone;
        firestarDie.Status = DieStatus.Character;
        firestarDie.Level = 3; // 5A/4D - survives

        var attacker = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        attacker.Zone = Zone.FieldZone;
        attacker.Status = DieStatus.Character;
        attacker.Level = 2; // 2A/3D placeholder

        TurnEngine.EnterAttackStep(state);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, firestarDie.Id);
        CombatEngine.DeclareBlockers(state, assignment, [firestarDie.Id]);

        var splits = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [attacker.Id] = new Dictionary<string, int> { [firestarDie.Id] = 2 }
        };
        CombatEngine.AssignCombatDamage(state, queue, assignment, splits);

        // Only the die that was actually damaged AND has the ability reacts.
        Assert.Contains(queue.Pending, a => a.Trigger == TriggerType.WhenDamaged && a.SourceDieId == firestarDie.Id);
        Assert.DoesNotContain(queue.Pending, a => a.Trigger == TriggerType.WhenDamaged && a.SourceDieId == attacker.Id);

        var teamBLifeBefore = state.GetPlayer("teamB").Life;
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => ["teamB"])));

        Assert.Equal(teamBLifeBefore - 1, state.GetPlayer("teamB").Life); // real choice: targeted the player
    }

    [Fact]
    public void FirestarAmazingFriend_WhenDamagedByAbilityDamage_AlsoFires_ChoosingACharacterTarget()
    {
        var state = BuildTwoTeamGame(extraTeamACardIds: [SampleCards.FirestarAmazingFriend.Id]);

        var firestarDie = FindUnpurchased(state, "teamA", SampleCards.FirestarAmazingFriend.Id);
        firestarDie.Zone = Zone.FieldZone;
        firestarDie.Status = DieStatus.Character;
        firestarDie.Level = 3;

        var otherTarget = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        otherTarget.Zone = Zone.FieldZone;
        otherTarget.Status = DieStatus.Character;
        otherTarget.Level = 3; // 4A/4D - survives Firestar's own 1-damage reaction

        var queue = new AbilityQueue();
        var spec = TargetSpec.CharacterDie("target character die");
        EffectInterpreter.Execute(
            new DealDamage(2, spec),
            new EffectContext(state, "teamB", SourceDieId: null, _ => [firestarDie.Id], Queue: queue));

        Assert.Contains(queue.Pending, a => a.Trigger == TriggerType.WhenDamaged && a.SourceDieId == firestarDie.Id);

        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [otherTarget.Id])));

        Assert.Equal(1, otherTarget.Damage); // real choice: targeted the character die this time
    }

    [Fact]
    public void LilandraFreedomFighter_TaxesOpponentActionDieUse_WithRealEnergyPayment()
    {
        var state = BuildTwoTeamGame(extraTeamBCardIds: [SampleCards.LilandraFreedomFighter.Id]);
        state.ActivePlayerId = "teamA";

        var lilandraDie = FindUnpurchased(state, "teamB", SampleCards.LilandraFreedomFighter.Id);
        lilandraDie.Zone = Zone.FieldZone;
        lilandraDie.Status = DieStatus.Character;
        lilandraDie.Level = 1;

        var actionDie = FindUnpurchased(state, "teamA", SampleCards.ShockingGrasp.Id);
        actionDie.Zone = Zone.ReservePool;
        actionDie.Status = DieStatus.Action;

        var queue = new AbilityQueue();

        // No energy offered for the surcharge - rejected before anything else happens.
        Assert.Throws<InvalidOperationException>(() => TurnEngine.UseActionDie(state, queue, actionDie.Id));
        Assert.Equal(Zone.ReservePool, actionDie.Zone); // the rejected attempt didn't burn the use

        var surchargeEnergy = GiveWildEnergy(state, "teamA", 1);
        TurnEngine.UseActionDie(state, queue, actionDie.Id, energyDieIdsToSpend: surchargeEnergy.Select(d => d.Id).ToList());

        Assert.Equal(Zone.OutOfPlay, surchargeEnergy[0].Zone); // the surcharge itself was really spent
        Assert.Equal(Zone.OutOfPlay, actionDie.Zone); // and the die's own use proceeded
    }

    [Fact]
    public void LilandraFreedomFighter_DoesNotTaxActionDieUse_WhenNotActive()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";

        var actionDie = FindUnpurchased(state, "teamA", SampleCards.ShockingGrasp.Id);
        actionDie.Zone = Zone.ReservePool;
        actionDie.Status = DieStatus.Action;

        var queue = new AbilityQueue();
        TurnEngine.UseActionDie(state, queue, actionDie.Id); // no energy offered, no Lilandra active - succeeds

        Assert.Equal(Zone.OutOfPlay, actionDie.Zone);
    }

    [Fact]
    public void LilandraMajestrix_TaxesOpponentActionDieAndGlobalAbilityUse_WithRealLifeLoss()
    {
        var state = BuildTwoTeamGame(extraTeamBCardIds: [SampleCards.LilandraMajestrix.Id]);
        state.ActivePlayerId = "teamA";

        var lilandraDie = FindUnpurchased(state, "teamB", SampleCards.LilandraMajestrix.Id);
        lilandraDie.Zone = Zone.FieldZone;
        lilandraDie.Status = DieStatus.Character;
        lilandraDie.Level = 1;

        var actionDie = FindUnpurchased(state, "teamA", SampleCards.ShockingGrasp.Id);
        actionDie.Zone = Zone.ReservePool;
        actionDie.Status = DieStatus.Action;

        var queue = new AbilityQueue();
        var teamALifeBeforeAction = state.GetPlayer("teamA").Life;
        TurnEngine.UseActionDie(state, queue, actionDie.Id); // no energy needed - it's a life tax, not an energy one
        Assert.Equal(teamALifeBeforeAction - 2, state.GetPlayer("teamA").Life);

        // Falcon's own Global (a real teamB card - rule 2.6.5.2 lets either
        // player activate any Global ability, same precedent the existing
        // Distraction test already relies on).
        var globalEnergy = GiveWildEnergy(state, "teamA", 1);
        var teamALifeBeforeGlobal = state.GetPlayer("teamA").Life;
        TurnEngine.UseGlobalAbility(state, queue, SampleCards.Falcon.Id, "teamA", globalEnergy.Select(d => d.Id).ToList());
        Assert.Equal(teamALifeBeforeGlobal - 2, state.GetPlayer("teamA").Life);
    }

    [Fact]
    public void MagnetoMasterOfMagnetismTeamwatch_FiresOffRealFieldScan_SpinsOpposingDieToDoubleEnergyFace()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.MagnetoMasterOfMagnetism.Id, SampleCards.MagnetoVisionary.Id]);
        state.ActivePlayerId = "teamA";

        var magnetoDie = FindUnpurchased(state, "teamA", SampleCards.MagnetoMasterOfMagnetism.Id);
        magnetoDie.Zone = Zone.FieldZone;
        magnetoDie.Status = DieStatus.Character;
        magnetoDie.Level = 1;

        var opposingTarget = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        opposingTarget.Zone = Zone.FieldZone;
        opposingTarget.Status = DieStatus.Character;
        opposingTarget.Level = 2;

        // Field a DIFFERENT Brotherhood of Mutants character die - Teamwatch
        // needs a shared affiliation with the fielded die, not just "any
        // different character" (TurnEngine.Field's own Teamwatch scan).
        var otherOwnDie = FindUnpurchased(state, "teamA", SampleCards.MagnetoVisionary.Id);
        otherOwnDie.Zone = Zone.ReservePool;
        otherOwnDie.Status = DieStatus.Character;
        otherOwnDie.Level = 1; // fielding cost 1

        var queue = new AbilityQueue();
        var fieldEnergy = GiveWildEnergy(state, "teamA", 1);
        TurnEngine.Field(state, queue, otherOwnDie.Id, energyDieIdsToSpend: fieldEnergy.Select(d => d.Id).ToList());

        Assert.Contains(queue.Pending, a => a.Trigger == TriggerType.Teamwatch && a.SourceDieId == magnetoDie.Id);

        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [opposingTarget.Id])));

        Assert.Equal(DieStatus.Energy, opposingTarget.Status);
        Assert.Equal(2, opposingTarget.EnergyAmount); // simplified "opponent's choice" -> always the double face
    }

    [Fact]
    public void MystiqueSheWalksAmongUsTeamwatch_FiresOffRealFieldScan_SpinsOpposingDieToDoubleEnergyFace()
    {
        var state = BuildTwoTeamGame(
            extraTeamACardIds: [SampleCards.MystiqueSheWalksAmongUs.Id, SampleCards.MagnetoVisionary.Id]);
        state.ActivePlayerId = "teamA";

        var mystiqueDie = FindUnpurchased(state, "teamA", SampleCards.MystiqueSheWalksAmongUs.Id);
        mystiqueDie.Zone = Zone.FieldZone;
        mystiqueDie.Status = DieStatus.Character;
        mystiqueDie.Level = 1;

        var opposingTarget = FindUnpurchased(state, "teamB", SampleCards.Falcon.Id);
        opposingTarget.Zone = Zone.FieldZone;
        opposingTarget.Status = DieStatus.Character;
        opposingTarget.Level = 2;

        // Field a DIFFERENT Brotherhood of Mutants character die - Teamwatch
        // needs a shared affiliation with the fielded die (TurnEngine.
        // Field's own Teamwatch scan), not just "any different character."
        var otherOwnDie = FindUnpurchased(state, "teamA", SampleCards.MagnetoVisionary.Id);
        otherOwnDie.Zone = Zone.ReservePool;
        otherOwnDie.Status = DieStatus.Character;
        otherOwnDie.Level = 1; // fielding cost 1

        var queue = new AbilityQueue();
        var fieldEnergy = GiveWildEnergy(state, "teamA", 1);
        TurnEngine.Field(state, queue, otherOwnDie.Id, energyDieIdsToSpend: fieldEnergy.Select(d => d.Id).ToList());

        Assert.Contains(queue.Pending, a => a.Trigger == TriggerType.Teamwatch && a.SourceDieId == mystiqueDie.Id);

        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [opposingTarget.Id])));

        Assert.Equal(DieStatus.Energy, opposingTarget.Status);
        Assert.Equal(2, opposingTarget.EnergyAmount);
    }
}

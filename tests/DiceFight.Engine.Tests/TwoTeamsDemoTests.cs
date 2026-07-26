using DiceFight.Engine.Combat;
using DiceFight.Engine.Data;
using DiceFight.Engine.Effects;
using DiceFight.Engine.Model;
using DiceFight.Engine.Queueing;
using Xunit;

namespace DiceFight.Engine.Tests;

// End-to-end proof that the two curated sample teams actually play against
// each other through the real engine: Field (Main Step) triggers a
// WhenFielded ability via the AbilityQueue + EffectInterpreter, then the
// fielded die attacks and deals real combat damage using the team's own
// card stats. This bypasses Purchase/Roll (not built yet - see
// RULES_ENGINE_DESIGN.md) by seeding Reserve Pool dice directly; everything
// downstream (Field, DeclareAttackers/Blockers, AssignCombatDamage,
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

    [Fact]
    public void FieldingDazzler_TriggersWhenFieldedAbility_ThroughTheRealQueueAndInterpreter()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";

        var dazzlerDie = new DieInstance
        {
            Id = "teamA-dazzler-1", CardId = SampleCards.Dazzler.Id, OwnerId = "teamA", ControllerId = "teamA",
            Zone = Zone.ReservePool, Status = DieStatus.Character, Level = 1 // level 1 fielding cost is 0
        };
        state.Dice.Add(dazzlerDie);
        var opposingTarget = state.DiceFor("teamB").First(); // a Sidekick, 1D

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
    public void FieldedCharacterAttacksUnblocked_DealsRealCombatDamageToOpponent()
    {
        var state = BuildTwoTeamGame();
        state.ActivePlayerId = "teamA";

        // Apocalypse's placeholder level-2 face: fielding cost 1, 2A/3D.
        var apocalypseDie = new DieInstance
        {
            Id = "teamA-apocalypse-1", CardId = SampleCards.Apocalypse.Id, OwnerId = "teamA", ControllerId = "teamA",
            Zone = Zone.ReservePool, Status = DieStatus.Character, Level = 2
        };
        var energyDie = new DieInstance
        {
            Id = "teamA-energy-1", CardId = null, OwnerId = "teamA", ControllerId = "teamA",
            Zone = Zone.ReservePool, Status = DieStatus.Energy
        };
        state.Dice.Add(apocalypseDie);
        state.Dice.Add(energyDie);

        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, apocalypseDie.Id, energyDieIdsToSpend: [energyDie.Id]);

        Assert.Equal(Zone.FieldZone, apocalypseDie.Zone);
        Assert.Equal(Zone.OutOfPlay, energyDie.Zone); // spent to pay the fielding cost

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
}

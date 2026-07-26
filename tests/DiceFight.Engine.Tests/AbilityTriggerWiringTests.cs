using DiceFight.Engine.Combat;
using DiceFight.Engine.Effects;
using DiceFight.Engine.Model;
using DiceFight.Engine.Queueing;
using Xunit;

namespace DiceFight.Engine.Tests;

// None of the scripted sample cards use WhenAttacks or WhenKOd, so this
// verifies CombatEngine's enqueueing for those two triggers directly with
// a purpose-built card, complementing TwoTeamsDemoTests' WhenFielded coverage.
public class AbilityTriggerWiringTests
{
    private static CardDef MakeCard(string id, TriggerType trigger, EffectNode effect) => new()
    {
        Id = id, Name = id, Type = CardType.Character, PurchaseCost = 3, DieLimit = 4,
        Levels = [new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 1)],
        Abilities = [new AbilityDef(trigger, Cost: null, Effect: effect)]
    };

    [Fact]
    public void DeclareAttackers_EnqueuesWhenAttacksAbility()
    {
        var pinger = MakeCard("pinger", TriggerType.WhenAttacks, new LoseLife(1));
        var catalog = new Dictionary<string, CardDef> { [pinger.Id] = pinger };
        var state = GameState.NewGame(catalog,
            new Player { Id = "p1", Name = "P1" }, new Player { Id = "p2", Name = "P2" });
        state.CurrentStep = TurnStep.Attack;
        state.AttackSubStep = AttackSubStep.DeclareAttackers;

        var die = new DieInstance
        {
            Id = "p1-pinger-1", CardId = pinger.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1
        };
        state.Dice.Add(die);

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [die.Id]);

        Assert.False(queue.IsEmpty);
        var queued = queue.Pending.Single();
        Assert.Equal(TriggerType.WhenAttacks, queued.Trigger);
        Assert.Equal(die.Id, queued.SourceDieId);
    }

    [Fact]
    public void AssignCombatDamage_EnqueuesWhenKOdAbilityForEachDieKOd()
    {
        var vengeful = MakeCard("vengeful", TriggerType.WhenKOd, new LoseLife(1));
        var catalog = new Dictionary<string, CardDef> { [vengeful.Id] = vengeful };
        var state = GameState.NewGame(catalog,
            new Player { Id = "p1", Name = "P1" }, new Player { Id = "p2", Name = "P2" });
        state.CurrentStep = TurnStep.Attack;
        state.AttackSubStep = AttackSubStep.ActionAndGlobalWindow;

        // 1A/1D vengeful die, blocked by a 3A Sidekick - lethal.
        var attacker = new DieInstance
        {
            Id = "p1-vengeful-1", CardId = vengeful.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.AttackZone, Status = DieStatus.Character, Level = 1
        };
        var blocker = state.DiceFor("p2").First();
        blocker.Zone = Zone.AttackZone;
        blocker.AppliedModifiers.Add(new Modifier(AttackDelta: 2, DefenseDelta: 0, "test-boost")); // 1A -> 3A
        state.Dice.Add(attacker);

        var queue = new AbilityQueue();
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, blocker.Id);
        var splits = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [attacker.Id] = new Dictionary<string, int> { [blocker.Id] = 1 }
        };

        var result = CombatEngine.AssignCombatDamage(state, queue, assignment, splits);

        Assert.Contains(attacker.Id, result.KOdDieIds);
        Assert.False(queue.IsEmpty);
        var queued = queue.Pending.Single();
        Assert.Equal(TriggerType.WhenKOd, queued.Trigger);
        Assert.Equal(attacker.Id, queued.SourceDieId);
    }
}

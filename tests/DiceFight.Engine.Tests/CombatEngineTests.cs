using DiceFight.Engine;
using DiceFight.Engine.Combat;
using DiceFight.Engine.Model;
using Xunit;

namespace DiceFight.Engine.Tests;

public class CombatEngineTests
{
    private static (GameState state, DieInstance bruiser, DieInstance unblockedAttacker, DieInstance sidekickBlocker)
        CreateSkirmishState()
    {
        var bruiserCard = new CardDef
        {
            Id = "bruiser",
            Name = "Bruiser",
            Type = CardType.Character,
            PurchaseCost = 3,
            DieLimit = 4,
            Levels = [new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 2)]
        };

        var catalog = new Dictionary<string, CardDef> { [bruiserCard.Id] = bruiserCard };
        var p1 = new Player { Id = "p1", Name = "Player One" };
        var p2 = new Player { Id = "p2", Name = "Player Two" };
        var state = GameState.NewGame(catalog, p1, p2);
        state.CurrentStep = TurnStep.Attack;
        state.AttackSubStep = AttackSubStep.DeclareAttackers;

        var bruiser = new DieInstance
        {
            Id = "p1-bruiser-1", CardId = bruiserCard.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1
        };
        var unblockedAttacker = state.DiceFor("p1").First(); // one of p1's sidekicks
        unblockedAttacker.Zone = Zone.FieldZone;
        unblockedAttacker.Status = DieStatus.SidekickCharacter;

        var sidekickBlocker = state.DiceFor("p2").First();
        sidekickBlocker.Zone = Zone.FieldZone;
        sidekickBlocker.Status = DieStatus.SidekickCharacter;

        state.Dice.Add(bruiser);

        return (state, bruiser, unblockedAttacker, sidekickBlocker);
    }

    [Fact]
    public void UnblockedAttacker_DealsDamageToPlayerAndLeavesPlay()
    {
        var (state, bruiser, unblocked, blocker) = CreateSkirmishState();

        CombatEngine.DeclareAttackers(state, [bruiser.Id, unblocked.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(bruiser.Id, blocker.Id); // only Bruiser is blocked
        CombatEngine.DeclareBlockers(state, assignment, [blocker.Id]);

        var splits = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [bruiser.Id] = new Dictionary<string, int> { [blocker.Id] = 3 } // Bruiser's full 3A
        };
        var result = CombatEngine.AssignCombatDamage(state, assignment, splits);

        // Unblocked sidekick attacker (1A) hits the player for 1 and leaves play.
        Assert.Equal(19, state.PlayerTwo.Life);
        Assert.Equal(Zone.OutOfPlay, unblocked.Zone);
        Assert.Contains(blocker.Id, result.KOdDieIds); // 3 damage vs 1 defense
        Assert.Equal(Zone.PrepArea, blocker.Zone);
    }

    [Fact]
    public void BlockedAttacker_SurvivesIfBlockerDamageBelowDefense()
    {
        var (state, bruiser, unblocked, blocker) = CreateSkirmishState();

        CombatEngine.DeclareAttackers(state, [bruiser.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(bruiser.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, assignment, [blocker.Id]);

        var splits = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [bruiser.Id] = new Dictionary<string, int> { [blocker.Id] = 3 }
        };
        var result = CombatEngine.AssignCombatDamage(state, assignment, splits);

        // Blocker's 1A doesn't reach Bruiser's 2D - Bruiser survives, returns to Field Zone.
        Assert.DoesNotContain(bruiser.Id, result.KOdDieIds);
        Assert.Equal(Zone.FieldZone, bruiser.Zone);
        Assert.Equal(1, bruiser.Damage);
        Assert.Equal(20, state.PlayerTwo.Life); // blocked attacker never hits the player
    }

    [Fact]
    public void AssignCombatDamage_RejectsIncompleteDamageSplit()
    {
        var (state, bruiser, _, blocker) = CreateSkirmishState();

        CombatEngine.DeclareAttackers(state, [bruiser.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(bruiser.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, assignment, [blocker.Id]);

        var incompleteSplit = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [bruiser.Id] = new Dictionary<string, int> { [blocker.Id] = 1 } // must be 3, not 1
        };

        Assert.Throws<InvalidOperationException>(() =>
            CombatEngine.AssignCombatDamage(state, assignment, incompleteSplit));
    }

    [Fact]
    public void DeclareAttackers_RejectsDieNotInFieldZone()
    {
        var (state, bruiser, _, _) = CreateSkirmishState();
        bruiser.Zone = Zone.ReservePool;

        Assert.Throws<InvalidOperationException>(() => CombatEngine.DeclareAttackers(state, [bruiser.Id]));
    }
}

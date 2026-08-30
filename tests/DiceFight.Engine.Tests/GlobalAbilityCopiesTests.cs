using DiceFight.Engine;
using DiceFight.Engine.Effects;
using DiceFight.Engine.Model;
using DiceFight.Engine.Queueing;
using Xunit;

namespace DiceFight.Engine.Tests;

// Rule 3.4.2.4 - "Global abilities on opposing cards are considered
// separate from your own, even if they have the same text" - and rule
// 2.6.5.3, which spells out what that means: "If both players have a card
// with the same Global ability having the condition 'only once per turn',
// a player may pay for that Global ability twice in the same turn because
// there are two cards with that Global ability available."
//
// So a once-per-turn Global's allowance counts COPIES OF THE CARD on the
// table, not the card name and not the player using it. This matters most
// for Basic Actions, since both players choose two of a shared pool and
// bringing the same one is common.
public class GlobalAbilityCopiesTests
{
    private static CardDef OncePerTurnGlobal(string id) => new()
    {
        Id = id,
        Name = id,
        Type = CardType.BasicAction,
        PurchaseCost = 2,
        DieLimit = 3,
        Abilities =
        [
            new AbilityDef(
                Trigger: TriggerType.Global,
                Cost: null,
                Effect: new GainLife(1),
                EnergyCost: new EnergyCost(1, null),
                OncePerTurn: true)
        ]
    };

    private static GameState CreateState(IEnumerable<string> teamOne, IEnumerable<string> teamTwo, params CardDef[] cards)
    {
        var p1 = new Player { Id = "p1", Name = "P1" };
        var p2 = new Player { Id = "p2", Name = "P2" };
        p1.TeamCardIds.AddRange(teamOne);
        p2.TeamCardIds.AddRange(teamTwo);
        var state = GameState.NewGame(cards.ToDictionary(c => c.Id), p1, p2);
        state.CurrentStep = TurnStep.Main;
        return state;
    }

    private static List<string> Energy(GameState state, string playerId, int count)
    {
        var dice = state.DiceIn(playerId, Zone.Bag).Take(count).ToList();
        foreach (var die in dice)
        {
            die.Zone = Zone.ReservePool;
            die.Status = DieStatus.Energy;
            die.EnergyKind = EnergyKind.Wild;
        }
        return dice.Select(d => d.Id).ToList();
    }

    private static void Use(GameState state, string cardId, string playerId)
    {
        var queue = new AbilityQueue();
        TurnEngine.UseGlobalAbility(state, queue, cardId, playerId, Energy(state, playerId, 1));
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [])));
    }

    [Fact]
    public void OnePlayerBroughtIt_OncePerTurnAllowsOneUse()
    {
        var state = CreateState(["boom"], [], OncePerTurnGlobal("boom"));

        Use(state, "boom", "p1");

        var ex = Assert.Throws<InvalidOperationException>(() => Use(state, "boom", "p1"));
        Assert.Contains("once per turn", ex.Message);
    }

    // The scenario rule 2.6.5.3 describes verbatim.
    [Fact]
    public void BothPlayersBroughtIt_OneOfThemMayUseItTwiceInATurn()
    {
        var state = CreateState(["boom"], ["boom"], OncePerTurnGlobal("boom"));

        Use(state, "boom", "p1");
        Use(state, "boom", "p1");

        var ex = Assert.Throws<InvalidOperationException>(() => Use(state, "boom", "p1"));
        Assert.Contains("already been used 2 times", ex.Message);
    }

    [Fact]
    public void BothPlayersBroughtIt_TheTwoUsesAreSharedBetweenThePlayers()
    {
        var state = CreateState(["boom"], ["boom"], OncePerTurnGlobal("boom"));

        Use(state, "boom", "p1");
        Use(state, "boom", "p2");

        Assert.Throws<InvalidOperationException>(() => Use(state, "boom", "p1"));
    }

    [Fact]
    public void CleanUp_ResetsTheAllowance()
    {
        var state = CreateState(["boom"], [], OncePerTurnGlobal("boom"));
        Use(state, "boom", "p1");

        state.GlobalsUsedThisTurn.Clear();

        Use(state, "boom", "p1");
    }
}

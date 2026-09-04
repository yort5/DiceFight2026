using DiceFight.V2.Model;

namespace DiceFight.V2.Tests;

// v3 "Instinct Clash" addition (2026-09-03) - TurnEngine.RerollOwn, the
// player-voluntary reroll rule 2.6.1 describes but v2 never exposed as a
// real action (Reroll previously existed only as a card-triggered effect).
public class RerollOwnTests
{
    private sealed class ScriptedRoller(params int[] faceIndices) : IDiceRoller
    {
        private readonly Queue<int> _queue = new(faceIndices);
        public int Roll(DieDefinition die) => _queue.Dequeue();
    }

    private static GameConfig BuildTinyConfig()
    {
        var basicDie = new DieDefinition("Basic",
        [
            new Face([new SymbolAmount("Wild", 1)]),
            new Face([new SymbolAmount("Fist", 1)]),
        ]);

        return new GameConfig(
            Id: "reroll-test",
            Name: "Reroll Test Config",
            EnergySymbols: [new SymbolDef("Fist"), new SymbolDef("Wild", IsWild: true)],
            Keywords: [],
            Rules: new RulesConfig(StartingLife: 10, DrawCount: 2, MaxTeamCards: 0, MaxTeamDice: 0, BasicActionCount: 0),
            BasicDicePool: [new BasicDicePoolEntry(basicDie, Count: 4)],
            BasicActionSlots: 0);
    }

    private static (GameState state, AbilityQueue queue) SetUp()
    {
        var config = BuildTinyConfig();
        var catalog = new Dictionary<string, CardDef>();
        var playerOne = new Player { Id = "p1", Name = "One" };
        var playerTwo = new Player { Id = "p2", Name = "Two" };
        var state = GameSetup.NewGame(config, catalog, playerOne, playerTwo);
        var queue = new AbilityQueue();
        TurnEngine.ClearAndDraw(state, queue, new Random(1));
        return (state, queue);
    }

    [Fact]
    public void RerollOwn_Reassigns_The_Face_Of_A_Die_Roll_Already_Placed_In_The_Reserve_Pool()
    {
        var (state, queue) = SetUp();
        var roller = new ScriptedRoller(0, 0); // both dice land on the Wild energy face first
        TurnEngine.Roll(state, queue, roller);

        // Rule 2.4.2 - Roll already placed it in the Reserve Pool; there's
        // no separate "rolled but not yet committed" zone to reroll out of
        // (see TurnEngine.Roll's own remarks).
        var die = state.DiceIn("p1", Zone.ReservePool).First();
        Assert.Equal(0, die.CurrentFaceIndex);

        var reroller = new ScriptedRoller(1); // reroll lands on the Fist energy face
        TurnEngine.RerollOwn(state, queue, reroller, [die.Id]);

        Assert.Equal(1, die.CurrentFaceIndex);
        Assert.Equal(Zone.ReservePool, die.Zone); // a reroll reassigns the face in place, never moves zones
    }

    [Fact]
    public void RerollOwn_Rejects_The_Same_Die_Twice_In_One_Step()
    {
        var (state, queue) = SetUp();
        TurnEngine.Roll(state, queue, new ScriptedRoller(0, 0));
        var die = state.DiceIn("p1", Zone.ReservePool).First();

        TurnEngine.RerollOwn(state, queue, new ScriptedRoller(1), [die.Id]);

        Assert.Throws<InvalidOperationException>(() =>
            TurnEngine.RerollOwn(state, queue, new ScriptedRoller(0), [die.Id]));
    }

    [Fact]
    public void RerollOwn_Rejects_A_Die_Once_The_Step_Has_Moved_On_To_Main()
    {
        var (state, queue) = SetUp();
        TurnEngine.Roll(state, queue, new ScriptedRoller(0, 0));
        var die = state.DiceIn("p1", Zone.ReservePool).First();
        TurnEngine.FinishRoll(state, queue);

        // Roll already put it in the Reserve Pool (unchanged by
        // FinishRoll, which has no zone left to move - see its own
        // remarks); what actually blocks the reroll now is RequireStep,
        // once FinishRoll has advanced past Roll and Reroll.
        Assert.Equal(Zone.ReservePool, die.Zone);
        Assert.Throws<InvalidOperationException>(() =>
            TurnEngine.RerollOwn(state, queue, new ScriptedRoller(1), [die.Id]));
    }

    [Fact]
    public void RerollOwn_Allowance_Resets_On_The_Next_Roll_Call()
    {
        var (state, queue) = SetUp();
        TurnEngine.Roll(state, queue, new ScriptedRoller(0, 0));
        var die = state.DiceIn("p1", Zone.ReservePool).First();
        TurnEngine.RerollOwn(state, queue, new ScriptedRoller(1), [die.Id]);

        // A fresh Roll() call (opening a new turn's Roll and Reroll Step
        // in real play) clears the per-step reroll tracker - asserted
        // directly here rather than via a second roll's own result, since
        // by now `die` already sits in the Reserve Pool and a real second
        // Roll() has nothing left in DiceFromBag/DiceFromPrep to re-roll.
        TurnEngine.Roll(state, queue, new ScriptedRoller(0, 0));
        TurnEngine.RerollOwn(state, queue, new ScriptedRoller(1), [die.Id]); // does not throw
        Assert.Equal(1, die.CurrentFaceIndex);
    }
}

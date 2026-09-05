using DiceFight.V2.Data;
using DiceFight.V2.Model;

namespace DiceFight.V2.Tests;

// v3 "Instinct Clash" (2026-09-03) - config validity plus a real scripted
// turn against the actual InstinctClashConfig/Catalog (not a tiny made-up
// config), the same acceptance-test shape TurnCycleTests uses for the
// engine itself: setup -> draw -> roll -> purchase a Character -> field a
// free Tardigrade -> attack (Champion passive applied) -> cleanup.
public class InstinctClashConfigTests
{
    private sealed class ScriptedRoller(params int[] faceIndices) : IDiceRoller
    {
        private readonly Queue<int> _queue = new(faceIndices);
        public int Roll(DieDefinition die) => _queue.Dequeue();
    }

    [Fact]
    public void Config_And_Catalog_Are_Structurally_Valid()
    {
        var config = InstinctClashConfig.Config;
        var cards = InstinctClashConfig.Catalog.Values.ToList();

        Assert.Empty(config.Validate());
        Assert.Empty(config.ValidateCatalog(cards));
    }

    [Fact]
    public void Every_Energy_Type_Has_Exactly_Eight_Characters()
    {
        foreach (var (_, ids) in InstinctClashConfig.CharactersByEnergyType)
            Assert.Equal(8, ids.Count);
    }

    [Fact]
    public void Full_Turn_Cycle_Runs_With_Champion_Passive_Applied()
    {
        var config = InstinctClashConfig.Config;
        var catalog = InstinctClashConfig.Catalog;

        var playerOne = new Player { Id = "p1", Name = "Wolf Player", ChampionId = "Wolf" };
        playerOne.TeamCardIds.AddRange(InstinctClashConfig.CharactersByEnergyType["Claw"]);
        var playerTwo = new Player { Id = "p2", Name = "Armadillo Player", ChampionId = "Armadillo" };
        playerTwo.TeamCardIds.AddRange(InstinctClashConfig.CharactersByEnergyType["Shell"]);

        var state = GameSetup.NewGame(config, catalog, playerOne, playerTwo);
        var queue = new AbilityQueue();

        // --- Setup: 8 Characters x DieLimit 4 = 32 Unpurchased, 8 Tardigrades in the Bag ---
        Assert.Equal(32, state.DiceIn("p1", Zone.Unpurchased).Count());
        Assert.Equal(8, state.DiceIn("p1", Zone.Bag).Count());
        Assert.Equal(20, playerOne.Life);

        // --- Clear and Draw (first turn: draws DrawCount=4 (the normal
        // count), then sets ONE of those SAME 4 drawn dice - not a 5th
        // extra draw - straight to Out of Play, rule 2.3.3's going-first
        // penalty; all Tardigrades - Characters start Unpurchased) ---
        TurnEngine.ClearAndDraw(state, queue, new Random(1));
        Assert.Equal(3, state.DiceIn("p1", Zone.DiceFromBag).Count());
        Assert.Single(state.DiceIn("p1", Zone.OutOfPlay));
        Assert.Equal(4, state.DiceIn("p1", Zone.Bag).Count());

        // --- Roll: every drawn die lands on the L1 face (index 0, 2 Claw energy) ---
        TurnEngine.Roll(state, queue, new ScriptedRoller(0, 0, 0));
        TurnEngine.FinishRoll(state, queue);
        var reserve = state.DiceIn("p1", Zone.ReservePool).ToList();
        Assert.Equal(3, reserve.Count);
        Assert.All(reserve, d => Assert.Equal(2, state.GetCurrentFace(d)!.Symbols.Single().Count));

        // --- Purchase Honey Badger (cost 2 Claw) using one L1 die's 2 energy ---
        var honeyBadgerId = InstinctClashConfig.HoneyBadger.Id;
        var unpurchased = state.Dice.First(d => d.CardId == honeyBadgerId && d.Zone == Zone.Unpurchased);
        var spendDie = reserve[0];
        TurnEngine.Purchase(state, queue, unpurchased.Id, [spendDie.Id]);
        Assert.Equal(Zone.UsedPile, unpurchased.Zone);
        Assert.Equal(Zone.OutOfPlay, spendDie.Zone);

        // --- Field a free Tardigrade (fielding cost 0, no energy needed) ---
        var toField = reserve[1];
        TurnEngine.Field(state, queue, toField.Id, []);
        Assert.Equal(Zone.FieldZone, toField.Zone);

        // --- Wolf's passive (+1 ATK to all your dice) is live: base 0 -> 1 ---
        Assert.Equal(0, state.GetCurrentFace(toField)!.Character!.Attack);
        Assert.Equal(1, QueryEngine.GetAttack(state, toField));

        // --- Attack step: the buffed Tardigrade attacks unblocked ---
        TurnEngine.EnterAttackStep(state, queue);
        CombatEngine.DeclareAttackers(state, queue, [toField.Id]);
        var assignment = new CombatAssignment();
        CombatEngine.DeclareBlockers(state, queue, assignment, []);
        CombatEngine.AssignCombatDamage(state, queue, assignment, new Dictionary<string, IReadOnlyDictionary<string, int>>());

        Assert.Equal(19, playerTwo.Life); // 20 - 1 (the Champion-buffed attack)
        // Rule 2.7.4.3.1 - an unblocked attacker leaves the Attack Zone
        // for Out of Play immediately (CombatEngine.cs's own citation),
        // not back to the Field Zone - that return path is only for a
        // BLOCKED survivor. Out of Play sweeps to the Used Pile at Clean Up.
        Assert.Equal(Zone.OutOfPlay, toField.Zone);

        // --- Clean Up: passes the turn ---
        TurnEngine.CleanUp(state, queue);
        Assert.Equal("p2", state.ActivePlayerId);
        Assert.Equal(Zone.UsedPile, toField.Zone);
    }
}

using DiceFight.V2.Data;
using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Tests;

// v3 "Dice Kingdom" (2026-09-03) - config validity plus a real scripted
// turn against the actual DiceKingdomConfig/Catalog (not a tiny made-up
// config), the same acceptance-test shape TurnCycleTests uses for the
// engine itself: setup -> draw -> roll -> purchase a Character -> field a
// free Tardigrade -> attack (Champion passive applied) -> cleanup.
public class DiceKingdomConfigTests
{
    private sealed class ScriptedRoller(params int[] faceIndices) : IDiceRoller
    {
        private readonly Queue<int> _queue = new(faceIndices);
        public int Roll(DieDefinition die) => _queue.Dequeue();
    }

    [Fact]
    public void Config_And_Catalog_Are_Structurally_Valid()
    {
        var config = DiceKingdomConfig.Config;
        var cards = DiceKingdomConfig.Catalog.Values.ToList();

        Assert.Empty(config.Validate());
        Assert.Empty(config.ValidateCatalog(cards));
    }

    [Fact]
    public void Every_Champion_Has_Exactly_Eight_Characters_With_No_Duplicates()
    {
        var seenAcrossChampions = new HashSet<string>();
        foreach (var (championId, ids) in DiceKingdomConfig.CharactersByChampion)
        {
            Assert.Equal(8, ids.Count);
            Assert.Equal(8, ids.Distinct().Count()); // no repeats within one Champion's own pack
            foreach (var id in ids)
                Assert.True(seenAcrossChampions.Add(id), $"\"{id}\" appears on more than one Champion's pack (via {championId}).");
        }
    }

    // Direct feedback (2026-09-12): every Champion's own-energy four needs
    // "at least one 2-cost and one 3-cost... a mid-range and a higher
    // cost power card." Checks the rule itself, not just today's specific
    // picks, so a future roster edit that breaks the curve fails loudly.
    [Fact]
    public void Every_Champions_Own_Energy_Characters_Cover_A_Real_Cost_Curve()
    {
        var catalog = DiceKingdomConfig.Catalog;
        foreach (var champion in DiceKingdomConfig.Champions)
        {
            var ownEnergyCosts = DiceKingdomConfig.CharactersByChampion[champion.Id]
                .Select(id => catalog[id])
                .Where(c => c.EnergySymbolIds.Contains(champion.EnergySymbolId))
                .Select(c => c.PurchaseCost)
                .OrderBy(c => c)
                .ToList();

            Assert.True(ownEnergyCosts.Count >= 4, $"{champion.Id} has fewer than 4 own-energy Characters.");
            Assert.Contains(2, ownEnergyCosts);
            Assert.Contains(3, ownEnergyCosts);
            Assert.True(ownEnergyCosts.Max() >= 5, $"{champion.Id} has no higher-cost \"power card\" (5+) in its own energy.");
        }
    }

    [Fact]
    public void Full_Turn_Cycle_Runs_With_Champion_Passive_Applied()
    {
        var config = DiceKingdomConfig.Config;
        var catalog = DiceKingdomConfig.Catalog;

        var playerOne = new Player { Id = "p1", Name = "Wolf Player", ChampionId = "Wolf" };
        playerOne.TeamCardIds.AddRange(DiceKingdomConfig.CharactersByChampion["Wolf"]);
        var playerTwo = new Player { Id = "p2", Name = "Armadillo Player", ChampionId = "Armadillo" };
        playerTwo.TeamCardIds.AddRange(DiceKingdomConfig.CharactersByChampion["Armadillo"]);

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
        var honeyBadgerId = DiceKingdomConfig.HoneyBadger.Id;
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

    // Trigger keywords: every card whose ability fires on one of these
    // triggers must carry the matching keyword, and vice versa, so the
    // trigger is always codified rather than free text.
    [Fact]
    public void Trigger_Keywords_Match_Each_Cards_Ability_Triggers()
    {
        foreach (var card in DiceKingdomConfig.Catalog.Values)
        {
            void Check(bool hasTrigger, string keyword) =>
                Assert.True(hasTrigger == card.Keywords.Contains(keyword),
                    $"{card.Name}: keyword \"{keyword}\" {(hasTrigger ? "missing" : "present without a matching trigger")}.");

            Check(card.Abilities.Any(a => a.Trigger == TriggerKind.DieFielded), "On Field");
            Check(card.Abilities.Any(a => a.Trigger == TriggerKind.DieAttacks), "On Attack");
            Check(card.Abilities.Any(a => a.Trigger == TriggerKind.DieBlocks), "On Block");
            Check(card.Abilities.Any(a => a.Trigger == TriggerKind.DieFaceChanged && a.Filter?.LevelIncreased == true), "Awaken");
            foreach (var keyword in card.Keywords.Where(k => k is "On Field" or "On Attack" or "On Block" or "Awaken"))
                Assert.Contains(keyword + ":", card.RawText);
        }
    }
}

using DiceFight.V2.Data;
using DiceFight.V2.Model;

namespace DiceFight.V2.Tests;

// V2_PLAN.md Phase 8 task 1 acceptance: DiceFightClassicConfig is valid
// and playable, AND a variant ruleset (draw 6, split Sidekick pools) is
// constructible in a test with ZERO engine changes - the direct proof of
// Direction-C readiness (ARCHITECTURE_REVIEW.md Part 3).
public class DiceFightClassicConfigTests
{
    [Fact]
    public void The_Classic_Config_Is_Internally_Valid()
    {
        var errors = DiceFightClassicConfig.Config.Validate();
        Assert.Empty(errors);
    }

    [Fact]
    public void The_Sidekick_Die_Has_Six_Faces_One_Character_And_Five_Distinct_Energy()
    {
        var faces = DiceFightClassicConfig.SidekickDie.Faces;
        Assert.Equal(6, faces.Count);

        var characterFaces = faces.Where(f => f.Character is not null).ToList();
        var characterFace = Assert.Single(characterFaces);
        Assert.Equal(1, characterFace.Character!.Attack);
        Assert.Equal(1, characterFace.Character.Defense);
        Assert.Equal(0, characterFace.Character.FieldingCost);

        var energySymbolIds = faces.Where(f => f.Character is null).Select(f => f.Symbols.Single().SymbolId).ToList();
        Assert.Equal(["Wild", "Fist", "Bolt", "Mask", "Shield"], energySymbolIds);
    }

    [Fact]
    public void A_Full_Turn_Cycle_Runs_Against_The_Real_Classic_Config()
    {
        var card = new Model.CardDef(
            Id: "smoke-test-card", Name: "Smoke Test Card", Subtitle: null, Set: "TEST",
            CardType: CardType.Character, PurchaseCost: 1, EnergySymbolIds: ["Fist"],
            Die: new DieDefinition("SmokeDie", [new Face([new SymbolAmount("Fist", 1)]), new Face([], new CharacterFaceData(1, 0, 1, 1), Kind: FaceKind.CharacterFace)]),
            DieLimit: 1, Affiliations: [], Keywords: [], RawText: "", Abilities: [], Continuous: []);

        var playerOne = new Player { Id = "p1", Name = "One" };
        playerOne.TeamCardIds.Add(card.Id);
        var playerTwo = new Player { Id = "p2", Name = "Two" };

        var state = GameSetup.NewGame(DiceFightClassicConfig.Config, new Dictionary<string, Model.CardDef> { [card.Id] = card }, playerOne, playerTwo);
        var queue = new AbilityQueue();

        Assert.Equal(20, playerOne.Life);
        Assert.Equal(8, state.DiceIn("p1", Zone.Bag).Count());

        TurnEngine.ClearAndDraw(state, queue, new Random(1));
        Assert.Equal(3, state.DiceIn("p1", Zone.PrepArea).Count()); // first turn: DrawCount(4) - 1
    }

    // Direction C - a variant ruleset expressed purely as a different
    // GameConfig value: draw 6 instead of 4, and two SEPARATE Sidekick-
    // equivalent pools instead of one (ARCHITECTURE_REVIEW.md Part 3's
    // own "8 identical Sidekick dice -> two 4-die sets" example) - both
    // via BasicDicePool being a list of (DieDefinition, count) entries,
    // not a single hardcoded pool. No engine file changes accompany this
    // test.
    [Fact]
    public void A_Variant_Config_With_A_Different_Draw_Count_And_Split_Sidekick_Pools_Just_Works()
    {
        var swiftPool = new DieDefinition("Swift", [
            new Face([], new CharacterFaceData(1, 0, 1, 1), Kind: FaceKind.CharacterFace),
            new Face([new SymbolAmount("Wild", 1)]),
        ]);
        var stoutPool = new DieDefinition("Stout", [
            new Face([], new CharacterFaceData(1, 0, 1, 2), Kind: FaceKind.CharacterFace),
            new Face([new SymbolAmount("Wild", 1)]),
        ]);

        var variant = DiceFightClassicConfig.Config with
        {
            Id = "dicefight-variant-split-pool",
            Rules = DiceFightClassicConfig.Config.Rules with { DrawCount = 6 },
            BasicDicePool = [new BasicDicePoolEntry(swiftPool, 4), new BasicDicePoolEntry(stoutPool, 4)],
        };

        Assert.Empty(variant.Validate());

        var playerOne = new Player { Id = "p1", Name = "One" };
        var playerTwo = new Player { Id = "p2", Name = "Two" };
        var state = GameSetup.NewGame(variant, new Dictionary<string, Model.CardDef>(), playerOne, playerTwo);

        Assert.Equal(4, state.Dice.Count(d => d.OwnerId == "p1" && d.PoolDieId == "Swift"));
        Assert.Equal(4, state.Dice.Count(d => d.OwnerId == "p1" && d.PoolDieId == "Stout"));

        var queue = new AbilityQueue();
        TurnEngine.ClearAndDraw(state, queue, new Random(1));
        Assert.Equal(5, state.DiceIn("p1", Zone.PrepArea).Count()); // first turn: DrawCount(6) - 1
    }
}

using DiceFight.V2.Model;

namespace DiceFight.V2.Tests;

// V2_PLAN.md Phase 2 acceptance test: setup -> draw -> roll -> purchase ->
// field -> attack step skipped -> cleanup -> next turn, against a
// deliberately tiny, made-up GameConfig (not the real Dice Masters
// config - that's Phase 8's job; using a tiny one here is itself part of
// the proof that nothing is hardcoded).
public class TurnCycleTests
{
    // A minimal roller that returns pre-scripted face indices in call
    // order, ignoring which die it's for - fine here since every drawn
    // die in this test shares the same DieDefinition, so identity doesn't
    // matter, only how many rolls happen and in what sequence.
    private sealed class ScriptedRoller(params int[] faceIndices) : IDiceRoller
    {
        private readonly Queue<int> _queue = new(faceIndices);
        public int Roll(DieDefinition die) => _queue.Dequeue();
    }

    private static GameConfig BuildTinyConfig()
    {
        var basicDie = new DieDefinition("Basic",
        [
            new Face([new SymbolAmount("Wild", 1)]),                                          // 0
            new Face([new SymbolAmount("Fist", 1)]),                                           // 1
            new Face([], Character: new CharacterFaceData(Level: 1, FieldingCost: 0, Attack: 1, Defense: 1)), // 2
            new Face([], Character: new CharacterFaceData(Level: 1, FieldingCost: 0, Attack: 1, Defense: 1)), // 3
        ]);

        return new GameConfig(
            Id: "tiny-test",
            Name: "Tiny Test Config",
            EnergySymbols: [new SymbolDef("Fist"), new SymbolDef("Wild", IsWild: true)],
            Keywords: [],
            Rules: new RulesConfig(StartingLife: 10, DrawCount: 3, MaxTeamCards: 2, MaxTeamDice: 4, BasicActionCount: 0),
            BasicDicePool: [new BasicDicePoolEntry(basicDie, Count: 4)],
            BasicActionSlots: 0);
    }

    private static CardDef BuildTestCharacterCard() => new(
        Id: "T001",
        Name: "Test Character",
        Subtitle: null,
        Set: "TEST",
        CardType: CardType.Character,
        PurchaseCost: 1,
        EnergySymbolIds: ["Fist"],
        Die: new DieDefinition("T001Die",
        [
            new Face([new SymbolAmount("Fist", 1)]),
            new Face([], Character: new CharacterFaceData(Level: 1, FieldingCost: 1, Attack: 2, Defense: 2)),
        ]),
        DieLimit: 1,
        Affiliations: [],
        Keywords: [],
        RawText: "",
        Abilities: [],
        Continuous: []);

    [Fact]
    public void Full_Turn_Cycle_Runs_Setup_Through_CleanUp_And_Passes_The_Turn()
    {
        var config = BuildTinyConfig();
        var card = BuildTestCharacterCard();
        var catalog = new Dictionary<string, CardDef> { [card.Id] = card };

        var playerOne = new Player { Id = "p1", Name = "One" };
        playerOne.TeamCardIds.Add(card.Id);
        var playerTwo = new Player { Id = "p2", Name = "Two" };

        // --- Setup ---
        var state = DiceFight.V2.GameSetup.NewGame(config, catalog, playerOne, playerTwo);
        var queue = new DiceFight.V2.AbilityQueue(); // Phase 4 - enqueue-only here, nothing drains yet (Phase 5)

        Assert.Equal(10, playerOne.Life);
        Assert.Single(state.DiceIn("p1", Zone.Unpurchased)); // T001's one die
        Assert.Equal(4, state.DiceIn("p1", Zone.Bag).Count()); // the basic pool
        Assert.Equal("p1", state.ActivePlayerId);
        Assert.Equal(TurnStep.StartOfTurn, state.CurrentStep); // Spike C - the turn opens on the start-of-turn window

        // --- Clear and Draw (first turn: DrawCount - 1 = 2) ---
        DiceFight.V2.TurnEngine.ClearAndDraw(state, queue, new Random(1));

        Assert.Equal(2, state.DiceIn("p1", Zone.PrepArea).Count());
        Assert.Equal(2, state.DiceIn("p1", Zone.Bag).Count());
        Assert.Equal(TurnStep.RollAndReroll, state.CurrentStep);

        // --- Roll (one lands on the Fist energy face, one on a character face) ---
        var roller = new ScriptedRoller(1, 2);
        DiceFight.V2.TurnEngine.Roll(state, queue, roller);
        DiceFight.V2.TurnEngine.FinishRoll(state);

        Assert.Equal(TurnStep.Main, state.CurrentStep);
        var reserveDice = state.DiceIn("p1", Zone.ReservePool).ToList();
        Assert.Equal(2, reserveDice.Count);
        var energyDie = reserveDice.Single(d => state.GetCurrentFace(d)!.Character is null);
        var characterDie = reserveDice.Single(d => state.GetCurrentFace(d)!.Character is not null);

        // --- Purchase (T001 costs 1 Fist) ---
        var unpurchasedDieId = FindUnpurchasedDieId(state, "T001");
        DiceFight.V2.TurnEngine.Purchase(state, queue, unpurchasedDieId, [energyDie.Id]);

        var purchased = state.Dice.Single(d => d.CardId == "T001");
        Assert.Equal(Zone.UsedPile, purchased.Zone);
        Assert.Equal("p1", purchased.ControllerId);
        Assert.Equal(Zone.OutOfPlay, energyDie.Zone);

        // --- Field (the rolled character-face pool die, free to field) ---
        DiceFight.V2.TurnEngine.Field(state, queue, characterDie.Id, []);

        Assert.Equal(Zone.FieldZone, characterDie.Zone);

        // --- Attack step: skipped ---
        DiceFight.V2.TurnEngine.SkipAttackStep(state, queue);
        Assert.Equal(TurnStep.Attack, state.CurrentStep);

        // --- Clean Up: passes the turn ---
        DiceFight.V2.TurnEngine.CleanUp(state, queue);

        Assert.Equal("p2", state.ActivePlayerId);
        Assert.Equal(TurnStep.StartOfTurn, state.CurrentStep); // Spike C - the turn opens on the start-of-turn window
        Assert.Empty(state.DiceIn("p1", Zone.ReservePool)); // swept to Used Pile
        Assert.Empty(state.DiceIn("p1", Zone.OutOfPlay));
        Assert.Equal(Zone.FieldZone, characterDie.Zone); // Field Zone dice are untouched by Clean Up
        Assert.False(state.IsFirstTurn);
    }

    private static string FindUnpurchasedDieId(DiceFight.V2.GameState state, string cardId) =>
        state.Dice.Single(d => d.CardId == cardId && d.Zone == Zone.Unpurchased).Id;
}

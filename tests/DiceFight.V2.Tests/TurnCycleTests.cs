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
            new Face([], new CharacterFaceData(Level: 1, FieldingCost: 0, Attack: 1, Defense: 1), Kind: FaceKind.CharacterFace), // 2
            new Face([], new CharacterFaceData(Level: 1, FieldingCost: 0, Attack: 1, Defense: 1), Kind: FaceKind.CharacterFace), // 3
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
            new Face([], new CharacterFaceData(Level: 1, FieldingCost: 1, Attack: 2, Defense: 2), Kind: FaceKind.CharacterFace),
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

        // --- Clear and Draw (first turn: draws DrawCount=3 (the normal
        // count, same as any other turn), then sets ONE of those SAME 3
        // drawn dice - not a 4th extra draw - straight to Out of Play,
        // rule 2.3.3's going-first penalty; the Bag ends down by exactly
        // DrawCount, not DrawCount + 1) ---
        DiceFight.V2.TurnEngine.ClearAndDraw(state, queue, new Random(1));

        Assert.Equal(2, state.DiceIn("p1", Zone.DiceFromBag).Count());
        Assert.Single(state.DiceIn("p1", Zone.OutOfPlay));
        Assert.Single(state.DiceIn("p1", Zone.Bag));
        Assert.Equal(TurnStep.RollAndReroll, state.CurrentStep);

        // --- Roll (one lands on the Fist energy face, the other on a
        // character face) ---
        var roller = new ScriptedRoller(1, 2);
        DiceFight.V2.TurnEngine.Roll(state, queue, roller);
        DiceFight.V2.TurnEngine.FinishRoll(state, queue);

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

    // --- Reserve Pool timing (rule 2.3.1 + the TURN SUMMARY's Main and
    // Cleanup entries). Previously v2 swept BOTH players' Reserve Pools
    // at Clean Up, which is neither what the rulebook says nor
    // survivable for the inactive player's Globals. ---

    private static DiceFight.V2.GameState TwoPlayerGame(out DiceFight.V2.AbilityQueue queue)
    {
        var config = BuildTinyConfig();
        var card = BuildTestCharacterCard();
        var state = DiceFight.V2.GameSetup.NewGame(config, new Dictionary<string, CardDef> { [card.Id] = card },
            new Player { Id = "p1", Name = "One" }, new Player { Id = "p2", Name = "Two" });
        state.CurrentStep = TurnStep.Main;
        queue = new DiceFight.V2.AbilityQueue();
        return state;
    }

    // Face 1 of the tiny config's basic die is a Fist energy face; face 2
    // is a character face.
    private static DieInstance PoolDieInReserve(DiceFight.V2.GameState state, string controllerId, int faceIndex, string id)
    {
        var die = new DieInstance { Id = id, PoolDieId = "Basic", OwnerId = controllerId, ControllerId = controllerId, Zone = Zone.ReservePool, CurrentFaceIndex = faceIndex };
        state.Dice.Add(die);
        return die;
    }

    [Fact]
    public void Unfielded_Character_Dice_Leave_At_The_End_Of_The_Main_Step_But_Energy_Stays()
    {
        var state = TwoPlayerGame(out var queue);
        var unfielded = PoolDieInReserve(state, "p1", 2, "unfielded-character");
        var energy = PoolDieInReserve(state, "p1", 1, "leftover-energy");

        DiceFight.V2.TurnEngine.EnterAttackStep(state, queue);

        Assert.Equal(Zone.UsedPile, unfielded.Zone); // TURN SUMMARY, end of Main Step
        Assert.Equal(Zone.ReservePool, energy.Zone); // energy is not swept here
    }

    [Fact]
    public void Leftover_Energy_Survives_The_Opponents_Whole_Turn()
    {
        var state = TwoPlayerGame(out var queue);
        var p2Energy = PoolDieInReserve(state, "p2", 1, "p2-energy"); // left from p2's own turn

        // Play out the rest of p1's turn.
        DiceFight.V2.TurnEngine.EnterAttackStep(state, queue);
        DiceFight.V2.TurnEngine.CleanUp(state, queue);

        // Still there - this is what funds the inactive player's Globals
        // (rule 2.6.5.2). Sweeping it at Clean Up left them with nothing.
        Assert.Equal(Zone.ReservePool, p2Energy.Zone);
        Assert.Equal("p2", state.ActivePlayerId);
    }

    [Fact]
    public void Your_Own_Leftover_Energy_Clears_At_Your_Next_Clear_And_Draw()
    {
        var state = TwoPlayerGame(out var queue);
        var p1Energy = PoolDieInReserve(state, "p1", 1, "p1-energy");

        // End p1's turn, then end p2's turn, handing control back to p1.
        DiceFight.V2.TurnEngine.EnterAttackStep(state, queue);
        DiceFight.V2.TurnEngine.CleanUp(state, queue);
        Assert.Equal(Zone.ReservePool, p1Energy.Zone); // survived its own Clean Up

        DiceFight.V2.TurnEngine.ClearAndDraw(state, queue, new Random(1)); // p2's turn opens
        Assert.Equal(Zone.ReservePool, p1Energy.Zone); // and the opponent's Clear and Draw
        DiceFight.V2.TurnEngine.FinishRoll(state, queue);
        DiceFight.V2.TurnEngine.EnterAttackStep(state, queue);
        DiceFight.V2.TurnEngine.CleanUp(state, queue);

        DiceFight.V2.TurnEngine.ClearAndDraw(state, queue, new Random(1)); // p1's own next turn - rule 2.3.1
        Assert.Equal(Zone.UsedPile, p1Energy.Zone);
    }

    // Rule 2.3.2/2.3.5-2.3.9 - direct feedback (2026-09-05): with only 1
    // die left in the Bag, Clear and Draw used to draw just that 1
    // instead of reshuffling the Used Pile back in and continuing to the
    // real DrawCount.
    [Fact]
    public void ClearAndDraw_Refills_The_Bag_From_The_Used_Pile_When_It_Runs_Short()
    {
        var config = BuildTinyConfig(); // DrawCount 3, a 4-die pool
        var card = BuildTestCharacterCard();
        var playerOne = new Player { Id = "p1", Name = "One" };
        var playerTwo = new Player { Id = "p2", Name = "Two" };
        var state = DiceFight.V2.GameSetup.NewGame(config, new Dictionary<string, CardDef> { [card.Id] = card }, playerOne, playerTwo);
        var queue = new DiceFight.V2.AbilityQueue();

        // First turn: DrawCount(3) drawn (one of those 3 set Out of Play
        // for going first - rule 2.3.3), leaving 1 of the 4-die pool
        // still in the Bag.
        DiceFight.V2.TurnEngine.ClearAndDraw(state, queue, new Random(1));
        Assert.Single(state.DiceIn("p1", Zone.Bag));

        // Simulate the rest of that turn having happened - everything
        // actually drawn/spent this turn ends up in the Used Pile,
        // exactly what a real Clean Up does to Reserve Pool/Out of Play
        // dice (TurnEngine.CleanUp) - done directly here so this test
        // stays focused on the draw/refill mechanic rather than playing
        // out a second full player's turn in between. The 1 die still
        // sitting in the Bag is untouched, same as a real Clean Up would
        // leave it.
        foreach (var die in state.Dice.Where(d => d.OwnerId == "p1" && d.Zone != Zone.Bag))
            die.Zone = Zone.UsedPile;
        state.CurrentStep = TurnStep.StartOfTurn;

        // Second turn: the Bag holds only 1 of the 3 dice this turn
        // needs - it must run dry mid-draw, reshuffle the Used Pile's 3
        // dice back in, and keep drawing to the real count (not the
        // going-first count - IsFirstTurn is false again after the first
        // ClearAndDraw call above).
        DiceFight.V2.TurnEngine.ClearAndDraw(state, queue, new Random(2));

        Assert.Equal(3, state.DiceIn("p1", Zone.DiceFromBag).Count());
        Assert.Single(state.DiceIn("p1", Zone.Bag));
        Assert.Empty(state.DiceIn("p1", Zone.UsedPile));
    }

    private static string FindUnpurchasedDieId(DiceFight.V2.GameState state, string cardId) =>
        state.Dice.Single(d => d.CardId == cardId && d.Zone == Zone.Unpurchased).Id;
}

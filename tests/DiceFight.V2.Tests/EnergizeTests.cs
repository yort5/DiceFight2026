using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Tests;

// V2_TAIL_POLICY.md's Energize entry, resolved 2026-09-01 (user-signed-off):
// "Whenever you roll this die on one of its double energy faces, you must
// use its Energize ability... During the Roll and Reroll Step, only check
// at the end of the Step" (Comprehensive Rules). Modeled as
// TurnStepEntered(Main) + Conditional(CountAtLeast(Self, SymbolCount>=2)) -
// NOT a DieFaceChanged filter, which cannot express a die already showing
// double energy that nobody rerolls (no face change happens).
//
// The Conditional gate is only evaluated when the queued ability actually
// DRAINS (EffectInterpreter), not at enqueue time - so most of these tests
// drain the queue and check a GrantCounter marker fired, rather than just
// asserting on AbilityQueue.Pending, which enqueues unconditionally for any
// candidate listener regardless of face.
//
// These tests exercise the generic mechanism through the real Roll ->
// FinishRoll path (ground rule 6), independent of any one migrated card.
public class EnergizeTests
{
    private sealed class FixedRoller(int index) : IDiceRoller
    {
        public int Roll(DieDefinition die) => index;
    }

    // Index 0/1 = double energy, 2 = single energy, 3 = character - the
    // same "doubles first" order MigrationDice.EnergyFaces uses.
    private static readonly DieDefinition EnergizeDie = new("EnergizeDie",
    [
        new Face([new SymbolAmount("Fist", 2)]),
        new Face([new SymbolAmount("Fist", 2)]),
        new Face([new SymbolAmount("Fist", 1)]),
        new Face([], new CharacterFaceData(1, 0, 1, 1), Kind: FaceKind.CharacterFace),
    ]);

    // A marker effect whose firing is directly observable (LifeChange(0)
    // would be a silent no-op either way).
    private static readonly EffectNode MarkerEffect = new GrantCounter(new TargetFilter(Self: true), "Fired", 1);

    // The real DpsCards.Energize() shape, mirrored here rather than
    // referenced directly - these tests exercise the GENERIC mechanism
    // independent of any one migrated card (the file's own header note).
    private static IReadOnlyList<TriggeredAbility> EnergizeAbilities(EffectNode effect)
    {
        var checkAndRun = new Conditional(
            new CountAtLeast(new TargetFilter(Self: true, Stat: new StatThreshold(StatKind.SymbolCount, Min: 2)), 1),
            Then: effect);
        return
        [
            new TriggeredAbility(TriggerKind.TurnStepEntered, checkAndRun, Filter: new EventFilter(Step: StepIds.Main)),
            new TriggeredAbility(TriggerKind.DieFaceChanged, checkAndRun, Filter: new EventFilter(RequireSelf: true, ExcludeCause: FaceChangeCause.Roll)),
        ];
    }

    private static CardDef BuildCard(string id, EffectNode? effect = null) => new(
        Id: id, Name: id, Subtitle: null, Set: "TEST", CardType: CardType.Character,
        PurchaseCost: 1, EnergySymbolIds: ["Fist"], Die: EnergizeDie, DieLimit: 1,
        Affiliations: [], Keywords: ["Energize"], RawText: "",
        Abilities: EnergizeAbilities(effect ?? MarkerEffect), Continuous: []);

    private static GameConfig BuildTinyConfig() => new(
        Id: "test", Name: "Test",
        EnergySymbols: [new SymbolDef("Fist")],
        Keywords: [],
        Rules: new RulesConfig(StartingLife: 10, DrawCount: 3, MaxTeamCards: 4, MaxTeamDice: 4, BasicActionCount: 0),
        BasicDicePool: [],
        BasicActionSlots: 0);

    private static GameState BuildState(CardDef card) => new()
    {
        Config = BuildTinyConfig(),
        CardCatalog = new Dictionary<string, CardDef> { [card.Id] = card },
        PlayerOne = new Player { Id = "p1", Name = "One" },
        PlayerTwo = new Player { Id = "p2", Name = "Two" },
        ActivePlayerId = "p1",
        CurrentStep = TurnStep.RollAndReroll,
    };

    private static bool Fired(GameState state, string cardId, string controllerId = "p1") =>
        state.Counters.GetValueOrDefault((controllerId, cardId, "Fired")) > 0;

    private static void Drain(GameState state, AbilityQueue queue) =>
        EffectInterpreter.DrainQueue(state, queue, new FixedRoller(0), new Random(1));

    [Fact]
    public void Rolling_Onto_A_Double_Energy_Face_Does_Not_Fire_Mid_Step()
    {
        var card = BuildCard("Energizer");
        var state = BuildState(card);
        var die = new DieInstance { Id = "d", CardId = card.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.PrepArea, CurrentFaceIndex = 2 };
        state.Dice.Add(die);
        var queue = new AbilityQueue();

        TurnEngine.Roll(state, queue, new FixedRoller(0)); // lands on a double face
        Drain(state, queue);

        // The decisive case the rule text calls out: NOT checked on the
        // roll itself - only once the Roll and Reroll Step actually ends.
        // Roll() fires DieFaceChanged only (no TurnStepEntered yet), so
        // there's nothing to enqueue at all here, let alone drain.
        Assert.False(Fired(state, card.Id));
    }

    [Fact]
    public void Landing_On_Double_Energy_And_Being_Left_Alone_Fires_At_Step_End()
    {
        var card = BuildCard("Energizer");
        var state = BuildState(card);
        var die = new DieInstance { Id = "d", CardId = card.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.PrepArea, CurrentFaceIndex = 2 };
        state.Dice.Add(die);
        var queue = new AbilityQueue();

        TurnEngine.Roll(state, queue, new FixedRoller(0));
        TurnEngine.FinishRoll(state, queue);
        Drain(state, queue);

        Assert.True(Fired(state, card.Id));
    }

    [Fact]
    public void Landing_On_A_Single_Energy_Face_Never_Fires()
    {
        var card = BuildCard("Energizer");
        var state = BuildState(card);
        var die = new DieInstance { Id = "d", CardId = card.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.PrepArea, CurrentFaceIndex = 2 };
        state.Dice.Add(die);
        var queue = new AbilityQueue();

        TurnEngine.Roll(state, queue, new FixedRoller(2)); // lands on the single face
        TurnEngine.FinishRoll(state, queue);
        Drain(state, queue);

        // The ability still ENQUEUES (Step:Main matches for any Reserve
        // Pool candidate) - it's the Conditional inside that gates on the
        // actual face, which is the behavior that matters.
        Assert.False(Fired(state, card.Id));
    }

    // Rule (1): "will not trigger if rolled in the Used Pile or Prep
    // Area" - a die never touched by this turn's Roll/FinishRoll (still
    // sitting dormant in the Used Pile) is not a candidate listener at
    // all, even though TurnStepEntered(Main) fires regardless.
    [Fact]
    public void A_Double_Energy_Face_Sitting_In_The_Used_Pile_Does_Not_Fire()
    {
        var card = BuildCard("Energizer");
        var state = BuildState(card);
        state.CurrentStep = TurnStep.Main;
        var die = new DieInstance { Id = "d", CardId = card.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.UsedPile, CurrentFaceIndex = 0 };
        state.Dice.Add(die);
        var queue = new AbilityQueue();

        EventBus.Fire(state, queue, new GameEvent(TriggerKind.TurnStepEntered, null, "p1", StepIds.Main));
        Assert.Empty(queue.Pending); // not even a candidate - never gets the chance to drain
    }

    // Rule text: "does not need to be active to trigger" - an already-
    // fielded die (Field Zone) showing double energy (e.g. spun there by
    // an effect) still fires at the same step boundary.
    [Fact]
    public void An_Already_Active_Die_On_Double_Energy_Also_Fires()
    {
        var card = BuildCard("Energizer");
        var state = BuildState(card);
        state.CurrentStep = TurnStep.Main;
        var die = new DieInstance { Id = "d", CardId = card.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.FieldZone, CurrentFaceIndex = 1 };
        state.Dice.Add(die);
        var queue = new AbilityQueue();

        EventBus.Fire(state, queue, new GameEvent(TriggerKind.TurnStepEntered, null, "p1", StepIds.Main));
        Drain(state, queue);

        Assert.True(Fired(state, card.Id));
    }

    // --- Part 30 (2026-09-01): the immediate check for a face change
    // OUTSIDE the Roll and Reroll Step. "Only check at the end of the
    // Step" is scoped to that one step; a reroll thrown by another
    // card's ability, or a die drawn-and-rolled by a Basic Action,
    // checks immediately per the rule's own default. ---

    // Storm "Queen"'s own shape: rerolling an opposing die that happens
    // to carry Energize, outside the Roll and Reroll Step entirely -
    // Reroll(Opposing CharacterDie), landing it on double energy.
    [Fact]
    public void An_Opposing_Reroll_Effect_Fires_Energize_Immediately_Outside_Roll_And_Reroll()
    {
        var card = BuildCard("Energizer");
        var state = BuildState(card);
        state.CurrentStep = TurnStep.Attack; // Storm's own trigger fires mid-Attack
        var die = new DieInstance { Id = "d", CardId = card.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.FieldZone, CurrentFaceIndex = 3 };
        state.Dice.Add(die);
        var queue = new AbilityQueue();

        var ctx = new EffectContext { State = state, Queue = queue, ControllerId = "p2", Trigger = TriggerKind.DieAttacks, Roller = new FixedRoller(0), Random = new Random(1) };
        EffectInterpreter.Execute(new Reroll(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Opposing)), ctx);
        Drain(state, queue);

        Assert.True(Fired(state, card.Id));
    }

    [Fact]
    public void A_Reroll_Landing_Off_Double_Energy_Does_Not_Fire()
    {
        var card = BuildCard("Energizer");
        var state = BuildState(card);
        state.CurrentStep = TurnStep.Attack;
        var die = new DieInstance { Id = "d", CardId = card.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.FieldZone, CurrentFaceIndex = 3 };
        state.Dice.Add(die);
        var queue = new AbilityQueue();

        var ctx = new EffectContext { State = state, Queue = queue, ControllerId = "p2", Trigger = TriggerKind.DieAttacks, Roller = new FixedRoller(2), Random = new Random(1) };
        EffectInterpreter.Execute(new Reroll(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Opposing)), ctx);
        Drain(state, queue);

        Assert.False(Fired(state, card.Id));
    }

    // Mutant Research Program's own shape: a die drawn straight from the
    // Bag and rolled by an ability, never touching TurnEngine.Roll at
    // all - the deferred TurnStepEntered(Main) check would never see
    // this (Main was entered long before this ability resolved, if it's
    // used mid-Main-Step), so only the immediate check can catch it.
    [Fact]
    public void A_Die_Drawn_And_Rolled_By_An_Ability_Fires_Energize_Immediately()
    {
        var card = BuildCard("Energizer");
        var state = BuildState(card);
        state.CurrentStep = TurnStep.Main;
        var die = new DieInstance { Id = "d", CardId = card.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.Bag, CurrentFaceIndex = null };
        state.Dice.Add(die);
        var queue = new AbilityQueue();

        var ctx = new EffectContext { State = state, Queue = queue, ControllerId = "p1", Trigger = TriggerKind.DieUsed, Roller = new FixedRoller(0), Random = new Random(1) };
        EffectInterpreter.Execute(new DrawToZone(1, Zone.ReservePool, Zone.Bag), ctx);
        Drain(state, queue);

        Assert.True(Fired(state, card.Id));
    }

    // RequireSelf (Part 30): an UNRELATED Energize-carrying die must not
    // react to a DIFFERENT die's reroll - the same shape as the Awaken
    // cross-fire bug this session also found and fixed.
    [Fact]
    public void An_Unrelated_Energize_Die_Does_Not_CrossFire_On_Another_Dies_Reroll()
    {
        var watcher = BuildCard("Watcher");
        var mover = BuildCard("Mover");
        var state = new GameState
        {
            Config = BuildTinyConfig(),
            CardCatalog = new Dictionary<string, CardDef> { [watcher.Id] = watcher, [mover.Id] = mover },
            PlayerOne = new Player { Id = "p1", Name = "One" },
            PlayerTwo = new Player { Id = "p2", Name = "Two" },
            ActivePlayerId = "p1",
            CurrentStep = TurnStep.Attack,
        };
        var watcherDie = new DieInstance { Id = "watcher-die", CardId = watcher.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.FieldZone, CurrentFaceIndex = 3 };
        var moverDie = new DieInstance { Id = "mover-die", CardId = mover.Id, OwnerId = "p2", ControllerId = "p2", Zone = Zone.FieldZone, CurrentFaceIndex = 3 };
        state.Dice.Add(watcherDie);
        state.Dice.Add(moverDie);
        var queue = new AbilityQueue();

        var ctx = new EffectContext { State = state, Queue = queue, ControllerId = "p1", Trigger = TriggerKind.DieAttacks, Roller = new FixedRoller(0), Random = new Random(1) };
        ctx.Bind("target", moverDie.Id); // only moverDie is targeted/rerolled - watcherDie never changes
        EffectInterpreter.Execute(new Reroll(new TargetFilter(Bound: "target")), ctx);
        Drain(state, queue);

        Assert.False(Fired(state, watcher.Id, "p1"));
        Assert.True(Fired(state, mover.Id, "p2"));
    }
}

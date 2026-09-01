using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Tests;

// V2_PLAN.md Phase 4 acceptance: (1) a card with "when another tagged die
// is fielded" fires through a REAL fielding action (ground rule 6 - not by
// invoking the ability directly), (2) three simultaneous triggers enqueue
// in v1's own ordering (active player first, then inactive, FIFO within
// each), (3) a self-only trigger doesn't fire for other dice.
public class EventBusTests
{
    private static readonly EffectNode StubEffect = new LifeChange(new Fixed(0));

    private static GameConfig BuildTinyConfig() => new(
        Id: "test", Name: "Test",
        EnergySymbols: [new SymbolDef("Fist")],
        Keywords: [],
        Rules: new RulesConfig(StartingLife: 10, DrawCount: 3, MaxTeamCards: 4, MaxTeamDice: 4, BasicActionCount: 0),
        BasicDicePool: [],
        BasicActionSlots: 0);

    private static CardDef BuildCard(string id, string name, IReadOnlyList<TriggeredAbility> abilities, IReadOnlyList<string>? affiliations = null) => new(
        Id: id,
        Name: name,
        Subtitle: null,
        Set: "TEST",
        CardType: CardType.Character,
        PurchaseCost: 1,
        EnergySymbolIds: ["Fist"],
        Die: new DieDefinition(id + "Die",
        [
            new Face([], Character: new CharacterFaceData(Level: 1, FieldingCost: 0, Attack: 1, Defense: 1)),
        ]),
        DieLimit: 1,
        Affiliations: affiliations ?? [],
        Keywords: [],
        RawText: "",
        Abilities: abilities,
        Continuous: []);

    private static GameState BuildState(GameConfig config, params CardDef[] cards)
    {
        return new GameState
        {
            Config = config,
            CardCatalog = cards.ToDictionary(c => c.Id),
            PlayerOne = new Player { Id = "p1", Name = "One" },
            PlayerTwo = new Player { Id = "p2", Name = "Two" },
            ActivePlayerId = "p1",
            CurrentStep = TurnStep.Main,
        };
    }

    private static DieInstance ActiveDie(GameState state, CardDef card, string ownerId, string? id = null)
    {
        var die = new DieInstance { Id = id ?? card.Id + "-die", CardId = card.Id, OwnerId = ownerId, ControllerId = ownerId, Zone = Zone.FieldZone, CurrentFaceIndex = 0 };
        state.Dice.Add(die);
        return die;
    }

    // --- (1) fires through a REAL fielding action ---

    [Fact]
    public void Ability_Watching_For_A_Tagged_DieFielded_Enqueues_When_That_Card_Is_Really_Fielded()
    {
        var watcher = BuildCard("Watcher", "Watcher",
            [new TriggeredAbility(TriggerKind.DieFielded, StubEffect, Filter: new EventFilter(Ownership: TargetOwnership.Own, Affiliations: new TagQuery(AnyOf: ["Affil"])))]);
        var trigger = BuildCard("Trigger", "Trigger", [], affiliations: ["Affil"]);

        var config = BuildTinyConfig();
        var state = BuildState(config, watcher, trigger);
        var watcherDie = ActiveDie(state, watcher, "p1");
        var queue = new AbilityQueue();

        // The card to be fielded, rolled and ready in the Reserve Pool -
        // exactly what TurnEngine.Field requires.
        var toField = new DieInstance { Id = "trigger-die", CardId = trigger.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.ReservePool, CurrentFaceIndex = 0 };
        state.Dice.Add(toField);

        TurnEngine.Field(state, queue, toField.Id, []); // the REAL action, not a direct EventBus.Fire call

        var pending = Assert.Single(queue.Pending);
        Assert.Equal(watcherDie.Id, pending.SourceDieId);
        Assert.Equal(TriggerKind.DieFielded, pending.Trigger);
    }

    // --- (2) ordering matches v1's: active player first, then inactive, FIFO within each ---

    [Fact]
    public void Simultaneous_Triggers_Enqueue_Active_Player_First_Then_Inactive_FIFO_Within_Each()
    {
        var watcher = BuildCard("Watcher", "Watcher",
            [new TriggeredAbility(TriggerKind.DieFielded, StubEffect, Filter: new EventFilter(Ownership: TargetOwnership.Any))]);
        var trigger = BuildCard("Trigger", "Trigger", []);

        var config = BuildTinyConfig();
        var state = BuildState(config, watcher, trigger);
        var queue = new AbilityQueue();

        // Deliberately inserted in p2-then-p1 order, to prove enqueue
        // order comes from "whose turn it is," not raw list order.
        var dieC = ActiveDie(state, watcher, "p2", "C");
        var dieA = ActiveDie(state, watcher, "p1", "A");
        var dieB = ActiveDie(state, watcher, "p1", "B");

        var toField = new DieInstance { Id = "trigger-die", CardId = trigger.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.ReservePool, CurrentFaceIndex = 0 };
        state.Dice.Add(toField);

        TurnEngine.Field(state, queue, toField.Id, []);

        // 4 total: A, B, C, and the fielded die's own null-filter self-check
        // doesn't match (Trigger has no DieFielded ability at all), so
        // exactly 3 are expected - A and B (p1, active) before C (p2).
        Assert.Equal(3, queue.Pending.Count);
        Assert.Equal(["A", "B", "C"], queue.Pending.Select(p => p.SourceDieId));
    }

    // --- (3) a self-only trigger doesn't fire for other dice ---

    [Fact]
    public void SelfOnly_Trigger_Does_Not_Fire_When_A_Different_Die_Is_Fielded()
    {
        var selfWatcher = BuildCard("SelfWatcher", "SelfWatcher",
            [new TriggeredAbility(TriggerKind.DieFielded, StubEffect)]); // Filter: null - self-only
        var other = BuildCard("Other", "Other", []);

        var config = BuildTinyConfig();
        var state = BuildState(config, selfWatcher, other);
        ActiveDie(state, selfWatcher, "p1");
        var queue = new AbilityQueue();

        var toField = new DieInstance { Id = "other-die", CardId = other.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.ReservePool, CurrentFaceIndex = 0 };
        state.Dice.Add(toField);

        TurnEngine.Field(state, queue, toField.Id, []);

        Assert.Empty(queue.Pending);
    }

    [Fact]
    public void SelfOnly_Trigger_Fires_When_Its_Own_Die_Is_The_One_Fielded()
    {
        var selfWatcher = BuildCard("SelfWatcher", "SelfWatcher",
            [new TriggeredAbility(TriggerKind.DieFielded, StubEffect)]);

        var config = BuildTinyConfig();
        var state = BuildState(config, selfWatcher);
        var queue = new AbilityQueue();

        var toField = new DieInstance { Id = "self-die", CardId = selfWatcher.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.ReservePool, CurrentFaceIndex = 0 };
        state.Dice.Add(toField);

        TurnEngine.Field(state, queue, toField.Id, []);

        var pending = Assert.Single(queue.Pending);
        Assert.Equal(toField.Id, pending.SourceDieId);
    }

    // --- DieFaceChanged emission (Part 1's "must be emitted from every
    // face-mutation site" mandate) ---

    [Fact]
    public void Roll_Emits_DieFaceChanged_For_A_Die_That_Was_Already_Showing_A_Face()
    {
        var watcher = BuildCard("Watcher", "Watcher",
            [new TriggeredAbility(TriggerKind.DieFaceChanged, StubEffect)]); // self-only

        var config = BuildTinyConfig();
        var state = BuildState(config, watcher);
        state.CurrentStep = TurnStep.RollAndReroll;
        // Deliberately NOT in Field/Attack Zone - proves the die can react
        // to its own face change purely as the event's subject, the same
        // way Energize/Awaken work on a die still mid-roll (see EventBus.
        // Fire's own remarks on why this needed a fix).
        var die = new DieInstance { Id = "watcher-die", CardId = watcher.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.PrepArea, CurrentFaceIndex = 0 };
        state.Dice.Add(die);
        var queue = new AbilityQueue();

        TurnEngine.Roll(state, queue, new FixedRoller(0));

        var pending = Assert.Single(queue.Pending);
        Assert.Equal(die.Id, pending.SourceDieId);
        Assert.Equal(TriggerKind.DieFaceChanged, pending.Trigger);
    }

    private sealed class FixedRoller(int index) : IDiceRoller
    {
        public int Roll(DieDefinition die) => index;
    }

    // --- UseGlobal: paid activation + once-per-turn (Phase 4 task 4) ---

    [Fact]
    public void UseGlobal_Spends_Energy_Enforces_OncePerTurn_And_Enqueues_The_Ability()
    {
        var card = BuildCard("Globey", "Globey",
            [new TriggeredAbility(TriggerKind.Global, StubEffect, EnergyCost: new EnergyCost(1, "Fist"), OncePerTurn: true)]);
        // BuildCard's die only has a character face (no energy symbols) -
        // a Fist-energy die needs its own definition to actually pay
        // Globey's cost, so this second card supplies one.
        var energyCard = BuildCard("Energy", "Energy", []) with
        {
            Die = new DieDefinition("EnergyDie", [new Face([new SymbolAmount("Fist", 1)])]),
        };

        var config = BuildTinyConfig();
        var state = BuildState(config, card, energyCard);
        var die = ActiveDie(state, card, "p1");
        var energyDie = new DieInstance { Id = "e1", CardId = "Energy", OwnerId = "p1", ControllerId = "p1", Zone = Zone.ReservePool, CurrentFaceIndex = 0 };
        state.Dice.Add(energyDie);
        var queue = new AbilityQueue();

        TurnEngine.UseGlobal(state, queue, card.Id, "p1", abilityIndex: 0, ["e1"]);

        Assert.Equal(Zone.OutOfPlay, energyDie.Zone); // energy spent
        Assert.Single(queue.Pending);
        Assert.Throws<InvalidOperationException>(() =>
            TurnEngine.UseGlobal(state, queue, card.Id, "p1", abilityIndex: 0, []));
    }
}

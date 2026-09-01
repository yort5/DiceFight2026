using DiceFight.V2;
using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Tests;

// V2_PLAN.md Phase 3 acceptance: (a) a modifier changes a stat and expires
// at Clean Up, (b) a purchase-cost modifier changes what Purchase charges,
// (c) an empty registry reproduces Phase 2 behavior unchanged.
public class QueryEngineTests
{
    // Minimal fixed-value stub implementations - real ones arrived with
    // Phase 6's continuous templates (ContinuousRegistry); these just
    // prove the registries work.
    private sealed class AlwaysAppliesDieModifier(int delta) : IDieStatModifier
    {
        public bool AppliesTo(GameState state, DieInstance die) => true;
        public int GetDelta(GameState state, DieInstance die) => delta;
    }

    private sealed class AlwaysAppliesCardModifier(int delta) : ICardCostModifier
    {
        public bool AppliesTo(GameState state, CardDef card, string payerId) => true;
        public int GetDelta(GameState state, CardDef card, string payerId) => delta;
    }

    private static CardDef BuildCharacterCard(int purchaseCost = 3) => new(
        Id: "T001",
        Name: "Test Character",
        Subtitle: null,
        Set: "TEST",
        CardType: CardType.Character,
        PurchaseCost: purchaseCost,
        EnergySymbolIds: ["Fist"],
        Die: new DieDefinition("T001Die",
        [
            new Face([], new CharacterFaceData(Level: 1, FieldingCost: 2, Attack: 3, Defense: 4), Kind: FaceKind.CharacterFace),
        ]),
        DieLimit: 1,
        Affiliations: [],
        Keywords: ["Overcrush"],
        RawText: "",
        Abilities: [],
        Continuous: []);

    private static GameState BuildMinimalState(CardDef card)
    {
        var config = new GameConfig(
            Id: "test", Name: "Test",
            EnergySymbols: [new SymbolDef("Fist")],
            Keywords: [],
            Rules: new RulesConfig(StartingLife: 10, DrawCount: 3, MaxTeamCards: 2, MaxTeamDice: 4, BasicActionCount: 0),
            BasicDicePool: [],
            BasicActionSlots: 0);

        return new GameState
        {
            Config = config,
            CardCatalog = new Dictionary<string, CardDef> { [card.Id] = card },
            PlayerOne = new Player { Id = "p1", Name = "One" },
            PlayerTwo = new Player { Id = "p2", Name = "Two" },
            ActivePlayerId = "p1",
        };
    }

    // --- (a) modifier changes a stat and expires at Clean Up ---

    [Fact]
    public void AppliedModifier_Changes_Attack_And_Expires_At_CleanUp_When_EndOfTurn()
    {
        var card = BuildCharacterCard();
        var state = BuildMinimalState(card);
        var die = new DieInstance { Id = "d1", CardId = card.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.FieldZone, CurrentFaceIndex = 0 };
        state.Dice.Add(die);

        Assert.Equal(3, QueryEngine.GetAttack(state, die)); // base, no modifier yet

        die.AppliedModifiers.Add(new AppliedModifier(AttackDelta: 2, DefenseDelta: 0, FieldingCostDelta: 0, Source: "test", Duration: Duration.EndOfTurn));
        Assert.Equal(5, QueryEngine.GetAttack(state, die));

        state.CurrentStep = TurnStep.Attack; // CleanUp requires the Attack step
        TurnEngine.CleanUp(state, new AbilityQueue());

        Assert.Equal(3, QueryEngine.GetAttack(state, die)); // expired
    }

    [Fact]
    public void AppliedModifier_With_Permanent_Duration_Survives_CleanUp()
    {
        var card = BuildCharacterCard();
        var state = BuildMinimalState(card);
        var die = new DieInstance { Id = "d1", CardId = card.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.FieldZone, CurrentFaceIndex = 0 };
        state.Dice.Add(die);
        die.AppliedModifiers.Add(new AppliedModifier(AttackDelta: 1, DefenseDelta: 0, FieldingCostDelta: 0, Source: "test", Duration: Duration.Permanent));

        state.CurrentStep = TurnStep.Attack;
        TurnEngine.CleanUp(state, new AbilityQueue());

        Assert.Equal(4, QueryEngine.GetAttack(state, die)); // still applied
    }

    [Fact]
    public void AppliedModifier_With_UntilYourNextTurn_Expires_When_Control_Returns_To_The_Granter()
    {
        var card = BuildCharacterCard();
        var state = BuildMinimalState(card);
        var die = new DieInstance { Id = "d1", CardId = card.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.FieldZone, CurrentFaceIndex = 0 };
        state.Dice.Add(die);
        // Granted during p1's own turn (ActivePlayerId is p1 here).
        die.AppliedModifiers.Add(new AppliedModifier(AttackDelta: 1, DefenseDelta: 0, FieldingCostDelta: 0, Source: "test", Duration: Duration.UntilYourNextTurn, GrantedDuringPlayerId: "p1"));

        // Clean Up ending p1's OWN turn - must survive (it needs to last
        // through the opponent's whole turn first).
        state.CurrentStep = TurnStep.Attack;
        TurnEngine.CleanUp(state, new AbilityQueue());
        Assert.Equal("p2", state.ActivePlayerId);
        Assert.Equal(4, QueryEngine.GetAttack(state, die));

        // Clean Up ending p2's turn (handing control back to p1, the
        // granter) - this is "the start of your next turn," so it expires.
        state.CurrentStep = TurnStep.Attack;
        TurnEngine.CleanUp(state, new AbilityQueue());
        Assert.Equal("p1", state.ActivePlayerId);
        Assert.Equal(3, QueryEngine.GetAttack(state, die));
    }

    [Fact]
    public void Registered_Continuous_AttackModifier_Applies_Alongside_Per_Die_AppliedModifiers()
    {
        var card = BuildCharacterCard();
        var state = BuildMinimalState(card);
        var die = new DieInstance { Id = "d1", CardId = card.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.FieldZone, CurrentFaceIndex = 0 };
        state.Dice.Add(die);
        die.AppliedModifiers.Add(new AppliedModifier(AttackDelta: 1, DefenseDelta: 0, FieldingCostDelta: 0, Source: "test", Duration: Duration.Permanent));

        state.AttackModifiers.Add(new AlwaysAppliesDieModifier(delta: 2));

        Assert.Equal(3 + 1 + 2, QueryEngine.GetAttack(state, die)); // base + per-die + continuous
    }

    // --- (b) a purchase-cost modifier changes what Purchase charges ---

    [Fact]
    public void Registered_PurchaseCostModifier_Changes_The_Effective_Cost()
    {
        var card = BuildCharacterCard(purchaseCost: 3);
        var state = BuildMinimalState(card);

        Assert.Equal(3, QueryEngine.GetPurchaseCost(state, card, "p1"));

        state.PurchaseCostModifiers.Add(new AlwaysAppliesCardModifier(delta: -1));

        Assert.Equal(2, QueryEngine.GetPurchaseCost(state, card, "p1"));
    }

    [Fact]
    public void Purchase_Charges_The_Discounted_Cost_Not_The_Printed_One()
    {
        var card = BuildCharacterCard(purchaseCost: 3);
        // A pool die with a 2-Fist-energy face, so exactly one energy die
        // covers the discounted cost of 2 but would have been short
        // against the printed cost of 3 - proving Purchase actually reads
        // the query, not the raw CardDef.PurchaseCost field.
        var pool = new DieDefinition("Energy2", [new Face([new SymbolAmount("Fist", 2)])]);
        var config = new GameConfig(
            Id: "test", Name: "Test",
            EnergySymbols: [new SymbolDef("Fist")],
            Keywords: [],
            Rules: new RulesConfig(StartingLife: 10, DrawCount: 3, MaxTeamCards: 2, MaxTeamDice: 4, BasicActionCount: 0),
            BasicDicePool: [new BasicDicePoolEntry(pool, 1)],
            BasicActionSlots: 0);

        var state = new GameState
        {
            Config = config,
            CardCatalog = new Dictionary<string, CardDef> { [card.Id] = card },
            PlayerOne = new Player { Id = "p1", Name = "One" },
            PlayerTwo = new Player { Id = "p2", Name = "Two" },
            ActivePlayerId = "p1",
            CurrentStep = TurnStep.Main,
        };
        state.PurchaseCostModifiers.Add(new AlwaysAppliesCardModifier(delta: -1)); // effective cost: 2

        var energyDie = new DieInstance { Id = "e1", PoolDieId = pool.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.ReservePool, CurrentFaceIndex = 0 };
        var toBuy = new DieInstance { Id = "unpurchased", CardId = card.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.Unpurchased };
        state.Dice.Add(energyDie);
        state.Dice.Add(toBuy);

        TurnEngine.Purchase(state, new AbilityQueue(), "unpurchased", ["e1"]);

        Assert.Equal(Zone.UsedPile, toBuy.Zone);
        Assert.Equal("p1", toBuy.ControllerId);
    }

    // Rule 2.6.2.3 + the Crossover glossary entry - a card with two
    // energy types needs at least one of EACH, whatever else is spent.
    // (Some real Crossover characters require all four.)
    [Fact]
    public void Purchasing_A_Crossover_Card_Requires_One_Of_Each_Of_Its_Energy_Types()
    {
        var crossover = BuildCharacterCard(purchaseCost: 3) with { EnergySymbolIds = ["Bolt", "Fist"] };
        var boltDie = new DieDefinition("BoltDie", [new Face([new SymbolAmount("Bolt", 1)])]);
        var fistDie = new DieDefinition("FistDie", [new Face([new SymbolAmount("Fist", 1)])]);
        var config = new GameConfig(
            Id: "test", Name: "Test",
            EnergySymbols: [new SymbolDef("Bolt"), new SymbolDef("Fist")],
            Keywords: [],
            Rules: new RulesConfig(StartingLife: 10, DrawCount: 3, MaxTeamCards: 2, MaxTeamDice: 4, BasicActionCount: 0),
            BasicDicePool: [new BasicDicePoolEntry(boltDie, 3), new BasicDicePoolEntry(fistDie, 3)],
            BasicActionSlots: 0);

        GameState Fresh()
        {
            var s = new GameState
            {
                Config = config,
                CardCatalog = new Dictionary<string, CardDef> { [crossover.Id] = crossover },
                PlayerOne = new Player { Id = "p1", Name = "One" },
                PlayerTwo = new Player { Id = "p2", Name = "Two" },
                ActivePlayerId = "p1",
                CurrentStep = TurnStep.Main,
            };
            s.Dice.Add(new DieInstance { Id = "unpurchased", CardId = crossover.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.Unpurchased });
            return s;
        }

        // Three Bolt covers the AMOUNT but has no Fist - rejected.
        var boltOnly = Fresh();
        for (var i = 0; i < 3; i++)
            boltOnly.Dice.Add(new DieInstance { Id = $"b{i}", PoolDieId = "BoltDie", OwnerId = "p1", ControllerId = "p1", Zone = Zone.ReservePool, CurrentFaceIndex = 0 });
        Assert.Throws<InvalidOperationException>(() =>
            TurnEngine.Purchase(boltOnly, new AbilityQueue(), "unpurchased", ["b0", "b1", "b2"]));

        // Two Bolt + one Fist covers the amount AND both types.
        var mixed = Fresh();
        for (var i = 0; i < 2; i++)
            mixed.Dice.Add(new DieInstance { Id = $"b{i}", PoolDieId = "BoltDie", OwnerId = "p1", ControllerId = "p1", Zone = Zone.ReservePool, CurrentFaceIndex = 0 });
        mixed.Dice.Add(new DieInstance { Id = "f0", PoolDieId = "FistDie", OwnerId = "p1", ControllerId = "p1", Zone = Zone.ReservePool, CurrentFaceIndex = 0 });
        TurnEngine.Purchase(mixed, new AbilityQueue(), "unpurchased", ["b0", "b1", "f0"]);
        Assert.Equal(Zone.UsedPile, mixed.Dice.Single(d => d.Id == "unpurchased").Zone);
    }

    // --- (c) an empty registry reproduces Phase 2 behavior unchanged ---

    [Fact]
    public void Empty_Registries_Reproduce_Base_Values_Unchanged()
    {
        var card = BuildCharacterCard();
        var state = BuildMinimalState(card);
        var die = new DieInstance { Id = "d1", CardId = card.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.FieldZone, CurrentFaceIndex = 0 };
        state.Dice.Add(die);

        Assert.Equal(3, QueryEngine.GetAttack(state, die));
        Assert.Equal(4, QueryEngine.GetDefense(state, die));
        Assert.Equal(2, QueryEngine.GetFieldingCost(state, die));
        Assert.Equal(3, QueryEngine.GetPurchaseCost(state, card, "p1"));
    }

    [Fact]
    public void GetKeywords_Returns_The_Printed_Set()
    {
        var card = BuildCharacterCard();
        var state = BuildMinimalState(card);
        var die = new DieInstance { Id = "d1", CardId = card.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.FieldZone, CurrentFaceIndex = 0 };
        state.Dice.Add(die);

        Assert.Equal(new HashSet<string> { "Overcrush" }, QueryEngine.GetKeywords(state, die));
    }

    [Fact]
    public void CanBeTargeted_Is_True_By_Default_And_False_When_An_Interceptor_Vetoes()
    {
        var card = BuildCharacterCard();
        var state = BuildMinimalState(card);
        var die = new DieInstance { Id = "d1", CardId = card.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.FieldZone, CurrentFaceIndex = 0 };
        state.Dice.Add(die);

        Assert.True(QueryEngine.CanBeTargeted(state, die, "p2", ProtectionFrom.Global));

        state.TargetingInterceptors.Add(new VetoingInterceptor());

        Assert.False(QueryEngine.CanBeTargeted(state, die, "p2", ProtectionFrom.Global));
    }

    private sealed class VetoingInterceptor : ITargetingInterceptor
    {
        public bool CanBeTargeted(GameState state, DieInstance die, string byPlayerId, ProtectionFrom triggerKind) => false;
    }

    // --- Affiliation is first-class, not a tag (V2_VOCABULARY_HISTORY.md Parts 17-21) ---

    // The whole point of the split, in one test: a card whose AFFILIATION
    // and whose KEYWORD happen to share a name are two different questions,
    // and asking one must not answer the other. Before the split both
    // landed in the same string set and "target an X-Men die" and "target
    // an Overcrush die" were indistinguishable in kind - which is exactly
    // the distinction blanking needs, since a blanked die keeps its
    // affiliation and loses its keyword.
    private static CardDef AffiliatedCard(string id, IReadOnlyList<string> affiliations, IReadOnlyList<string> keywords) => new(
        Id: id, Name: $"Card {id}", Subtitle: null, Set: "TEST", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Fist"],
        Die: new DieDefinition($"{id}Die", [new Face([], new CharacterFaceData(1, 2, 3, 4), Kind: FaceKind.CharacterFace)]),
        DieLimit: 1, Affiliations: affiliations, Keywords: keywords,
        RawText: "", Abilities: [], Continuous: []);

    private static (GameState State, DieInstance Die) StateWith(CardDef card)
    {
        var state = BuildMinimalState(card);
        var die = new DieInstance { Id = "d1", CardId = card.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.FieldZone, CurrentFaceIndex = 0 };
        state.Dice.Add(die);
        return (state, die);
    }

    [Fact]
    public void GetTags_No_Longer_Contains_Affiliations()
    {
        var card = AffiliatedCard("A1", ["X-Men"], ["Overcrush"]);
        var (state, die) = StateWith(card);

        var tags = QueryEngine.GetTags(state, die);

        Assert.DoesNotContain("X-Men", tags);
        Assert.Contains("Overcrush", tags);   // keywords ARE abilities and stay
        Assert.Contains(card.Name, tags);     // so does the card name
    }

    [Fact]
    public void GetAffiliations_Returns_The_Printed_Affiliations()
    {
        var card = AffiliatedCard("A1", ["X-Men", "Brotherhood of Mutants"], []);
        var (state, die) = StateWith(card);

        var affiliations = QueryEngine.GetAffiliations(state, die);

        Assert.Equal(["Brotherhood of Mutants", "X-Men"], affiliations.OrderBy(a => a));
    }

    // A die with no card at all - a Sidekick - has no affiliations and
    // must not throw asking.
    [Fact]
    public void GetAffiliations_Of_A_Cardless_Die_Is_Empty()
    {
        var card = AffiliatedCard("A1", ["X-Men"], []);
        var state = BuildMinimalState(card);
        // IsSidekick is derived from CardId being null, not settable.
        var sidekick = new DieInstance { Id = "sk", CardId = null, OwnerId = "p1", ControllerId = "p1", Zone = Zone.FieldZone };
        state.Dice.Add(sidekick);

        Assert.Empty(QueryEngine.GetAffiliations(state, sidekick));
    }

    [Fact]
    public void An_Affiliation_Filter_Does_Not_Match_A_Same_Named_Keyword()
    {
        // One die is X-Men by affiliation; the other has "X-Men" as a
        // KEYWORD. Nothing stops a real catalog from doing this, and the
        // two must not be confusable.
        var affiliated = AffiliatedCard("A1", ["X-Men"], []);
        var keyworded = AffiliatedCard("A2", [], ["X-Men"]);
        var state = BuildMinimalState(affiliated);
        ((Dictionary<string, CardDef>)state.CardCatalog)[keyworded.Id] = keyworded;
        var byAffiliation = new DieInstance { Id = "d1", CardId = affiliated.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.FieldZone, CurrentFaceIndex = 0 };
        var byKeyword = new DieInstance { Id = "d2", CardId = keyworded.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.FieldZone, CurrentFaceIndex = 0 };
        state.Dice.Add(byAffiliation);
        state.Dice.Add(byKeyword);

        var affiliationMatch = TargetResolver.Query(state, "p1",
            new TargetFilter(Kind: TargetKind.CharacterDie, Count: 2, Affiliations: new TagQuery(AnyOf: ["X-Men"])),
            new Dictionary<string, string>());
        var tagMatch = TargetResolver.Query(state, "p1",
            new TargetFilter(Kind: TargetKind.CharacterDie, Count: 2, Tags: new TagQuery(AnyOf: ["X-Men"])),
            new Dictionary<string, string>());

        Assert.Equal(["d1"], affiliationMatch);
        Assert.Equal(["d2"], tagMatch);
    }
}

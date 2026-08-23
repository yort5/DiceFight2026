using DiceFight.V2;
using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Tests;

// V2_PLAN.md Phase 3 acceptance: (a) a modifier changes a stat and expires
// at Clean Up, (b) a purchase-cost modifier changes what Purchase charges,
// (c) an empty registry reproduces Phase 2 behavior unchanged.
public class QueryEngineTests
{
    // Minimal fixed-value stub implementations - real ones arrive with
    // Phase 6's continuous templates; these just prove the registries work.
    private sealed class AlwaysAppliesDieModifier(int delta) : IDieStatModifier
    {
        public bool AppliesTo(GameState state, DieInstance die) => true;
        public int Delta => delta;
    }

    private sealed class AlwaysAppliesCardModifier(int delta) : ICardCostModifier
    {
        public bool AppliesTo(GameState state, CardDef card, string payerId) => true;
        public int Delta => delta;
    }

    private static CardDef BuildCharacterCard(int purchaseCost = 3) => new(
        Id: "T001",
        Name: "Test Character",
        Subtitle: null,
        Set: "TEST",
        CardType: CardType.Character,
        PurchaseCost: purchaseCost,
        EnergySymbolId: "Fist",
        Die: new DieDefinition("T001Die",
        [
            new Face([], Character: new CharacterFaceData(Level: 1, FieldingCost: 2, Attack: 3, Defense: 4)),
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
}

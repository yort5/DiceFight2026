using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Tests;

// V2_PLAN.md Phase 6 acceptance: a test per continuous template,
// including an aura appearing/disappearing as its source die enters/
// leaves the field, two auras stacking additively, and the Phase 0 paper
// expressions for the 5 ex-Grants* cards (V2_VOCABULARY.md Part 2,
// Bucket C) running as real card definitions.
public class ContinuousRegistryTests
{
    private static readonly Face Level1Char = new([], new CharacterFaceData(Level: 1, FieldingCost: 2, Attack: 2, Defense: 2));
    private static readonly Face SidekickChar = new([], new CharacterFaceData(Level: 1, FieldingCost: 0, Attack: 1, Defense: 1));

    private static GameConfig BuildConfig() => new(
        Id: "test", Name: "Test",
        EnergySymbols: [new SymbolDef("Fist")],
        Keywords: [],
        Rules: new RulesConfig(StartingLife: 10, DrawCount: 3, MaxTeamCards: 4, MaxTeamDice: 4, BasicActionCount: 0),
        BasicDicePool: [],
        BasicActionSlots: 0);

    private static CardDef BuildCard(string id, IReadOnlyList<Face> faces, IReadOnlyList<ContinuousDef>? continuous = null, IReadOnlyList<string>? affiliations = null, int purchaseCost = 1) => new(
        Id: id, Name: id, Subtitle: null, Set: "TEST", CardType: CardType.Character,
        PurchaseCost: purchaseCost, EnergySymbolIds: ["Fist"],
        Die: new DieDefinition(id + "Die", faces),
        DieLimit: 1, Affiliations: affiliations ?? [], Keywords: [], RawText: "",
        Abilities: [], Continuous: continuous ?? []);

    private static GameState BuildState(params CardDef[] cards)
    {
        var state = new GameState
        {
            Config = BuildConfig(),
            CardCatalog = cards.ToDictionary(c => c.Id),
            PlayerOne = new Player { Id = "p1", Name = "One", Life = 10 },
            PlayerTwo = new Player { Id = "p2", Name = "Two", Life = 10 },
            ActivePlayerId = "p1",
            CurrentStep = TurnStep.Attack,
        };
        ContinuousRegistry.RegisterAll(state);
        return state;
    }

    private static DieInstance AddDie(GameState state, CardDef card, string controllerId, Zone zone, int? faceIndex, string? id = null)
    {
        var die = new DieInstance { Id = id ?? card.Id + "-die", CardId = card.Id, OwnerId = controllerId, ControllerId = controllerId, Zone = zone, CurrentFaceIndex = faceIndex };
        state.Dice.Add(die);
        return die;
    }

    // --- StatAura ---

    [Fact]
    public void StatAura_Applies_While_The_Source_Is_Active_And_Stops_When_It_Leaves()
    {
        var source = BuildCard("Source", [Level1Char], [new StatAura(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own), AtkDelta: new Fixed(1))]);
        var target = BuildCard("Target", [Level1Char]);
        var state = BuildState(source, target);
        var sourceDie = AddDie(state, source, "p1", Zone.FieldZone, 0);
        var targetDie = AddDie(state, target, "p1", Zone.FieldZone, 0);

        Assert.Equal(3, QueryEngine.GetAttack(state, targetDie));

        sourceDie.Zone = Zone.UsedPile;
        Assert.Equal(2, QueryEngine.GetAttack(state, targetDie));
    }

    [Fact]
    public void StatAura_From_Two_Different_Sources_Stacks_Additively()
    {
        var sourceA = BuildCard("SourceA", [Level1Char], [new StatAura(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own), AtkDelta: new Fixed(1))]);
        var sourceB = BuildCard("SourceB", [Level1Char], [new StatAura(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own), AtkDelta: new Fixed(2))]);
        var target = BuildCard("Target", [Level1Char]);
        var state = BuildState(sourceA, sourceB, target);
        AddDie(state, sourceA, "p1", Zone.FieldZone, 0);
        AddDie(state, sourceB, "p1", Zone.FieldZone, 0);
        var targetDie = AddDie(state, target, "p1", Zone.FieldZone, 0);

        Assert.Equal(2 + 1 + 2, QueryEngine.GetAttack(state, targetDie));
    }

    // --- CostModifier ---

    [Fact]
    public void CostModifier_Discounts_The_Matching_Purchase_Cost()
    {
        var source = BuildCard("Source", [Level1Char], [new CostModifier(CostKind.Purchase, new TargetFilter(Kind: TargetKind.Player, Ownership: TargetOwnership.Own), Delta: -1)]);
        var target = BuildCard("Target", [Level1Char], purchaseCost: 3);
        var state = BuildState(source, target);
        AddDie(state, source, "p1", Zone.FieldZone, 0);

        Assert.Equal(2, QueryEngine.GetPurchaseCost(state, target, "p1"));
        Assert.Equal(3, QueryEngine.GetPurchaseCost(state, target, "p2")); // not the granter's own controller
    }

    // --- PermanentContinuous (V2_VOCABULARY.md Part 21) ---

    // The continuous half of the permanent/blankable split. Nothing
    // blanks it yet, so what this pins is that ContinuousOf reads the
    // permanent list at all - a permanent aura that never registers would
    // be silently inert, and every one of the 34 immune clauses is
    // exactly this shape (King Black Bolt's purchase restriction,
    // Strahd's "doesn't count as an Adventurer").
    [Fact]
    public void A_PermanentContinuous_Aura_Registers_Like_An_Ordinary_One()
    {
        var source = BuildCard("Source", [Level1Char]) with
        {
            PermanentContinuous = [new TagAura(
                new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Own, Tags: new TagQuery(AnyOf: ["sidekick"])),
                ["Swarm"])],
        };
        var state = BuildState(source);
        AddDie(state, source, "p1", Zone.FieldZone, 0);
        var sidekick = new DieInstance { Id = "sk", OwnerId = "p1", ControllerId = "p1", Zone = Zone.FieldZone, CurrentFaceIndex = null };
        state.Dice.Add(sidekick);

        Assert.Contains("Swarm", QueryEngine.GetTags(state, sidekick));
    }

    // --- TagAura ---

    [Fact]
    public void TagAura_Grants_A_Tag_While_The_Source_Is_Active()
    {
        var source = BuildCard("Source", [Level1Char], [new TagAura(new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Own, Tags: new TagQuery(AnyOf: ["sidekick"])), ["Swarm"])]);
        var state = BuildState(source);
        AddDie(state, source, "p1", Zone.FieldZone, 0);
        var sidekick = new DieInstance { Id = "sk", OwnerId = "p1", ControllerId = "p1", Zone = Zone.FieldZone, CurrentFaceIndex = null };
        state.Dice.Add(sidekick);

        Assert.Contains("Swarm", QueryEngine.GetTags(state, sidekick));
        Assert.Contains("Swarm", QueryEngine.GetKeywords(state, sidekick));
    }

    // --- CombatRule (no consumer yet - just proves registration + AppliesTo) ---

    [Fact]
    public void CombatRule_Registers_And_Applies_To_The_Matching_Die()
    {
        var source = BuildCard("Source", [Level1Char], [new CombatRule(CombatRuleKind.BlocksN, new TargetFilter(Self: true), N: 2)]);
        var state = BuildState(source);
        var die = AddDie(state, source, "p1", Zone.FieldZone, 0);

        var rule = Assert.Single(state.CombatRules);
        Assert.Equal(CombatRuleKind.BlocksN, rule.Kind);
        Assert.Equal(2, rule.N);
        Assert.True(rule.AppliesTo(state, die));
    }

    // --- DamageModifier ---

    [Fact]
    public void DamageModifier_Reduce_Lowers_Damage_Actually_Marked()
    {
        var source = BuildCard("Source", [Level1Char], [new DamageModifier(DamageModifierMode.Reduce, new TargetFilter(Self: true), Amount: 1)]);
        var state = BuildState(source);
        var die = AddDie(state, source, "p1", Zone.FieldZone, 0); // 2 Defense

        var ctx = new EffectContext { State = state, Queue = new AbilityQueue(), ControllerId = "p2", Trigger = TriggerKind.Global, Roller = new StubRoller(), Random = new Random(1) };
        EffectInterpreter.Execute(new DealDamage(new Fixed(2), new TargetFilter(Ownership: TargetOwnership.Opposing)), ctx);

        Assert.Equal(1, die.Damage); // 2 - 1 = 1, below the 2-Defense KO threshold
        Assert.Equal(Zone.FieldZone, die.Zone); // still alive
    }

    [Fact]
    public void DamageModifier_Multipliers_Apply_Before_Flat_Reductions()
    {
        // Double (x2) then Reduce(1): 1 damage -> 2 (doubled) -> 1 (reduced).
        // If reduction applied first instead, 1 -> 0 -> 0, a different result -
        // this proves the fixed ordering rule (V2_VOCABULARY.md Part 1/11).
        var source = BuildCard("Source", [Level1Char], [
            new DamageModifier(DamageModifierMode.Double, new TargetFilter(Self: true)),
            new DamageModifier(DamageModifierMode.Reduce, new TargetFilter(Self: true), Amount: 1),
        ]);
        var state = BuildState(source);
        var die = AddDie(state, source, "p1", Zone.FieldZone, 0);

        var ctx = new EffectContext { State = state, Queue = new AbilityQueue(), ControllerId = "p2", Trigger = TriggerKind.Global, Roller = new StubRoller(), Random = new Random(1) };
        EffectInterpreter.Execute(new DealDamage(new Fixed(1), new TargetFilter(Ownership: TargetOwnership.Opposing)), ctx);

        Assert.Equal(1, die.Damage);
    }

    private sealed class StubRoller : IDiceRoller
    {
        public int Roll(DieDefinition die) => 0;
    }

    // --- TargetingProtection ---

    [Fact]
    public void TargetingProtection_Blocks_The_Opponent_But_Not_The_Granters_Own_Controller()
    {
        var source = BuildCard("Source", [Level1Char], [new TargetingProtection(new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Own), ProtectionFrom.Global)]);
        var state = BuildState(source);
        var die = AddDie(state, source, "p1", Zone.FieldZone, 0);

        Assert.False(QueryEngine.CanBeTargeted(state, die, "p2", ProtectionFrom.Global));
        Assert.True(QueryEngine.CanBeTargeted(state, die, "p1", ProtectionFrom.Global));
        Assert.True(QueryEngine.CanBeTargeted(state, die, "p2", ProtectionFrom.Action)); // different trigger kind
    }

    // --- Phase 0 paper examples (V2_VOCABULARY.md Part 2, Bucket C) ---

    [Fact]
    public void Paper_Example_CaptainMarvel_AlphaFlight_Grants_AtkAndDef_To_Own_Character_Dice()
    {
        var captainMarvel = BuildCard("CaptainMarvel", [Level1Char], [
            new StatAura(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own), AtkDelta: new Fixed(1), DefDelta: new Fixed(1)),
        ]);
        var ally = BuildCard("Ally", [Level1Char]);
        var state = BuildState(captainMarvel, ally);
        AddDie(state, captainMarvel, "p1", Zone.FieldZone, 0);
        var allyDie = AddDie(state, ally, "p1", Zone.FieldZone, 0);

        Assert.Equal(3, QueryEngine.GetAttack(state, allyDie));
        Assert.Equal(3, QueryEngine.GetDefense(state, allyDie));
    }

    [Fact]
    public void Paper_Example_Darkseid_ForceOfEntropy_Grants_Swarm_To_Own_Sidekicks()
    {
        var darkseid = BuildCard("Darkseid", [Level1Char], [
            new TagAura(new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Own, Tags: new TagQuery(AnyOf: ["sidekick"])), ["Swarm"]),
        ]);
        var state = BuildState(darkseid);
        AddDie(state, darkseid, "p1", Zone.FieldZone, 0);
        var sidekick = new DieInstance { Id = "sk", OwnerId = "p1", ControllerId = "p1", Zone = Zone.FieldZone };
        state.Dice.Add(sidekick);

        Assert.Contains("Swarm", QueryEngine.GetKeywords(state, sidekick));
    }

    [Fact]
    public void Paper_Example_Deadpool_CollectThis_Fields_Cheap_Character_Dice_Free()
    {
        var deadpool = BuildCard("Deadpool", [Level1Char], [
            new CostModifier(CostKind.Fielding, new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own, Stat: new StatThreshold(StatKind.FieldingCost, Max: 2)), Delta: -2),
        ]);
        var state = BuildState(deadpool);
        AddDie(state, deadpool, "p1", Zone.FieldZone, 0);
        var cheapDie = AddDie(state, deadpool, "p1", Zone.FieldZone, 0, "other"); // reuse the same 2-cost card as the "target"

        Assert.Equal(0, QueryEngine.GetFieldingCost(state, cheapDie)); // 2 - 2 = 0
    }

    [Fact]
    public void Paper_Example_JeanGrey_XaviersDream_Taxes_Opponents_Globals_Only_While_A_Sidekick_Is_Active()
    {
        var jeanGrey = BuildCard("JeanGrey", [Level1Char], [
            new CostModifier(CostKind.GlobalEnergy, new TargetFilter(Kind: TargetKind.Player, Ownership: TargetOwnership.Opposing), Delta: 1,
                ActiveWhen: new CountAtLeast(new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Own, Tags: new TagQuery(AnyOf: ["sidekick"])), 1)),
        ]);
        var opposingCard = BuildCard("Opposing", [Level1Char]);
        var state = BuildState(jeanGrey, opposingCard);
        AddDie(state, jeanGrey, "p1", Zone.FieldZone, 0);
        var ability = new TriggeredAbility(TriggerKind.Global, new LifeChange(new Fixed(0)), EnergyCost: new EnergyCost(1));

        // No own Sidekick active yet - ActiveWhen fails, no tax.
        Assert.Equal(1, QueryEngine.GetGlobalEnergyCost(state, opposingCard, ability, "p2"));

        var sidekick = new DieInstance { Id = "sk", OwnerId = "p1", ControllerId = "p1", Zone = Zone.FieldZone };
        state.Dice.Add(sidekick);

        Assert.Equal(2, QueryEngine.GetGlobalEnergyCost(state, opposingCard, ability, "p2"));
    }

    [Fact]
    public void Paper_Example_Moira_IfItsReal_Continuous_Half_Grants_Defense_While_Wolverine_Is_Active()
    {
        var moira = BuildCard("Moira", [Level1Char], [
            new StatAura(new TargetFilter(Self: true), DefDelta: new Fixed(1),
                ActiveWhen: new CountAtLeast(new TargetFilter(Kind: TargetKind.AnyDie, Tags: new TagQuery(AnyOf: ["Wolverine"])), 1)),
        ]);
        var wolverine = BuildCard("Wolverine", [Level1Char]);
        var state = BuildState(moira, wolverine);
        var moiraDie = AddDie(state, moira, "p1", Zone.FieldZone, 0);

        Assert.Equal(2, QueryEngine.GetDefense(state, moiraDie)); // Wolverine not active yet

        AddDie(state, wolverine, "p1", Zone.FieldZone, 0);
        Assert.Equal(3, QueryEngine.GetDefense(state, moiraDie));
    }
}

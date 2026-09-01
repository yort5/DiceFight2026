using DiceFight.V2;
using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Tests;

// Spike A (V2_VOCABULARY_HISTORY.md Parts 19-21, signed off 2026-09-01).
//
// What blanking removes is a narrower cut than "the die does nothing",
// and the narrowness is the whole design: a blanked die keeps every
// printed ATTRIBUTE (rules 1.2's closed list), keeps its face stats,
// keeps anything GRANTED to it (rule 3.4.8.2's own reasoning - a granted
// ability does not come from the blanked card), and keeps its card's
// PERMANENT text. It loses its keywords, its triggered abilities, its
// Globals and its continuous auras.
public class BlankingTests
{
    private static readonly Face Level1Char = new([], Character: new CharacterFaceData(1, 1, 4, 3));

    private static CardDef BuildCard(
        string id,
        IReadOnlyList<string>? affiliations = null,
        IReadOnlyList<string>? keywords = null,
        IReadOnlyList<TriggeredAbility>? abilities = null,
        IReadOnlyList<ContinuousDef>? continuous = null,
        IReadOnlyList<TriggeredAbility>? permanentAbilities = null) => new(
        Id: id, Name: $"Card {id}", Subtitle: null, Set: "TEST", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Fist"],
        Die: new DieDefinition($"{id}Die", [Level1Char]),
        DieLimit: 4, Affiliations: affiliations ?? [], Keywords: keywords ?? [],
        RawText: "", Abilities: abilities ?? [], Continuous: continuous ?? [],
        PermanentAbilities: permanentAbilities);

    private static GameState BuildState(params CardDef[] cards) => new()
    {
        Config = new GameConfig(
            Id: "test", Name: "Test",
            EnergySymbols: [new SymbolDef("Fist")], Keywords: [],
            Rules: new RulesConfig(StartingLife: 20, DrawCount: 4, MaxTeamCards: 8, MaxTeamDice: 20, BasicActionCount: 0),
            BasicDicePool: [], BasicActionSlots: 0),
        CardCatalog = cards.ToDictionary(c => c.Id),
        PlayerOne = new Player { Id = "p1", Name = "One" },
        PlayerTwo = new Player { Id = "p2", Name = "Two" },
        ActivePlayerId = "p1",
        CurrentStep = TurnStep.Main,
    };

    private static DieInstance AddDie(GameState state, CardDef card, string controllerId, string id)
    {
        var die = new DieInstance
        {
            Id = id, CardId = card.Id, OwnerId = controllerId, ControllerId = controllerId,
            Zone = Zone.FieldZone, CurrentFaceIndex = 0,
        };
        state.Dice.Add(die);
        return die;
    }

    // --- What a blanked die LOSES ---

    [Fact]
    public void A_Blanked_Die_Loses_Its_Keywords_And_Its_Abilities()
    {
        var ability = new TriggeredAbility(TriggerKind.DieFielded, new LifeChange(new Fixed(0)));
        var card = BuildCard("C1", affiliations: ["X-Men"], keywords: ["Overcrush"], abilities: [ability]);
        var state = BuildState(card);
        var die = AddDie(state, card, "p1", "d1");

        Assert.Contains("Overcrush", QueryEngine.GetTags(state, die));
        Assert.Contains(ability, QueryEngine.AbilitiesOf(state, die));

        die.Suppressions.Add(new DieSuppression(Duration.EndOfTurn));

        Assert.False(QueryEngine.AbilitiesActive(state, die));
        Assert.DoesNotContain("Overcrush", QueryEngine.GetTags(state, die));
        Assert.DoesNotContain(ability, QueryEngine.AbilitiesOf(state, die));
    }

    // --- What it KEEPS: the part that is easy to get wrong ---

    // Rules 1.2's closed attribute list. A blanked Wolverine is still
    // named Wolverine, still X-Men, still 4A - he just does nothing.
    [Fact]
    public void A_Blanked_Die_Keeps_Its_Attributes_And_Its_Stats()
    {
        var card = BuildCard("C1", affiliations: ["X-Men"], keywords: ["Overcrush"]);
        var state = BuildState(card);
        var die = AddDie(state, card, "p1", "d1");

        die.Suppressions.Add(new DieSuppression(Duration.EndOfTurn));

        Assert.Contains("X-Men", QueryEngine.GetAffiliations(state, die));
        Assert.Contains(card.Name, QueryEngine.GetTags(state, die));   // name is an attribute
        Assert.Contains("Fist", QueryEngine.GetTags(state, die));      // so is energy type
        Assert.Equal(4, QueryEngine.GetAttack(state, die));
        Assert.Equal(3, QueryEngine.GetDefense(state, die));
    }

    // The user's Part 16 correction, and the reason it is not a detail:
    // Psylocke grants a die Overcrush, Shriek blanks that die, it keeps
    // Overcrush. Rule 3.4.8.2 blanks abilities "because dice refer to
    // their card" - a granted ability does not come from the blanked card.
    [Fact]
    public void Blanking_Does_Not_Remove_A_Granted_Ability()
    {
        var granted = new TriggeredAbility(TriggerKind.DieFielded, new LifeChange(new Fixed(0)));
        var card = BuildCard("C1", abilities: [new TriggeredAbility(TriggerKind.DieKOd, new LifeChange(new Fixed(0)))]);
        var state = BuildState(card);
        var die = AddDie(state, card, "p1", "d1");
        die.GrantedAbilities.Add(new GrantedAbility(granted, Duration.EndOfTurn));
        die.GrantedTags.Add(new GrantedTag("Overcrush", Duration.EndOfTurn));

        die.Suppressions.Add(new DieSuppression(Duration.EndOfTurn));

        Assert.Contains(granted, QueryEngine.AbilitiesOf(state, die));
        Assert.Contains("Overcrush", QueryEngine.GetTags(state, die));  // granted tags survive too
        Assert.Single(QueryEngine.AbilitiesOf(state, die));             // but the card's own is gone
    }

    // The PermanentText half - King Black Bolt's "this text may not be
    // ignored", which is one CLAUSE of a card that otherwise blanks.
    [Fact]
    public void Blanking_Does_Not_Remove_Permanent_Text()
    {
        var ordinary = new TriggeredAbility(TriggerKind.DieFielded, new LifeChange(new Fixed(0)));
        var permanent = new TriggeredAbility(TriggerKind.DieKOd, new LifeChange(new Fixed(0)));
        var card = BuildCard("C1", abilities: [ordinary], permanentAbilities: [permanent]);
        var state = BuildState(card);
        var die = AddDie(state, card, "p1", "d1");

        die.Suppressions.Add(new DieSuppression(Duration.EndOfTurn));

        Assert.Equal([permanent], QueryEngine.AbilitiesOf(state, die));
    }

    // --- Scope: die vs card ---

    // The default. Two dice from one card; blanking one leaves the other
    // alone, which is exactly why Wolverine "No More Distractions" has to
    // print "for all copies of that die" to get both.
    [Fact]
    public void Die_Scoped_Blanking_Leaves_Other_Copies_Of_The_Same_Card_Alone()
    {
        var card = BuildCard("C1", keywords: ["Overcrush"]);
        var state = BuildState(card);
        var blanked = AddDie(state, card, "p1", "d1");
        var sibling = AddDie(state, card, "p1", "d2");

        // Self: true resolves to the bound "self" - the one die, not a
        // filter that could pick either copy.
        EffectInterpreter.Execute(
            new BlankText(new TargetFilter(Self: true)),
            BuildContext(state, "p1", blanked.Id));

        Assert.False(QueryEngine.AbilitiesActive(state, blanked));
        Assert.True(QueryEngine.AbilitiesActive(state, sibling));
    }

    // Card scope reaches every copy, including one that is not in play -
    // which a die-scoped blank could never do, and which is why the two
    // scopes both exist.
    [Fact]
    public void Card_Scoped_Blanking_Reaches_Every_Copy_Including_Unpurchased_Ones()
    {
        var card = BuildCard("C1", keywords: ["Overcrush"]);
        var state = BuildState(card);
        var inPlay = AddDie(state, card, "p2", "d1");
        var stillInTheBag = new DieInstance
        {
            Id = "d2", CardId = card.Id, OwnerId = "p2", ControllerId = "p2", Zone = Zone.Unpurchased,
        };
        state.Dice.Add(stillInTheBag);

        EffectInterpreter.Execute(new BlankCardText(AllOpposing: true), BuildContext(state, "p1"));

        Assert.False(QueryEngine.AbilitiesActive(state, inPlay));
        Assert.False(QueryEngine.AbilitiesActive(state, stillInTheBag));
        Assert.False(QueryEngine.CardTextActive(state, "p2", card.Id));
        Assert.True(QueryEngine.CardTextActive(state, "p1", card.Id));  // per-player, not global
    }

    // --- Expiry ---

    [Fact]
    public void Blanking_Expires_At_Clean_Up()
    {
        var card = BuildCard("C1", keywords: ["Overcrush"]);
        var state = BuildState(card);
        var die = AddDie(state, card, "p1", "d1");

        EffectInterpreter.Execute(new BlankCardText(AllOpposing: true), BuildContext(state, "p2"));
        Assert.False(QueryEngine.AbilitiesActive(state, die));

        state.CurrentStep = TurnStep.Attack; // CleanUp follows the Attack step
        TurnEngine.CleanUp(state, new AbilityQueue());

        Assert.True(QueryEngine.AbilitiesActive(state, die));
        Assert.Empty(state.CardSuppressions);
    }

    private sealed class FixedRoller(int index) : IDiceRoller
    {
        public int Roll(DieDefinition die) => index;
    }

    private static EffectContext BuildContext(GameState state, string controllerId, string? selfId = null)
    {
        var ctx = new EffectContext
        {
            State = state,
            Queue = new AbilityQueue(),
            ControllerId = controllerId,
            Trigger = TriggerKind.Global,
            Roller = new FixedRoller(0),
            Random = new Random(0),
        };
        if (selfId is not null) ctx.Bind("self", selfId);
        return ctx;
    }

    // --- Continuous blanking (D'Ken's shape) ---

    // D'Ken "Shi'ar Civil War": while active, opposing character dice
    // with Purchase Cost 3 or lower have no text. Conditional, so it is
    // recomputed on read rather than stored - the whole reason
    // AbilitiesActive is a query and not a flag.
    [Fact]
    public void A_Continuous_Blank_Applies_While_Its_Source_Is_Active()
    {
        var dken = BuildCard("DKEN", continuous:
            [new AbilityBlank(new TargetFilter(
                Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Opposing, Count: 99,
                Stat: new StatThreshold(StatKind.PurchaseCost, Max: 3)))]);
        var cheap = BuildCard("CHEAP", keywords: ["Overcrush"]);
        var state = BuildState(dken, cheap);
        ContinuousRegistry.RegisterAll(state);

        var victim = AddDie(state, cheap, "p2", "victim");
        Assert.True(QueryEngine.AbilitiesActive(state, victim)); // no source on the board yet

        var source = AddDie(state, dken, "p1", "dken");
        Assert.False(QueryEngine.AbilitiesActive(state, victim));
        Assert.DoesNotContain("Overcrush", QueryEngine.GetTags(state, victim));

        // Leaves play and the blank simply stops being true - nothing to
        // sweep, which is the point of deriving it rather than storing it.
        source.Zone = Zone.UsedPile;
        Assert.True(QueryEngine.AbilitiesActive(state, victim));
    }

    // The recursion break (QueryEngine.AbilitiesActiveBase). A blanked
    // D'Ken blanks nothing - v1 answered this the same way through its
    // GetCard choke point, and V2_PLAN.md Phase 8 task 3 asked for the
    // answer explicitly.
    [Fact]
    public void A_Blanked_Source_Grants_No_Continuous_Blank()
    {
        var dken = BuildCard("DKEN", continuous:
            [new AbilityBlank(new TargetFilter(
                Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Opposing, Count: 99))]);
        var cheap = BuildCard("CHEAP", keywords: ["Overcrush"]);
        var state = BuildState(dken, cheap);
        ContinuousRegistry.RegisterAll(state);

        var source = AddDie(state, dken, "p1", "dken");
        var victim = AddDie(state, cheap, "p2", "victim");
        Assert.False(QueryEngine.AbilitiesActive(state, victim));

        source.Suppressions.Add(new DieSuppression(Duration.EndOfTurn));

        Assert.True(QueryEngine.AbilitiesActive(state, victim));
    }

    // --- Lockout (Blob / Drax) ---

    [Fact]
    public void A_Chosen_Card_Cannot_Be_Purchased_Or_Fielded_By_The_Opponent()
    {
        var blob = BuildCard("BLOB", continuous:
        [
            new Lockout(SuppressionKind.CantPurchase, MemoryName: "chosen"),
            new Lockout(SuppressionKind.CantField, MemoryName: "chosen"),
        ]);
        var prey = BuildCard("PREY");
        var state = BuildState(blob, prey);
        ContinuousRegistry.RegisterAll(state);

        var source = AddDie(state, blob, "p1", "blob");
        var target = AddDie(state, prey, "p2", "prey");

        Assert.True(QueryEngine.CanPurchase(state, "p2", prey.Id));

        EffectInterpreter.Execute(
            new RememberCard(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Opposing), "chosen"),
            BuildContext(state, "p1", source.Id));

        Assert.False(QueryEngine.CanPurchase(state, "p2", prey.Id));
        Assert.False(QueryEngine.CanField(state, "p2", prey.Id));
        // The SOURCE's opponent is locked out, not everyone.
        Assert.True(QueryEngine.CanPurchase(state, "p1", prey.Id));
        // And a locked-out card is not an ignored one - the die keeps its
        // text, it just cannot be bought again.
        Assert.True(QueryEngine.AbilitiesActive(state, target));

        source.Zone = Zone.UsedPile; // "until Blob leaves the Field Zone"
        Assert.True(QueryEngine.CanPurchase(state, "p2", prey.Id));
    }

    // Magneto AOU139's "Professor X can't be fielded" - named outright,
    // so it needs no choice step and no memory.
    [Fact]
    public void A_Lockout_Can_Name_Its_Card_Outright()
    {
        var magneto = BuildCard("MAG", continuous: [new Lockout(SuppressionKind.CantField, CardId: "PROFX")]);
        var profX = BuildCard("PROFX");
        var state = BuildState(magneto, profX);
        ContinuousRegistry.RegisterAll(state);
        AddDie(state, magneto, "p1", "mag");

        Assert.False(QueryEngine.CanField(state, "p2", "PROFX"));
        Assert.True(QueryEngine.CanField(state, "p2", "MAG"));
    }

    // "Replacing all previous choices" - a second choice overwrites the
    // first rather than stacking a second lockout. The memory is keyed on
    // the SOURCE card, which is what makes that automatic.
    [Fact]
    public void A_Second_Choice_Replaces_The_First()
    {
        var blob = BuildCard("BLOB", continuous: [new Lockout(SuppressionKind.CantPurchase, MemoryName: "chosen")]);
        var first = BuildCard("FIRST");
        var second = BuildCard("SECOND");
        var state = BuildState(blob, first, second);
        ContinuousRegistry.RegisterAll(state);

        var source = AddDie(state, blob, "p1", "blob");
        AddDie(state, first, "p2", "first");
        AddDie(state, second, "p2", "second");

        // Card names are tags, so each picks out exactly one opposing die.
        void Choose(CardDef card) => EffectInterpreter.Execute(
            new RememberCard(
                new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Opposing,
                    Tags: new TagQuery(AnyOf: [card.Name])),
                "chosen"),
            BuildContext(state, "p1", source.Id));

        Choose(first);
        Assert.False(QueryEngine.CanPurchase(state, "p2", first.Id));
        Assert.True(QueryEngine.CanPurchase(state, "p2", second.Id));

        Choose(second);

        Assert.True(QueryEngine.CanPurchase(state, "p2", first.Id));
        Assert.False(QueryEngine.CanPurchase(state, "p2", second.Id));
    }
}

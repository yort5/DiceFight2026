using DiceFight.V2;
using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Tests;

// Spike A (V2_VOCABULARY.md Parts 19-21, signed off 2026-09-01).
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
}
